// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Editing.Tests;

public class LinePrefixTests
{
    [Fact]
    public void Bullet_list_marks_every_line_the_selection_touches()
    {
        Edits.Run(Edits.Selection("one\ntwo", 0, 0, 1, 3), MarkdownEditCommand.BulletList)
            .ShouldBe("- one\n- two");
    }

    [Fact]
    public void Bullet_list_applied_twice_clears_it()
    {
        Edits.Run(Edits.Selection("- one\n- two", 0, 2, 1, 5), MarkdownEditCommand.BulletList)
            .ShouldBe("one\ntwo");
    }

    [Fact]
    public void A_part_marked_selection_gets_finished_rather_than_cleared()
    {
        Edits.Run(Edits.Selection("- one\ntwo", 0, 2, 1, 3), MarkdownEditCommand.BulletList)
            .ShouldBe("- one\n- two");
    }

    [Fact]
    public void Numbered_list_counts_from_one_down_the_selection()
    {
        Edits.Run(Edits.Selection("one\ntwo\nthree", 0, 0, 2, 5), MarkdownEditCommand.NumberedList)
            .ShouldBe("1. one\n2. two\n3. three");
    }

    [Fact]
    public void Blank_lines_inside_a_selection_stay_blank()
    {
        // They separate blocks; numbering them would build a list out of the gaps.
        Edits.Run(Edits.Selection("one\n\ntwo", 0, 0, 2, 3), MarkdownEditCommand.BulletList)
            .ShouldBe("- one\n\n- two");
    }

    [Fact]
    public void Indentation_survives()
    {
        Edits.Run(Edits.Caret("    one", 0, 5), MarkdownEditCommand.BulletList)
            .ShouldBe("    - one");
    }

    [Fact]
    public void Switching_list_style_replaces_the_marker_rather_than_stacking_one_on()
    {
        Edits.Run(Edits.Caret("- one", 0, 3), MarkdownEditCommand.NumberedList).ShouldBe("1. one");
        Edits.Run(Edits.Caret("1. one", 0, 4), MarkdownEditCommand.BulletList).ShouldBe("- one");
        Edits.Run(Edits.Caret("- one", 0, 3), MarkdownEditCommand.TaskList).ShouldBe("- [ ] one");
    }

    [Fact]
    public void Task_list_is_distinct_from_a_plain_bullet_when_toggling()
    {
        // A task item is not a plain bullet, so asking for a bullet converts rather than
        // clearing, and asking for a task twice clears.
        Edits.Run(Edits.Caret("- [ ] one", 0, 7), MarkdownEditCommand.BulletList).ShouldBe("- one");
        Edits.Run(Edits.Caret("- [ ] one", 0, 7), MarkdownEditCommand.TaskList).ShouldBe("one");
    }

    [Fact]
    public void Blockquotes_nest_instead_of_being_rewritten()
    {
        Edits.Run(Edits.Caret("one", 0, 1), MarkdownEditCommand.Blockquote).ShouldBe("> one");
        Edits.Run(Edits.Caret("> one", 0, 3), MarkdownEditCommand.Blockquote).ShouldBe("one");
    }

    [Fact]
    public void A_heading_replaces_whatever_level_the_line_already_had()
    {
        Edits.Run(Edits.Caret("Title", 0, 0), MarkdownEditCommand.Heading2).ShouldBe("## Title");
        Edits.Run(Edits.Caret("# Title", 0, 3), MarkdownEditCommand.Heading2).ShouldBe("## Title");
    }

    [Fact]
    public void Asking_for_the_level_a_line_already_is_takes_the_heading_off()
    {
        Edits.Run(Edits.Caret("## Title", 0, 4), MarkdownEditCommand.Heading2).ShouldBe("Title");
    }

    [Fact]
    public void Heading_level_moves_up_and_down_within_one_to_six()
    {
        Edits.Run(Edits.Caret("# Title", 0, 3), MarkdownEditCommand.HeadingIncrease).ShouldBe("## Title");
        Edits.Run(Edits.Caret("## Title", 0, 4), MarkdownEditCommand.HeadingDecrease).ShouldBe("# Title");

        Edits.Run(Edits.Caret("###### Title", 0, 8), MarkdownEditCommand.HeadingIncrease)
            .ShouldBe("###### Title");
        Edits.Run(Edits.Caret("# Title", 0, 3), MarkdownEditCommand.HeadingDecrease)
            .ShouldBe("# Title");
    }

    [Fact]
    public void Increasing_from_plain_text_makes_a_heading_but_decreasing_does_not()
    {
        Edits.Run(Edits.Caret("Title", 0, 2), MarkdownEditCommand.HeadingIncrease).ShouldBe("# Title");
        Edits.Run(Edits.Caret("Title", 0, 2), MarkdownEditCommand.HeadingDecrease).ShouldBe("Title");
    }

    [Fact]
    public void A_command_in_an_empty_document_still_does_something()
    {
        Edits.Run(Edits.Caret(string.Empty, 0, 0), MarkdownEditCommand.BulletList).ShouldBe("- ");
    }
}
