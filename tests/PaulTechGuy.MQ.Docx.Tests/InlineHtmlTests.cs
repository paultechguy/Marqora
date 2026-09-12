// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Docx.Tests;

/// <summary>
/// The inline HTML a markdown document is allowed to contain.
///
/// Markdig hands these across as written - an opening tag, the text between, then a closing
/// tag, each its own node - and nothing was being done with them, so a key written as
/// <c>kbd</c> arrived in Word as body text. The formatting was gone and the content stayed,
/// which is the worse of the two ways to lose something: nothing looks missing.
///
/// What is covered is what Word has a form of. That is a limit of the target rather than a
/// choice: a Word run can be bold, italic, underlined, struck, raised, lowered, colored,
/// shaded and refaced, and that is the list. A tag asking for anything else keeps its content
/// and loses its formatting, which is the only honest thing left to do with it.
/// </summary>
public class InlineHtmlTests
{
    [Fact]
    public async Task A_key_is_set_like_code()
    {
        using var exported = await ExportedDocument.FromAsync("Press <kbd>Ctrl</kbd> to stop.\n");

        string xml = exported.DocumentXml();

        xml.ShouldContain("MarqoraCodeChar");
        exported.PlainText().ShouldContain("Ctrl");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task Only_what_is_between_the_tags_is_affected()
    {
        using var exported = await ExportedDocument.FromAsync("Press <kbd>Ctrl</kbd> to stop.\n");

        string text = exported.PlainText();

        text.ShouldBe("Press Ctrl to stop.");

        // The tags themselves are not content.
        text.ShouldNotContain("kbd");
    }

    [Theory]
    [InlineData("<b>x</b>", "<w:b ")]
    [InlineData("<strong>x</strong>", "<w:b ")]
    [InlineData("<i>x</i>", "<w:i ")]
    [InlineData("<em>x</em>", "<w:i ")]
    [InlineData("<u>x</u>", "<w:u ")]
    [InlineData("<s>x</s>", "<w:strike")]
    [InlineData("<del>x</del>", "<w:strike")]
    [InlineData("<sub>x</sub>", "subscript")]
    [InlineData("<sup>x</sup>", "superscript")]
    [InlineData("<mark>x</mark>", "MarqoraMark")]
    [InlineData("<code>x</code>", "MarqoraCodeChar")]
    public async Task Each_tag_becomes_the_Word_property_that_means_the_same(
        string markup,
        string expected)
    {
        using var exported = await ExportedDocument.FromAsync($"Text {markup} more.\n");

        exported.DocumentXml().ShouldContain(expected);
        exported.PlainText().ShouldContain("x");
    }

    [Fact]
    public async Task Markdown_and_HTML_asking_for_different_things_both_apply()
    {
        using var exported = await ExportedDocument.FromAsync("**<kbd>Ctrl</kbd>**\n");

        string xml = exported.DocumentXml();

        xml.ShouldContain("<w:b ");
        xml.ShouldContain("MarqoraCodeChar");
    }

    [Fact]
    public async Task Nested_tags_both_apply()
    {
        using var exported = await ExportedDocument.FromAsync("<b><i>both</i></b>\n");

        string xml = exported.DocumentXml();

        xml.ShouldContain("<w:b ");
        xml.ShouldContain("<w:i ");
        exported.PlainText().ShouldContain("both");
    }

    [Fact]
    public async Task A_line_break_tag_is_a_line_break()
    {
        using var exported = await ExportedDocument.FromAsync("One<br>Two\n");

        exported.DocumentXml().ShouldContain("<w:br");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// A span carries nothing on its own. Its style is the only thing that might, and only the
    /// parts of it Word has somewhere to put.
    /// </summary>
    [Fact]
    public async Task A_color_in_a_style_attribute_reaches_the_document()
    {
        using var exported = await ExportedDocument.FromAsync(
            "<span style=\"color: #b3261e\">warning</span>\n");

        exported.DocumentXml().ShouldContain("B3261E");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_short_hex_color_is_expanded()
    {
        using var exported = await ExportedDocument.FromAsync(
            "<span style=\"color:#abc\">x</span>\n");

        exported.DocumentXml().ShouldContain("AABBCC");
    }

    [Fact]
    public async Task A_background_color_becomes_shading()
    {
        using var exported = await ExportedDocument.FromAsync(
            "<span style=\"background-color: #fff3a3\">x</span>\n");

        exported.DocumentXml().ShouldContain("FFF3A3");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// A named color, or one written as a function, has no six-digit form to give Word. The
    /// text still arrives; only the color is lost.
    /// </summary>
    [Fact]
    public async Task A_color_Word_cannot_express_loses_the_color_and_keeps_the_text()
    {
        using var exported = await ExportedDocument.FromAsync(
            "<span style=\"color: rebeccapurple\">still here</span>\n");

        exported.PlainText().ShouldContain("still here");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// An unknown tag keeps its content. Dropping the text along with the formatting would
    /// lose something the author wrote, to no purpose.
    /// </summary>
    [Fact]
    public async Task An_unknown_tag_keeps_what_it_surrounds()
    {
        using var exported = await ExportedDocument.FromAsync(
            "<ruby>kanji<rt>reading</rt></ruby>\n");

        exported.PlainText().ShouldContain("kanji");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// A closing tag with nothing open must not unwind the stack past the bottom, and an
    /// opening tag never closed must not swallow the rest of the document.
    /// </summary>
    [Fact]
    public async Task Unbalanced_markup_does_not_break_the_document()
    {
        using var exported = await ExportedDocument.FromAsync(
            "Stray </b> close.\n\n<b>Never closed.\n\nAfter.\n");

        string text = exported.PlainText();

        text.ShouldContain("Stray");
        text.ShouldContain("After.");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task An_html_comment_is_not_mistaken_for_a_tag()
    {
        using var exported = await ExportedDocument.FromAsync("Before <!-- a note --> after.\n");

        string text = exported.PlainText();

        text.ShouldContain("Before");
        text.ShouldContain("after.");
        text.ShouldNotContain("a note");
    }
}
