// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using PaulTechGuy.MQ.Cli.Commands;
using PaulTechGuy.MQ.Cli.Reporting;
using PaulTechGuy.MQ.Cli.Settings;
using PaulTechGuy.MQ.Cli.Text;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Formatting;

namespace PaulTechGuy.MQ.Cli.Verbs;

/// <summary>
/// number: Number Headings (CLI-VB-NUM-01), through the app's own rewriter, which moves the links
/// that point at a renamed heading along with it.
///
/// Where numbering starts is decided as the app's dialog suggests it: --from when given, then the
/// heading numbering the settings name, then the level the document already numbers from, then
/// h1. What follows each number is --style, or the settings' style.
/// </summary>
internal static class NumberVerb
{
    public static async Task<ExitCode> RunAsync(
        RunContext context,
        ParseResult parse,
        InputOptions inputs,
        ModeOptions modes,
        Option<string?> from,
        Option<string?> style)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parse);

        AppSettings settings = (await SettingsSource.LoadAsync(context).ConfigureAwait(false)).Settings;

        HeadingNumbering? asked = parse.GetValue(from) switch
        {
            "h1" => HeadingNumbering.FromHeading1,
            "h2" => HeadingNumbering.FromHeading2,
            "h3" => HeadingNumbering.FromHeading3,
            _ => null,
        };

        HeadingNumberStyle numberStyle = parse.GetValue(style) switch
        {
            "two-spaces" => HeadingNumberStyle.TwoSpaces,
            "one-space" => HeadingNumberStyle.OneSpace,
            "dot-space" => HeadingNumberStyle.DotSpace,
            "paren-space" => HeadingNumberStyle.ParenSpace,
            _ => settings.HeadingNumberStyle,
        };

        return await TextVerbRunner.RunAsync(context, parse, inputs, modes, document =>
        {
            HeadingNumberDetector.Scan scan = HeadingNumberRewriter.Read(document.File.Text);

            if (scan.Headings.Count == 0)
            {
                return new VerbResult(document.File.Text, [new DocumentFinding(0, 0, "no-headings", FindingSeverity.Info, "Nothing to number: the document has no headings.")]);
            }

            HeadingNumbering start = asked
                ?? (settings.HeadingNumbering != HeadingNumbering.Off ? settings.HeadingNumbering
                    : scan.IsNumbered ? scan.StartLevel
                    : HeadingNumbering.FromHeading1);

            HeadingNumberRewriter.Result result = HeadingNumberRewriter.Apply(document.File.Text, start, numberStyle);

            DocumentFinding[] uncertain = [.. result.UncertainLines.Select(line => new DocumentFinding(
                line,
                0,
                "uncertain-heading",
                FindingSeverity.Warning,
                "This heading's anchor cannot be worked out from its text, so links to it were left as they were."))];

            string summary = result.Changed == 1 ? "1 heading numbered" : $"{result.Changed} headings numbered";

            if (result.LinksMoved > 0)
            {
                summary += result.LinksMoved == 1 ? ", 1 link moved" : $", {result.LinksMoved} links moved";
            }

            return new VerbResult(result.Text, uncertain, summary);
        }).ConfigureAwait(false);
    }
}

/// <summary>unnumber: Remove Heading Numbers (CLI-VB-UNN-01), the rewriter's own removal, links moved with the headings.</summary>
internal static class UnnumberVerb
{
    public static Task<ExitCode> RunAsync(RunContext context, ParseResult parse, InputOptions inputs, ModeOptions modes) =>
        TextVerbRunner.RunAsync(context, parse, inputs, modes, document =>
        {
            HeadingNumberRewriter.Result result = HeadingNumberRewriter.Remove(document.File.Text);

            return new VerbResult(
                result.Text,
                [],
                result.Changed == 1 ? "1 heading number removed" : $"{result.Changed} heading numbers removed");
        });
}
