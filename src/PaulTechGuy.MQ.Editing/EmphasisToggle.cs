// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Editing;

/// <summary>
/// Bold, italic, strikethrough and inline code: paired markers that go on if they are
/// absent and come off if they are already there.
///
/// Which of the two it is comes from <see cref="InlineMarks"/>, which pairs the markers on
/// the whole line rather than looking just outside the selection. That is what lets a mark
/// be taken off from inside another one: with the caret in "***word***", bold and italic
/// are both on, and each button removes its own layer and leaves the other standing.
/// </summary>
internal static class EmphasisToggle
{
    public static EditResult Apply(EditContext context, string marker)
    {
        TextRange selection = Selections.Normalize(context);

        // A selection spanning lines gets markers at each end rather than per line;
        // markdown emphasis runs across a soft line break perfectly well.
        if (!selection.IsSingleLine)
        {
            return new EditResult(
                [
                    new TextEdit(TextRange.At(selection.Start), marker),
                    new TextEdit(TextRange.At(selection.End), marker),
                ],
                null);
        }

        if (Resolve(context, selection) is not { } target)
        {
            return EditResult.None;
        }

        (int line, string text, int start, int end) = target;

        if (start == end)
        {
            // The caret is not in a word. Leave an empty pair behind with the caret
            // inside, so whatever they type next lands between the markers.
            var caret = new TextPosition(line, start + marker.Length);

            return new EditResult(
                [new TextEdit(TextRange.At(new TextPosition(line, start)), marker + marker)],
                TextRange.At(caret));
        }

        if (MarkToRemove(text, start, end, marker) is { } mark)
        {
            return Replace(line, mark.OpenStart, mark.CloseEnd, text[mark.OpenEnd..mark.CloseStart]);
        }

        return Replace(line, start, end, marker + text[start..end] + marker, marker.Length);
    }

    /// <summary>
    /// Whether pressing this marker's button would take markers off rather than put them on.
    ///
    /// This is what the toolbar shows, and it asks <see cref="MarkToRemove"/> the same
    /// question <see cref="Apply"/> does, so the indicator cannot disagree with what the
    /// button then does. It answers "what will this do", not "is this text bold": a
    /// selection spanning lines reads false, because emphasis across lines is always
    /// added, never removed.
    /// </summary>
    public static bool WouldRemove(EditContext context, string marker)
    {
        TextRange selection = Selections.Normalize(context);

        if (!selection.IsSingleLine || Resolve(context, selection) is not { } target)
        {
            return false;
        }

        (_, string text, int start, int end) = target;

        return start != end && MarkToRemove(text, start, end, marker) is not null;
    }

    /// <summary>
    /// The line and the span the command acts on, with an empty selection widened to the
    /// word under the caret — that is what the user means almost every time they hit
    /// Ctrl+B mid-word. A span that comes back empty means the caret was not in a word.
    /// </summary>
    private static (int Line, string Text, int Start, int End)? Resolve(EditContext context, TextRange selection)
    {
        int line = selection.Start.Line;

        if (context.LineAt(line) is not { } text)
        {
            return null;
        }

        int start = Math.Clamp(selection.Start.Column, 0, text.Length);
        int end = Math.Clamp(selection.End.Column, 0, text.Length);

        if (start == end)
        {
            (start, end) = WordAt(text, start);
        }

        return (line, text, start, end);
    }

    /// <summary>
    /// The pair this button would take off, or null when it would add one instead.
    ///
    /// The innermost pair wrapping the span wins, so asking for bold inside
    /// "~~***word***~~" strips the "**" and leaves the italic and the strikethrough alone.
    /// </summary>
    private static InlineMark? MarkToRemove(string text, int start, int end, string marker)
    {
        IReadOnlyList<InlineMark> marks = InlineMarks.On(text);
        InlineMark? innermost = null;

        foreach (InlineMark mark in marks)
        {
            if (mark.Marker != marker || !mark.Wraps(start, end))
            {
                continue;
            }

            if (innermost is null || mark.OpenStart > innermost.Value.OpenStart)
            {
                innermost = mark;
            }
        }

        return innermost ?? Selected(marks, start, end, marker);
    }

    /// <summary>
    /// The pair a selection covers outright, markers and all, or one nested directly
    /// inside it: selecting the whole of "***word***" and pressing bold means the "**"
    /// layer within it, not another pair wrapped round the lot.
    ///
    /// The selection has to land exactly on a pair for this, which is what keeps
    /// "**a** **b**" selected end to end from being read as one bold run — the markers at
    /// either end close and open different phrases.
    /// </summary>
    private static InlineMark? Selected(IReadOnlyList<InlineMark> marks, int start, int end, string marker)
    {
        while (true)
        {
            InlineMark? covered = null;

            foreach (InlineMark mark in marks)
            {
                if (mark.OpenStart == start && mark.CloseEnd == end)
                {
                    covered = mark;

                    break;
                }
            }

            if (covered is not { } layer)
            {
                return null;
            }

            if (layer.Marker == marker)
            {
                return layer;
            }

            // Not this one, so step inside it and look at what it wraps.
            start = layer.OpenEnd;
            end = layer.CloseStart;
        }
    }

    /// <summary>
    /// Replaces a span and leaves the text that landed there selected, so a toggle can be
    /// pressed twice in a row and act on the same words both times.
    /// </summary>
    private static EditResult Replace(int line, int start, int end, string text, int selectionOffset = 0)
    {
        var range = new TextRange(new TextPosition(line, start), new TextPosition(line, end));
        int from = start + selectionOffset;
        int to = start + text.Length - selectionOffset;

        return new EditResult(
            [new TextEdit(range, text)],
            new TextRange(new TextPosition(line, from), new TextPosition(line, to)));
    }

    private static (int Start, int End) WordAt(string text, int caret)
    {
        static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c) || c == '_';

        int start = caret;
        int end = caret;

        while (start > 0 && IsWordCharacter(text[start - 1]))
        {
            start--;
        }

        while (end < text.Length && IsWordCharacter(text[end]))
        {
            end++;
        }

        return (start, end);
    }
}
