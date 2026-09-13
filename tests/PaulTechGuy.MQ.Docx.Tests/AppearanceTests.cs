// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using DocumentFormat.OpenXml.Packaging;
using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

// By name only: the Wordprocessing namespace has a PageMargin of its own, and importing it
// whole would make this file's PageMargin - the export setting - ambiguous.
using Hyperlink = DocumentFormat.OpenXml.Wordprocessing.Hyperlink;
using Paragraph = DocumentFormat.OpenXml.Wordprocessing.Paragraph;
using RunProperties = DocumentFormat.OpenXml.Wordprocessing.RunProperties;
using Text = DocumentFormat.OpenXml.Wordprocessing.Text;

namespace PaulTechGuy.MQ.Docx.Tests;

/// <summary>
/// How the document looks once Word opens it, as distinct from what it contains.
///
/// Everything here came out of reading a real export rather than out of the schema: a contents
/// list in the wrong face, margins that did not mean what their names said, code with nothing
/// under it. None of it makes a file invalid, and none of it would ever have failed a test
/// about structure.
/// </summary>
public class AppearanceTests
{
    /// <summary>
    /// A contents field builds its entries in the TOC styles. When the file does not define
    /// those, the entries end up wearing the formatting of the headings they came from - so the
    /// contents list comes out bold, in the heading face, a different size on every line.
    /// </summary>
    [Fact]
    public async Task Contents_entries_are_ordinary_body_text()
    {
        using var exported = await ExportedDocument.FromAsync(
            "# One\n\n## Two\n\n### Three\n",
            new DocxExportSetup { IncludeTableOfContents = true });

        string styles = exported.StylesXml();

        styles.ShouldContain("w:styleId=\"TOC1\"");
        styles.ShouldContain("w:styleId=\"TOC2\"");
        styles.ShouldContain("w:styleId=\"TOC3\"");

        // Based on Normal, in the body face and the body size - not the heading's.
        styles.ShouldContain("toc 1");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// The other half of the same defect, and the half the TOC styles cannot reach.
    ///
    /// A contents entry is not just the heading's text: Word copies the heading's direct
    /// character formatting into it as well, and direct formatting beats the style of the line
    /// it lands in. So a heading holding inline code or a bold word put a shaded monospaced
    /// word - or a bold one - in the middle of an otherwise ordinary contents line, with no
    /// switch on the field to say otherwise.
    ///
    /// A heading is therefore written plain and dressed by its style, which is what a heading
    /// in Word is anyway. The paragraph underneath is the control: the same markdown keeps
    /// everything it asked for, so this is a rule about headings and not a loss everywhere.
    /// </summary>
    [Fact]
    public async Task A_headings_own_emphasis_is_not_written_so_its_contents_entry_is_plain()
    {
        const string Line = "Run `--check` with **care**, ==stay== *sharp* "
            + "and [read the docs](https://example.com)";

        using var exported = await ExportedDocument.FromAsync(
            $"# {Line}\n\n{Line}.\n",
            new DocxExportSetup { IncludeTableOfContents = true });

        using WordprocessingDocument file = WordprocessingDocument.Open(exported.Path, false);

        Paragraph heading = file.MainDocumentPart!.Document!.Body!
            .Descendants<Paragraph>()
            .First(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value == "Heading1");

        heading.Descendants<RunProperties>().ShouldBeEmpty();

        string text = string.Concat(heading.Descendants<Text>().Select(t => t.Text));

        text.ShouldBe("Run --check with care, stay sharp and read the docs");

        // The link still links. It is the blue underline that goes, with everything else the
        // contents field would have copied.
        heading.Descendants<Hyperlink>().ShouldNotBeEmpty();

        string xml = exported.DocumentXml();

        xml.ShouldContain("MarqoraCodeChar");
        xml.ShouldContain("MarqoraMark");
        xml.ShouldContain("<w:b ");
        xml.ShouldContain("<w:i ");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// Word's Normal is an inch, and so is the export's. It was half of one - borrowed from a
    /// PDF dialog that meant something else by the word - until somebody opened the result next
    /// to a real Word document and saw the difference.
    /// </summary>
    [Fact]
    public async Task Normal_margins_are_the_inch_Word_means_by_the_word()
    {
        using var exported = await ExportedDocument.FromAsync("Text.\n");

        string xml = exported.DocumentXml();

        xml.ShouldContain("w:top=\"1440\"");
        xml.ShouldContain("w:left=\"1440\"");
        xml.ShouldContain("w:right=\"1440\"");
    }

    /// <summary>
    /// Two of Word's presets change the measure without changing the page, which a single
    /// measurement cannot express.
    /// </summary>
    [Theory]
    [InlineData(PageMargin.Narrow, "720", "720")]
    [InlineData(PageMargin.Moderate, "1440", "1080")]
    [InlineData(PageMargin.Wide, "1440", "2880")]
    public async Task Words_presets_can_differ_top_from_side(
        PageMargin margin,
        string vertical,
        string horizontal)
    {
        using var exported = await ExportedDocument.FromAsync(
            "Text.\n",
            new DocxExportSetup { Margin = margin });

        string xml = exported.DocumentXml();

        xml.ShouldContain($"w:top=\"{vertical}\"");
        xml.ShouldContain($"w:left=\"{horizontal}\"");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// The two dialogs now mean the same thing by every preset, so carrying the margin across
    /// would no longer change the page - and it is still not carried, because a document meant
    /// to be edited and one meant to be printed want different margins. Pinned so that the
    /// answer stays a decision rather than becoming an accident of the enums matching.
    /// </summary>
    [Fact]
    public void The_margin_is_not_seeded_from_the_PDF_setup()
    {
        DocxExportSetup seeded = DocxExportSetup.SeededFrom(
            new PdfPageSetup { Paper = PaperSize.A4, Margin = PageMargin.Narrow });

        seeded.Paper.ShouldBe(PaperSize.A4);
        seeded.Margin.ShouldBe(PageMargin.Normal);
    }

    [Fact]
    public async Task The_page_number_sits_against_the_outer_margin()
    {
        using var exported = await ExportedDocument.FromAsync("Text.\n");

        using WordprocessingDocument file = WordprocessingDocument.Open(exported.Path, false);

        file.MainDocumentPart!.FooterParts.First().Footer!.OuterXml
            .ShouldContain("w:val=\"right\"");
    }

    /// <summary>
    /// Contextual spacing is what tells these apart: Word drops the gap between paragraphs of
    /// the same style, so the lines of a fence sit tight and the prose after the last one still
    /// gets its air. Set to zero, the next paragraph began immediately under the box.
    /// </summary>
    [Fact]
    public async Task A_code_block_has_air_under_it_but_not_between_its_lines()
    {
        using var exported = await ExportedDocument.FromAsync(
            "Before.\n\n```js\nvar a = 1;\nvar b = 2;\n```\n\nAfter.\n");

        string styles = exported.StylesXml();

        styles.ShouldContain("MarqoraCode");
        styles.ShouldContain("contextualSpacing");
        styles.ShouldNotContain("w:after=\"0\" w:line=\"264\"");
    }

    /// <summary>
    /// Inline code is bold and a fence is not, which is a deliberate asymmetry rather than an
    /// oversight. A word of code inside a sentence has to hold its own against the prose
    /// around it; thirty lines in a shaded, bordered box already stand apart, and setting all
    /// of them bold makes a listing heavy to read.
    /// </summary>
    [Fact]
    public async Task Inline_code_is_bold_and_a_fence_is_not()
    {
        using var exported = await ExportedDocument.FromAsync(
            "A `snippet` inline.\n\n```js\nvar a = 1;\n```\n");

        string styles = exported.StylesXml();

        StyleOf(styles, "MarqoraCodeChar").ShouldContain("<w:b ");
        StyleOf(styles, "MarqoraCode").ShouldNotContain("<w:b ");

        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// One style definition out of the styles part, so an assertion about one cannot be
    /// satisfied by another that happens to sit beside it.
    /// </summary>
    /// <summary>
    /// Two fences in a row stay two boxes.
    ///
    /// Word draws one frame around consecutive paragraphs that carry identical borders, and
    /// that is exactly what makes the lines of a single fence read as one box. It cannot tell
    /// where one fence ends and the next begins, so two back to back - the shape the
    /// cheatsheet uses to show a fence inside a fence - came out welded into a single box,
    /// the second half with no edge above it and no air. An empty paragraph between them
    /// carries no borders to match and breaks the group.
    /// </summary>
    [Fact]
    public async Task Two_fences_in_a_row_stay_two_boxes()
    {
        using var exported = await ExportedDocument.FromAsync(
            "```text\none\n```\n\n```text\ntwo\n```\n");

        string xml = exported.DocumentXml();

        // One seam per fence, and nothing else in a document sets an exact line height.
        CountOf(xml, "w:lineRule=\"exact\"").ShouldBe(2);
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// Eight points of background on every side of the code.
    ///
    /// The vertical figure was briefly twelve - about one line of the code face, matching the
    /// preview, whose fences are padded 1em - and came back to eight as too much air. Pinned
    /// here because a figure that has moved twice is one somebody will move again by accident.
    /// </summary>
    [Fact]
    public async Task A_fence_is_padded_on_every_side()
    {
        using var exported = await ExportedDocument.FromAsync("```js\nvar a = 1;\n```\n");

        string style = StyleOf(exported.StylesXml(), "MarqoraCode");

        CountOf(style, "w:space=\"8\"").ShouldBe(4);
    }

    private static int CountOf(string haystack, string needle)
    {
        int count = 0;
        int at = 0;

        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }

    private static string StyleOf(string styles, string styleId)
    {
        int at = styles.IndexOf($"w:styleId=\"{styleId}\"", StringComparison.Ordinal);

        at.ShouldBeGreaterThan(-1, $"the styles part should define {styleId}");

        int end = styles.IndexOf("</w:style>", at, StringComparison.Ordinal);

        return end < 0 ? styles[at..] : styles[at..end];
    }

    /// <summary>
    /// Word's own default, and what a new Word document uses. Already correct before the
    /// appearance pass; pinned here so it stays that way.
    /// </summary>
    [Fact]
    public async Task A_paragraph_has_eight_points_under_it()
    {
        using var exported = await ExportedDocument.FromAsync("One.\n\nTwo.\n");

        // 160 twentieths of a point is 8pt.
        exported.StylesXml().ShouldContain("w:after=\"160\"");
    }
}
