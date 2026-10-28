// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using PaulTechGuy.MQ.Analysis;
using PaulTechGuy.MQ.Cli.Commands;
using PaulTechGuy.MQ.Cli.Inputs;
using PaulTechGuy.MQ.Cli.Reporting;
using PaulTechGuy.MQ.Cli.Text;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Rendering;

namespace PaulTechGuy.MQ.Cli.Verbs;

/// <summary>
/// check: what the source pane's squiggles say, for a whole tree at once (CLI-VB-CHK-01, CHK-02).
///
/// The document is rendered and handed to the app's own analyzer with the request the app builds,
/// so a finding here is a squiggle there. What fails the run:
/// <list type="bullet">
/// <item>Errors: a dead local link, a dead anchor, a missing picture - and, unless
/// --ignore-external-images, a picture from the web or from outside the document's folder, which
/// the app blocks and so never shows (amendment A13b).</item>
/// <item>Hints, only with --style, and only under --strict: the style rules and missing alt text.</item>
/// </list>
/// Web links are never contacted; --verbose lists them as not checked. Positions are one-based, in
/// the compiler's file:line:column order, which editors and CI logs already know how to follow.
/// </summary>
internal static class CheckVerb
{
    public static Task<ExitCode> RunAsync(
        RunContext context,
        ParseResult parse,
        InputOptions inputs,
        Option<bool> style,
        Option<bool> strict,
        Option<bool> ignoreExternal)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parse);

        bool withStyle = parse.GetValue(style);
        bool strictly = parse.GetValue(strict);
        bool external = !parse.GetValue(ignoreExternal);

        var renderer = new MarkdigMarkdownRenderer(new ReporterLogger<MarkdigMarkdownRenderer>(context.Reporter));
        var analyzer = new MarkdownAnalyzer();

        return ReportVerbRunner.RunAsync(context, parse, inputs, document =>
        {
            DocumentFinding[] findings = Findings(document, renderer, analyzer, withStyle, external);

            bool fails = findings.Any(f => f.Severity == FindingSeverity.Error)
                || (strictly && findings.Any(f => f.Severity == FindingSeverity.Hint));

            // Information is for --verbose; everything else is the result.
            string[] lines = [.. findings
                .Where(f => f.Severity != FindingSeverity.Info || context.Reporter.IsVerbose)
                .Select(f => $"{Reporter.Locate(document.Display, f)}: {f.Severity.ToString().ToLowerInvariant()}: {f.Message} [{f.Code}]")];

            return new Report(lines, findings, fails);
        });
    }

    /// <summary>The analyzer's findings for one document, in document order, as the CLI reports them.</summary>
    internal static DocumentFinding[] Findings(Document document, MarkdigMarkdownRenderer renderer, MarkdownAnalyzer analyzer, bool withStyle, bool externalIsError)
    {
        RenderedMarkdown rendered = renderer.Render(document.File.Text);

        AnalysisResult result = analyzer.Analyze(new AnalysisRequest
        {
            Text = document.File.Text,
            DocumentPath = document.LocationPath,
            Links = rendered.Links,
            Outline = rendered.Outline,
            Anchors = rendered.Anchors,
            CheckImageAltText = withStyle,
            CheckBlockedImages = true,
        });

        IEnumerable<DocumentFinding> links = result.LinkFindings.Select(finding => new DocumentFinding(
            finding.Line + 1,
            finding.Start + 1,
            CodeOf(finding.Kind),
            SeverityOf(finding.Kind, externalIsError),
            finding.Message));

        IEnumerable<DocumentFinding> style = withStyle
            ? result.Diagnostics.Select(diagnostic => new DocumentFinding(diagnostic.Line + 1, diagnostic.Column + 1, diagnostic.Rule, FindingSeverity.Hint, diagnostic.Message))
            : [];

        IEnumerable<DocumentFinding> web = rendered.Links
            .Where(link => !link.IsImage && IsWeb(link.Url))
            .Select(link => new DocumentFinding(link.SourceLine + 1, link.SourceColumn + 1, "web-link-not-checked", FindingSeverity.Info, $"Web link not checked: {link.Url}"));

        return [.. links.Concat(style).Concat(web).OrderBy(f => f.Line).ThenBy(f => f.Column)];
    }

    private static string CodeOf(LinkFindingKind kind) => kind switch
    {
        LinkFindingKind.MissingImage => "missing-image",
        LinkFindingKind.BrokenLink => "broken-link",
        LinkFindingKind.DeadAnchor => "dead-anchor",
        LinkFindingKind.MissingAltText => "missing-alt-text",
        LinkFindingKind.RemoteMedia => "remote-image",
        LinkFindingKind.OutsideFolder => "outside-folder",
        _ => kind.ToString(),
    };

    private static FindingSeverity SeverityOf(LinkFindingKind kind, bool externalIsError) => kind switch
    {
        LinkFindingKind.MissingAltText => FindingSeverity.Hint,
        LinkFindingKind.RemoteMedia or LinkFindingKind.OutsideFolder => externalIsError ? FindingSeverity.Error : FindingSeverity.Info,
        _ => FindingSeverity.Error,
    };

    private static bool IsWeb(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
