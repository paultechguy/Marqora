// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Markdown.Tests;

public class OrderedRunsTests
{
    private static IReadOnlyList<OrderedRuns.Run> Find(string document) =>
        OrderedRuns.Find(document.Split('\n'));

    [Fact]
    public void One_sequence_is_one_run()
    {
        IReadOnlyList<OrderedRuns.Run> runs = Find("1. Alpha\n2. Beta\n3. Gamma");

        runs.Count.ShouldBe(1);
        runs[0].Lines.ShouldBe([0, 1, 2]);
        runs[0].Numbers.ShouldBe([1, 2, 3]);
        runs[0].Indent.ShouldBe(0);
        runs[0].Delimiter.ShouldBe('.');
    }

    [Fact]
    public void A_nested_list_is_a_run_of_its_own()
    {
        IReadOnlyList<OrderedRuns.Run> runs = Find("1. Alpha\n   1. One\n   2. Two\n2. Beta");

        runs.Count.ShouldBe(2);
        runs[0].Lines.ShouldBe([0, 3]);
        runs[1].Lines.ShouldBe([1, 2]);
        runs[1].Indent.ShouldBe(3);
    }

    [Fact]
    public void One_blank_line_is_a_loose_list_and_two_end_it()
    {
        Find("1. Alpha\n\n2. Beta").Count.ShouldBe(1);
        Find("1. Alpha\n\n\n2. Beta").Count.ShouldBe(2);
    }

    [Fact]
    public void A_line_at_the_margin_ends_the_list_but_an_indented_one_does_not()
    {
        Find("1. Alpha\nprose\n2. Beta").Count.ShouldBe(2);
        Find("1. Alpha\n   more of Alpha\n2. Beta").Count.ShouldBe(1);
    }

    [Fact]
    public void Changing_the_delimiter_starts_a_new_list()
    {
        // CommonMark ends a list when "1." becomes "1)", however adjacent the two look.
        IReadOnlyList<OrderedRuns.Run> runs = Find("1. Alpha\n2) Beta");

        runs.Count.ShouldBe(2);
        runs[1].Delimiter.ShouldBe(')');
    }

    [Fact]
    public void A_number_that_jumps_after_a_blank_line_starts_a_new_list()
    {
        // The author beginning a second list, not losing count - so the sequence restarts from
        // whatever they wrote rather than being corrected into the first one.
        Find("1. Alpha\n\n7. Beta\n8. Gamma").Count.ShouldBe(2);
    }

    [Fact]
    public void A_run_where_every_number_agrees_is_the_shorthand()
    {
        Find("1. Alpha\n1. Beta\n1. Gamma")[0].Repeats.ShouldBeTrue();
        Find("1. Alpha\n2. Beta\n3. Gamma")[0].Repeats.ShouldBeFalse();
    }

    [Fact]
    public void A_run_half_fixed_by_hand_is_not_the_shorthand()
    {
        Find("1. Alpha\n1. Beta\n2. Gamma")[0].Repeats.ShouldBeFalse();
    }

    [Fact]
    public void A_run_of_one_item_is_never_the_shorthand()
    {
        // There is nothing for it to agree with. Reading it as the shorthand would freeze the
        // number of every single-entry list, including the one a freshly nested item starts.
        Find("1. Alpha")[0].Repeats.ShouldBeFalse();
    }

    [Fact]
    public void Numbers_inside_a_fenced_block_are_code_and_end_the_run()
    {
        string[] lines = "1. Alpha\n```\n2. not a list\n```\n3. Beta".Split('\n');
        IReadOnlyList<OrderedRuns.Run> runs =
            OrderedRuns.Find(lines, MarkdownRegionScanner.FindProtectedLines(lines));

        runs.Count.ShouldBe(2);
        runs[0].Lines.ShouldBe([0]);
        runs[1].Lines.ShouldBe([4]);
    }

    [Fact]
    public void A_bullet_list_holds_no_runs_at_all()
    {
        Find("- Alpha\n- Beta").ShouldBeEmpty();
    }
}
