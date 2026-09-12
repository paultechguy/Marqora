// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using DocumentFormat.OpenXml.Packaging;
using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Docx.Tests;

/// <summary>
/// The constructs Word has no equivalent for, and has to be shown how to draw: the five
/// callouts, footnotes, definition lists, figures and abbreviations.
///
/// The footnote tests are the strict ones. A footnotes part without its two separator notes
/// at ids -1 and 0 either draws no rule above the notes or makes Word offer to repair the
/// file, and neither failure names itself.
/// </summary>
public class CalloutAndNoteTests
{
    [Theory]
    [InlineData("NOTE", CalloutKind.Note)]
    [InlineData("TIP", CalloutKind.Tip)]
    [InlineData("IMPORTANT", CalloutKind.Important)]
    [InlineData("WARNING", CalloutKind.Warning)]
    [InlineData("CAUTION", CalloutKind.Caution)]
    public async Task Each_callout_gets_its_own_style_and_its_own_color(
        string marker,
        CalloutKind kind)
    {
        using var exported = await ExportedDocument.FromAsync(
            $"> [!{marker}]\n> Something worth saying.\n");

        string xml = exported.DocumentXml();

        xml.ShouldContain($"MarqoraCallout{kind}");
        exported.StylesXml().ShouldContain(CalloutColors.BarOf(kind));

        // The panel is the bar color over white, because Word's shading has no alpha.
        exported.StylesXml().ShouldContain(CalloutColors.FillOf(kind));
    }

    /// <summary>
    /// AlertBlock derives from QuoteBlock. Asked about the base type first, every callout in
    /// every document would come out as an ordinary block quote - which looks like a choice
    /// rather than a bug.
    /// </summary>
    [Fact]
    public async Task A_callout_is_not_a_block_quote()
    {
        using var exported = await ExportedDocument.FromAsync("> [!WARNING]\n> Mind the gap.\n");

        string xml = exported.DocumentXml();

        xml.ShouldContain("MarqoraCalloutWarning");
        xml.ShouldNotContain("w:val=\"Quote\"");
    }

    [Fact]
    public async Task A_callout_opens_with_its_name()
    {
        using var exported = await ExportedDocument.FromAsync("> [!TIP]\n> Try this.\n");

        string text = exported.PlainText();

        text.ShouldStartWith("Tip");
        text.ShouldContain("Try this.");
    }

    [Fact]
    public async Task A_callout_can_hold_more_than_a_paragraph()
    {
        using var exported = await ExportedDocument.FromAsync(
            "> [!NOTE]\n> First.\n>\n> - one\n> - two\n");

        exported.PlainText().ShouldContain("one");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_footnote_lands_in_the_footnotes_part()
    {
        using var exported = await ExportedDocument.FromAsync(
            "Text with a note.[^1]\n\n[^1]: The note itself.\n");

        exported.FootnotesXml().ShouldContain("The note itself.");
        exported.DocumentXml().ShouldContain("footnoteReference");

        // The body is at the foot of the page, not in the middle of the prose.
        exported.DocumentXml().ShouldNotContain("The note itself.");
    }

    /// <summary>
    /// Markdig moves footnote definitions to the end of the document rather than copying
    /// them, so a walker that skips the group loses every note it was given.
    /// </summary>
    [Fact]
    public async Task Footnote_bodies_are_not_dropped()
    {
        using var exported = await ExportedDocument.FromAsync(
            "One.[^a] Two.[^b]\n\n[^a]: First note.\n[^b]: Second note.\n");

        string footnotes = exported.FootnotesXml();

        footnotes.ShouldContain("First note.");
        footnotes.ShouldContain("Second note.");
    }

    [Fact]
    public async Task The_two_separator_notes_Word_requires_are_present()
    {
        using var exported = await ExportedDocument.FromAsync("Text.[^1]\n\n[^1]: Note.\n");

        string footnotes = exported.FootnotesXml();

        footnotes.ShouldContain("w:id=\"-1\"");
        footnotes.ShouldContain("w:id=\"0\"");
        footnotes.ShouldContain("separator");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_document_with_no_notes_gets_no_footnotes_part()
    {
        using var exported = await ExportedDocument.FromAsync("Just prose.\n");

        HasFootnotesPart(exported.Path).ShouldBeFalse();
        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_definition_list_becomes_a_term_and_an_indented_definition()
    {
        using var exported = await ExportedDocument.FromAsync(
            "Fence\n:   A block of code.\n\nCallout\n:   A box with a colored bar.\n");

        string xml = exported.DocumentXml();

        xml.ShouldContain("MarqoraTerm");
        xml.ShouldContain("MarqoraDefinition");
        exported.PlainText().ShouldContain("A block of code.");
    }

    /// <summary>
    /// The preview shows an abbreviation's expansion as a tooltip. A Word file has no hover,
    /// so the expansion goes in parentheses - once, on first use, or a page that says HTML
    /// eight times would explain it eight times.
    /// </summary>
    [Fact]
    public async Task An_abbreviation_is_expanded_on_first_use_only()
    {
        using var exported = await ExportedDocument.FromAsync(
            "*[HTML]: HyperText Markup Language\n\nHTML is a format. More HTML follows.\n");

        string text = exported.PlainText();

        text.ShouldContain("HyperText Markup Language");

        int expansions = text.Split("HyperText Markup Language").Length - 1;
        expansions.ShouldBe(1);
    }

    [Fact]
    public async Task Everything_here_validates_together()
    {
        using var exported = await ExportedDocument.FromAsync(
            "> [!CAUTION]\n> Careful.[^1]\n\n"
            + "Term\n:   Definition.\n\n"
            + "*[API]: Application Programming Interface\n\nThe API.\n\n"
            + "[^1]: A note inside a callout's reach.\n");

        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// A callout needs air under it, and the shading has to stop before the air starts.
    ///
    /// Word fills a shaded paragraph out to its borders, and where there is no border to stop
    /// at it fills on through the paragraph's own spacing. Edged only on the left, the panel
    /// therefore absorbed whatever gap was set beneath it, and the prose after a callout began
    /// hard against its bottom edge - while a code fence, bordered on four sides, had looked
    /// right all along. The top and bottom edges exist for that reason only: drawn in the fill
    /// color they are invisible as lines, and they give the shading somewhere to end.
    /// </summary>
    [Fact]
    public async Task A_callout_has_air_under_it_and_shading_that_stops_before_the_air()
    {
        using var exported = await ExportedDocument.FromAsync(
            "> [!CAUTION]\n> This one can hurt.\n\nPlain text after it.\n");

        string style = StyleOf(exported.StylesXml(), "MarqoraCalloutCaution");

        style.ShouldContain("<w:bottom ");
        style.ShouldContain("w:after=\"160\"");

        // And the gap still does not open up between the panel's own paragraphs.
        style.ShouldContain("contextualSpacing");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    private static string StyleOf(string styles, string styleId)
    {
        int at = styles.IndexOf($"w:styleId=\"{styleId}\"", StringComparison.Ordinal);

        at.ShouldBeGreaterThan(-1, $"the styles part should define {styleId}");

        int end = styles.IndexOf("</w:style>", at, StringComparison.Ordinal);

        return end < 0 ? styles[at..] : styles[at..end];
    }

    private static bool HasFootnotesPart(string path)
    {
        using WordprocessingDocument file = WordprocessingDocument.Open(path, false);

        return file.MainDocumentPart!.FootnotesPart is not null;
    }
}
