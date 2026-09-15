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

    /// <summary>
    /// What one document is numbered with, which is the preference until a reader says
    /// otherwise about that document. The View menu's Heading Numbers item is this rule, and
    /// so is what a Word export or a Folio writes for that document.
    /// </summary>
    [Theory]
    [InlineData(HeadingNumbering.Off)]
    [InlineData(HeadingNumbering.FromHeading1)]
    [InlineData(HeadingNumbering.FromHeading2)]
    [InlineData(HeadingNumbering.FromHeading3)]
    public void A_document_nobody_has_spoken_for_follows_the_preference(HeadingNumbering preference)
    {
        HeadingNumbers.Effective(preference, null).ShouldBe(preference);
    }

    /// <summary>
    /// The case the whole thing exists for: someone else's document that writes its own
    /// section numbers into the heading text, read with the preference left switched on for
    /// every other document.
    /// </summary>
    [Theory]
    [InlineData(HeadingNumbering.FromHeading1)]
    [InlineData(HeadingNumbering.FromHeading2)]
    [InlineData(HeadingNumbering.FromHeading3)]
    public void A_document_stood_down_is_not_numbered_whatever_the_preference(HeadingNumbering preference)
    {
        HeadingNumbers.Effective(preference, false).ShouldBe(HeadingNumbering.Off);
    }

    /// <summary>
    /// Switched on for one document, it numbers from wherever the preference starts - so the
    /// document reads like every other one in the app rather than like a third setting.
    /// </summary>
    [Theory]
    [InlineData(HeadingNumbering.FromHeading1)]
    [InlineData(HeadingNumbering.FromHeading2)]
    [InlineData(HeadingNumbering.FromHeading3)]
    public void A_document_switched_on_borrows_the_level_from_the_preference(HeadingNumbering preference)
    {
        HeadingNumbers.Effective(preference, true).ShouldBe(preference);
    }

    /// <summary>
    /// The one level that cannot be borrowed. Off names no level, and a reader asking for
    /// numbers has to be given some, so the count starts at the first heading.
    /// </summary>
    [Fact]
    public void Switched_on_against_a_preference_of_off_numbers_from_the_first_level()
    {
        HeadingNumbers.Effective(HeadingNumbering.Off, true).ShouldBe(HeadingNumbering.FromHeading1);
    }
}
