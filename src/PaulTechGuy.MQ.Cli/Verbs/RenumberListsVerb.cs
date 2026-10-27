// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using PaulTechGuy.MQ.Cli.Commands;
using PaulTechGuy.MQ.Cli.Reporting;
using PaulTechGuy.MQ.Cli.Text;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Editing;

namespace PaulTechGuy.MQ.Cli.Verbs;

/// <summary>
/// renumber-lists: Renumber List on every numbered list in turn (CLI-VB-REN-01, amendment A6).
/// --style sets the numbering for all of them; without it each list gets the numbering the app
/// would suggest for it. A list asked for zeros that cannot take them is left alone and named.
/// </summary>
internal static class RenumberListsVerb
{
    public static Task<ExitCode> RunAsync(RunContext context, ParseResult parse, InputOptions inputs, ModeOptions modes, Option<string?> style)
    {
        ArgumentNullException.ThrowIfNull(parse);

        ListNumbering? numbering = parse.GetValue(style) switch
        {
            "sequential" => ListNumbering.Sequential,
            "ones" => ListNumbering.Ones,
            "zeros" => ListNumbering.Zeros,
            _ => null,
        };

        return TextVerbRunner.RunAsync(context, parse, inputs, modes, document =>
        {
            ListsRenumbered result = DocumentLists.RenumberAll(document.File.Text, numbering);

            DocumentFinding[] leftAlone = [.. result.LeftAlone.Select(line => new DocumentFinding(
                line,
                0,
                "list-left-alone",
                FindingSeverity.Warning,
                "Left alone: a list can start at 0 only after a blank line or a heading, as Renumber List requires."))];

            return new VerbResult(result.Text, leftAlone, result.Changed == 1 ? "1 list renumbered" : $"{result.Changed} lists renumbered");
        });
    }
}
