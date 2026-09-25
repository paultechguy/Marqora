// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Formatting.Tests;

/// <summary>
/// The rule that settles bold and italic on one pair of characters.
///
/// The case that prompted these is a blank-line placeholder, <c>**____**</c>, in a table. Two
/// faults met there: four underscores were read as a <c>__</c> delimiter with more after it,
/// and the match was free to run on through the pipe into the next cell. Together they
/// rewrote the row as <c>****__** | **__****</c>, which renders as stray asterisks.
///
/// Every test runs with the emphasis rule alone switched on, apart from the one that runs the
/// table formatter too, because that is the pairing the author actually had.
/// </summary>
public sealed class EmphasisTests
{
    private static readonly FormatOptions EmphasisOnly = new()
    {
        HeadingSpace = false,
        TrailingWhitespace = false,
        NormalizeMarkers = false,
        LineEndings = false,
        BlankLines = false,
        ListMarkerSpace = false,
        LinkSyntax = false,
        EofNewline = false,
        CollapseBlanks = false,
        OrderedNumbering = false,
        BlockquoteSpace = false,
        FormatTables = false,
        TidyCodeFences = false,
        SetextToAtx = false,
        UnifyEmphasis = true,
        ReflowParagraphs = false,
    };

    private static string Format(string markdown, EmphasisStyle style, bool tables = false) =>
        new MarkdownFormatter()
            .Format(markdown, EmphasisOnly with { Emphasis = style, FormatTables = tables })
            .Text.Replace("\r\n", "\n");

    private static string Lines(params string[] lines) => string.Join("\n", lines);

    // ------------------------------------------------------------ the reported table

    private static readonly string PhaseTable = Lines(
        "| Phase                          | Window                  | Status        | Started  | Completed |",
        "| ------------------------------ | ----------------------- | ------------- | -------- | --------- |",
        "| 0 — Instrument & Baseline      | 2026-09-22 → 2026-09-30 | ☐ Not started | **____** | **____**  |",
        "| 1 — Observe, jobs disabled     | 2026-10-01 → 2026-10-31 | ☐ Not started | **____** | **____**  |",
        "| 2 — Quiet period (6 mo)        | 2026-11-01 → 2027-05-01 | ☐ Not started | **____** | **____**  |",
        "| 3 — Final archive & deallocate | 2027-05-15 → 2027-05-29 | ☐ Not started | **____** | **____**  |",
        "| 4 — Deallocated soak           | 2027-05-29 → 2027-06-29 | ☐ Not started | **____** | **____**  |",
        "| 5 — Delete                     | 2027-06-29 → **____**   | ☐ Not started | **____** | **____**  |");

    [Theory]
    [InlineData(EmphasisStyle.Asterisk)]
    [InlineData(EmphasisStyle.Underscore)]
    public void A_placeholder_table_comes_back_as_it_was_written(EmphasisStyle style)
    {
        Format(PhaseTable, style, tables: true).ShouldBe(PhaseTable);
    }

    // ------------------------------------------------------------ delimiter runs

    [Theory]
    [InlineData("**____**")]
    [InlineData("sign here: **____** and date: **____**")]
    public void A_run_longer_than_the_delimiter_is_not_emphasis(string line)
    {
        Format(line, EmphasisStyle.Asterisk).ShouldBe(line);
        Format(line, EmphasisStyle.Underscore).ShouldBe(line);
    }

    [Fact]
    public void Underscores_inside_a_word_are_left_alone()
    {
        Format("snake__case__name", EmphasisStyle.Asterisk).ShouldBe("snake__case__name");
    }

    [Fact]
    public void Bold_is_still_converted_both_ways()
    {
        Format("a __bold__ word", EmphasisStyle.Asterisk).ShouldBe("a **bold** word");
        Format("a **bold** word", EmphasisStyle.Underscore).ShouldBe("a __bold__ word");
    }

    // ------------------------------------------------------------ table cells

    [Fact]
    public void Emphasis_does_not_reach_across_a_cell_boundary()
    {
        Format("| __a | b__ |", EmphasisStyle.Asterisk).ShouldBe("| __a | b__ |");
    }

    [Fact]
    public void Emphasis_inside_one_cell_is_still_converted()
    {
        Format("| __a__ | b |", EmphasisStyle.Asterisk).ShouldBe("| **a** | b |");
    }

    [Fact]
    public void An_escaped_pipe_belongs_to_its_cell()
    {
        Format(@"| __a \| b__ | c |", EmphasisStyle.Asterisk).ShouldBe(@"| **a \| b** | c |");
    }

    [Fact]
    public void A_pipe_in_a_paragraph_does_not_split_the_emphasis()
    {
        Format("some __a | b__ text", EmphasisStyle.Asterisk).ShouldBe("some **a | b** text");
    }
}
