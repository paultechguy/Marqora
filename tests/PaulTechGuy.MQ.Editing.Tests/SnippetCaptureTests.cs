// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Editing.Tests;

/// <summary>
/// What a snippet carrying <c>$SEL</c> takes in with it.
///
/// Note stands in for all five callouts throughout — they differ only in the word between the
/// brackets, and testing each would test the table in BuiltInSnippets rather than this code.
/// </summary>
public class SnippetCaptureTests
{
    private const string Note = "> [!NOTE]\n> $SEL$0";

    // ------------------------------------------------------------------ a selection

    [Fact]
    public void A_selection_inside_a_paragraph_splits_it()
    {
        // "If it fails." out of the middle. What was on either side becomes a paragraph of its
        // own rather than being swallowed or left welded to the callout.
        string document = "The build runs nightly. If it fails. Ask before deleting.";

        Edits.RunSnippet(Edits.Selection(document, 0, 24, 0, 36), Note)
            .ShouldBe("The build runs nightly.\n\n> [!NOTE]\n> If it fails.\n\nAsk before deleting.");
    }

    [Fact]
    public void The_space_a_selection_leaves_behind_goes_with_it()
    {
        // Taking the words alone would leave "nightly.  Ask" carrying a double space, and a
        // selection that reached the end of a line would leave a trailing one.
        Edits.RunSnippet(Edits.Selection("one two three", 0, 4, 0, 7), Note)
            .ShouldBe("one\n\n> [!NOTE]\n> two\n\nthree");
    }

    [Fact]
    public void A_selection_that_is_a_whole_line_needs_no_paragraph_either_side()
    {
        Edits.RunSnippet(Edits.Selection("Intro.\nSelected line.", 1, 0, 1, 14), Note)
            .ShouldBe("Intro.\n\n> [!NOTE]\n> Selected line.");
    }

    [Fact]
    public void A_selection_spanning_blocks_becomes_one_callout()
    {
        // The blank line between them has to keep the callout open, so it comes across as a bare
        // ">" - with no trailing space, which nothing would ever see and a formatter would strip.
        Edits.RunSnippet(Edits.Selection("First one.\n\nSecond one.", 0, 0, 2, 11), Note)
            .ShouldBe("> [!NOTE]\n> First one.\n>\n> Second one.");
    }

    [Fact]
    public void A_selected_list_is_captured_whole()
    {
        // A caret in a list captures nothing, but selecting one says what it means.
        Edits.RunSnippet(Edits.Selection("- one\n- two", 0, 0, 1, 5), Note)
            .ShouldBe("> [!NOTE]\n> - one\n> - two");
    }

    [Fact]
    public void A_selection_ending_at_the_start_of_the_next_line_stops_before_it()
    {
        // Dragging down through a line and releasing at the start of the next one: that line
        // holds none of the selection, so it is not part of the capture either.
        Edits.RunSnippet(Edits.Selection("Taken.\nLeft alone.", 0, 0, 1, 0), Note)
            .ShouldBe("> [!NOTE]\n> Taken.\n\nLeft alone.");
    }

    // ------------------------------------------------------------------- a caret

    [Fact]
    public void A_caret_in_a_paragraph_captures_the_whole_paragraph()
    {
        Edits.RunSnippet(Edits.Caret("Above\n\nOne line paragraph.\n\nBelow", 2, 5), Note)
            .ShouldBe("Above\n\n> [!NOTE]\n> One line paragraph.\n\nBelow");
    }

    [Fact]
    public void A_caret_captures_every_line_of_a_wrapped_paragraph()
    {
        string document = "Intro.\n\nFirst line of it\nand its second line.\n\nTail.";

        Edits.RunSnippet(Edits.Caret(document, 3, 4), Note)
            .ShouldBe("Intro.\n\n> [!NOTE]\n> First line of it\n> and its second line.\n\nTail.");
    }

    [Fact]
    public void A_caret_in_a_heading_captures_the_heading()
    {
        Edits.RunSnippet(Edits.Caret("# Title\n\nBody.", 0, 3), Note)
            .ShouldBe("> [!NOTE]\n> # Title\n\nBody.");
    }

    [Fact]
    public void A_setext_heading_brings_its_underline()
    {
        // The run of "=" is not the line after the paragraph, it is part of the same block, and
        // a callout holding the text without it would be holding a different thing.
        Edits.RunSnippet(Edits.Caret("Title\n=====\n\nBody.", 0, 2), Note)
            .ShouldBe("> [!NOTE]\n> Title\n> =====\n\nBody.");
    }

    // ---------------------------------------------------- where it captures nothing

    [Fact]
    public void A_caret_in_a_list_captures_nothing()
    {
        // Every one of these would wrap correctly. They are left alone because the click did not
        // look like it was going to move them - see Paragraphs for the reasoning.
        Edits.RunSnippet(Edits.Caret("- one\n- two", 0, 3), Note)
            .ShouldBe("> [!NOTE]\n> \n\n- one\n- two");
    }

    [Fact]
    public void A_caret_in_a_table_captures_nothing()
    {
        Edits.RunSnippet(Edits.Caret("| a | b |\n| - | - |", 0, 3), Note)
            .ShouldBe("> [!NOTE]\n> \n\n| a | b |\n| - | - |");
    }

    [Fact]
    public void A_caret_already_inside_a_callout_captures_nothing()
    {
        // Otherwise reaching for Note inside a Note would nest one in the other.
        Edits.RunSnippet(Edits.Caret("> [!TIP]\n> Something.", 1, 5), Note)
            .ShouldBe("> [!TIP]\n\n> [!NOTE]\n> \n\n> Something.");
    }

    [Fact]
    public void A_caret_on_a_blank_line_captures_nothing()
    {
        Edits.RunSnippet(Edits.Caret("Above\n\nBelow", 1, 0), Note)
            .ShouldBe("Above\n\n> [!NOTE]\n> \n\nBelow");
    }

    [Fact]
    public void A_lazy_continuation_is_not_a_paragraph()
    {
        // "continuation" reads as prose on its own and is not prose on its own: it belongs to
        // the list item above it, and lifting it out would break the item.
        Edits.RunSnippet(Edits.Caret("- item\ncontinuation", 1, 4), Note)
            .ShouldBe("- item\n\n> [!NOTE]\n> \n\ncontinuation");
    }

    [Fact]
    public void A_caret_inside_a_fenced_block_captures_nothing()
    {
        Edits.RunSnippet(Edits.Caret("```\nprose inside\n```", 1, 2), Note)
            .ShouldBe("```\n\n> [!NOTE]\n> \n\nprose inside\n```");
    }

    [Fact]
    public void A_caret_inside_front_matter_captures_nothing()
    {
        Edits.RunSnippet(Edits.Caret("---\ntitle: x\n---\n\nBody.", 1, 3), Note)
            .ShouldBe("---\n\n> [!NOTE]\n> \n\ntitle: x\n---\n\nBody.");
    }

    [Fact]
    public void A_selection_that_straddles_a_fence_captures_nothing()
    {
        // Half a code block inside a callout leaves both halves broken.
        Edits.RunSnippet(Edits.Selection("```\ncode\n```", 1, 0, 1, 4), Note)
            .ShouldBe("```\n\n> [!NOTE]\n> \n\ncode\n```");
    }

    [Fact]
    public void A_whole_fenced_block_selected_is_captured()
    {
        // Opening on the fence line and closing on the other one is the whole block, which goes
        // into a callout perfectly well.
        Edits.RunSnippet(Edits.Selection("```\ncode\n```", 0, 0, 2, 3), Note)
            .ShouldBe("> [!NOTE]\n> ```\n> code\n> ```");
    }

    [Fact]
    public void A_selection_of_nothing_but_whitespace_captures_nothing()
    {
        Edits.RunSnippet(Edits.Selection("word    word", 0, 4, 0, 8), Note)
            .ShouldBe("> [!NOTE]\n> \n\nword    word");
    }

    // ------------------------------------------------------------------- the markers

    [Fact]
    public void Captured_text_is_never_scanned_for_markers()
    {
        // The capture is the user's own document, and a shell script in it carries "$0". One
        // walk over the body, with the captured text appended rather than re-read, is what
        // stops the snippet eating a line of it.
        Edits.RunSnippet(Edits.Caret("echo $0 now", 0, 4), Note)
            .ShouldBe("> [!NOTE]\n> echo $0 now");
    }

    [Fact]
    public void A_doubled_dollar_escapes_the_capture_marker()
    {
        // "$$SEL" is a literal "$SEL", and a body carrying only that one captures nothing - so
        // this behaves like any other snippet and replaces the selection.
        Edits.RunSnippet(Edits.Selection("replace me", 0, 0, 0, 7), "literal $$SEL here")
            .ShouldBe("literal $SEL here me");
    }

    [Fact]
    public void The_caret_lands_after_the_captured_text()
    {
        // "> One line paragraph." is 21 characters, on the second line of the block.
        Edits.CaretAfterSnippet(Edits.Caret("Above\n\nOne line paragraph.\n\nBelow", 2, 5), Note)
            .ShouldBe(new TextPosition(3, 21));
    }

    [Fact]
    public void The_caret_clears_a_blank_line_the_split_added()
    {
        // The block starts two lines below where the capture did, because the words in front of
        // it needed a paragraph and a blank line of their own.
        Edits.CaretAfterSnippet(Edits.Selection("one two three", 0, 4, 0, 7), Note)
            .ShouldBe(new TextPosition(3, 5));
    }

    [Fact]
    public void A_single_line_snippet_captures_without_becoming_a_block()
    {
        // It is inline text, so it keeps the spaces around it rather than being given blank
        // lines and a paragraph of its own.
        Edits.RunSnippet(Edits.Selection("make bold now", 0, 5, 0, 9), "**$SEL**")
            .ShouldBe("make **bold** now");
    }

    [Fact]
    public void A_marker_on_its_own_line_repeats_no_prefix()
    {
        string body = "<details>\n<summary>Detail</summary>\n\n$SEL\n\n</details>";

        Edits.RunSnippet(Edits.Caret("First.\nSecond.", 0, 2), body)
            .ShouldBe("<details>\n<summary>Detail</summary>\n\nFirst.\nSecond.\n\n</details>");
    }

    [Fact]
    public void A_marker_after_a_list_bullet_indents_what_follows_it()
    {
        // The bullet cannot be repeated - that would be a second item - so it comes across as
        // two spaces, which is what a list item's continuation is indented by.
        Edits.RunSnippet(Edits.Caret("First.\nSecond.", 0, 2), "- $SEL")
            .ShouldBe("- First.\n  Second.");
    }

    [Fact]
    public void A_body_without_the_marker_is_unaffected_by_a_selection()
    {
        // The whole point of the marker being opt-in: everything that did not ask for capture
        // goes in exactly as it did before there was one.
        Edits.RunSnippet(Edits.Selection("Some prose.", 0, 0, 0, 4), "```\n```")
            .ShouldBe("```\n```\n\nSome prose.");
    }

    [Fact]
    public void A_narrower_context_than_the_document_captures_nothing()
    {
        // Finding a paragraph's edges reads from the top of the file. A context that starts
        // partway down cannot answer, and says so rather than guessing.
        var window = new EditContext(
            ["Above", "A paragraph.", "Below"],
            FirstLine: 40,
            new TextRange(new TextPosition(41, 3), new TextPosition(41, 3)));

        Edits.TextFromSnippet(window, Note).ShouldBe("\n> [!NOTE]\n> \n\n");
    }
}
