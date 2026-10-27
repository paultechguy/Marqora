// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using PaulTechGuy.MQ.Cli.Reporting;

namespace PaulTechGuy.MQ.Cli.Commands;

/// <summary>The markdown a verb works on: files, glob patterns, or "-" for standard input.</summary>
internal sealed class InputOptions
{
    public required Argument<string[]> Inputs { get; init; }

    /// <summary>Where relative links resolve for standard input, which has no folder of its own (CLI-IN-03).</summary>
    public required Option<string?> BaseDir { get; init; }
}

/// <summary>How a text verb delivers its result (CLI-OUT-01 to CLI-OUT-05).</summary>
internal sealed class ModeOptions
{
    public required Option<bool> Write { get; init; }

    public required Option<bool> Check { get; init; }

    public required Option<bool> DryRun { get; init; }
}

/// <summary>How a text verb was asked to deliver its result.</summary>
internal enum TextMode
{
    /// <summary>The result to stdout; no file touched.</summary>
    Print,

    /// <summary>Rewrite each input in place.</summary>
    Write,

    /// <summary>Touch nothing; list the files that would change and exit 1 if any would.</summary>
    Check,

    /// <summary>Touch nothing; print a unified diff of what would change.</summary>
    DryRun,
}

/// <summary>
/// What every command is built from: the global options, the inputs and modes the text verbs
/// share, each command's examples, and the one path every action runs through.
/// </summary>
internal sealed class CommandShared(CliEnvironment environment)
{
    private readonly Dictionary<Command, string[]> _examples = [];

    public CliEnvironment Environment => environment;

    public GlobalOptions Globals { get; } = new();

    public IReadOnlyDictionary<Command, string[]> Examples => _examples;

    /// <summary>A verb, with its examples recorded for help and its alias, if it has one.</summary>
    public Command Verb(string name, string description, string[] examples, string? alias = null)
    {
        var command = new Command(name, description);

        if (alias is not null)
        {
            command.Aliases.Add(alias);
        }

        _examples[command] = examples;

        return command;
    }

    /// <summary>
    /// The inputs argument and --base-dir. "-" must stand alone: standard input cannot be mixed
    /// with files, because there would be no telling which output was whose (CLI-IN-02). A verb
    /// that refuses standard input, as folio does, is not offered --base-dir, which only it uses.
    /// </summary>
    public static InputOptions AddInputs(Command command, string description = "Markdown files or glob patterns, or - for standard input.", bool readsStandardInput = true)
    {
        ArgumentNullException.ThrowIfNull(command);

        var inputs = new InputOptions
        {
            Inputs = new Argument<string[]>("inputs")
            {
                Description = description,
                Arity = ArgumentArity.OneOrMore,
            },
            BaseDir = new Option<string?>("--base-dir")
            {
                Description = "The folder relative links and pictures resolve against when reading standard input. Defaults to the current folder.",
                HelpName = "folder",
            },
        };

        command.Arguments.Add(inputs.Inputs);
        if (readsStandardInput)
        {
            command.Options.Add(inputs.BaseDir);
        }

        command.Validators.Add(result =>
        {
            string[] given = result.GetValue(inputs.Inputs) ?? [];

            // An argument that takes any number of values swallows an unknown option as one of
            // them, so "--writ" would be read as a file and the run would quietly not write. It is
            // reported the way the parser reports an unknown token, so it gets the same "did you
            // mean". A file whose name really starts with a dash is still reachable as ./-name.
            foreach (string token in given)
            {
                if (token.Length > 1 && token[0] == '-')
                {
                    result.AddError($"Unrecognized command or argument '{token}'.");
                    return;
                }
            }

            if (given.Length > 1 && given.Contains("-"))
            {
                result.AddError("'-' reads standard input and cannot be combined with files.");
            }
        });

        return inputs;
    }

    /// <summary>--write, --check and --dry-run: at most one of them (CLI-CMD-04).</summary>
    public static ModeOptions AddModes(Command command, string what)
    {
        ArgumentNullException.ThrowIfNull(command);

        var modes = new ModeOptions
        {
            Write = new Option<bool>("--write", "-w") { Description = $"Rewrite each file in place with {what}." },
            Check = new Option<bool>("--check") { Description = "Change nothing; list the files that would change, and exit 1 if any would." },
            DryRun = new Option<bool>("--dry-run") { Description = "Change nothing; print a unified diff of what would change." },
        };

        command.Options.Add(modes.Write);
        command.Options.Add(modes.Check);
        command.Options.Add(modes.DryRun);

        command.Validators.Add(result =>
        {
            int chosen = (result.GetValue(modes.Write) ? 1 : 0)
                + (result.GetValue(modes.Check) ? 1 : 0)
                + (result.GetValue(modes.DryRun) ? 1 : 0);

            if (chosen > 1)
            {
                result.AddError("--write, --check and --dry-run cannot be combined; choose one.");
            }
        });

        return modes;
    }

    public static TextMode ModeOf(ParseResult parse, ModeOptions modes)
    {
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(modes);

        return parse.GetValue(modes.Write) ? TextMode.Write
            : parse.GetValue(modes.Check) ? TextMode.Check
            : parse.GetValue(modes.DryRun) ? TextMode.DryRun
            : TextMode.Print;
    }

    /// <summary>
    /// The action every command runs through: the global options read, the reporter and result
    /// writer made, and what can end a run turned into its exit code. A verb returns its outcome;
    /// it never writes an exit code of its own.
    /// </summary>
    public void SetAction(Command command, string verb, Func<RunContext, ParseResult, Task<ExitCode>> run)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(run);

        command.SetAction(async (parse, cancellationToken) =>
        {
            ColorMode color = parse.GetValue(Globals.Color) switch
            {
                "always" => ColorMode.Always,
                "never" => ColorMode.Never,
                _ => ColorMode.Auto,
            };

            bool quiet = parse.GetValue(Globals.Quiet);
            bool verbose = parse.GetValue(Globals.Verbose);
            var reporter = new Reporter(
                environment.Error,
                quiet ? Verbosity.Quiet : verbose ? Verbosity.Verbose : Verbosity.Normal,
                Reporter.UsesColor(color, environment));

            if (quiet && verbose)
            {
                reporter.Usage("--quiet and --verbose cannot be combined.", HelpPath(command));
                return (int)ExitCode.Usage;
            }

            var context = new RunContext
            {
                Environment = environment,
                Reporter = reporter,
                Results = new ResultWriter(environment.Out, parse.GetValue(Globals.Json)),
                Verb = verb,
                FailFast = parse.GetValue(Globals.FailFast),
                Settings = parse.GetValue(Globals.Settings),
                CancellationToken = cancellationToken,
            };

            try
            {
                return (int)await run(context, parse).ConfigureAwait(false);
            }
            catch (UsageException ex)
            {
                reporter.Usage(ex.Message, HelpPath(command));
                return (int)ExitCode.Usage;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                reporter.Error(null, "Canceled.");
                return (int)ExitCode.Canceled;
            }
        });
    }

    /// <summary>"marqora-cli export pdf": the command as it would be typed, for "See ... --help".</summary>
    public static string HelpPath(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var names = new List<string>();

        for (Symbol? symbol = command; symbol is Command current; symbol = current.Parents.FirstOrDefault())
        {
            names.Add(current is RootCommand ? "marqora-cli" : current.Name);
        }

        names.Reverse();

        return string.Join(' ', names);
    }
}
