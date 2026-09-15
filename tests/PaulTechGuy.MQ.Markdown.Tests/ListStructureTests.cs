// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Markdown.Tests;

public class ListStructureTests
{
    private static ListStructure.ListItem Read(string line)
    {
        ListStructure.TryRead(line, out ListStructure.ListItem item).ShouldBeTrue($"'{line}' should read as a list item");

        return item;
    }

    [Theory]
    [InlineData("- Alpha", 2)]
    [InlineData("* Alpha", 2)]
    [InlineData("+ Alpha", 2)]
    [InlineData("1. Alpha", 3)]
    [InlineData("1) Alpha", 3)]
    [InlineData("10. Alpha", 4)]
    [InlineData("100. Alpha", 5)]
    public void The_content_column_is_where_the_items_text_starts(string line, int column)
    {
        Read(line).ContentColumn.ShouldBe(column);
    }

    [Fact]
    public void An_indented_item_carries_its_indent_into_the_content_column()
    {
        Read("    - Alpha").ContentColumn.ShouldBe(6);
        Read("    - Alpha").IndentWidth.ShouldBe(4);
    }

    [Fact]
    public void A_task_box_is_content_rather_than_marker()
    {
        // GitHub draws a checkbox, but CommonMark reads "[ ] Alpha" as the item's text - so a
        // child nests against column 2, and anything past that is inside the paragraph.
        ListStructure.ListItem item = Read("- [ ] Alpha");

        item.ContentColumn.ShouldBe(2);
        item.TaskBox.ShouldBe("[ ] ");
        item.Content.ShouldBe("Alpha");
    }

    [Fact]
    public void An_over_wide_gap_falls_back_to_one_column_past_the_marker()
    {
        // Five spaces of gap starts an indented code block inside the item, so the content is
        // read as beginning right after the marker instead.
        Read("-     Alpha").ContentColumn.ShouldBe(2);
        Read("-    Alpha").ContentColumn.ShouldBe(5);
    }

    [Fact]
    public void An_empty_item_has_a_content_column_of_its_own()
    {
        Read("-").ContentColumn.ShouldBe(2);
        Read("- ").ContentColumn.ShouldBe(2);
    }

    [Theory]
    [InlineData("-foo")]
    [InlineData("1.foo")]
    [InlineData("Alpha")]
    [InlineData("")]
    [InlineData("# Heading")]
    public void Things_that_are_not_list_items(string line)
    {
        ListStructure.IsItem(line).ShouldBeFalse();
    }

    [Theory]
    [InlineData("- - -")]
    [InlineData("---")]
    [InlineData("***")]
    [InlineData("* * *")]
    [InlineData("___")]
    [InlineData("  - - - -")]
    public void A_thematic_break_is_not_a_list_item(string line)
    {
        // It matches every bullet pattern in the tree, and indenting one four columns turns a
        // rule into a code block.
        ListStructure.IsItem(line).ShouldBeFalse();
    }

    [Fact]
    public void An_ordered_item_reports_its_number_and_delimiter()
    {
        ListStructure.ListItem item = Read("  12) Alpha");

        item.Ordered.ShouldBeTrue();
        item.Number.ShouldBe(12);
        item.Delimiter.ShouldBe(')');
        item.Marker.ShouldBe("12)");
    }

    [Fact]
    public void A_bullet_reports_no_number()
    {
        Read("- Alpha").Ordered.ShouldBeFalse();
    }
}
