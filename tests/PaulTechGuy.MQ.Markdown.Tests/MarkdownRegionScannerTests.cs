// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Markdown;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Markdown.Tests;

/// <summary>
/// The scanner had no tests of its own until it moved here, and it now has two callers who want
/// different answers from it.
///
/// The bug worth catching is the indented-code flag changing what the style checks see. They have
/// always ignored four-space blocks, deliberately, because that is what the formatter protects and
/// the two must agree; spell checking cannot afford to. Every test below that turns the flag on
/// has a partner that leaves it off.
/// </summary>
public sealed class MarkdownRegionScannerTests
{
    // ------------------------------------------------------------------------ fenced blocks

    [Fact]
    public void A_fenced_block_is_protected_including_both_fence_lines()
    {
        Protected(
        [
            "before",
            "```csharp",
            "var x = 1;",
            "```",
            "after",
        ])
        .ShouldBe([false, true, true, true, false]);
    }

    [Fact]
    public void A_tilde_fence_works_the_same_way()
    {
        Protected(["~~~", "code", "~~~", "after"]).ShouldBe([true, true, true, false]);
    }

    [Fact]
    public void A_closing_fence_must_be_at_least_as_long_as_the_one_that_opened_it()
    {
        // The short run is content inside the block, not the end of it.
        Protected(["````", "``", "still code", "````"]).ShouldBe([true, true, true, true]);
    }

    [Fact]
    public void A_backtick_run_carrying_a_backtick_is_an_inline_span_rather_than_a_fence()
    {
        Protected(["``` `not a fence` ```", "after"]).ShouldBe([false, false]);
    }

    [Fact]
    public void An_unclosed_fence_protects_the_rest_of_the_document()
    {
        Protected(["before", "```", "code", "more code"]).ShouldBe([false, true, true, true]);
    }

    // --------------------------------------------------------------------------- front matter

    [Fact]
    public void Front_matter_at_the_top_of_the_file_is_protected()
    {
        Protected(["---", "title: Notes", "---", "body"]).ShouldBe([true, true, true, false]);
    }

    [Fact]
    public void A_rule_further_down_the_document_is_not_front_matter()
    {
        // Three dashes below the first line is a thematic break, and the text after it is prose.
        Protected(["body", "---", "more body"]).ShouldBe([false, false, false]);
    }

    // ------------------------------------------------------------------------- indented code

    [Fact]
    public void An_indented_block_is_not_protected_by_default()
    {
        // What the style checks have always seen, and what the formatter protects.
        Protected(["text", "", "    var x = 1;", "after"])
            .ShouldBe([false, false, false, false]);
    }

    [Fact]
    public void An_indented_block_is_protected_when_asked_for()
    {
        Protected(["text", "", "    var x = 1;", "after"], indented: true)
            .ShouldBe([false, false, true, false]);
    }

    [Fact]
    public void A_document_that_opens_with_an_indented_line_opens_with_code()
    {
        Protected(["    var x = 1;", "after"], indented: true).ShouldBe([true, false]);
    }

    [Fact]
    public void A_blank_line_inside_an_indented_block_does_not_end_it()
    {
        Protected(["text", "", "    one", "", "    two", "after"], indented: true)
            .ShouldBe([false, false, true, true, true, false]);
    }

    [Fact]
    public void A_lazy_list_continuation_is_not_read_as_code()
    {
        // No blank line before it, so it is the list item's own text carrying on - which is
        // prose, and must still be checked.
        Protected(["- item one", "    continued text"], indented: true)
            .ShouldBe([false, false]);
    }

    [Fact]
    public void A_tab_counts_as_four_columns()
    {
        Protected(["text", "", "\tvar x = 1;", "after"], indented: true)
            .ShouldBe([false, false, true, false]);
    }

    [Fact]
    public void Three_spaces_are_not_enough_to_open_a_block()
    {
        Protected(["text", "", "   only three", "after"], indented: true)
            .ShouldBe([false, false, false, false]);
    }

    [Fact]
    public void A_fence_inside_a_document_with_indented_code_still_wins()
    {
        Protected(["```", "    indented inside a fence", "```", "after"], indented: true)
            .ShouldBe([true, true, true, false]);
    }

    private static bool[] Protected(string[] lines, bool indented = false) =>
        MarkdownRegionScanner.FindProtectedLines(lines, indented);
}
