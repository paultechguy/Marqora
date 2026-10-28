// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Runtime.InteropServices;
using PaulTechGuy.MQ.Cli.Commands;
using PaulTechGuy.MQ.Cli.Reporting;
using PaulTechGuy.MQ.Cli.Verbs;

namespace PaulTechGuy.MQ.Cli;

/// <summary>
/// One run of marqora-cli: the command line parsed, a parse error turned into exit 2 with a
/// sentence and a pointer to help, and otherwise the verb invoked and its exit code returned.
///
/// Takes its environment as an argument, so a test runs it in-process and reads back what it
/// wrote. <see cref="Run"/> is the process's own entry, with Ctrl+C wired up.
/// </summary>
internal static class CliApp
{
    private const string UnrecognizedPrefix = "Unrecognized command or argument '";

    /// <summary>The process entry: the real console, and Ctrl+C or Ctrl+Break ending the run with 130.</summary>
    public static int Run(string[] args)
    {
        using var cancel = new CancellationTokenSource();

        // Both signals cancel. The handler says it has been dealt with, so the runtime does not end
        // the process before the verb has cleaned up what it started (CLI-RPT-08).
        using PosixSignalRegistration interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Cancel);
        using PosixSignalRegistration quit = PosixSignalRegistration.Create(PosixSignal.SIGQUIT, Cancel);

        void Cancel(PosixSignalContext context)
        {
            context.Cancel = true;
            cancel.Cancel();
        }

        return RunAsync(args, CliEnvironment.FromProcess(), cancel.Token).GetAwaiter().GetResult();
    }

    public static async Task<int> RunAsync(string[] args, CliEnvironment environment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(environment);

        CommandTree tree = CommandTree.Create(environment);

        args = EmptyAfterEquals(args, tree);

        // No response files: "@args.rsp" is a file name like any other (CLI-CMD-05).
        ParseResult parse = tree.Root.Parse(args, new ParserConfiguration { ResponseFileTokenReplacer = null });

        bool answersItself = parse.Action is ExamplesHelpAction or VersionVerb.Action;

        if (!answersItself && parse.Errors.Count > 0)
        {
            ReportParseError(parse, tree, environment);
            return (int)ExitCode.Usage;
        }

        if (!answersItself && OptionTakenAsValue(args, parse.CommandResult.Command, tree) is { } swallowed)
        {
            new Reporter(environment.Error, Verbosity.Normal, Reporter.UsesColor(ColorMode.Auto, environment))
                .Usage(swallowed, CommandShared.HelpPath(parse.CommandResult.Command));
            return (int)ExitCode.Usage;
        }

        var invocation = new InvocationConfiguration
        {
            Output = environment.Out,
            Error = environment.Error,

            // An exception is a bug, and is left to say so with its stack rather than be turned
            // into an exit code that claims to mean something else.
            EnableDefaultExceptionHandler = false,

            // Ctrl+C is handled in Run, where the verb can clean up before the process ends.
            ProcessTerminationTimeout = null,
        };

        try
        {
            return await parse.InvokeAsync(invocation, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (int)ExitCode.Canceled;
        }
    }

    /// <summary>
    /// The first parse error, as one sentence: an unknown verb or option with the nearest real one
    /// suggested, otherwise the parser's own words. Always ends by pointing at help for the
    /// command the error belongs to (CLI-CMD-04).
    /// </summary>
    private static void ReportParseError(ParseResult parse, CommandTree tree, CliEnvironment environment)
    {
        Command command = parse.CommandResult.Command;
        var reporter = new Reporter(environment.Error, Verbosity.Normal, Reporter.UsesColor(ColorMode.Auto, environment));

        // An unknown word is the error to report when there is one: "fromat" also leaves the root
        // without a command, and "no command was given" would hide the typo that caused it.
        ParseError error = parse.Errors.FirstOrDefault(e => IsUnrecognized(e.Message)) ?? parse.Errors[0];
        string message = error.Message;

        if (IsUnrecognized(message))
        {
            string token = message[UnrecognizedPrefix.Length..^2];

            message = token.StartsWith('-')
                ? UnknownOption(token, command, tree)
                : UnknownCommand(token, command);
        }
        else
        {
            message = OneSentence(message);
        }

        reporter.Usage(message, CommandShared.HelpPath(command));
    }

    /// <summary>
    /// "--with=" with nothing after the "=" means an empty value. The parser instead reads the
    /// option as having no value and takes the next word, so "--with= --dry-run" replaced with
    /// the text "--dry-run". Split into the option and an empty word, it means what it says.
    /// Words after "--" are names, and are left alone.
    /// </summary>
    private static string[] EmptyAfterEquals(string[] args, CommandTree tree)
    {
        if (!args.Any(arg => arg.Length > 2 && arg[0] == '-' && arg[^1] == '='))
        {
            return args;
        }

        HashSet<string> names = new(OptionNames(tree.Root), StringComparer.Ordinal);
        var rewritten = new List<string>(args.Length + 1);
        bool literal = false;

        foreach (string arg in args)
        {
            literal |= arg == "--";

            if (!literal && arg.Length > 2 && arg[^1] == '=' && names.Contains(arg[..^1]))
            {
                rewritten.Add(arg[..^1]);
                rewritten.Add(string.Empty);
            }
            else
            {
                rewritten.Add(arg);
            }
        }

        return [.. rewritten];
    }

    /// <summary>
    /// An option that needs a value, followed by one of the command's own options as a separate
    /// word: "--with --dry-run". Almost always a value that went missing, as Windows PowerShell
    /// 5.1 drops an empty '' before marqora-cli sees it, and taking the option as the text would
    /// run a different command from the one asked for. Written as "--with=--dry-run", the text is
    /// plainly meant and is taken.
    /// </summary>
    private static string? OptionTakenAsValue(string[] args, Command command, CommandTree tree)
    {
        Option[] options = [.. command.Options.Concat(tree.Shared.Globals.All).Distinct()];
        HashSet<string> names = new(options.SelectMany(NamesOf), StringComparer.Ordinal);

        for (int i = 0; i < args.Length - 1 && args[i] != "--"; i++)
        {
            Option? option = options.FirstOrDefault(o => NamesOf(o).Contains(args[i], StringComparer.Ordinal));

            if (option is null || option.ValueType == typeof(bool) || option.Arity.MaximumNumberOfValues == 0)
            {
                continue;
            }

            string next = args[i + 1];
            int equals = next.IndexOf('=', StringComparison.Ordinal);

            if (names.Contains(equals > 0 ? next[..equals] : next))
            {
                return $"'{args[i]}' needs a value, but the next word is the option '{next}'. "
                    + $"To use that text as the value, write {args[i]}={next}.";
            }
        }

        return null;
    }

    private static IEnumerable<string> NamesOf(Option option) => option.Aliases.Prepend(option.Name);

    private static IEnumerable<string> OptionNames(Command command) =>
        command.Options.SelectMany(NamesOf).Concat(command.Subcommands.SelectMany(OptionNames));

    private static bool IsUnrecognized(string message) =>
        message.StartsWith(UnrecognizedPrefix, StringComparison.Ordinal) && message.EndsWith("'.", StringComparison.Ordinal);

    /// <summary>
    /// The parser lists the allowed values one to a line, tab-indented; on a terminal that is four
    /// lines for one mistake. Folded into the sentence it belongs to.
    /// </summary>
    private static string OneSentence(string message)
    {
        const string Marker = "Must be one of:";

        int at = message.IndexOf(Marker, StringComparison.Ordinal);

        if (at < 0)
        {
            return message.ReplaceLineEndings(" ").Trim();
        }

        string[] values = message[(at + Marker.Length)..]
            .Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => value.Trim('\''))
            .ToArray();

        return $"{message[..at].Trim()} Must be one of: {string.Join(", ", values)}.";
    }

    private static string UnknownOption(string token, Command command, CommandTree tree)
    {
        IEnumerable<string> names = command.Options
            .Concat(tree.Shared.Globals.All)
            .Where(option => !option.Hidden)
            .SelectMany(option => option.Aliases.Prepend(option.Name))
            .Where(name => name.StartsWith("--", StringComparison.Ordinal));

        return Suggest.Closest(token, names) is { } near
            ? $"Unknown option '{token}'. Did you mean '{near}'?"
            : $"Unknown option '{token}'.";
    }

    private static string UnknownCommand(string token, Command command)
    {
        if (command.Subcommands.Count == 0)
        {
            return $"Unexpected argument '{token}'.";
        }

        IEnumerable<string> names = command.Subcommands
            .Where(sub => !sub.Hidden)
            .SelectMany(sub => sub.Aliases.Prepend(sub.Name));

        string what = command is RootCommand ? "a marqora-cli command" : $"a '{command.Name}' command";

        return Suggest.Closest(token, names) is { } near
            ? $"'{token}' is not {what}. Did you mean '{near}'?"
            : $"'{token}' is not {what}.";
    }
}
