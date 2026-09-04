// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

public class OutlineNavigationTests
{
    /// <summary>
    /// A document that opens with prose, so the "no heading owns this line yet" case is in
    /// every fixture rather than in one test that remembers to ask for it.
    /// </summary>
    private static readonly OutlineHeading[] Document =
    [
        Heading(1, "Introduction", 4),
        Heading(2, "Why it exists", 12),
        Heading(3, "A note on scope", 20),
        Heading(2, "What it is not", 31),
        Heading(1, "Reference", 48),
    ];

    private static OutlineHeading Heading(int level, string text, int line) => new()
    {
        Level = level,
        Text = text,
        Slug = text.ToLowerInvariant().Replace(' ', '-'),
        SourceLine = line,
    };

    // ------------------------------------------------------------ IndexOfHeadingAt

    [Fact]
    public void A_line_above_the_first_heading_belongs_to_no_heading()
    {
        OutlineNavigation.IndexOfHeadingAt(Document, 0).ShouldBe(-1);
        OutlineNavigation.IndexOfHeadingAt(Document, 3).ShouldBe(-1);
    }

    [Fact]
    public void An_empty_outline_answers_for_every_line()
    {
        OutlineNavigation.IndexOfHeadingAt([], 0).ShouldBe(-1);
        OutlineNavigation.IndexOfHeadingAt([], 5_000).ShouldBe(-1);
    }

    /// <summary>
    /// The heading's own line belongs to the heading, not to the one before it. Off by one
    /// here would put the caret in a section and light up its predecessor.
    /// </summary>
    [Theory]
    [InlineData(4, 0)]
    [InlineData(12, 1)]
    [InlineData(20, 2)]
    [InlineData(31, 3)]
    [InlineData(48, 4)]
    public void A_heading_owns_the_line_it_sits_on(int line, int expected) =>
        OutlineNavigation.IndexOfHeadingAt(Document, line).ShouldBe(expected);

    [Theory]
    [InlineData(5, 0)]
    [InlineData(11, 0)]
    [InlineData(13, 1)]
    [InlineData(19, 1)]
    [InlineData(30, 2)]
    [InlineData(47, 3)]
    [InlineData(49, 4)]
    public void A_line_belongs_to_the_last_heading_above_it(int line, int expected) =>
        OutlineNavigation.IndexOfHeadingAt(Document, line).ShouldBe(expected);

    /// <summary>Past the end of the document is still the final section, not nothing.</summary>
    [Fact]
    public void A_line_below_the_last_heading_belongs_to_it() =>
        OutlineNavigation.IndexOfHeadingAt(Document, 10_000).ShouldBe(4);

    [Fact]
    public void One_heading_owns_everything_from_its_line_on()
    {
        OutlineHeading[] single = [Heading(1, "Only", 7)];

        OutlineNavigation.IndexOfHeadingAt(single, 6).ShouldBe(-1);
        OutlineNavigation.IndexOfHeadingAt(single, 7).ShouldBe(0);
        OutlineNavigation.IndexOfHeadingAt(single, 8).ShouldBe(0);
    }

    // -------------------------------------------------------------------- Filter

    /// <summary>
    /// Nothing to filter hands back the same list, which is the common case and the one
    /// worth not allocating for.
    /// </summary>
    [Fact]
    public void An_unfiltered_outline_is_returned_as_it_is()
    {
        OutlineNavigation.Filter(Document, null, OutlineNavigation.UnlimitedDepth)
            .ShouldBeSameAs(Document);

        OutlineNavigation.Filter(Document, "   ", OutlineNavigation.UnlimitedDepth)
            .ShouldBeSameAs(Document);
    }

    [Fact]
    public void A_depth_limit_drops_the_levels_below_it()
    {
        IReadOnlyList<OutlineHeading> kept = OutlineNavigation.Filter(Document, null, 2);

        kept.Select(h => h.Text).ShouldBe(
            ["Introduction", "Why it exists", "What it is not", "Reference"]);
    }

    [Fact]
    public void A_depth_limit_of_one_keeps_only_the_top_level()
    {
        OutlineNavigation.Filter(Document, null, 1)
            .Select(h => h.Text)
            .ShouldBe(["Introduction", "Reference"]);
    }

    [Fact]
    public void The_filter_ignores_case()
    {
        OutlineNavigation.Filter(Document, "WHY", OutlineNavigation.UnlimitedDepth)
            .Select(h => h.Text)
            .ShouldBe(["Why it exists"]);
    }

    [Fact]
    public void The_filter_matches_anywhere_in_the_heading()
    {
        OutlineNavigation.Filter(Document, "exists", OutlineNavigation.UnlimitedDepth)
            .Select(h => h.Text)
            .ShouldBe(["Why it exists"]);
    }

    [Fact]
    public void Surrounding_space_is_not_part_of_the_term() =>
        OutlineNavigation.Filter(Document, "  Reference  ", OutlineNavigation.UnlimitedDepth)
            .Select(h => h.Text)
            .ShouldBe(["Reference"]);

    /// <summary>
    /// A parent is not kept to explain a matching child. The list is flat and has no
    /// expanders, so a row that did not match would be indistinguishable from one that did.
    /// </summary>
    [Fact]
    public void A_match_does_not_drag_its_parents_along() =>
        OutlineNavigation.Filter(Document, "scope", OutlineNavigation.UnlimitedDepth)
            .Select(h => h.Text)
            .ShouldBe(["A note on scope"]);

    [Fact]
    public void A_term_that_matches_nothing_gives_nothing() =>
        OutlineNavigation.Filter(Document, "kangaroo", OutlineNavigation.UnlimitedDepth)
            .ShouldBeEmpty();

    [Fact]
    public void The_depth_limit_and_the_term_both_apply()
    {
        // "A note on scope" carries the term but is an H3, so the limit rules it out.
        OutlineNavigation.Filter(Document, "o", 2)
            .Select(h => h.Text)
            .ShouldBe(["Introduction", "What it is not"]);
    }
}
