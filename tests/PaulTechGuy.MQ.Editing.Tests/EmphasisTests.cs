// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Editing.Tests;

public class EmphasisTests
{
    [Fact]
    public void Bold_with_no_selection_wraps_the_word_under_the_caret()
    {
        Edits.Run(Edits.Caret("hello world", 0, 2), MarkdownEditCommand.Bold)
            .ShouldBe("**hello** world");
    }

    [Fact]
    public void Bold_wraps_an_explicit_selection()
    {
        Edits.Run(Edits.Selection("hello world", 0, 6, 0, 11), MarkdownEditCommand.Bold)
            .ShouldBe("hello **world**");
    }

    [Fact]
    public void Bold_leaves_the_wrapped_text_selected_so_it_can_be_toggled_again()
    {
        Edits.Selected(Edits.Selection("hello world", 0, 6, 0, 11), MarkdownEditCommand.Bold)
            .ShouldBe("world");
    }

    [Fact]
    public void Bold_unwraps_when_the_markers_sit_outside_the_selection()
    {
        Edits.Run(Edits.Selection("**hello** world", 0, 2, 0, 7), MarkdownEditCommand.Bold)
            .ShouldBe("hello world");
    }

    [Fact]
    public void Bold_unwraps_when_the_markers_sit_inside_the_selection()
    {
        Edits.Run(Edits.Selection("**hello** world", 0, 0, 0, 9), MarkdownEditCommand.Bold)
            .ShouldBe("hello world");
    }

    [Fact]
    public void Bold_on_a_word_it_just_bolded_takes_it_back_off()
    {
        EditContext first = Edits.Caret("hello", 0, 2);
        string bolded = Edits.Run(first, MarkdownEditCommand.Bold);
        bolded.ShouldBe("**hello**");

        // The caret is left inside the markers, which is where a second press starts from.
        Edits.Run(Edits.Selection(bolded, 0, 2, 0, 7), MarkdownEditCommand.Bold)
            .ShouldBe("hello");
    }

    [Fact]
    public void Bold_outside_a_word_leaves_an_empty_pair_with_the_caret_between_them()
    {
        Edits.Run(Edits.Caret(string.Empty, 0, 0), MarkdownEditCommand.Bold)
            .ShouldBe("****");

        Edits.Selected(Edits.Caret(string.Empty, 0, 0), MarkdownEditCommand.Bold)
            .ShouldBe(string.Empty);
    }

    [Fact]
    public void A_caret_inside_a_bold_phrase_unbolds_the_whole_phrase()
    {
        // The caret sits in "text", which is not itself wrapped -- the markers are further
        // out, either side of "bold text". Wrapping again would give "**bold **text****".
        Edits.Run(Edits.Caret("**bold text**", 0, 8), MarkdownEditCommand.Bold)
            .ShouldBe("bold text");
    }

    [Fact]
    public void A_caret_inside_a_single_word_phrase_still_unwraps()
    {
        Edits.Run(Edits.Caret("**bold**", 0, 4), MarkdownEditCommand.Bold).ShouldBe("bold");
        Edits.Run(Edits.Caret("*slanted*", 0, 4), MarkdownEditCommand.Italic).ShouldBe("slanted");
        Edits.Run(Edits.Caret("`code`", 0, 3), MarkdownEditCommand.InlineCode).ShouldBe("code");
        Edits.Run(Edits.Caret("~~gone~~", 0, 4), MarkdownEditCommand.Strikethrough).ShouldBe("gone");
    }

    [Fact]
    public void A_caret_between_two_separate_phrases_wraps_rather_than_joining_them()
    {
        // The nearest markers either side belong to different runs. Treating them as a
        // pair would swallow both phrases into one.
        Edits.Run(Edits.Caret("**a** plain **b**", 0, 8), MarkdownEditCommand.Bold)
            .ShouldBe("**a** **plain** **b**");
    }

    [Fact]
    public void Italic_on_bold_text_adds_a_layer_rather_than_peeling_the_bold_off()
    {
        // "**word**" is bold, not two italics. Asking for italic has to nest.
        Edits.Run(Edits.Selection("**word**", 0, 2, 0, 6), MarkdownEditCommand.Italic)
            .ShouldBe("***word***");
    }

    [Fact]
    public void Italic_still_unwraps_genuine_italics()
    {
        Edits.Run(Edits.Selection("*word*", 0, 1, 0, 5), MarkdownEditCommand.Italic)
            .ShouldBe("word");
    }

    [Fact]
    public void Each_button_peels_off_its_own_layer_and_leaves_the_other()
    {
        // "***word***" is bold and italic together. Either mark has to be removable
        // without disturbing the other, which is what the toolbar promises by lighting
        // both buttons.
        Edits.Run(Edits.Caret("***word***", 0, 5), MarkdownEditCommand.Italic).ShouldBe("**word**");
        Edits.Run(Edits.Caret("***word***", 0, 5), MarkdownEditCommand.Bold).ShouldBe("*word*");
    }

    [Fact]
    public void Marks_of_different_kinds_nest_and_come_apart_one_at_a_time()
    {
        Edits.Run(Edits.Caret("~~**word**~~", 0, 6), MarkdownEditCommand.Bold).ShouldBe("~~word~~");
        Edits.Run(Edits.Caret("~~**word**~~", 0, 6), MarkdownEditCommand.Strikethrough)
            .ShouldBe("**word**");
        Edits.Run(Edits.Caret("~~***word***~~", 0, 6), MarkdownEditCommand.Italic)
            .ShouldBe("~~**word**~~");
    }

    [Fact]
    public void The_innermost_matching_layer_is_the_one_that_comes_off()
    {
        // Both pairs are bold and both wrap the caret. Taking the outer one off would
        // leave "**word**" still bold, which is not what pressing bold once should do.
        Edits.Run(Edits.Caret("**a **word** b**", 0, 7), MarkdownEditCommand.Bold)
            .ShouldBe("**a word b**");
    }

    [Fact]
    public void Selecting_the_markers_as_well_still_takes_off_the_layer_that_was_asked_for()
    {
        Edits.Run(Edits.Selection("***word***", 0, 0, 0, 10), MarkdownEditCommand.Italic)
            .ShouldBe("**word**");
        Edits.Run(Edits.Selection("***word***", 0, 0, 0, 10), MarkdownEditCommand.Bold)
            .ShouldBe("*word*");
    }

    [Fact]
    public void Markers_inside_a_code_span_are_text_and_are_left_alone()
    {
        // The asterisks in "`**word**`" are characters, not bold, so the pair the caret
        // sits inside is the code span. Taking it off hands the asterisks back.
        Edits.Run(Edits.Caret("`**word**`", 0, 5), MarkdownEditCommand.InlineCode)
            .ShouldBe("**word**");
    }

    [Fact]
    public void Inline_code_and_strikethrough_use_their_own_markers()
    {
        Edits.Run(Edits.Caret("hello", 0, 1), MarkdownEditCommand.InlineCode).ShouldBe("`hello`");
        Edits.Run(Edits.Caret("hello", 0, 1), MarkdownEditCommand.Strikethrough).ShouldBe("~~hello~~");
    }

    [Fact]
    public void A_selection_spanning_lines_gets_one_marker_at_each_end()
    {
        Edits.Run(Edits.Selection("one\ntwo", 0, 0, 1, 3), MarkdownEditCommand.Bold)
            .ShouldBe("**one\ntwo**");
    }

    [Fact]
    public void A_selection_ending_at_the_start_of_a_line_leaves_that_line_alone()
    {
        // Dragging down through "one" and releasing on the next line selects no text
        // there, so the closing marker belongs at the end of "one".
        Edits.Run(Edits.Selection("one\ntwo", 0, 0, 1, 0), MarkdownEditCommand.Bold)
            .ShouldBe("**one**\ntwo");
    }
}
