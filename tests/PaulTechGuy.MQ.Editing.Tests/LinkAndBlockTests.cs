// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Editing.Tests;

public class LinkAndBlockTests
{
    [Fact]
    public void Selected_text_becomes_the_label_and_the_destination_is_left_selected()
    {
        Edits.Run(Edits.Selection("see Marqora now", 0, 4, 0, 11), MarkdownEditCommand.Link)
            .ShouldBe("see [Marqora](url) now");

        Edits.Selected(Edits.Selection("see Marqora now", 0, 4, 0, 11), MarkdownEditCommand.Link)
            .ShouldBe("url");
    }

    [Fact]
    public void A_selected_url_becomes_the_destination_and_the_caret_goes_to_the_label()
    {
        Edits.Run(Edits.Selection("https://example.com", 0, 0, 0, 19), MarkdownEditCommand.Link)
            .ShouldBe("[](https://example.com)");

        // Empty selection, sitting between the brackets where the label has to be typed.
        Edits.Selected(Edits.Selection("https://example.com", 0, 0, 0, 19), MarkdownEditCommand.Link)
            .ShouldBe(string.Empty);
    }

    [Theory]
    [InlineData("www.example.com")]
    [InlineData("mailto:paul@example.com")]
    [InlineData("ftp://files.example.com")]
    public void Other_url_shapes_are_recognised_too(string url)
    {
        Edits.Run(Edits.Selection(url, 0, 0, 0, url.Length), MarkdownEditCommand.Link)
            .ShouldBe($"[]({url})");
    }

    [Fact]
    public void An_empty_selection_leaves_an_empty_link_to_fill_in()
    {
        Edits.Run(Edits.Caret(string.Empty, 0, 0), MarkdownEditCommand.Link).ShouldBe("[]()");
    }

    [Fact]
    public void A_table_arrives_with_the_first_heading_selected()
    {
        Edits.Run(Edits.Caret(string.Empty, 0, 0), MarkdownEditCommand.Table)
            .ShouldBe("| Column 1 | Column 2 | Column 3 |\n| --- | --- | --- |\n|  |  |  |\n");

        Edits.Selected(Edits.Caret(string.Empty, 0, 0), MarkdownEditCommand.Table)
            .ShouldBe("Column 1");
    }

    [Fact]
    public void A_block_gets_a_blank_line_between_it_and_the_text_it_lands_on()
    {
        Edits.Run(Edits.Caret("Some prose", 0, 0), MarkdownEditCommand.HorizontalRule)
            .ShouldBe("---\n\nSome prose");
    }

    [Fact]
    public void A_block_gets_a_blank_line_above_it_as_well_when_it_needs_one()
    {
        Edits.Run(Edits.Caret("Above\nBelow", 1, 0), MarkdownEditCommand.HorizontalRule)
            .ShouldBe("Above\n\n---\n\nBelow");
    }

    [Fact]
    public void A_block_landing_on_a_blank_line_does_not_add_more_air()
    {
        Edits.Run(Edits.Caret("Above\n\nBelow", 1, 0), MarkdownEditCommand.HorizontalRule)
            .ShouldBe("Above\n\n---\n\nBelow");
    }

    [Fact]
    public void An_empty_code_fence_leaves_the_caret_on_the_line_between_the_rails()
    {
        Edits.Run(Edits.Caret(string.Empty, 0, 0), MarkdownEditCommand.CodeBlock)
            .ShouldBe("```\n\n```\n");
    }

    [Fact]
    public void A_code_fence_wraps_whatever_is_selected()
    {
        Edits.Run(Edits.Selection("var x = 1;\nvar y = 2;", 0, 0, 1, 10), MarkdownEditCommand.CodeBlock)
            .ShouldBe("```\nvar x = 1;\nvar y = 2;\n```");
    }
}
