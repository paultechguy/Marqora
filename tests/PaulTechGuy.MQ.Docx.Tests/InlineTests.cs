// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Docx.Tests;

/// <summary>
/// What the things inside a paragraph become.
///
/// The space-preservation test is the one that catches a whole class of damage. XML drops
/// leading and trailing whitespace in an element unless told not to, and markdown produces
/// runs that are nothing but a space constantly - every emphasised word in the middle of a
/// sentence leaves one either side. Without the flag the document reads "onetwothree" and the
/// cause is not obvious from looking at it.
/// </summary>
public class InlineTests
{
    [Fact]
    public async Task Strong_and_emphasis_are_bold_and_italic()
    {
        using var exported = await ExportedDocument.FromAsync("**bold** and *italic*\n");

        string xml = exported.DocumentXml();

        xml.ShouldContain("<w:b ");
        xml.ShouldContain("<w:i ");
    }

    [Fact]
    public async Task Nested_emphasis_produces_one_run_wearing_both()
    {
        using var exported = await ExportedDocument.FromAsync("**bold _and italic_**\n");

        string xml = exported.DocumentXml();

        // Markdown nests what Word flattens. If the walker emitted as it descended rather
        // than carrying the format down, "and italic" would be italic and not bold.
        xml.ShouldContain("and italic");
        xml.ShouldContain("<w:b ");
        xml.ShouldContain("<w:i ");
    }

    [Fact]
    public async Task The_emphasis_extras_each_map_to_something_Word_has()
    {
        using var exported = await ExportedDocument.FromAsync(
            "~~struck~~ and ==marked== and H~2~O and x^2^ and ++inserted++\n");

        string xml = exported.DocumentXml();

        xml.ShouldContain("<w:strike");
        xml.ShouldContain("MarqoraMark");
        xml.ShouldContain("subscript");
        xml.ShouldContain("superscript");
        xml.ShouldContain("<w:u ");
    }

    [Fact]
    public async Task Spaces_around_emphasis_survive()
    {
        using var exported = await ExportedDocument.FromAsync("one **two** three\n");

        string xml = exported.DocumentXml();

        // Every text element needs the preserve flag, not just the ones that look like they
        // need it: the run holding " three " is the one that loses its spaces.
        xml.ShouldContain("xml:space=\"preserve\"");
        exported.PlainText().ShouldBe("one two three");
    }

    [Fact]
    public async Task Inline_code_takes_the_code_character_style()
    {
        using var exported = await ExportedDocument.FromAsync("Call `Run()` first.\n");

        string xml = exported.DocumentXml();

        xml.ShouldContain("MarqoraCodeChar");
        xml.ShouldContain("Run()");
    }

    [Fact]
    public async Task An_external_link_becomes_a_relationship_rather_than_plain_text()
    {
        using var exported = await ExportedDocument.FromAsync(
            "See [the docs](https://example.com/page).\n");

        string xml = exported.DocumentXml();

        xml.ShouldContain("<w:hyperlink");
        xml.ShouldContain("Hyperlink");
        xml.ShouldContain("the docs");
    }

    [Fact]
    public async Task A_link_to_a_heading_in_this_document_becomes_an_anchor()
    {
        using var exported = await ExportedDocument.FromAsync(
            "# Why it exists\n\nSee [above](#why-it-exists).\n");

        string xml = exported.DocumentXml();

        xml.ShouldContain("w:anchor=");
        xml.ShouldNotContain("w:anchor=\"why-it-exists\"");
    }

    /// <summary>
    /// A forward link resolves only because anchors are collected in a pass before anything
    /// is written. Walking and writing in one pass would leave this link as plain text.
    /// </summary>
    [Fact]
    public async Task A_link_to_a_heading_further_down_still_resolves()
    {
        using var exported = await ExportedDocument.FromAsync(
            "See [below](#later-on).\n\n# Later on\n");

        exported.DocumentXml().ShouldContain("w:anchor=");
    }

    [Fact]
    public async Task A_link_to_a_heading_that_is_not_here_stays_as_text()
    {
        using var exported = await ExportedDocument.FromAsync("See [nowhere](#no-such-heading).\n");

        string xml = exported.DocumentXml();

        xml.ShouldContain("nowhere");
        xml.ShouldNotContain("w:anchor=");
    }

    [Fact]
    public async Task A_hard_break_is_a_break_and_a_soft_one_is_a_space()
    {
        using var exported = await ExportedDocument.FromAsync("one  \ntwo\nthree\n");

        string xml = exported.DocumentXml();

        xml.ShouldContain("<w:br");
        exported.PlainText().ShouldContain("two three");
    }

    [Fact]
    public async Task Task_list_items_carry_a_box_that_shows_whether_they_are_done()
    {
        using var exported = await ExportedDocument.FromAsync("- [x] done\n- [ ] not done\n");

        string text = exported.PlainText();

        text.ShouldContain("☒");
        text.ShouldContain("☐");
    }

    [Fact]
    public async Task Emoji_shortcodes_arrive_as_characters()
    {
        using var exported = await ExportedDocument.FromAsync("Ship it :rocket:\n");

        exported.PlainText().ShouldContain("🚀");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// A surrogate pair has to survive the invalid-character filter intact. Copying it half
    /// at a time would strip both halves, because neither is legal XML on its own.
    /// </summary>
    [Fact]
    public async Task Text_with_control_characters_is_cleaned_without_losing_emoji()
    {
        using var exported = await ExportedDocument.FromAsync("beforeafter 🚀\n");

        string text = exported.PlainText();

        text.ShouldContain("🚀");
        text.ShouldNotContain("");
        exported.ValidationErrors().ShouldBeEmpty();
    }
}
