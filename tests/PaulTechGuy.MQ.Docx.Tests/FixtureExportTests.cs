// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Docx.Tests;

/// <summary>
/// The torture-test document, exported whole.
///
/// <c>docs/UltimateMarkdownContent.md</c> is where five schema errors were found that no other
/// test reached - a link inside a footnote, a diagram inside a list item, a task list inside a
/// quote inside a list. Each of those passed in isolation because no isolated test combined
/// them, and Word answered the combination by calling the file corrupt. See
/// <c>docs/WordVsPdf-Comparison.md</c>, §3.1.
///
/// The diagrams go through the picture path, not the fallback: without a preview, every
/// diagram is written as its source in a code block, and the paragraph shape that produced one
/// of the five errors - a centered picture inside a list item - is never built. So the test
/// stands in for the preview the way the harvest reads it: one <c>pre.mermaid</c> per fence,
/// keyed on its source line, and a one-pixel PNG for each.
/// </summary>
public sealed partial class FixtureExportTests
{
    /// <summary>A transparent 1x1 PNG, the same bytes the fixture itself embeds.</summary>
    private static readonly byte[] Pixel = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Theory]
    [InlineData(HeadingNumbering.Off)]
    [InlineData(HeadingNumbering.FromHeading1)]
    [InlineData(HeadingNumbering.FromHeading2)]
    [InlineData(HeadingNumbering.FromHeading3)]
    public async Task The_fixture_validates(HeadingNumbering numbering)
    {
        using ExportedDocument exported = await ExportFixtureAsync(numbering);

        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// What the side-by-side comparison found wrong in Word, checked where it was found.
    /// One test per finding would export the document seven times for one fact each.
    /// </summary>
    [Fact]
    public async Task The_fixture_carries_what_the_comparison_found_missing()
    {
        using ExportedDocument exported = await ExportFixtureAsync();

        using (WordprocessingDocument file = WordprocessingDocument.Open(exported.Path, false))
        {
            MainDocumentPart main = file.MainDocumentPart!;

            // A link inside a footnote is the footnotes part's relationship (§3.1).
            main.FootnotesPart!.HyperlinkRelationships.ShouldNotBeEmpty();

            // No link target holds a space, which Word prints as a field error (§3.3).
            main.HyperlinkRelationships
                .Concat(main.FootnotesPart.HyperlinkRelationships)
                .Select(r => r.Uri.OriginalString)
                .ShouldAllBe(target => !target.Contains(' '));
        }

        string text = exported.PlainText();
        string xml = exported.DocumentXml();

        // An unmatched bracket keeps its "[" (§3.4).
        text.ShouldContain("[broken][no-such-ref]");

        // A soft hyphen is Word's optional hyphen, never a printed one (§3.6).
        xml.ShouldNotContain("­");
        xml.ShouldContain("<w:softHyphen />");

        // A raw HTML block keeps its words, and the report says its styling did not come (§3.5).
        text.ShouldContain("A styled callout built from raw HTML.");
        exported.Skipped.ShouldContain(row => row.Contains("Raw HTML", StringComparison.Ordinal));

        // Comments, scripts and bare tag lines are not reported: a report that lists every
        // <details> line is one nobody reads.
        exported.Issues.Count(i => i.Problem.StartsWith("Raw HTML: its text", StringComparison.Ordinal))
            .ShouldBeLessThanOrEqualTo(6);

        // What shows without words is reported rather than left out in silence: the iframe
        // and three <svg> drawings. (The inline <hr /> is drawn as a rule now, and the inline
        // <img> is reported as a picture, under its alt text.) Those are written as images
        // with an svg data URL, but the unescaped markup breaks the link, so Markdig reads
        // raw HTML and the preview draws it.
        exported.Issues
            .Where(i => i.Problem.StartsWith("Raw HTML that Word cannot show", StringComparison.Ordinal))
            .Select(i => i.Line)
            .Order()
            .ShouldBe([685, 701, 761, 909]);
    }

    internal static async Task<ExportedDocument> ExportFixtureAsync(
        HeadingNumbering numbering = HeadingNumbering.FromHeading2)
    {
        string path = Path.Combine(RepoRoot(), "docs", "UltimateMarkdownContent.md");
        string markdown = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        return await ExportedDocument.FromAsync(
            markdown,
            new ExportLayout { IncludeCoverPage = true, IncludeTableOfContents = true },
            numbering,
            renderedPreviewHtml: DiagramHarvest(markdown),
            sourceDocumentPath: path,
            diagramPng: _ => Task.FromResult<byte[]?>(Pixel));
    }

    /// <summary>
    /// A <c>pre.mermaid</c> for every mermaid fence, wherever it sits - a list item, a quote -
    /// stamped with the zero-based line of its opening fence, as Markdig numbers it.
    /// </summary>
    private static string DiagramHarvest(string markdown)
    {
        var html = new StringBuilder();
        string[] lines = markdown.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            if (MermaidFence().IsMatch(lines[i]))
            {
                html.Append(
                    CultureInfo.InvariantCulture,
                    $"<pre class=\"mermaid\" data-src-line=\"{i}\" data-mq-diagram=\"d{i}\"></pre>");
            }
        }

        return html.ToString();
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PaulTechGuy.MQ.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root could not be found.");
    }

    [GeneratedRegex(@"^[\s>]*```mermaid\s*$")]
    private static partial Regex MermaidFence();
}
