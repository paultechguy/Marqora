// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Formatting.Tests;

public class HeadingNumberRewriterTests
{
    private static string Number(
        string markdown,
        HeadingNumbering start = HeadingNumbering.FromHeading1,
        HeadingNumberStyle style = HeadingNumberStyle.TwoSpaces) =>
        HeadingNumberRewriter.Apply(markdown, start, style).Text;

    private static string Strip(string markdown) => HeadingNumberRewriter.Remove(markdown).Text;

    // -------------------------------------------------------------- numbering

    [Fact]
    public void An_unnumbered_document_is_numbered_from_the_first_heading()
    {
        Number(
            """
            # Introduction
            ## Purpose
            ## Scope
            # Method
            """).ShouldBe(
            """
            # 1  Introduction
            ## 1.1  Purpose
            ## 1.2  Scope
            # 2  Method
            """);
    }

    [Fact]
    public void Numbering_from_heading_two_leaves_the_title_alone()
    {
        Number(
            """
            # Specification
            ## Introduction
            ## Scope
            """,
            HeadingNumbering.FromHeading2).ShouldBe(
            """
            # Specification
            ## 1  Introduction
            ## 2  Scope
            """);
    }

    [Fact]
    public void A_numbered_document_is_renumbered_rather_than_prefixed()
    {
        // The case the command exists for: a section inserted into a numbered document, where
        // every number below it is now one out. Prefixing instead of replacing would give
        // "1  1  Alpha".
        Number(
            """
            # 1  Alpha
            # Inserted
            # 2  Beta
            """).ShouldBe(
            """
            # 1  Alpha
            # 2  Inserted
            # 3  Beta
            """);
    }

    [Fact]
    public void Renumbering_a_document_that_is_already_right_changes_nothing()
    {
        const string document = "# 1  Alpha\n## 1.1  One\n# 2  Beta";

        HeadingNumberRewriter.Result result =
            HeadingNumberRewriter.Apply(document, HeadingNumbering.FromHeading1, HeadingNumberStyle.TwoSpaces);

        result.IsUnchanged.ShouldBeTrue();
        result.Changed.ShouldBe(0);
        result.Text.ShouldBe(document);
    }

    [Fact]
    public void Moving_the_start_level_down_takes_the_title_number_off()
    {
        Number("# 1  Alpha\n## 1.1  One", HeadingNumbering.FromHeading2).ShouldBe("# Alpha\n## 1  One");
    }

    [Theory]
    [InlineData(HeadingNumberStyle.TwoSpaces, "# 1  Alpha")]
    [InlineData(HeadingNumberStyle.OneSpace, "# 1 Alpha")]
    [InlineData(HeadingNumberStyle.DotSpace, "# 1. Alpha")]
    [InlineData(HeadingNumberStyle.ParenSpace, "# 1) Alpha")]
    [InlineData(HeadingNumberStyle.Tab, "# 1\tAlpha")]
    public void Each_style_writes_its_own_separator(HeadingNumberStyle style, string expected)
    {
        Number("# Alpha", HeadingNumbering.FromHeading1, style).ShouldBe(expected);
    }

    [Fact]
    public void Numbering_from_off_is_removing()
    {
        Number("# 1  Alpha\n# 2  Beta", HeadingNumbering.Off).ShouldBe("# Alpha\n# Beta");
    }

    // --------------------------------------------------------------- removing

    [Fact]
    public void Removing_takes_the_numbers_and_leaves_the_words()
    {
        Strip(
            """
            # 1 Introduction
            ## 1.1 Purpose
            # 2 Method
            """).ShouldBe(
            """
            # Introduction
            ## Purpose
            # Method
            """);
    }

    [Fact]
    public void A_document_that_never_had_numbers_is_left_alone()
    {
        const string document = "# 2026 Budget\n## Overview\n## 3D Printing";

        HeadingNumberRewriter.Result result = HeadingNumberRewriter.Remove(document);

        result.IsUnchanged.ShouldBeTrue();
        result.Text.ShouldBe(document);
    }

    [Fact]
    public void A_year_inside_a_numbered_document_keeps_its_number()
    {
        Strip(
            """
            # 1 Introduction
            # 2 Scope
            # 2026 Budget
            # 4 Method
            """).ShouldBe(
            """
            # Introduction
            # Scope
            # 2026 Budget
            # Method
            """);
    }

    // ------------------------------------------------------------ round trip

    [Fact]
    public void Numbering_an_unnumbered_document_and_removing_gives_the_original_back()
    {
        // The invariant the pair stands on. Anything that breaks it means the two commands
        // disagree about what a number is.
        const string document =
            """
            # Introduction

            Some prose about 2026 and 3D printing.

            ## Purpose
            ### Detail
            ## Scope
            # Method
            """;

        Strip(Number(document)).ShouldBe(document);
    }

    [Theory]
    [InlineData(HeadingNumberStyle.TwoSpaces)]
    [InlineData(HeadingNumberStyle.OneSpace)]
    [InlineData(HeadingNumberStyle.DotSpace)]
    [InlineData(HeadingNumberStyle.ParenSpace)]
    [InlineData(HeadingNumberStyle.Tab)]
    public void The_round_trip_holds_for_every_style(HeadingNumberStyle style)
    {
        const string document = "# Alpha\n## One\n## Two\n# Beta\n### Deep";

        Strip(Number(document, HeadingNumbering.FromHeading1, style)).ShouldBe(document);
    }

    // ----------------------------------------------------------- not touched

    [Fact]
    public void Line_endings_survive_untouched()
    {
        // The result is the input with prefixes spliced in, never a document reassembled from
        // lines, so a file's endings come out as they went in - mixed ones included.
        Number("# Alpha\r\n## One\r\n").ShouldBe("# 1  Alpha\r\n## 1.1  One\r\n");
        Number("# Alpha\r\n## One\n# Beta").ShouldBe("# 1  Alpha\r\n## 1.1  One\n# 2  Beta");
    }

    [Fact]
    public void Fenced_code_and_front_matter_are_not_touched()
    {
        Number(
            """
            ---
            title: Something
            ---

            # Alpha

            ```bash
            # not a heading
            ```

            ## One
            """).ShouldBe(
            """
            ---
            title: Something
            ---

            # 1  Alpha

            ```bash
            # not a heading
            ```

            ## 1.1  One
            """);
    }

    [Fact]
    public void An_empty_heading_counts_but_is_not_given_a_number()
    {
        // It has to keep its place in the count, or everything below it renumbers. Writing into
        // it would turn an empty heading into one titled "2".
        Number("# Alpha\n#\n# Beta").ShouldBe("# 1  Alpha\n#\n# 3  Beta");
    }

    [Fact]
    public void An_underlined_heading_is_numbered_in_place()
    {
        Number("Alpha\n=====\nOne\n---").ShouldBe("1  Alpha\n=====\n1.1  One\n---");
    }

    [Fact]
    public void Trailing_and_leading_whitespace_on_other_lines_is_preserved()
    {
        Number("# Alpha\n\n   indented prose   \n\n## One").ShouldBe(
            "# 1  Alpha\n\n   indented prose   \n\n## 1.1  One");
    }

    [Fact]
    public void An_empty_document_is_left_alone()
    {
        HeadingNumberRewriter.Apply(string.Empty, HeadingNumbering.FromHeading1, HeadingNumberStyle.TwoSpaces)
            .IsUnchanged.ShouldBeTrue();

        HeadingNumberRewriter.Remove(string.Empty).IsUnchanged.ShouldBeTrue();
    }

    // ------------------------------------------------------------------ links

    [Fact]
    public void A_link_to_a_renamed_heading_is_moved()
    {
        // The breakage this exists to stop. Numbering "Scope" makes its anchor "2--scope", and
        // a link left pointing at "#scope" is dead - silently, because it still looks clickable.
        Number(
            """
            # Contents

            - [Scope](#scope)

            # Scope
            """).ShouldBe(
            """
            # 1  Contents

            - [Scope](#2--scope)

            # 2  Scope
            """);
    }

    [Fact]
    public void A_whole_table_of_contents_moves()
    {
        HeadingNumberRewriter.Result result = HeadingNumberRewriter.Apply(
            """
            # Contents

            - [Introduction](#introduction)
            - [Scope](#scope)

            # Introduction
            # Scope
            """,
            HeadingNumbering.FromHeading1,
            HeadingNumberStyle.TwoSpaces);

        result.LinksMoved.ShouldBe(2);
        result.Text.ShouldContain("(#2--introduction)");
        result.Text.ShouldContain("(#3--scope)");
    }

    [Fact]
    public void Removing_the_numbers_moves_the_links_back()
    {
        // The round trip has to hold for the links too, or the pair leaves a document that
        // neither numbers its headings nor resolves its own contents.
        const string document =
            """
            # Contents

            - [Scope](#scope)
            - [Method](#method)

            # Scope
            # Method
            """;

        Strip(Number(document)).ShouldBe(document);
    }

    [Fact]
    public void A_duplicate_heading_keeps_its_link_pointing_at_the_right_one()
    {
        // Two headings reading "Scope" are "scope" and "scope-1"; numbering makes them distinct
        // on their own, so the counted suffix disappears and the link has to follow it.
        Number(
            """
            [second](#scope-1)

            # Scope
            # Scope
            """).ShouldBe(
            """
            [second](#2--scope)

            # 1  Scope
            # 2  Scope
            """);
    }

    [Fact]
    public void A_reference_definition_and_an_html_anchor_move_too()
    {
        HeadingNumberRewriter.Result result = HeadingNumberRewriter.Apply(
            "[ref]: #scope\n\n<a href=\"#scope\">go</a>\n\n# Scope",
            HeadingNumbering.FromHeading1,
            HeadingNumberStyle.TwoSpaces);

        result.LinksMoved.ShouldBe(2);
        result.Text.ShouldBe("[ref]: #1--scope\n\n<a href=\"#1--scope\">go</a>\n\n# 1  Scope");
    }

    [Fact]
    public void A_link_written_as_an_example_is_left_alone()
    {
        HeadingNumberRewriter.Result result = HeadingNumberRewriter.Apply(
            """
            # Scope

            Write it as `[text](#scope)` in your source.

            ```markdown
            [text](#scope)
            ```
            """,
            HeadingNumbering.FromHeading1,
            HeadingNumberStyle.TwoSpaces);

        result.LinksMoved.ShouldBe(0);
        result.Text.ShouldContain("`[text](#scope)`");
        result.Text.ShouldContain("```markdown\n[text](#scope)\n```");
    }

    [Fact]
    public void A_link_into_another_document_is_left_alone()
    {
        HeadingNumberRewriter.Result result = HeadingNumberRewriter.Apply(
            "[x](other.md#scope)\n\n# Scope",
            HeadingNumbering.FromHeading1,
            HeadingNumberStyle.TwoSpaces);

        result.LinksMoved.ShouldBe(0);
        result.Text.ShouldContain("[x](other.md#scope)");
    }

    [Fact]
    public void A_heading_whose_anchor_cannot_be_read_from_the_source_is_reported_not_guessed()
    {
        // The source says "[Scope](https://example.com)"; the rendered heading says "Scope".
        // Slugifying the source would invent "scopehttpsexamplecom" and repoint a working link
        // at a name nothing has, which is worse than leaving it where the author put it.
        HeadingNumberRewriter.Result result = HeadingNumberRewriter.Apply(
            "[go](#scope)\n\n# [Scope](https://example.com)",
            HeadingNumbering.FromHeading1,
            HeadingNumberStyle.TwoSpaces);

        result.UncertainHeadings.ShouldBe(1);
        result.LinksMoved.ShouldBe(0);
        result.Text.ShouldContain("[go](#scope)");
        result.Text.ShouldContain("# 1  [Scope](https://example.com)");
    }

    [Fact]
    public void An_intraword_underscore_is_not_emphasis_and_does_not_make_a_heading_uncertain()
    {
        HeadingNumberRewriter.Result result = HeadingNumberRewriter.Apply(
            "[go](#snake_case)\n\n# snake_case",
            HeadingNumbering.FromHeading1,
            HeadingNumberStyle.TwoSpaces);

        result.UncertainHeadings.ShouldBe(0);
        result.LinksMoved.ShouldBe(1);
        result.Text.ShouldContain("[go](#1--snake_case)");
    }

    [Fact]
    public void A_link_is_matched_without_case_the_way_the_dead_anchor_check_matches_it()
    {
        HeadingNumberRewriter.Apply("[go](#Scope)\n\n# Scope", HeadingNumbering.FromHeading1, HeadingNumberStyle.TwoSpaces)
            .Text.ShouldContain("[go](#1--scope)");
    }

    [Fact]
    public void A_document_with_no_links_reports_none_moved()
    {
        HeadingNumberRewriter.Apply("# Alpha\n## One", HeadingNumbering.FromHeading1, HeadingNumberStyle.TwoSpaces)
            .LinksMoved.ShouldBe(0);
    }

    // ------------------------------------------------------------------ count

    [Fact]
    public void The_count_is_the_headings_that_actually_moved()
    {
        HeadingNumberRewriter.Result result = HeadingNumberRewriter.Apply(
            "# 1  Alpha\n# Inserted\n# 2  Beta",
            HeadingNumbering.FromHeading1,
            HeadingNumberStyle.TwoSpaces);

        // Alpha was already "1" and did not move; the other two did.
        result.Changed.ShouldBe(2);
    }
}
