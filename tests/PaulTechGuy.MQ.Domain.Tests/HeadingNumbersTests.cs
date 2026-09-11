// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

/// <summary>
/// The numbering rule, level by level.
///
/// These are here rather than in the renderer's tests because none of it is about markdown:
/// given the levels in order, the numbers follow, and the two places that show them - the
/// preview and the outline panel - both ask this one function so they cannot disagree.
/// </summary>
public class HeadingNumbersTests
{
    private static IReadOnlyList<string> Number(HeadingNumbering numbering, params int[] levels) =>
        HeadingNumbers.Compute(levels, numbering);

    [Fact]
    public void Off_numbers_nothing()
    {
        Number(HeadingNumbering.Off, 1, 2, 3).ShouldBe(["", "", ""]);
    }

    [Fact]
    public void A_document_with_no_headings_is_answered_with_nothing()
    {
        HeadingNumbers.Compute([], HeadingNumbering.FromHeading1).ShouldBeEmpty();
    }

    /// <summary>The ordinary case, and the one a reader will check by eye.</summary>
    [Fact]
    public void Each_level_counts_within_the_one_above_it()
    {
        Number(HeadingNumbering.FromHeading1, 1, 2, 3, 2, 1)
            .ShouldBe(["1", "1.1", "1.1.1", "1.2", "2"]);
    }

    /// <summary>
    /// The half a CSS counter chain could not express, and the reason the numbers used to
    /// run on across a document's chapters: a heading above the numbered range is not
    /// numbered itself, but it still starts everything below it again.
    /// </summary>
    [Fact]
    public void A_heading_above_the_range_restarts_the_levels_beneath_it()
    {
        Number(HeadingNumbering.FromHeading2, 1, 2, 3, 2, 1, 2)
            .ShouldBe(["", "1", "1.1", "2", "", "1"]);
    }

    /// <summary>
    /// A document that opens deeper than the count starts - notes that are all "###" - reads
    /// as 1, 2, 3 rather than 0.0.1, because leading zeros are dropped.
    /// </summary>
    [Fact]
    public void A_document_that_never_uses_the_top_levels_still_counts_from_one()
    {
        Number(HeadingNumbering.FromHeading1, 3, 3, 3).ShouldBe(["1", "2", "3"]);
    }

    /// <summary>
    /// A level skipped in the middle of a chain keeps its zero: under "2" a "###" with no
    /// "##" before it is "2.0.1". Asserted rather than left to chance because it is what the
    /// preview has always shown - only a leading zero was ever dropped - and this rule is
    /// now the only copy of that behavior.
    /// </summary>
    [Fact]
    public void A_level_skipped_mid_chain_keeps_its_place()
    {
        Number(HeadingNumbering.FromHeading1, 1, 3).ShouldBe(["1", "1.0.1"]);
    }

    [Fact]
    public void The_count_can_start_at_the_third_level()
    {
        Number(HeadingNumbering.FromHeading3, 1, 2, 3, 4, 3, 2, 3)
            .ShouldBe(["", "", "1", "1.1", "2", "", "1"]);
    }

    /// <summary>
    /// Six levels deep, which is as far as markdown goes and as far as the counters reach.
    /// </summary>
    [Fact]
    public void The_sixth_level_is_numbered_like_the_rest()
    {
        Number(HeadingNumbering.FromHeading1, 1, 2, 3, 4, 5, 6)
            .ShouldBe(["1", "1.1", "1.1.1", "1.1.1.1", "1.1.1.1.1", "1.1.1.1.1.1"]);
    }
}
