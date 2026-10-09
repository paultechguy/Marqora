// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MdFootnote = Markdig.Extensions.Footnotes.Footnote;
using Markdig.Extensions.Mathematics;
using Markdig.Syntax;
using MdDocument = Markdig.Syntax.MarkdownDocument;
using Markdig.Syntax.Inlines;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Rendering;
using Shouldly;
using Xunit;
using MdTable = Markdig.Extensions.Tables.Table;
using OfficeMath = DocumentFormat.OpenXml.Math.OfficeMath;
using WordTable = DocumentFormat.OpenXml.Wordprocessing.Table;

namespace PaulTechGuy.MQ.Docx.Tests;

/// <summary>
/// The content census (docs/Export-Alignment-Plan.md, §9): nothing the document holds goes
/// missing from the Word export without the report saying so.
///
/// The fixture is counted twice - its footnotes, tables, list items, equations and diagrams
/// as Markdig reads them, which is what the preview and the PDF are drawn from, and the same
/// things in the .docx. Each count must agree, or the difference must be rows in the report.
/// A silent loss is the one thing this cannot let through: a missing table that the report
/// names is a known gap; one it does not name is a bug the reader finds.
/// </summary>
public sealed class ContentCensusTests
{
    [Fact]
    public async Task Nothing_the_fixture_holds_goes_missing_from_Word_unreported()
    {
        using ExportedDocument exported = await FixtureExportTests.ExportFixtureAsync(HeadingNumbering.Off);

        string markdown = await FixtureTextAsync();
        Census source = SourceCensus(markdown);
        Census word = WordCensus(exported.Path, exported.Issues, Pictures(markdown));

        source.ShouldBe(word, $"Markdig {source}; Word {word}; report: {string.Join(" | ", exported.Skipped)}");
    }

    /// <summary>
    /// The report's rows that stand for an equation or a diagram written as its source, and for
    /// a picture left out. Word's wording, from ExportReport and DocxImages.
    /// </summary>
    private static bool IsEquationRow(ExportIssue issue) =>
        issue.Problem.StartsWith("No equation from the preview", StringComparison.Ordinal)
            || issue.Problem.StartsWith("No Word form for", StringComparison.Ordinal)
            || issue.Problem.StartsWith("Could not be converted", StringComparison.Ordinal);

    private static bool IsDiagramRow(ExportIssue issue) =>
        issue.Problem.StartsWith("The diagram could not be drawn", StringComparison.Ordinal);

    private static bool IsPictureRow(ExportIssue issue) =>
        issue.Problem is "Not on this machine" or "Not found" or "Could not be read" or "Not a picture Word can show"
            || issue.Problem.StartsWith("The document has not been saved", StringComparison.Ordinal);

    /// <summary>What a document holds, counted the same way on both sides.</summary>
    private sealed record Census(int Footnotes, int Tables, int ListItems, int Equations, int Diagrams)
    {
        public override string ToString() =>
            $"footnotes {Footnotes}, tables {Tables}, list items {ListItems}, equations {Equations}, diagrams {Diagrams}";
    }

    private static Census SourceCensus(string markdown)
    {
        MdDocument document = Markdig.Markdown.Parse(markdown, MarqoraMarkdownPipeline.CreateBuilder().Build());

        return new Census(
            document.Descendants<MdFootnote>().Count(),
            document.Descendants<MdTable>().Count()
                + document.Descendants<HtmlBlock>().Count(html => html.Lines.ToString().TrimStart().StartsWith("<table", StringComparison.OrdinalIgnoreCase)),
            // A task item is written with its box as text and no list numbering - one Word
            // list level cannot hold a ticked box and an empty one - so it is counted as the
            // numbered paragraphs Word writes, which leave it out.
            document.Descendants<ListItemBlock>().Count(item => !IsTaskItem(item)),
            document.Descendants<MathBlock>().Count() + document.Descendants<MathInline>().Count(),
            document.Descendants<FencedCodeBlock>().Count(f => string.Equals(f.Info, "mermaid", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>An item whose first paragraph opens with a task box, as Word's NumberingPlan reads one.</summary>
    private static bool IsTaskItem(ListItemBlock item) =>
        item.FirstOrDefault() is ParagraphBlock { Inline.FirstChild: Markdig.Extensions.TaskLists.TaskList };

    /// <summary>
    /// What Word holds, the gaps its report names counted in: an equation or a diagram written
    /// as its source is there, said to be, and so is counted; the pictures a drawing count
    /// includes are taken back out of it, leaving the diagrams.
    /// </summary>
    /// <param name="pictures">The pictures the source names, embedded or reported.</param>
    private static Census WordCensus(string path, IReadOnlyList<ExportIssue> report, int pictures)
    {
        using WordprocessingDocument file = WordprocessingDocument.Open(path, false);

        MainDocumentPart main = file.MainDocumentPart!;
        Body body = main.Document!.Body!;
        Footnotes? notes = main.FootnotesPart?.Footnotes;

        // Word's own two notes, the separator and the continuation, have ids below one.
        int footnotes = notes?.Elements<Footnote>().Count(n => n.Id is { } id && id.Value > 0) ?? 0;

        IEnumerable<DocumentFormat.OpenXml.OpenXmlElement> everywhere =
            notes is null ? [body] : [body, notes];

        int embeddedPictures = pictures - report.Count(IsPictureRow);
        int drawings = everywhere.Sum(part => part.Descendants<Drawing>().Count());

        return new Census(
            footnotes,
            // A nested quote is written as a one-cell table captioned as a quote; it is the
            // quote's box, not one of the document's tables.
            everywhere.Sum(part => part.Descendants<WordTable>().Count(
                t => t.GetFirstChild<TableProperties>()?.GetFirstChild<TableCaption>()?.Val?.Value != "Block quote")),
            everywhere.Sum(part => part.Descendants<Paragraph>().Count(p => p.ParagraphProperties?.NumberingProperties is not null)),
            everywhere.Sum(part => part.Descendants<OfficeMath>().Count()) + report.Count(IsEquationRow),
            drawings - embeddedPictures + report.Count(IsDiagramRow));
    }

    /// <summary>
    /// The pictures the markdown names - image links, wherever they sit, and an
    /// <c>&lt;img&gt;</c> written inline as HTML, which Word now embeds or reports as a picture.
    /// </summary>
    private static int Pictures(string markdown)
    {
        MdDocument document =
            Markdig.Markdown.Parse(markdown, MarqoraMarkdownPipeline.CreateBuilder().Build());

        return document.Descendants<LinkInline>().Count(link => link.IsImage)
            + document.Descendants<HtmlInline>().Count(html =>
                html.Tag.StartsWith("<img", StringComparison.OrdinalIgnoreCase));
    }

    private static Task<string> FixtureTextAsync() =>
        File.ReadAllTextAsync(Repository.Root() + "/docs/UltimateMarkdownContent.md", TestContext.Current.CancellationToken);
}
