// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;
using Note = PaulTechGuy.MQ.Markdown.CriticMarkupWriter.Note;

namespace PaulTechGuy.MQ.Markdown.Tests;

/// <summary>
/// The CriticMarkup copy a review page carries. Each note arrives as the preview saw it - a
/// block's source line and the rendered text that was selected - and has to land beside the
/// same words in the source, or, failing that, beside the block, without ever breaking the
/// markdown around it.
/// </summary>
public sealed class CriticMarkupWriterTests
{
    private static string Write(string source, params Note[] notes) => CriticMarkupWriter.Write(source, notes);

    // ------------------------------------------------------------------ inline

    /// <summary>Plain text in a paragraph is wrapped where it stands.</summary>
    [Fact]
    public void A_passage_in_a_paragraph_is_wrapped_in_place() =>
        Write("Intro.\n\nThe server is the source of truth here.\n", new Note(2, 14, "source of truth", "Contradicts goal 2."))
            .ShouldBe("Intro.\n\nThe server is the {==source of truth==}{>>Contradicts goal 2.<<} here.\n");

    /// <summary>No notes, no change - not even to line endings.</summary>
    [Fact]
    public void No_notes_returns_the_source_untouched() =>
        Write("a\r\nb").ShouldBe("a\r\nb");

    /// <summary>The preview drops emphasis markers; the wrap takes them in whole rather than splitting them.</summary>
    [Fact]
    public void A_passage_across_emphasis_is_wrapped_with_its_markers() =>
        Write("Keep the **newer edit** always.\n", new Note(0, 9, "newer edit always", "Why?"))
            .ShouldBe("Keep the {==**newer edit** always==}{>>Why?<<}.\n");

    /// <summary>A link's text is what the reader selected; its target goes with it.</summary>
    [Fact]
    public void A_passage_that_is_a_link_is_wrapped_with_its_target() =>
        Write("See [the guide](https://example.com/guide) first.\n", new Note(0, 4, "the guide", "Stale link?"))
            .ShouldBe("See {==[the guide](https://example.com/guide)==}{>>Stale link?<<} first.\n");

    /// <summary>An underscore inside a word is part of it, not emphasis to be taken in.</summary>
    [Fact]
    public void An_underscore_inside_a_word_is_not_pulled_into_the_wrap() =>
        Write("Call snake_case here.\n", new Note(0, 5, "snake", "Rename."))
            .ShouldBe("Call {==snake==}{>>Rename.<<}_case here.\n");

    /// <summary>Emphasis around exactly the passage goes inside the wrap.</summary>
    [Fact]
    public void Emphasis_around_exactly_the_passage_is_taken_in() =>
        Write("A *key* point.\n", new Note(0, 2, "key", "Why?"))
            .ShouldBe("A {==*key*==}{>>Why?<<} point.\n");

    /// <summary>
    /// A link whose text is its own address: the passage must be found in the text, never inside
    /// the target, where wrapping it would break the link.
    /// </summary>
    [Fact]
    public void A_link_whose_text_is_its_address_is_not_wrapped_inside_the_target() =>
        Write("Go to [example.com](https://example.com) now.\n", new Note(0, 6, "example.com", "Dead?"))
            .ShouldBe("Go to {==[example.com](https://example.com)==}{>>Dead?<<} now.\n");

    /// <summary>A code span's text is shown, so it can be commented; the backticks go inside the wrap.</summary>
    [Fact]
    public void A_code_span_is_wrapped_with_its_backticks() =>
        Write("Call `retry()` twice.\n", new Note(0, 5, "retry()", "Why twice?"))
            .ShouldBe("Call {==`retry()`==}{>>Why twice?<<} twice.\n");

    /// <summary>A ==marked== run renders without its equals signs.</summary>
    [Fact]
    public void A_marked_run_is_wrapped_with_its_markers() =>
        Write("This is ==very important== now.\n", new Note(0, 8, "very important", "Is it?"))
            .ShouldBe("This is {====very important====}{>>Is it?<<} now.\n");

    /// <summary>A footnote reference renders as its number; the passage before it is still found.</summary>
    [Fact]
    public void A_passage_before_a_footnote_reference_is_found() =>
        Write("Rare in practice.[^1]\n\n[^1]: Last quarter.\n", new Note(0, 0, "Rare in practice.", "Source?"))
            .ShouldBe("{==Rare in practice.==}{>>Source?<<}[^1]\n\n[^1]: Last quarter.\n");

    /// <summary>A soft line break renders as a space, so a passage can run across two source lines.</summary>
    [Fact]
    public void A_passage_across_a_soft_line_break_is_found() =>
        Write("pushes changes within a\nfew seconds of the connection.\n", new Note(0, 15, "within a few seconds", "Give a number."))
            .ShouldBe("pushes changes {==within a\nfew seconds==}{>>Give a number.<<} of the connection.\n");

    [Fact]
    public void A_passage_in_a_list_item_is_wrapped() =>
        Write("- Resolve using last writer wins, keeping history.\n", new Note(0, 14, "last writer wins", "By whose clock?"))
            .ShouldBe("- Resolve using {==last writer wins==}{>>By whose clock?<<}, keeping history.\n");

    [Fact]
    public void A_passage_in_a_heading_is_wrapped() =>
        Write("## Conflict handling\n", new Note(0, 0, "Conflict", "Rename?"))
            .ShouldBe("## {==Conflict==}{>>Rename?<<} handling\n");

    /// <summary>
    /// A note in a table row cannot break the row or open a new cell: its line breaks become
    /// spaces and pilcrows, and its pipes are escaped.
    /// </summary>
    [Fact]
    public void A_note_in_a_table_cell_stays_on_the_row() =>
        Write(
            "| Item | Budget |\n|---|---|\n| Photos | 50 MB |\n",
            new Note(2, 6, "50 MB", "Too small | see photos.\n\nA visit is 20+ photos."))
            .ShouldBe("| Item | Budget |\n|---|---|\n| Photos | {==50 MB==}{>>Too small \\| see photos. ¶ A visit is 20+ photos.<<} |\n");

    /// <summary>Where the passage appears twice in the block, the one nearest the selection is taken.</summary>
    [Fact]
    public void A_repeated_passage_takes_the_occurrence_nearest_the_selection() =>
        Write("one two one two\n", new Note(0, 8, "one", "Second."))
            .ShouldBe("one two {==one==}{>>Second.<<} two\n");

    [Fact]
    public void Two_notes_on_one_line_read_in_order() =>
        Write(
            "alpha beta gamma\n",
            new Note(0, 0, "alpha", "A"),
            new Note(0, 11, "gamma", "G"))
            .ShouldBe("{==alpha==}{>>A<<} beta {==gamma==}{>>G<<}\n");

    /// <summary>Two comments on overlapping text cannot both wrap it; the second stands after the block.</summary>
    [Fact]
    public void An_overlapping_note_is_written_after_the_block() =>
        Write(
            "alpha beta gamma\n",
            new Note(0, 0, "alpha beta", "First"),
            new Note(0, 6, "beta gamma", "Second"))
            .ShouldBe("{==alpha beta==}{>>First<<} gamma\n\n{>>On \"beta gamma\": Second<<}\n");

    /// <summary>A note that tries to close the markup early is defused.</summary>
    [Fact]
    public void A_note_cannot_close_the_comment_early() =>
        Write("word\n", new Note(0, 0, "word", "a <<} b"))
            .ShouldBe("{==word==}{>>a << } b<<}\n");

    [Fact]
    public void Crlf_line_endings_are_kept() =>
        Write("First line.\r\n\r\nSecond line here.\r\n", new Note(2, 7, "line", "Hm."))
            .ShouldBe("First line.\r\n\r\nSecond {==line==}{>>Hm.<<} here.\r\n");

    // -------------------------------------------------------------- standalone

    /// <summary>A code block is never altered: the note goes after its closing fence.</summary>
    [Fact]
    public void A_note_in_a_code_block_goes_after_the_fence() =>
        Write(
            "Text.\n\n```\nfor x in y:\n    go()\n```\n\nMore.\n",
            new Note(2, 0, "go()", "Retry?"))
            .ShouldBe("Text.\n\n```\nfor x in y:\n    go()\n```\n\n{>>On \"go()\": Retry?<<}\n\nMore.\n");

    /// <summary>
    /// Text the preview changed on the way to the screen - an emoji shortcode here - cannot be
    /// found again, so the note is kept beside its block with the passage quoted.
    /// </summary>
    [Fact]
    public void A_passage_that_cannot_be_found_goes_after_its_block() =>
        Write(
            "We ship :rocket: soon.\nStill same paragraph.\n\nNext.\n",
            new Note(0, 8, "🚀 soon", "Date?"))
            .ShouldBe("We ship :rocket: soon.\nStill same paragraph.\n\n{>>On \"🚀 soon\": Date?<<}\n\nNext.\n");

    /// <summary>
    /// A note that could not be placed goes after its block - at the same position a later
    /// note's wrap ends. The wrap's opening must not shift the standalone note into the middle
    /// of the wrapped words: it goes after the whole of the wrap.
    /// </summary>
    [Fact]
    public void A_standalone_note_and_a_wrap_ending_at_the_same_place_do_not_interleave() =>
        Write(
            "Alpha “beta” and gamma delta.\n",
            new Note(0, 0, "Alpha \"beta\"", "smart quotes"),
            new Note(0, 17, "gamma delta.", "end"))
            .ShouldBe("Alpha “beta” and {==gamma delta.==}{>>end<<}\n\n{>>On \"Alpha 'beta'\": smart quotes<<}\n");

    /// <summary>
    /// Code is never altered, including by the fallback search: a passage inside a code span
    /// that is not the whole span goes after the block rather than into the code.
    /// </summary>
    [Fact]
    public void A_passage_inside_a_code_span_is_not_wrapped_inside_the_code() =>
        Write("Run `npm install foo` now.\n", new Note(0, 8, "install", "Pin the version."))
            .ShouldBe("Run `npm install foo` now.\n\n{>>On \"install\": Pin the version.<<}\n");

    /// <summary>A standalone note keeps its paragraphs, in the document's own line endings.</summary>
    [Fact]
    public void A_standalone_note_keeps_its_paragraphs() =>
        Write("```\nx\n```\r\n", new Note(0, 0, "x", "One.\n\nTwo."))
            .ShouldBe("```\nx\n```\r\n\r\n{>>On \"x\": One.\r\n\r\nTwo.<<}\r\n");

    /// <summary>A block at the end of the file with no newline after it still gets its note.</summary>
    [Fact]
    public void A_note_at_the_end_of_a_file_without_a_newline_is_appended() =>
        Write("Only line", new Note(0, 0, "missing text", "Where?"))
            .ShouldBe("Only line\n\n{>>On \"missing text\": Where?<<}");

    /// <summary>A line past the end, from a preview that drifted, lands on the last line rather than throwing.</summary>
    [Fact]
    public void A_line_past_the_end_is_clamped() =>
        Write("a\nb\n", new Note(40, 0, "b", "Clamped."))
            .ShouldBe("a\n{==b==}{>>Clamped.<<}\n");
}
