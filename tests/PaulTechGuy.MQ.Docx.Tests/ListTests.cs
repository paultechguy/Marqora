// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Docx.Tests;

/// <summary>
/// Lists, and the one mistake that would make every document with two of them wrong.
///
/// Word keeps the shape of a list and its running count in separate places. Two markdown
/// lists that share a numbering instance are, as far as Word is concerned, a single list
/// interrupted by other content - so the second one carries on 4, 5, 6 instead of starting
/// again. Nothing about the file looks wrong until it is opened, and the first list is fine,
/// which is what makes it worth a test of its own.
/// </summary>
public partial class ListTests
{
    [Fact]
    public async Task Two_separate_ordered_lists_do_not_continue_each_other()
    {
        using var exported = await ExportedDocument.FromAsync(
            "1. one\n2. two\n\nA paragraph between them.\n\n1. one again\n2. two again\n");

        IReadOnlyList<string> ids = NumberingIds(exported.DocumentXml());

        ids.Distinct().Count().ShouldBe(2, "each markdown list needs its own numbering instance");
    }

    [Fact]
    public async Task A_bullet_list_is_numbered_as_a_bullet_list()
    {
        using var exported = await ExportedDocument.FromAsync("- one\n- two\n- three\n");

        string xml = exported.DocumentXml();

        xml.ShouldContain("numPr");
        xml.ShouldContain("ListParagraph");
        exported.NumberingXml().ShouldContain("bullet");
    }

    [Fact]
    public async Task An_ordered_list_that_starts_at_five_starts_at_five()
    {
        using var exported = await ExportedDocument.FromAsync("5. five\n6. six\n");

        string numbering = exported.NumberingXml();

        numbering.ShouldContain("startOverride");
        numbering.ShouldContain("w:val=\"5\"");
    }

    [Fact]
    public async Task Nested_lists_share_one_instance_and_step_down_a_level()
    {
        using var exported = await ExportedDocument.FromAsync(
            "- one\n    - nested\n        - deeper\n- two\n");

        string xml = exported.DocumentXml();

        // One list, three depths: the levels differ and the instance does not.
        NumberingIds(xml).Distinct().Count().ShouldBe(1);

        xml.ShouldContain("<w:ilvl w:val=\"0\" />");
        xml.ShouldContain("<w:ilvl w:val=\"1\" />");
        xml.ShouldContain("<w:ilvl w:val=\"2\" />");
    }

    [Fact]
    public async Task Alphabetic_and_roman_lists_keep_their_own_formats()
    {
        using var exported = await ExportedDocument.FromAsync(
            "a. first\nb. second\n\nText.\n\ni. one\nii. two\n");

        string numbering = exported.NumberingXml();

        numbering.ShouldContain("lowerLetter");
        numbering.ShouldContain("lowerRoman");
    }

    /// <summary>
    /// Word defines a list's marker once per level rather than per item, so a ticked box and
    /// an empty one cannot come from one definition. Task items are written as indented
    /// paragraphs with the box as text instead - which means they must carry no numbering at
    /// all, or Word would draw a bullet in front of every checkbox.
    /// </summary>
    [Fact]
    public async Task A_task_list_gets_boxes_rather_than_bullets()
    {
        using var exported = await ExportedDocument.FromAsync("- [x] done\n- [ ] waiting\n");

        string xml = exported.DocumentXml();

        xml.ShouldNotContain("numPr");
        exported.PlainText().ShouldContain("☒");
        exported.PlainText().ShouldContain("☐");
    }

    [Fact]
    public async Task A_tight_list_is_not_double_spaced()
    {
        using var exported = await ExportedDocument.FromAsync("- one\n- two\n");

        exported.DocumentXml().ShouldContain("contextualSpacing");
    }

    [Fact]
    public async Task A_loose_list_keeps_the_gaps_its_author_put_there()
    {
        using var exported = await ExportedDocument.FromAsync("- one\n\n- two\n");

        exported.DocumentXml().ShouldNotContain("contextualSpacing");
    }

    /// <summary>
    /// A second paragraph inside an item is continuation text: it lines up under the item but
    /// must not carry numbering of its own, or the list would count it as another item.
    /// </summary>
    [Fact]
    public async Task Continuation_text_inside_an_item_is_indented_but_not_numbered()
    {
        using var exported = await ExportedDocument.FromAsync(
            "1. first\n\n   more about first\n\n2. second\n");

        string xml = exported.DocumentXml();

        // Two items, two markers - not three.
        NumberingIds(xml).Count.ShouldBe(2);
        xml.ShouldContain("more about first");
    }

    [Fact]
    public async Task Lists_validate()
    {
        using var exported = await ExportedDocument.FromAsync(
            "- one\n    1. a\n    2. b\n- two\n\nText.\n\n3. three\n4. four\n");

        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// One ticked item does not cost the rest of the list its markers.
    ///
    /// This is the cheatsheet's own list, and the shape is easy to misread. The blank line
    /// before the ticked items does not start a second list - it only makes the existing one
    /// loose - so Fruit, Vegetables and the two checkboxes are siblings. Asking "is this a
    /// task list?" of the list rather than of the item answered yes, and the answer took the
    /// bullets off Fruit and Vegetables, flattened Apple and Pear up to their level, and did
    /// the same to Carrot and Leek underneath.
    /// </summary>
    [Fact]
    public async Task A_ticked_item_leaves_its_neighbours_markers_alone()
    {
        using var exported = await ExportedDocument.FromAsync(
            "- Fruit\n  - Apple\n  - Pear\n- Vegetables\n  1. Carrot\n  2. Leek\n\n"
            + "- [x] Ship the exporter\n- [ ] Write the cheatsheet\n");

        string xml = exported.DocumentXml();

        // Six items can be numbered and two cannot, so six markers and not none.
        CountOf(xml, "<w:numPr>").ShouldBe(6);

        // And the sublists are still sublists rather than six paragraphs at one indent.
        xml.ShouldContain("<w:ilvl w:val=\"1\" />");

        exported.PlainText().ShouldContain("☒");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_list_nested_under_a_ticked_item_keeps_its_numbering()
    {
        using var exported = await ExportedDocument.FromAsync(
            "- [x] done\n  1. first\n  2. second\n");

        string xml = exported.DocumentXml();

        // The two numbered lines are numbered; the ticked line above them is not.
        CountOf(xml, "<w:numPr>").ShouldBe(2);
        exported.NumberingXml().ShouldContain("decimal");
    }

    /// <summary>
    /// Word's own step is half an inch and the preview's is 1.6em, which is nearer a quarter.
    /// Half an inch put every list in the Word file visibly further in than the same list in
    /// the PDF beside it.
    /// </summary>
    [Fact]
    public async Task A_list_is_indented_about_as_far_as_the_preview_indents_it()
    {
        using var exported = await ExportedDocument.FromAsync("- one\n  - two\n");

        string numbering = exported.NumberingXml();

        numbering.ShouldContain("w:left=\"360\"");
        numbering.ShouldContain("w:left=\"720\"");

        // The style must not state a second, disagreeing indent of its own.
        StyleOf(exported.StylesXml(), "ListParagraph").ShouldNotContain("<w:ind ");
    }

    private static string StyleOf(string styles, string styleId)
    {
        int at = styles.IndexOf($"w:styleId=\"{styleId}\"", StringComparison.Ordinal);

        at.ShouldBeGreaterThan(-1, $"the styles part should define {styleId}");

        int end = styles.IndexOf("</w:style>", at, StringComparison.Ordinal);

        return end < 0 ? styles[at..] : styles[at..end];
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

    private static IReadOnlyList<string> NumberingIds(string xml) =>
        [.. NumberIdPattern().Matches(xml).Select(m => m.Groups[1].Value)];

    [GeneratedRegex("<w:numId w:val=\"(\\d+)\"")]
    private static partial Regex NumberIdPattern();
}
