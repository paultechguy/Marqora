// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Editing.Tests;

public class ListRenumberTests
{
    private const ListNumbering Zeros = ListNumbering.Zeros;
    private const ListNumbering Ones = ListNumbering.Ones;
    private const ListNumbering Sequential = ListNumbering.Sequential;

    private static string Doc(params string[] lines) => string.Join("\n", lines);

    // ------------------------------------------------------------ the three numberings

    [Fact]
    public void A_counted_list_becomes_all_ones()
    {
        Edits.Renumber(Edits.Caret(Doc("1. abcd", "2. defg", "3. hijk"), 1, 4), Ones)
            .ShouldBe(Doc("1. abcd", "1. defg", "1. hijk"));
    }

    [Fact]
    public void A_counted_list_becomes_all_zeros()
    {
        Edits.Renumber(Edits.Caret(Doc("1. abcd", "2. defg", "3. hijk"), 0, 0), Zeros)
            .ShouldBe(Doc("0. abcd", "0. defg", "0. hijk"));
    }

    [Fact]
    public void A_repeated_list_counts_up_from_one()
    {
        Edits.Renumber(Edits.Caret(Doc("0. abcd", "0. defg", "0. hijk"), 2, 4), Sequential)
            .ShouldBe(Doc("1. abcd", "2. defg", "3. hijk"));
    }

    [Fact]
    public void Sequential_starts_at_one_whatever_the_list_started_at()
    {
        Edits.Renumber(Edits.Caret(Doc("5. abcd", "6. defg"), 0, 0), Sequential)
            .ShouldBe(Doc("1. abcd", "2. defg"));
    }

    [Fact]
    public void The_delimiter_and_the_text_are_kept()
    {
        Edits.Renumber(Edits.Caret(Doc("1) abcd", "2)  [x] defg"), 0, 0), Ones)
            .ShouldBe(Doc("1) abcd", "1)  [x] defg"));
    }

    [Fact]
    public void A_list_already_numbered_that_way_is_left_alone()
    {
        EditContext context = Edits.Caret(Doc("1. abcd", "1. defg"), 0, 0);

        new MarkdownEditor().RenumberList(Ones, context).IsEmpty.ShouldBeTrue();
    }

    // ------------------------------------------------------------ which list

    [Fact]
    public void The_caret_picks_only_the_list_it_is_in()
    {
        Edits.Renumber(Edits.Caret(Doc("1. a", "2. b", "", "", "1. c", "2. d"), 4, 2), Ones)
            .ShouldBe(Doc("1. a", "2. b", "", "", "1. c", "1. d"));
    }

    [Fact]
    public void A_caret_on_a_continuation_line_picks_its_list()
    {
        Edits.Renumber(Edits.Caret(Doc("1. a", "   more of a", "2. b"), 1, 5), Ones)
            .ShouldBe(Doc("1. a", "   more of a", "1. b"));
    }

    [Fact]
    public void A_caret_on_the_blank_inside_a_loose_list_picks_it()
    {
        Edits.Renumber(Edits.Caret(Doc("1. a", "", "2. b"), 1, 0), Ones)
            .ShouldBe(Doc("1. a", "", "1. b"));
    }

    [Fact]
    public void A_caret_outside_any_list_describes_nothing()
    {
        Edits.DescribeList(Edits.Caret(Doc("Some text", "", "1. a", "2. b"), 0, 3)).ShouldBeNull();
    }

    [Fact]
    public void A_bullet_list_is_not_a_numbered_list()
    {
        Edits.DescribeList(Edits.Caret(Doc("- a", "- b"), 0, 2)).ShouldBeNull();
    }

    [Fact]
    public void Numbers_inside_a_fence_are_code()
    {
        Edits.DescribeList(Edits.Caret(Doc("```", "1. a", "2. b", "```"), 1, 2)).ShouldBeNull();
    }

    [Fact]
    public void A_caret_in_a_nested_list_renumbers_that_level_only()
    {
        Edits.Renumber(Edits.Caret(Doc("1. a", "   1. x", "   2. y", "2. b"), 2, 6), Zeros)
            .ShouldBe(Doc("1. a", "   1. x", "   2. y", "2. b"));

        Edits.Renumber(Edits.Caret(Doc("1. a", "", "   1. x", "   2. y", "2. b"), 3, 6), Zeros)
            .ShouldBe(Doc("1. a", "", "   0. x", "   0. y", "2. b"));
    }

    [Fact]
    public void A_caret_on_a_parent_item_leaves_its_sub_list_alone()
    {
        Edits.Renumber(Edits.Caret(Doc("1. a", "   1. x", "   2. y", "2. b"), 3, 3), Ones)
            .ShouldBe(Doc("1. a", "   1. x", "   2. y", "1. b"));
    }

    [Fact]
    public void A_selection_across_a_whole_list_picks_the_outer_level()
    {
        Edits.Renumber(Edits.Selection(Doc("1. a", "   1. x", "   2. y", "2. b"), 1, 0, 3, 4), Ones)
            .ShouldBe(Doc("1. a", "   1. x", "   2. y", "1. b"));
    }

    [Fact]
    public void A_selection_across_two_lists_renumbers_both()
    {
        Edits.Renumber(Edits.Selection(Doc("1. a", "2. b", "", "", "1. c", "2. d"), 0, 0, 5, 4), Ones)
            .ShouldBe(Doc("1. a", "1. b", "", "", "1. c", "1. d"));
    }

    [Fact]
    public void A_fence_inside_an_item_does_not_restart_the_count()
    {
        Edits.Renumber(
                Edits.Caret(Doc("1. a", "   ```", "   code", "   ```", "1. b", "1. c"), 5, 3),
                Sequential)
            .ShouldBe(Doc("1. a", "   ```", "   code", "   ```", "2. b", "3. c"));
    }

    // ------------------------------------------------------------ wider markers

    [Fact]
    public void A_wider_marker_carries_what_sits_under_its_item()
    {
        string[] items = [.. Enumerable.Range(0, 10).Select(i => $"1. item{i}")];
        string before = Doc([.. items[..9], "1. item9", "   1. child", "   more"]);
        string[] expected =
        [
            .. Enumerable.Range(0, 9).Select(i => $"{i + 1}. item{i}"),
            "10. item9",
            "    1. child",
            "    more",
        ];

        Edits.Renumber(Edits.Caret(before, 0, 0), Sequential).ShouldBe(Doc(expected));
    }

    [Fact]
    public void A_narrower_marker_leaves_its_children_where_they_are()
    {
        string[] items = [.. Enumerable.Range(1, 10).Select(i => $"{i}. item")];
        string before = Doc([.. items, "    1. child"]);
        string[] expected = [.. Enumerable.Repeat("1. item", 10), "    1. child"];

        Edits.Renumber(Edits.Caret(before, 0, 0), Ones).ShouldBe(Doc(expected));
    }

    // ------------------------------------------------------------ the prompt

    [Fact]
    public void A_counted_list_is_offered_the_shorthand()
    {
        Edits.DescribeList(Edits.Caret(Doc("1. a", "2. b", "3. c"), 0, 0))
            .ShouldBe(new OrderedListSummary(3, Ones, CanStartAtZero: true));
    }

    [Fact]
    public void A_repeated_list_is_offered_counting()
    {
        Edits.DescribeList(Edits.Caret(Doc("0. a", "0. b"), 0, 0))!.Suggested.ShouldBe(Sequential);
        Edits.DescribeList(Edits.Caret(Doc("1. a", "1. b"), 0, 0))!.Suggested.ShouldBe(Sequential);
    }

    [Fact]
    public void A_list_straight_under_text_cannot_start_at_zero()
    {
        // CommonMark lets a list interrupt a paragraph only when it starts at one; at zero these
        // lines would become more of the paragraph.
        EditContext context = Edits.Caret(Doc("Steps:", "1. a", "2. b"), 1, 3);

        Edits.DescribeList(context)!.CanStartAtZero.ShouldBeFalse();
        Edits.Renumber(context, Zeros).ShouldBe(Doc("Steps:", "1. a", "2. b"));
    }

    [Fact]
    public void A_sub_list_straight_under_its_parent_cannot_start_at_zero()
    {
        Edits.DescribeList(Edits.Caret(Doc("1. a", "   1. x", "   2. y"), 1, 6))!
            .CanStartAtZero.ShouldBeFalse();
    }

    [Fact]
    public void A_list_under_a_heading_or_a_blank_can_start_at_zero()
    {
        Edits.DescribeList(Edits.Caret(Doc("# Steps", "1. a", "2. b"), 1, 3))!.CanStartAtZero.ShouldBeTrue();
        Edits.DescribeList(Edits.Caret(Doc("Steps:", "", "1. a", "2. b"), 2, 3))!.CanStartAtZero.ShouldBeTrue();
    }
}
