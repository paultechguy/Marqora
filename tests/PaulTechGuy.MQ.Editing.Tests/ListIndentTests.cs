// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Editing.Tests;

public class ListIndentTests
{
    private const MarkdownEditCommand In = MarkdownEditCommand.IncreaseIndent;
    private const MarkdownEditCommand Out = MarkdownEditCommand.DecreaseIndent;

    // ------------------------------------------------------------ the content column

    [Fact]
    public void A_bullet_child_lands_on_its_parents_content_column()
    {
        Edits.Run(Edits.Caret("- Alpha\n- Beta", 1, 4), In).ShouldBe("- Alpha\n  - Beta");
    }

    [Fact]
    public void An_ordered_child_lands_past_the_number_and_its_delimiter()
    {
        Edits.Run(Edits.Caret("1. Alpha\n2. Beta", 1, 5), In).ShouldBe("1. Alpha\n   1. Beta");
    }

    [Fact]
    public void A_wide_marker_pushes_its_children_further_out()
    {
        // "10. " is four wide where "1. " is three, and the child has to clear it.
        Edits.Run(Edits.Caret("10. Alpha\n11. Beta", 1, 6), In).ShouldBe("10. Alpha\n    1. Beta");
    }

    [Fact]
    public void A_task_box_is_content_rather_than_marker()
    {
        // The child nests against column 2, not against the far side of the box. At column 6 it
        // would be four past the content column, which renders as a code block inside the item.
        Edits.Run(Edits.Caret("- [ ] Alpha\n- [ ] Beta", 1, 8), In)
            .ShouldBe("- [ ] Alpha\n  - [ ] Beta");
    }

    [Fact]
    public void A_task_box_survives_the_move()
    {
        Edits.Run(Edits.Caret("- [x] Alpha\n- [x] Beta", 1, 8), In)
            .ShouldBe("- [x] Alpha\n  - [x] Beta");
    }

    [Fact]
    public void An_over_wide_gap_counts_as_one_column_past_the_marker()
    {
        // Five spaces after the marker starts an indented code block inside the item, so
        // CommonMark reads the content as beginning one column past the marker instead.
        Edits.Run(Edits.Caret("-     Alpha\n- Beta", 1, 4), In).ShouldBe("-     Alpha\n  - Beta");
    }

    // ------------------------------------------------------------ when nothing should happen

    [Fact]
    public void The_first_item_of_a_list_has_nothing_to_nest_under()
    {
        Edits.Run(Edits.Caret("- Alpha\n- Beta", 0, 4), In).ShouldBe("- Alpha\n- Beta");
    }

    [Fact]
    public void The_first_child_of_an_item_has_no_sibling_either()
    {
        Edits.Run(Edits.Caret("- Alpha\n  - Beta", 1, 6), In).ShouldBe("- Alpha\n  - Beta");
    }

    [Fact]
    public void A_plain_paragraph_is_left_alone()
    {
        // Indenting prose in markdown makes a code block, which is never what was meant.
        Edits.Run(Edits.Caret("Alpha\nBeta", 1, 2), In).ShouldBe("Alpha\nBeta");
    }

    [Fact]
    public void An_item_at_the_margin_cannot_go_further_left()
    {
        Edits.Run(Edits.Caret("- Alpha\n- Beta", 1, 4), Out).ShouldBe("- Alpha\n- Beta");
    }

    [Fact]
    public void A_list_inside_a_fenced_block_is_code_and_not_a_list()
    {
        const string doc = "```\n- Alpha\n- Beta\n```";

        Edits.Run(Edits.Caret(doc, 2, 4), In).ShouldBe(doc);
    }

    [Fact]
    public void A_thematic_break_is_not_a_list_item()
    {
        // "- - -" matches every bullet pattern in the tree. Indenting it four columns would turn
        // a rule into a code block.
        const string doc = "- Alpha\n- - -";

        Edits.Run(Edits.Caret(doc, 1, 3), In).ShouldBe(doc);
    }

    // ------------------------------------------------------------ what travels

    [Fact]
    public void An_items_children_travel_with_it()
    {
        Edits.Run(Edits.Caret("- Alpha\n- Beta\n  - One\n  - Two\n- Gamma", 1, 4), In)
            .ShouldBe("- Alpha\n  - Beta\n    - One\n    - Two\n- Gamma");
    }

    [Fact]
    public void An_indented_continuation_line_travels_too()
    {
        Edits.Run(Edits.Caret("- Alpha\n- Beta\n  more text\n- Gamma", 1, 4), In)
            .ShouldBe("- Alpha\n  - Beta\n    more text\n- Gamma");
    }

    [Fact]
    public void Blank_lines_inside_the_block_stay_blank()
    {
        // Padding one would leave a line of spaces, which the style checks flag.
        Edits.Run(Edits.Caret("- Alpha\n- Beta\n\n  - One\n- Gamma", 1, 4), In)
            .ShouldBe("- Alpha\n  - Beta\n\n    - One\n- Gamma");
    }

    [Fact]
    public void A_selection_shifts_as_one_block_and_keeps_its_shape()
    {
        Edits.Run(Edits.Selection("- Alpha\n- Beta\n  - One\n  - Two\n- Gamma", 1, 0, 3, 7), In)
            .ShouldBe("- Alpha\n  - Beta\n    - One\n    - Two\n- Gamma");
    }

    [Fact]
    public void A_whole_list_selected_nests_under_its_own_first_item()
    {
        // The commonest thing anyone tries, and it used to do nothing at all: a list's first item
        // has no sibling above it, and under a strict block shift that one stuck item set the
        // distance for everything. It is left behind instead, and the rest go under it.
        Edits.Run(Edits.Selection("- Alpha\n- Beta\n- Gamma", 0, 0, 2, 7), In)
            .ShouldBe("- Alpha\n  - Beta\n  - Gamma");
    }

    [Fact]
    public void A_whole_checkbox_list_nests_the_same_way()
    {
        // Task items are ordinary bullets with a box in their text, and behave as such.
        Edits.Run(Edits.Selection("- [x] Alpha\n- [x] Beta\n- [ ] Gamma", 0, 0, 2, 11), In)
            .ShouldBe("- [x] Alpha\n  - [x] Beta\n  - [ ] Gamma");
    }

    [Fact]
    public void Leaving_the_first_item_behind_still_moves_the_rest_as_one_block()
    {
        // A is stuck, so it is dropped and B and C shift together by what C needs. They keep
        // their relative depths, which is the whole point of moving as a block - B stays deeper
        // than C rather than being flattened level with it.
        Edits.Run(Edits.Selection("- A\n  - B\n- C", 0, 0, 2, 3), In)
            .ShouldBe("- A\n    - B\n  - C");
    }

    [Fact]
    public void A_selection_with_nowhere_at_all_to_go_still_does_nothing()
    {
        // Every item in turn is the first at its own level, so there is no sibling anywhere in
        // the selection to become a child of.
        const string doc = "- Alpha\n  - Beta\n    - Gamma";

        Edits.Run(Edits.Selection(doc, 0, 0, 2, 11), In).ShouldBe(doc);
    }

    [Fact]
    public void Pressing_again_on_a_nested_selection_does_not_build_a_staircase()
    {
        // Three items nested under A with the selection kept. B is A's first child and cannot go
        // deeper; leaving it behind and nesting C and D under it would turn a flat list into a
        // staircase one press at a time. The block cannot move as a block, so nothing moves.
        const string doc = "- [x] A\n  - [x] B\n  - [x] C\n  - [x] D\n- [x] E";

        Edits.Run(Edits.Selection(doc, 1, 0, 3, 9), In).ShouldBe(doc);
    }

    [Fact]
    public void A_whole_list_indented_twice_stops_after_the_first_press()
    {
        // The first press nests everything under the first item, because that item is the top of
        // its list with nothing above it. The second finds that item still stuck for the same
        // reason - but the next one is now a first child, and a first child is not left behind.
        const string doc = "- Alpha\n- Beta\n- Gamma";

        EditContext all = Edits.Selection(doc, 0, 0, 2, 7);
        const string once = "- Alpha\n  - Beta\n  - Gamma";

        Edits.Run(all, In).ShouldBe(once);
        Edits.Run(Edits.Then(all, In), In).ShouldBe(once);
    }

    [Fact]
    public void Nesting_siblings_under_a_first_child_is_still_possible_by_selecting_only_them()
    {
        // The capability is not lost, it is just not what a block selection asks for. Select the
        // items to move and leave the would-be parent out, and they nest under it.
        Edits.Run(Edits.Selection("- A\n  - B\n  - C\n  - D", 2, 0, 3, 5), In)
            .ShouldBe("- A\n  - B\n    - C\n    - D");
    }

    [Fact]
    public void A_parents_wrapped_text_is_not_the_end_of_the_list()
    {
        // The parent's continuation line sits at exactly its children's indent. The first press
        // nests everything under the parent; the second finds the parent's wrapped text directly
        // above the selection and must keep scanning to the parent itself, so that it knows the
        // top item is a first child and not the start of a list with nothing above it. Read as the
        // latter, the rest of the selection nested under the top item, a level per press.
        const string doc = "- Parent, line one\n  parent, line two\n- Alpha\n  alpha, line two\n- Beta\n  beta, line two";
        const string once = "- Parent, line one\n  parent, line two\n  - Alpha\n    alpha, line two\n  - Beta\n    beta, line two";

        EditContext selected = Edits.Selection(doc, 2, 0, 4, 6);

        Edits.Run(selected, In).ShouldBe(once);
        Edits.Run(Edits.Then(selected, In), In).ShouldBe(once);
    }

    [Fact]
    public void Decrease_reaches_a_nested_parent_past_its_lazy_continuation()
    {
        // The lazy line is indented less than the child but more than the margin. It is the
        // parent's text, and the child steps back to the parent's indent - not to the margin,
        // which is where stopping on that line used to send it.
        Edits.Run(Edits.Caret("- Outer\n  - Parent\n   lazy text\n    - Child", 3, 8), Out)
            .ShouldBe("- Outer\n  - Parent\n   lazy text\n  - Child");
    }

    // ------------------------------------------------------------ going back out

    [Fact]
    public void Decrease_snaps_back_to_the_ancestors_indent()
    {
        Edits.Run(Edits.Caret("- Alpha\n  - Beta", 1, 6), Out).ShouldBe("- Alpha\n- Beta");
    }

    [Fact]
    public void Decrease_with_no_ancestor_goes_to_the_margin()
    {
        Edits.Run(Edits.Caret("   - Alpha", 0, 7), Out).ShouldBe("- Alpha");
    }

    [Fact]
    public void A_whole_nest_flattens_one_level_per_press()
    {
        // The item at the margin cannot move, and must not stop the others moving. Selecting the
        // set once and pressing twice is what flattening has to cost - not one selection per
        // level.
        const string doc = "- Alpha\n  - Beta\n    - Gamma";

        EditContext all = Edits.Selection(doc, 0, 0, 2, 11);

        Edits.Run(all, Out).ShouldBe("- Alpha\n- Beta\n  - Gamma");
        Edits.Run(Edits.Then(all, Out), Out).ShouldBe("- Alpha\n- Beta\n- Gamma");
    }

    [Fact]
    public void An_item_at_the_margin_does_not_hold_the_rest_of_the_selection_back()
    {
        Edits.Run(Edits.Selection("- Alpha\n    - Beta", 0, 0, 1, 10), Out)
            .ShouldBe("- Alpha\n- Beta");
    }

    [Fact]
    public void A_selection_that_can_move_freely_still_keeps_its_shape()
    {
        // Nothing is clamped here, so every item finding its own ancestor works out to exactly
        // the shift the whole block would have made.
        Edits.Run(Edits.Selection("- Alpha\n  - Beta\n    - Gamma", 1, 0, 2, 11), Out)
            .ShouldBe("- Alpha\n- Beta\n  - Gamma");
    }

    [Fact]
    public void Unselected_children_travel_with_a_selected_parent_on_the_way_out()
    {
        // Dragging as far as Beta and dragging as far as Gamma give the same answer.
        Edits.Run(Edits.Selection("- Alpha\n  - Beta\n    - Gamma", 0, 0, 1, 8), Out)
            .ShouldBe("- Alpha\n- Beta\n  - Gamma");
    }

    [Fact]
    public void A_flat_list_is_left_alone_rather_than_renumbered()
    {
        // Nothing can move, so nothing may change - a press that does nothing must not quietly
        // rewrite the numbering of the list it was pressed in.
        const string doc = "1. Alpha\n3. Beta\n7. Gamma";

        Edits.Run(Edits.Selection(doc, 0, 0, 2, 8), Out).ShouldBe(doc);
    }

    [Fact]
    public void Increase_then_decrease_puts_the_text_back_exactly()
    {
        const string doc = "- Alpha\n- Beta\n  - One\n- Gamma";

        Edits.Run(Edits.Then(Edits.Caret(doc, 1, 4), In), Out).ShouldBe(doc);
    }

    [Fact]
    public void Increase_then_decrease_puts_an_ordered_list_back_exactly()
    {
        const string doc = "1. Alpha\n2. Beta\n3. Gamma";

        Edits.Run(Edits.Then(Edits.Caret(doc, 1, 5), In), Out).ShouldBe(doc);
    }

    [Fact]
    public void The_selection_moves_with_the_text()
    {
        // Without this the caret would be left where the word used to start.
        Edits.Selected(Edits.Selection("- Alpha\n- Beta", 1, 2, 1, 6), In).ShouldBe("Beta");
    }

    // ------------------------------------------------------------ numbering

    [Fact]
    public void The_new_nested_run_starts_again_at_one()
    {
        Edits.Run(Edits.Caret("1. Alpha\n2. Beta\n3. Gamma", 1, 5), In)
            .ShouldBe("1. Alpha\n   1. Beta\n2. Gamma");
    }

    [Fact]
    public void The_run_the_item_left_closes_its_gap()
    {
        Edits.Run(Edits.Caret("1. Alpha\n2. Beta\n3. Gamma\n4. Delta", 1, 5), In)
            .ShouldBe("1. Alpha\n   1. Beta\n2. Gamma\n3. Delta");
    }

    [Fact]
    public void A_run_that_repeats_one_number_is_left_as_it_was()
    {
        // The "1. 1. 1." shorthand is deliberate, and renumbering it would change what the
        // document says rather than tidy it.
        Edits.Run(Edits.Caret("1. Alpha\n1. Beta\n1. Gamma", 1, 5), In)
            .ShouldBe("1. Alpha\n   1. Beta\n1. Gamma");
    }

    [Fact]
    public void A_different_delimiter_is_a_different_list()
    {
        // CommonMark starts a new list when "1." becomes "1)", so Beta's new run is not merged
        // into Alpha's. Gamma keeps the number it was written with, because a run starts where
        // its author started it - which is where Format Document leaves it too.
        Edits.Run(Edits.Caret("1. Alpha\n1) Beta\n2) Gamma", 1, 5), In)
            .ShouldBe("1. Alpha\n   1) Beta\n2) Gamma");
    }

    [Fact]
    public void An_item_indented_into_an_existing_nested_run_carries_on_from_it()
    {
        // There is already a list at that depth, so this joins it rather than starting again.
        Edits.Run(Edits.Caret("1. Alpha\n   1. One\n2. Beta", 2, 5), In)
            .ShouldBe("1. Alpha\n   1. One\n   2. Beta");
    }

    [Fact]
    public void An_item_outdented_into_an_existing_run_carries_on_from_it_too()
    {
        Edits.Run(Edits.Caret("1. Alpha\n2. Beta\n   1. One", 2, 8), Out)
            .ShouldBe("1. Alpha\n2. Beta\n3. One");
    }

    [Fact]
    public void A_bullet_list_is_not_renumbered_into_anything()
    {
        Edits.Run(Edits.Caret("- Alpha\n- Beta\n- Gamma", 1, 4), In)
            .ShouldBe("- Alpha\n  - Beta\n- Gamma");
    }

    // ------------------------------------------------------------ the scope this needs

    [Fact]
    public void Indent_asks_for_the_whole_document_and_nothing_else_does()
    {
        // The parent, the subtree and the fences are all at unknown distances, so a window cannot
        // answer them - and a scan that runs off one cannot tell that from the end of the file.
        EditContextScopes.For(In).ShouldBe(EditContextScope.Document);
        EditContextScopes.For(Out).ShouldBe(EditContextScope.Document);
        EditContextScopes.For(MarkdownEditCommand.Bold).ShouldBe(EditContextScope.Selection);
        EditContextScopes.For(MarkdownEditCommand.BulletList).ShouldBe(EditContextScope.Selection);
    }
}
