// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Markdown.Tests;

public class HeadingScannerTests
{
    private static IReadOnlyList<HeadingScanner.ScannedHeading> Find(string document) =>
        HeadingScanner.Find(document.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'));

    private static HeadingScanner.ScannedHeading Only(string document)
    {
        IReadOnlyList<HeadingScanner.ScannedHeading> found = Find(document);

        found.Count.ShouldBe(1);

        return found[0];
    }

    // ------------------------------------------------------------------ shape

    [Fact]
    public void Every_atx_level_is_read()
    {
        IReadOnlyList<HeadingScanner.ScannedHeading> found =
            Find("# One\n## Two\n### Three\n#### Four\n##### Five\n###### Six");

        found.Select(h => h.Level).ShouldBe([1, 2, 3, 4, 5, 6]);
        found.Select(h => h.Text).ShouldBe(["One", "Two", "Three", "Four", "Five", "Six"]);
        found.Select(h => h.Line).ShouldBe([0, 1, 2, 3, 4, 5]);
    }

    [Fact]
    public void Three_spaces_of_indent_is_still_a_heading_and_four_is_not()
    {
        Only("   # Indented").Text.ShouldBe("Indented");

        // A fourth column is code, whatever it looks like.
        Find("    # Indented").ShouldBeEmpty();
    }

    [Fact]
    public void Hashes_need_a_space_after_them()
    {
        Find("#Heading").ShouldBeEmpty();

        // Seven is past the deepest level, so it is a paragraph starting with hashes.
        Find("####### Seven").ShouldBeEmpty();
    }

    [Fact]
    public void A_closing_run_of_hashes_is_furniture_but_only_when_it_stands_apart()
    {
        Only("## Scope ##").Text.ShouldBe("Scope");
        Only("## Scope ###########").Text.ShouldBe("Scope");

        // No space in front of it, so the hash is one of the words.
        Only("## Scope#").Text.ShouldBe("Scope#");
        Only("## Written in C#").Text.ShouldBe("Written in C#");
    }

    [Fact]
    public void A_heading_with_no_words_is_still_a_heading()
    {
        // It takes part in the count, so a scanner that skipped it would renumber everything
        // below it.
        Only("##").Level.ShouldBe(2);
        Only("##").Text.ShouldBe(string.Empty);
        Only("## ").Text.ShouldBe(string.Empty);
    }

    // --------------------------------------------------------------- skipping

    [Fact]
    public void Fenced_code_is_not_scanned()
    {
        IReadOnlyList<HeadingScanner.ScannedHeading> found = Find(
            """
            # Real

            ```bash
            # not a heading
            ```

            ## Also real
            """);

        found.Select(h => h.Text).ShouldBe(["Real", "Also real"]);
    }

    [Fact]
    public void Front_matter_is_not_scanned()
    {
        IReadOnlyList<HeadingScanner.ScannedHeading> found = Find(
            """
            ---
            title: Something
            ---

            # Real
            """);

        found.Select(h => h.Text).ShouldBe(["Real"]);
    }

    // ----------------------------------------------------------------- setext

    [Fact]
    public void An_underline_reads_as_level_one_or_two()
    {
        HeadingScanner.ScannedHeading equals = Only("Scope\n=====");

        equals.Level.ShouldBe(1);
        equals.Setext.ShouldBeTrue();
        equals.Text.ShouldBe("Scope");

        Only("Scope\n-----").Level.ShouldBe(2);
    }

    [Fact]
    public void A_list_item_over_dashes_is_a_list_and_a_rule()
    {
        // Reading the item as a title is how a rewriter deletes a horizontal rule.
        Find("- Item\n-----").ShouldBeEmpty();
        Find("> Quoted\n-----").ShouldBeEmpty();
    }

    [Fact]
    public void An_underline_begins_nothing_of_its_own()
    {
        // The dashes are stepped over rather than weighed again as the title of what follows.
        IReadOnlyList<HeadingScanner.ScannedHeading> found = Find("Scope\n-----\n=====");

        found.Count.ShouldBe(1);
        found[0].Line.ShouldBe(0);
    }

    // ----------------------------------------------------------------- prefix

    [Fact]
    public void A_number_prefix_is_read_with_its_terminator()
    {
        Only("# 1.2 Scope").Prefix!.Value.Components.ShouldBe([1, 2]);
        Only("# 1.2 Scope").Prefix!.Value.Terminator.ShouldBe('\0');
        Only("# 1.2. Scope").Prefix!.Value.Terminator.ShouldBe('.');
        Only("# 2) Scope").Prefix!.Value.Terminator.ShouldBe(')');
        Only("# 1.2.3 Data").Prefix!.Value.Components.ShouldBe([1, 2, 3]);
    }

    [Fact]
    public void A_section_mark_belongs_to_the_prefix()
    {
        HeadingScanner.NumberPrefix prefix = Only("# §1.2 Scope").Prefix!.Value;

        prefix.Section.ShouldBeTrue();
        prefix.Components.ShouldBe([1, 2]);
        Only("# §1.2 Scope").Title.ShouldBe("Scope");
    }

    [Fact]
    public void A_word_that_starts_with_a_digit_is_not_a_prefix()
    {
        // Nothing separates the digits from the word, so there is no number to read.
        Only("# 3D Printing").Prefix.ShouldBeNull();
        Only("# 1970s Architecture").Prefix.ShouldBeNull();
        Only("# 1.2Scope").Prefix.ShouldBeNull();
    }

    [Fact]
    public void A_number_with_no_words_after_it_is_left_alone()
    {
        // Stripping this would leave an empty heading behind.
        Only("# 1.").Prefix.ShouldBeNull();
        Only("# 1.2").Prefix.ShouldBeNull();
    }

    [Fact]
    public void A_year_is_read_as_a_candidate()
    {
        // Structurally identical to a section number, and this is the wrong layer to tell them
        // apart - HeadingNumberDetector needs the rest of the document to do that.
        Only("# 2026 Budget").Prefix!.Value.Components.ShouldBe([2026]);
    }

    [Fact]
    public void A_tab_separates_a_number_from_its_words()
    {
        Only("# 1.2\tScope").Title.ShouldBe("Scope");
    }

    [Fact]
    public void Written_normalizes_a_leading_zero()
    {
        // "01.2" is still the author numbering; it comes back tidied rather than unrecognized.
        Only("# 01.2 Scope").Prefix!.Value.Written.ShouldBe("1.2");
    }

    [Fact]
    public void An_underlined_heading_carries_a_prefix_too()
    {
        HeadingScanner.ScannedHeading heading = Only("1.2 Scope\n---------");

        heading.Level.ShouldBe(2);
        heading.Prefix!.Value.Components.ShouldBe([1, 2]);
        heading.Title.ShouldBe("Scope");
    }

    // --------------------------------------------------------------- splicing

    [Fact]
    public void TextStart_and_the_prefix_length_locate_the_words()
    {
        const string line = "## 1.2  Scope";

        HeadingScanner.ScannedHeading heading = Only(line);

        heading.TextStart.ShouldBe(3);
        heading.Text.ShouldBe("1.2  Scope");
        heading.Prefix!.Value.Length.ShouldBe(5);
        heading.Title.ShouldBe("Scope");

        // What a rewriter does: keep everything up to the text, then write its own number.
        string rebuilt = string.Concat(line.AsSpan(0, heading.TextStart), "9.9  ", heading.Title);

        rebuilt.ShouldBe("## 9.9  Scope");
    }

    [Fact]
    public void An_indented_heading_keeps_its_indent_in_TextStart()
    {
        HeadingScanner.ScannedHeading heading = Only("  ### Scope");

        heading.TextStart.ShouldBe(6);
        heading.Title.ShouldBe("Scope");
    }
}
