// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Editing.Tests;

public class ToggleStateTests
{
    [Fact]
    public void A_caret_inside_emphasis_reads_as_active()
    {
        Edits.Describe(Edits.Caret("**bold text**", 0, 8)).Bold.ShouldBeTrue();
        Edits.Describe(Edits.Caret("*slanted*", 0, 4)).Italic.ShouldBeTrue();
        Edits.Describe(Edits.Caret("`code`", 0, 3)).InlineCode.ShouldBeTrue();
        Edits.Describe(Edits.Caret("~~gone~~", 0, 4)).Strikethrough.ShouldBeTrue();
    }

    [Fact]
    public void Plain_text_reads_as_inactive()
    {
        MarkdownMarkState state = Edits.Describe(Edits.Caret("plain words", 0, 3));

        state.Bold.ShouldBeFalse();
        state.Italic.ShouldBeFalse();
        state.InlineCode.ShouldBeFalse();
        state.Strikethrough.ShouldBeFalse();
        state.HeadingLevel.ShouldBe(0);
    }

    [Fact]
    public void Bold_text_does_not_read_as_italic()
    {
        // Asking for italic there nests rather than unwrapping, so the italic button must
        // not claim it is already on.
        MarkdownMarkState state = Edits.Describe(Edits.Caret("**bold**", 0, 4));

        state.Bold.ShouldBeTrue();
        state.Italic.ShouldBeFalse();
    }

    [Fact]
    public void A_word_that_is_bold_and_italic_reads_as_both()
    {
        // The reported defect: "***word***" showed bold alone, so the italic the user had
        // just applied could not be taken off again.
        MarkdownMarkState state = Edits.Describe(Edits.Caret("***word***", 0, 5));

        state.Bold.ShouldBeTrue();
        state.Italic.ShouldBeTrue();
    }

    [Fact]
    public void Every_layer_wrapping_the_caret_reads_as_active()
    {
        MarkdownMarkState state = Edits.Describe(Edits.Caret("~~***word***~~", 0, 6));

        state.Bold.ShouldBeTrue();
        state.Italic.ShouldBeTrue();
        state.Strikethrough.ShouldBeTrue();
        state.InlineCode.ShouldBeFalse();
    }

    [Fact]
    public void Markers_inside_a_code_span_are_text_rather_than_emphasis()
    {
        // "`**word**`" is four literal asterisks around a word. Lighting bold there would
        // promise a button that could turn it off, and there is nothing to turn off.
        MarkdownMarkState state = Edits.Describe(Edits.Caret("`**word**`", 0, 5));

        state.InlineCode.ShouldBeTrue();
        state.Bold.ShouldBeFalse();
    }

    [Fact]
    public void Line_prefixes_read_from_the_line_the_caret_is_on()
    {
        Edits.Describe(Edits.Caret("- item", 0, 3)).BulletList.ShouldBeTrue();
        Edits.Describe(Edits.Caret("1. item", 0, 4)).NumberedList.ShouldBeTrue();
        Edits.Describe(Edits.Caret("- [ ] item", 0, 8)).TaskList.ShouldBeTrue();
        Edits.Describe(Edits.Caret("> quoted", 0, 4)).Blockquote.ShouldBeTrue();
    }

    [Fact]
    public void A_task_item_is_not_reported_as_a_plain_bullet()
    {
        MarkdownMarkState state = Edits.Describe(Edits.Caret("- [ ] item", 0, 8));

        state.TaskList.ShouldBeTrue();
        state.BulletList.ShouldBeFalse();
    }

    [Fact]
    public void Heading_level_is_reported_for_the_dropdown_label()
    {
        Edits.Describe(Edits.Caret("## Title", 0, 4)).HeadingLevel.ShouldBe(2);
        Edits.Describe(Edits.Caret("###### Deep", 0, 8)).HeadingLevel.ShouldBe(6);
    }

    [Fact]
    public void A_selection_of_mixed_heading_levels_reports_none()
    {
        // The heading command is all-or-nothing across the selection, so the label has to
        // be too, or it would name a level the button will not produce.
        Edits.Describe(Edits.Selection("# One\n## Two", 0, 0, 1, 6)).HeadingLevel.ShouldBe(0);
    }

    [Fact]
    public void A_partly_marked_selection_reads_as_inactive()
    {
        // Pressing the button there finishes the job rather than clearing it, so showing
        // it lit would promise the opposite of what happens.
        Edits.Describe(Edits.Selection("- one\ntwo", 0, 2, 1, 3)).BulletList.ShouldBeFalse();
        Edits.Describe(Edits.Selection("- one\n- two", 0, 2, 1, 5)).BulletList.ShouldBeTrue();
    }

    [Fact]
    public void Emphasis_reads_inactive_across_a_multi_line_selection()
    {
        // Emphasis spanning lines is always added, never removed, so this is honest
        // rather than a gap. Documented on MarkdownMarkState.
        Edits.Describe(Edits.Selection("**one\ntwo**", 0, 2, 1, 3)).Bold.ShouldBeFalse();
    }
}

/// <summary>
/// The test that stops the toolbar lying.
///
/// Pressing a toggle must flip what the toolbar reports. If the indicator says "on" and
/// pressing it leaves it on, the button has made the text more bold rather than less — the
/// exact defect that shipped in the first release and went unnoticed because the predicate
/// and the action were never compared.
/// </summary>
public class ToggleStateConsistencyTests
{
    private static readonly MarkdownEditCommand[] Emphasis =
    [
        MarkdownEditCommand.Bold,
        MarkdownEditCommand.Italic,
        MarkdownEditCommand.Strikethrough,
        MarkdownEditCommand.InlineCode,
    ];

    private static readonly MarkdownEditCommand[] Toggles =
    [
        .. Emphasis,
        MarkdownEditCommand.BulletList,
        MarkdownEditCommand.NumberedList,
        MarkdownEditCommand.TaskList,
        MarkdownEditCommand.Blockquote,
    ];

    public static TheoryData<string, int> Fixtures => new()
    {
        { "plain words here", 6 },
        { "**bold text**", 8 },
        { "**bold**", 4 },
        { "*slanted words*", 5 },
        { "`code span`", 3 },
        { "~~struck out~~", 5 },
        { "**a** plain **b**", 8 },
        { "***word***", 5 },
        { "~~**bold**~~", 6 },
        { "**~~struck~~**", 6 },
        { "~~***word***~~", 6 },
        { "`**literal**`", 5 },
        { "- item", 3 },
        { "1. item", 4 },
        { "- [ ] item", 8 },
        { "> quoted", 4 },
        { "## Heading", 5 },
        { "    indented", 7 },
        { "- **bold** item", 4 },
    };

    /// <summary>
    /// The same emphasis cases with the code spans left out, for the reverse direction.
    /// Inside a code span "**" is two literal asterisks, so the bold button genuinely
    /// cannot turn bold on there and honestly stays unlit.
    /// </summary>
    public static TheoryData<string, int> EmphasisFixtures => new()
    {
        { "plain words here", 6 },
        { "**bold text**", 8 },
        { "**bold**", 4 },
        { "*slanted words*", 5 },
        { "~~struck out~~", 5 },
        { "***word***", 5 },
        { "~~**bold**~~", 6 },
        { "**~~struck~~**", 6 },
        { "~~***word***~~", 6 },
        { "- **bold** item", 4 },
        { "## Heading", 5 },
    };

    /// <summary>The guarantee that matters: a lit button turns itself off.</summary>
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void A_toggle_that_reads_active_turns_itself_off(string document, int column)
    {
        foreach (MarkdownEditCommand command in Toggles)
        {
            EditContext before = Edits.Caret(document, 0, column);

            if (!Read(Edits.Describe(before), command))
            {
                continue;
            }

            MarkdownMarkState after = Edits.Describe(Edits.Then(before, command));

            Read(after, command).ShouldBeFalse(
                $"\"{document}\" at column {column}: {command} was lit, so pressing it "
                    + "should have cleared it rather than adding another layer.");
        }
    }

    /// <summary>
    /// The other half, for emphasis: an unlit button lights up.
    ///
    /// This is the direction that failed. Italic inside bold added a layer the toolbar
    /// could not see, so the button stayed unlit and every further press added another,
    /// leaving no way back. Asserting it here means a mark the editor can put on is
    /// always one it can find again.
    /// </summary>
    [Theory]
    [MemberData(nameof(EmphasisFixtures))]
    public void An_emphasis_toggle_that_reads_inactive_turns_itself_on(string document, int column)
    {
        foreach (MarkdownEditCommand command in Emphasis)
        {
            EditContext before = Edits.Caret(document, 0, column);

            if (Read(Edits.Describe(before), command))
            {
                continue;
            }

            MarkdownMarkState after = Edits.Describe(Edits.Then(before, command));

            Read(after, command).ShouldBeTrue(
                $"\"{document}\" at column {column}: {command} was unlit, so pressing it "
                    + "should have put a mark on that the toolbar can find again.");
        }
    }

    [Fact]
    public void Italic_inside_bold_is_reversible()
    {
        // The reported defect, kept as a worked example. Bolding a word and then
        // italicising it gives "***word***", where the toolbar showed bold alone: the
        // italic button was unlit, and pressing it added a fourth asterisk rather than
        // taking the italic off.
        EditContext bold = Edits.Caret("**word**", 0, 4);

        Edits.Describe(bold).Italic.ShouldBeFalse();

        EditContext nested = Edits.Then(bold, MarkdownEditCommand.Italic);

        Edits.Describe(nested).Bold.ShouldBeTrue();
        Edits.Describe(nested).Italic.ShouldBeTrue();

        // Either button now peels off its own layer and leaves the other standing.
        Edits.Run(nested, MarkdownEditCommand.Italic).ShouldBe("**word**");
        Edits.Run(nested, MarkdownEditCommand.Bold).ShouldBe("*word*");
    }

    private static bool Read(MarkdownMarkState state, MarkdownEditCommand command) => command switch
    {
        MarkdownEditCommand.Bold => state.Bold,
        MarkdownEditCommand.Italic => state.Italic,
        MarkdownEditCommand.Strikethrough => state.Strikethrough,
        MarkdownEditCommand.InlineCode => state.InlineCode,
        MarkdownEditCommand.BulletList => state.BulletList,
        MarkdownEditCommand.NumberedList => state.NumberedList,
        MarkdownEditCommand.TaskList => state.TaskList,
        MarkdownEditCommand.Blockquote => state.Blockquote,
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };
}
