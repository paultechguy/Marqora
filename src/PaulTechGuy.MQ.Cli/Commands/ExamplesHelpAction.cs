// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;

namespace PaulTechGuy.MQ.Cli.Commands;

/// <summary>
/// The library's own help, followed by the command's examples (CLI-CMD-03). The examples live in
/// one place, the command tree, and the manual repeats them; a test holds the two together and
/// another fails on a verb that has none.
/// </summary>
internal sealed class ExamplesHelpAction(HelpAction inner, IReadOnlyDictionary<Command, string[]> examples)
    : SynchronousCommandLineAction
{
    /// <summary>The installed manual and recipes, said the way a reader would go looking.</summary>
    internal const string ManualFolder = @"CLI.md and CLI-Recipes.md, in the docs folder beside marqora-cli.exe";

    /// <summary>The manual online, printed for the reader to follow; nothing here fetches it.</summary>
    internal const string ManualUrl = "https://github.com/paultechguy/Marqora/blob/master/docs/CLI.md";

    public override bool ClearsParseErrors => true;

    public override int Invoke(ParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        int result = inner.Invoke(parseResult);

        Command command = parseResult.CommandResult.Command;
        TextWriter output = parseResult.InvocationConfiguration.Output;

        // The library lists a verb's other names only in its parent's command list; a reader who
        // asked for the verb's own help is told them too (CLI-CMD-02).
        if (command is not RootCommand && command.Aliases.Count > 0)
        {
            output.WriteLine("Also called:");

            foreach (string alias in command.Aliases)
            {
                output.WriteLine("  " + alias);
            }

            output.WriteLine();
        }

        if (examples.TryGetValue(command, out string[]? lines) && lines.Length > 0)
        {
            output.WriteLine("Examples:");

            foreach (string line in lines)
            {
                output.WriteLine("  " + line);
            }

            output.WriteLine();
        }

        // Where to read further, on every command's help, because --help is where somebody in
        // a terminal looks first and the manual is the one thing it cannot hold. Named by
        // folder rather than by full path so the text is the same on every machine.
        output.WriteLine("Manual:");
        output.WriteLine("  " + ManualFolder);
        output.WriteLine("  " + ManualUrl);
        output.WriteLine();

        return result;
    }
}
