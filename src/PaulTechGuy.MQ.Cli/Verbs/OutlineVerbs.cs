// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using System.Globalization;
using PaulTechGuy.MQ.Cli.Commands;
using PaulTechGuy.MQ.Cli.Reporting;
using PaulTechGuy.MQ.Cli.Settings;
using PaulTechGuy.MQ.Cli.Text;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Rendering;

namespace PaulTechGuy.MQ.Cli.Verbs;

/// <summary>
/// toc: the document's contents, as a nested markdown list of links (CLI-VB-TOC-01).
///
/// The headings, their numbers and their anchors are the renderer's own, with the heading
/// numbering in force, so every link lands on a heading in the preview. The levels listed are the
/// ones the exports' contents list: three deep from where the numbering starts, or from Heading 2
/// when nothing is numbered. Never written into the document; --write is refused.
/// </summary>
internal static class TocVerb
{
    public static async Task<ExitCode> RunAsync(RunContext context, ParseResult parse, InputOptions inputs)
    {
        ArgumentNullException.ThrowIfNull(context);

        HeadingNumbering numbering = (await SettingsSource.LoadAsync(context).ConfigureAwait(false)).Settings.HeadingNumbering;
        (int first, int last) = ContentsListing.Levels(numbering);
        var renderer = new MarkdigMarkdownRenderer(new ReporterLogger<MarkdigMarkdownRenderer>(context.Reporter));

        return await ReportVerbRunner.RunAsync(context, parse, inputs, document =>
        {
            string[] lines = [.. renderer.Render(document.File.Text, numbering).Outline
                .Where(heading => heading.Level >= first && heading.Level <= last && heading.Slug.Length > 0)
                .Select(heading => $"{new string(' ', (heading.Level - first) * 2)}- [{Escape(Label(heading))}](#{heading.Slug})")];

            return new Report(lines, [], Fails: false)
            {
                Json = line => line with { Text = lines.Length == 0 ? string.Empty : string.Join('\n', lines) + "\n" },
            };
        }).ConfigureAwait(false);
    }

    /// <summary>The heading as the preview shows it: its number, when it has one, then its words.</summary>
    internal static string Label(OutlineHeading heading) =>
        heading.Number.Length > 0 ? $"{heading.Number} {heading.Text}" : heading.Text;

    /// <summary>Brackets and backslashes in a heading would end the link's label early.</summary>
    private static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal);
}

/// <summary>
/// outline: the heading tree as the outline pane shows it, with each heading's line
/// (CLI-VB-OUT-01). With --json, each heading's level, text, anchor and line.
/// </summary>
internal static class OutlineVerb
{
    public static async Task<ExitCode> RunAsync(RunContext context, ParseResult parse, InputOptions inputs)
    {
        ArgumentNullException.ThrowIfNull(context);

        HeadingNumbering numbering = (await SettingsSource.LoadAsync(context).ConfigureAwait(false)).Settings.HeadingNumbering;
        var renderer = new MarkdigMarkdownRenderer(new ReporterLogger<MarkdigMarkdownRenderer>(context.Reporter));

        return await ReportVerbRunner.RunAsync(context, parse, inputs, document =>
        {
            IReadOnlyList<OutlineHeading> outline = renderer.Render(document.File.Text, numbering).Outline;

            string[] lines = [.. outline.Select(heading => string.Create(
                CultureInfo.InvariantCulture,
                $"{heading.SourceLine + 1,5}  {new string(' ', (heading.Level - 1) * 2)}{TocVerb.Label(heading)}"))];

            JsonHeading[] headings = [.. outline.Select(heading => new JsonHeading(heading.Level, TocVerb.Label(heading), heading.Slug, heading.SourceLine + 1))];

            return new Report(lines, [], Fails: false) { Json = line => line with { Headings = headings } };
        }).ConfigureAwait(false);
    }
}
