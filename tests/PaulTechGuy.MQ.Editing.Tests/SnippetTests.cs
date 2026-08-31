// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Editing.Tests;

public class SnippetTests
{
    [Fact]
    public void A_single_line_snippet_goes_in_at_the_caret()
    {
        Edits.RunSnippet(Edits.Caret("hello world", 0, 5), " there")
            .ShouldBe("hello there world");
    }

    [Fact]
    public void A_snippet_replaces_whatever_is_selected()
    {
        Edits.RunSnippet(Edits.Selection("replace me", 0, 0, 0, 7), "kept")
            .ShouldBe("kept me");
    }

    [Fact]
    public void Without_a_marker_the_caret_lands_after_what_was_inserted()
    {
        Edits.RunSnippet(Edits.Caret(string.Empty, 0, 0), "**bold**").ShouldBe("**bold**");

        // An empty selection sitting past the last character.
        Edits.SelectedAfterSnippet(Edits.Caret(string.Empty, 0, 0), "**bold**").ShouldBe(string.Empty);
    }

    [Fact]
    public void A_marker_on_the_first_line_places_the_caret_there()
    {
        Edits.RunSnippet(Edits.Caret(string.Empty, 0, 0), "| $0 | b |\n| - | - |")
            .ShouldBe("|  | b |\n| - | - |\n");
    }

    [Fact]
    public void A_marker_on_a_later_line_places_the_caret_there()
    {
        Edits.RunSnippet(Edits.Caret(string.Empty, 0, 0), "```\n$0\n```")
            .ShouldBe("```\n\n```\n");
    }

    [Fact]
    public void A_marker_partway_along_an_inserted_line_offsets_from_the_caret()
    {
        // The snippet goes in at column 6, so its own column 5 lands at column 11.
        Edits.RunSnippet(Edits.Caret("start end", 0, 6), "[$0](url)")
            .ShouldBe("start [](url)end");
    }

    [Fact]
    public void Every_marker_is_removed_even_though_only_the_first_moves_the_caret()
    {
        // A stray "$0" left behind in the document is worse than one that disappears.
        Edits.RunSnippet(Edits.Caret(string.Empty, 0, 0), "a$0b$0c").ShouldBe("abc");
    }

    [Fact]
    public void A_doubled_dollar_escapes_to_a_literal_marker()
    {
        // Shell scripts and regular expressions contain "$0" often enough to need this.
        Edits.RunSnippet(Edits.Caret(string.Empty, 0, 0), "echo $$0").ShouldBe("echo $0");
    }

    [Fact]
    public void Windows_line_endings_do_not_survive_into_the_document()
    {
        // The shell turns every newline into the document's own ending, so a carriage
        // return arriving from a Notepad-authored snippet would come out doubled.
        string inserted = Edits.RunSnippet(Edits.Caret(string.Empty, 0, 0), "one\r\ntwo\r\n");

        inserted.ShouldNotContain("\r");
        inserted.ShouldBe("one\ntwo\n");
    }

    [Fact]
    public void A_byte_order_mark_is_stripped()
    {
        Edits.RunSnippet(Edits.Caret(string.Empty, 0, 0), "﻿# Title").ShouldBe("# Title");
    }

    [Fact]
    public void One_trailing_newline_is_treated_as_the_end_of_a_file_rather_than_content()
    {
        // "line\n" is a one-line file, so it inserts inline rather than as a block.
        Edits.RunSnippet(Edits.Caret("before after", 0, 7), "line\n").ShouldBe("before lineafter");
    }

    [Fact]
    public void A_block_snippet_gets_the_blank_lines_markdown_needs_around_it()
    {
        Edits.RunSnippet(Edits.Caret("Some prose", 0, 0), "```\n```")
            .ShouldBe("```\n```\n\nSome prose");

        Edits.RunSnippet(Edits.Caret("Above\nBelow", 1, 0), "```\n```")
            .ShouldBe("Above\n\n```\n```\n\nBelow");
    }

    [Fact]
    public void An_empty_snippet_does_nothing()
    {
        Edits.RunSnippet(Edits.Caret("untouched", 0, 4), string.Empty).ShouldBe("untouched");
        Edits.RunSnippet(Edits.Caret("untouched", 0, 4), "\n").ShouldBe("untouched");
    }
}
