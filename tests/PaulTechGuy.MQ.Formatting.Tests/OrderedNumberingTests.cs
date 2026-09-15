// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Formatting.Tests;

/// <summary>
/// The ordered-numbering rule, which decides over a whole run rather than a line at a time.
///
/// Two things are being watched here. The first is that a run whose items all carry the same
/// number is left carrying it: that is the "1. 1. 1." shorthand, and a formatter that counts
/// it up undoes the thing the author was doing every time they save. The second is that the
/// run boundaries are right, because the decision is only as good as its idea of where the
/// list ends — a nested list, a loose one, a fence and a paragraph in the middle all have to
/// land on the correct side of it, and getting that wrong is invisible until it eats a real
/// document.
///
/// Every test runs with the numbering rule alone switched on, so a failure is about numbering
/// and not about some other rule having tidied the line on the way past.
/// </summary>
public sealed class OrderedNumberingTests
{
    private static readonly FormatOptions NumberingOnly = new()
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
        OrderedNumbering = true,
        BlockquoteSpace = false,
        FormatTables = false,
        TidyCodeFences = false,
        SetextToAtx = false,
        UnifyEmphasis = false,
        ReflowParagraphs = false,
    };

    // The endings are normalized on the way out so the assertions can be written in plain
    // newlines; this rule has nothing to say about them, and LineEndings is off in any case.
    private static string Format(string markdown) =>
        new MarkdownFormatter().Format(markdown, NumberingOnly).Text.Replace("\r\n", "\n");

    private static string Lines(params string[] lines) => string.Join("\n", lines);

    // ------------------------------------------------------------------ counting up

    [Fact]
    public void CountsUpFromWhereTheListStarts()
    {
        Format(Lines("1. one", "1. two", "5. three"))
            .ShouldBe(Lines("1. one", "2. two", "3. three"));
    }

    [Fact]
    public void KeepsAStartOtherThanOne()
    {
        Format(Lines("5. five", "1. six", "1. seven"))
            .ShouldBe(Lines("5. five", "6. six", "7. seven"));
    }

    [Fact]
    public void LeavesACorrectlyNumberedListAlone()
    {
        Format(Lines("1. one", "2. two", "3. three"))
            .ShouldBe(Lines("1. one", "2. two", "3. three"));
    }

    // ------------------------------------------------------------ the repeated run

    [Fact]
    public void LeavesARepeatedRunRepeated()
    {
        Format(Lines("1. one", "1. two", "1. three"))
            .ShouldBe(Lines("1. one", "1. two", "1. three"));
    }

    [Fact]
    public void LeavesARepeatedRunOfZerosAlone()
    {
        Format(Lines("0) one", "0) two", "0) three"))
            .ShouldBe(Lines("0) one", "0) two", "0) three"));
    }

    [Fact]
    public void LeavesARepeatedRunOfSomeOtherNumberAlone()
    {
        Format(Lines("7. seven", "7. seven", "7. seven"))
            .ShouldBe(Lines("7. seven", "7. seven", "7. seven"));
    }

    /// <summary>
    /// Two items is the shortest run that can carry the signal, and is what the editor's own
    /// repeat needs before it will fire. The formatter has to agree with it or the two would
    /// pull the same list in opposite directions.
    /// </summary>
    [Fact]
    public void TwoItemsAreEnoughToCountAsARepeat()
    {
        Format(Lines("1. one", "1. two"))
            .ShouldBe(Lines("1. one", "1. two"));
    }

    [Fact]
    public void FinishesARunThatOnlyPartlyAgrees()
    {
        Format(Lines("1. one", "1. two", "2. three"))
            .ShouldBe(Lines("1. one", "2. two", "3. three"));
    }

    /// <summary>
    /// Leaving the numbers alone is not the same as leaving the line alone. The rule still
    /// writes the indent and the single space after the delimiter, so a repeated list is
    /// tidied like any other.
    /// </summary>
    [Fact]
    public void ARepeatedRunIsStillTidied()
    {
        Format(Lines("1.   one", "1.\ttwo"))
            .ShouldBe(Lines("1. one", "1. two"));
    }

    // ----------------------------------------------------------- where a run ends

    [Fact]
    public void ANestedRepeatIsJudgedApartFromTheListAroundIt()
    {
        Format(Lines("1. one", "   1. inner", "   5. inner", "1. two"))
            .ShouldBe(Lines("1. one", "   1. inner", "   2. inner", "1. two"));
    }

    [Fact]
    public void AnOuterRunIsJudgedApartFromANestedRepeat()
    {
        Format(Lines("1. one", "   1. inner", "   1. inner", "5. two"))
            .ShouldBe(Lines("1. one", "   1. inner", "   1. inner", "2. two"));
    }

    /// <summary>One blank line inside a list is a loose list, and does not end the run.</summary>
    [Fact]
    public void OneBlankLineDoesNotEndTheRun()
    {
        Format(Lines("1. one", string.Empty, "1. two", string.Empty, "1. three"))
            .ShouldBe(Lines("1. one", string.Empty, "1. two", string.Empty, "1. three"));
    }

    [Fact]
    public void TwoBlankLinesStartAFreshRun()
    {
        // The second list repeats and is left alone; the first is a list of its own and is
        // judged without reference to it.
        Format(Lines("1. one", "3. two", string.Empty, string.Empty, "1. one", "1. two"))
            .ShouldBe(Lines("1. one", "2. two", string.Empty, string.Empty, "1. one", "1. two"));
    }

    [Fact]
    public void ProseAtTheLeftMarginEndsTheRun()
    {
        Format(Lines("1. one", "1. two", string.Empty, "Prose.", string.Empty, "1. one", "5. two"))
            .ShouldBe(Lines("1. one", "1. two", string.Empty, "Prose.", string.Empty, "1. one", "2. two"));
    }

    [Fact]
    public void NumbersInsideAFenceAreNotAList()
    {
        Format(Lines("```", "1. one", "5. two", "```"))
            .ShouldBe(Lines("```", "1. one", "5. two", "```"));
    }

    // ----------------------------------------------------------- a frozen range

    /// <summary>
    /// Formatting a selection freezes everything outside it. A run can then straddle the
    /// edge, and the numbering is still worked out over the whole run — the frozen lines
    /// simply do not get written.
    /// </summary>
    [Fact]
    public void FrozenLinesKeepTheirNumbersWhileTheRestIsRenumbered()
    {
        string source = Lines("1. one", "5. two", "9. three");

        new MarkdownFormatter().FormatLines(source, 1, 2, NumberingOnly).Text
            .Replace("\r\n", "\n")
            .ShouldBe(Lines("1. one", "2. two", "3. three"));
    }

    /// <summary>
    /// A frozen line still counts towards whether the run repeats. Reading only the lines
    /// that may be written would call this run uniform and leave the mistake in place.
    /// </summary>
    [Fact]
    public void AFrozenLineStillCountsTowardsTheRepeat()
    {
        string source = Lines("1. one", "1. two", "5. three");

        new MarkdownFormatter().FormatLines(source, 1, 2, NumberingOnly).Text
            .Replace("\r\n", "\n")
            .ShouldBe(Lines("1. one", "2. two", "3. three"));
    }
}
