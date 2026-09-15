// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Markdown;

namespace PaulTechGuy.MQ.Editing;

/// <summary>
/// Moves list items in and out a level.
///
/// The one rule everything here follows: a child's marker lands on its parent's content column.
/// That is what CommonMark uses to decide what is inside what, so it comes out right for any
/// marker width without a preference to read — <c>- </c> nests at two and <c>10. </c> at four.
///
/// Three things make this unlike the other authoring commands.
///
/// It needs the whole document, <see cref="EditContextScope.Document"/>. The parent is not at a
/// known distance, the subtree that travels with an item is not either, and whether a line sits
/// inside a fenced code block can only be answered from the top of the file. A bigger window
/// would be worse than none: a scan that ran off the edge of one cannot tell that from reaching
/// the end of the document, so it would half-move a subtree and leave nothing to say it had.
///
/// It works on a copy of the document rather than computing edits directly. Shifting and
/// renumbering interact — moving an item out of one run and into another changes both — and doing
/// it to text and then diffing is far easier to be sure of than working every edit out in
/// advance. Only the lines that really changed become edits.
///
/// It does nothing at all when the caret is not on a list item. Indenting a paragraph in markdown
/// produces an indented code block, which is never what someone reaching for this meant.
/// </summary>
internal static class ListIndent
{
    /// <summary>Moves the selected items one level in (<paramref name="direction"/> 1) or out (-1).</summary>
    public static EditResult Apply(EditContext context, int direction)
    {
        List<(int Line, string Text)> content = Selections.Targets(context, out TextRange selection);

        if (content.Count == 0)
        {
            return EditResult.None;
        }

        bool[] guarded = MarkdownRegionScanner.FindProtectedLines(context.Lines);

        if (!Anchor(context, guarded, content, out int lastIndent))
        {
            return EditResult.None;
        }

        int last = Subtree(context, guarded, content[^1].Line, lastIndent);

        List<string> lines = [.. context.Lines];

        int first = direction > 0
            ? Indent(context, guarded, lines, content, last)
            : Outdent(context, guarded, lines, content, last);

        // Renumbering is a consequence of moving something. A press that turned out to have
        // nothing to move must not rewrite the numbers of the list it was pressed in.
        if (first < 0 || !Moved(context, lines))
        {
            return EditResult.None;
        }

        Renumber(lines, context.FirstLine, first, last);

        return Diff(context, lines, selection);
    }

    /// <summary>
    /// Going in, the block moves as one by whatever its shallowest selected item needs, and the
    /// first line is left behind when that item has nowhere to go.
    ///
    /// Moving as one is the part that matters. Letting each item find its own parent would be
    /// wrong here in a way it is not on the way out: an item already sitting at its parent's
    /// content column cannot move, so its parent would slide underneath it and the branch would
    /// flatten.
    ///
    /// But refusing outright when the shallowest item is stuck makes the commonest thing anyone
    /// tries — select a whole list, press Tab — do nothing at all, because a list's first item has
    /// no sibling above it to become a child of. So that line is dropped from the block and the
    /// rest tried again: the first item stays put and everything after it nests underneath it,
    /// which is what selecting a list and indenting it is asking for. Repeating that walks down
    /// the selection until something can move or nothing is left.
    ///
    /// Returns the first line actually moved, or -1 when nothing could be.
    /// </summary>
    private static int Indent(
        EditContext context,
        bool[] guarded,
        List<string> lines,
        List<(int Line, string Text)> content,
        int last)
    {
        for (int skip = 0; skip < content.Count; skip++)
        {
            if (!Shallowest(context, guarded, content, skip, out int anchorIndent))
            {
                return -1;
            }

            int first = content[skip].Line;
            int shift = StepIn(context, guarded, first, anchorIndent, out bool underParent);

            if (shift > 0)
            {
                Shift(lines, context.FirstLine, first, last, shift);

                return first;
            }

            /*
                Stuck, and it matters why.

                With no list above it at all, the item is the top of its list, and there is nothing
                it could ever nest under: leaving it behind and nesting the rest beneath it is what
                selecting a whole list and pressing Tab means. That is the case the loop is for.

                A first child is stuck for the opposite reason - its parent is right there. Leaving
                it behind would nest its own siblings under it, and a flat list selected once and
                indented three times would come out as a staircase, one extra level per press.
                Nothing is the right answer: the selection cannot go deeper as the block it is.
            */
            if (underParent)
            {
                return -1;
            }
        }

        return -1;
    }

    /// <summary>
    /// Going out, every selected item steps back to its own ancestor's indent, and one already at
    /// the margin simply stays there.
    ///
    /// This is the half that cannot move as a block. The shallowest item sets the distance, so as
    /// soon as it reaches the margin the distance is zero and a block shift can never move
    /// anything again — which makes flattening a nested list impossible without selecting it one
    /// level at a time. Letting each item find its own ancestor means repeated presses peel a
    /// level off at a time until the whole thing is flat, which is what every editor does and
    /// what anyone pressing it twice expects.
    ///
    /// Shape is lost on the way, and that is the point: flattening is the operation. Where
    /// nothing is clamped this computes exactly what the block shift would have, so a selection
    /// that can move freely behaves as it always did.
    /// </summary>
    private static int Outdent(
        EditContext context,
        bool[] guarded,
        List<string> lines,
        List<(int Line, string Text)> content,
        int last)
    {
        int first = content[0].Line;
        HashSet<int> selected = [.. content.Select(t => t.Line)];

        // What the last selected item moved by. Anything after it that is not itself a selected
        // item — a wrapped line, a child left out of the selection, a code block inside the item —
        // travels with it, so how far the selection happened to be dragged does not change the
        // result.
        int carried = 0;

        for (int line = first; line <= last; line++)
        {
            if (selected.Contains(line)
                && !Guarded(context, guarded, line)
                && context.LineAt(line) is { } text
                && ListStructure.TryRead(text, out ListStructure.ListItem item))
            {
                carried = StepOut(context, guarded, line, item.IndentWidth);
            }

            int i = line - context.FirstLine;

            if (carried > 0 && i >= 0 && i < lines.Count && lines[i].Trim().Length > 0)
            {
                lines[i] = lines[i][Removable(lines[i], carried)..];
            }
        }

        return first;
    }

    /// <summary>Whether anything actually changed.</summary>
    private static bool Moved(EditContext context, List<string> lines)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i] != context.Lines[i])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The shallowest selected list item, whose move decides the distance, and the depth of the
    /// last selected one, which is where the subtree scan starts from.
    /// </summary>
    private static bool Anchor(
        EditContext context,
        bool[] guarded,
        List<(int Line, string Text)> content,
        out int lastIndent)
    {
        lastIndent = 0;
        bool found = false;

        foreach ((int line, string text) in content)
        {
            if (Guarded(context, guarded, line) || !ListStructure.TryRead(text, out ListStructure.ListItem item))
            {
                continue;
            }

            found = true;
            lastIndent = item.IndentWidth;
        }

        return found;
    }

    /// <summary>
    /// The shallowest selected list item from <paramref name="from"/> down, which is the one whose
    /// move sets the distance for the whole block.
    /// </summary>
    private static bool Shallowest(
        EditContext context,
        bool[] guarded,
        List<(int Line, string Text)> content,
        int from,
        out int indent)
    {
        indent = int.MaxValue;

        for (int i = from; i < content.Count; i++)
        {
            (int line, string text) = content[i];

            if (!Guarded(context, guarded, line) && ListStructure.TryRead(text, out ListStructure.ListItem item))
            {
                indent = Math.Min(indent, item.IndentWidth);
            }
        }

        return indent != int.MaxValue;
    }

    /// <summary>
    /// How far right the block moves: onto the content column of the item above it at its own
    /// level, the sibling it becomes a child of.
    ///
    /// The scan starts above the selection rather than above the anchor. Starting at the anchor
    /// would walk back through the selection itself, and for "- A", "..- B", "- C" all selected it
    /// would offer A as a parent for C — a parent that is about to move as well.
    ///
    /// Zero when there is no such sibling. The first item of a list has nothing to nest under, and
    /// CommonMark cannot write a child of nothing: the spaces would go in and the preview would
    /// not change. <paramref name="underParent"/> says which kind of first item it was - one with
    /// a parent above it, or one at the top of its list with no list above at all - because the
    /// caller answers the two differently.
    /// </summary>
    private static int StepIn(
        EditContext context,
        bool[] guarded,
        int start,
        int anchorIndent,
        out bool underParent)
    {
        underParent = false;
        int blanks = 0;

        for (int i = start - 1; i >= context.FirstLine; i--)
        {
            if (context.LineAt(i) is not { } text || Guarded(context, guarded, i))
            {
                return 0;
            }

            if (text.Trim().Length == 0)
            {
                // One blank line inside a list is a loose list, not the end of it. Two is.
                if (++blanks > 1)
                {
                    return 0;
                }

                continue;
            }

            blanks = 0;

            if (ListStructure.TryRead(text, out ListStructure.ListItem above))
            {
                if (above.IndentWidth > anchorIndent)
                {
                    continue;
                }

                if (above.IndentWidth == anchorIndent)
                {
                    return above.ContentColumn - anchorIndent;
                }

                // A shallower item is the parent, which makes the anchor the first child at its
                // own level — so there is no sibling there to nest under either. Said so, rather
                // than folded into the zero: the caller treats a first child differently from an
                // item with no list above it at all.
                underParent = true;

                return 0;
            }

            /*
                A line that is not a list item. Only one at the margin ends the scan - a lazy
                continuation reads as the end of a list here, as it does everywhere else in the
                tree. Anything indented is text inside some item, and the scan keeps going to find
                out whose.

                Not "at or below the anchor's indent", which this used to say. A parent whose own
                text wraps puts its continuation lines at exactly its content column - which is
                exactly the indent of its children. Stopping there read the parent's wrapped text
                as the end of the list, so a first child was reported as having no list above it at
                all, and the caller took that as permission to leave it behind and nest its
                siblings underneath it.
            */
            if (IndentWidth(text) == 0)
            {
                return 0;
            }
        }

        return 0;
    }

    /// <summary>
    /// How far left the block can move: back to the nearest ancestor's own indent, or to the
    /// margin when it has no ancestor.
    /// </summary>
    private static int StepOut(EditContext context, bool[] guarded, int start, int anchorIndent)
    {
        if (anchorIndent == 0)
        {
            return 0;
        }

        int blanks = 0;

        for (int i = start - 1; i >= context.FirstLine; i--)
        {
            if (context.LineAt(i) is not { } text || Guarded(context, guarded, i))
            {
                break;
            }

            if (text.Trim().Length == 0)
            {
                if (++blanks > 1)
                {
                    break;
                }

                continue;
            }

            blanks = 0;

            if (ListStructure.TryRead(text, out ListStructure.ListItem above))
            {
                if (above.IndentWidth < anchorIndent)
                {
                    return anchorIndent - above.IndentWidth;
                }

                continue;
            }

            // The same rule as StepIn: only a line at the margin ends the scan. A lazy continuation
            // of a nested parent sits between that parent's indent and its children's, and
            // stopping on it would send the child to the margin instead of back to its parent.
            if (IndentWidth(text) == 0)
            {
                break;
            }
        }

        return anchorIndent;
    }

    /// <summary>
    /// The last line that travels with the block: everything indented deeper than the last
    /// selected item, which is its continuation lines and its nested children.
    ///
    /// A fenced block inside an item comes too. Its interior is guarded, so the blank-line rule is
    /// not applied there — two blank lines in a code sample do not end the list around it.
    /// </summary>
    private static int Subtree(EditContext context, bool[] guarded, int end, int lastIndent)
    {
        int blanks = 0;

        for (int i = end + 1; ; i++)
        {
            if (context.LineAt(i) is not { } text)
            {
                return end;
            }

            bool inside = Guarded(context, guarded, i);

            if (text.Trim().Length == 0)
            {
                if (!inside && ++blanks > 1)
                {
                    return end;
                }

                continue;
            }

            blanks = 0;

            if (IndentWidth(text) <= lastIndent)
            {
                return end;
            }

            end = i;
        }
    }

    /// <summary>
    /// Moves every non-blank line of the block to the right.
    ///
    /// Blank lines are left exactly as they were. Padding one turns it into a line of spaces,
    /// which the style checks flag as trailing whitespace and which
    /// <c>Blank_lines_inside_a_selection_stay_blank</c> exists to prevent.
    ///
    /// Spaces go in at the front rather than the indent being rewritten, so a line indented with
    /// tabs keeps them.
    /// </summary>
    private static void Shift(List<string> lines, int firstLine, int first, int last, int by)
    {
        for (int line = first; line <= last; line++)
        {
            int i = line - firstLine;

            if (i < 0 || i >= lines.Count || lines[i].Trim().Length == 0)
            {
                continue;
            }

            lines[i] = new string(' ', by) + lines[i];
        }
    }

    /// <summary>
    /// How many leading spaces can actually come off, up to <paramref name="wanted"/>. Stops at a
    /// tab rather than guessing how wide one is.
    /// </summary>
    private static int Removable(string text, int wanted)
    {
        int taken = 0;

        while (taken < wanted && taken < text.Length && text[taken] == ' ')
        {
            taken++;
        }

        return taken;
    }

    /// <summary>
    /// Puts the numbering right on the lists the move disturbed.
    ///
    /// Scoped to the enclosing list rather than the document: this is a side effect of moving an
    /// item, not the Format Document command, and a numbered list three pages away is none of its
    /// business. Inside that list it follows the rules the formatter follows, so the two never
    /// disagree about the same text — including leaving a run that repeats one number alone.
    /// </summary>
    private static void Renumber(List<string> lines, int firstLine, int first, int last)
    {
        bool[] guarded = MarkdownRegionScanner.FindProtectedLines(lines);

        int blockStart = first - firstLine;
        int blockEnd = last - firstLine;
        (int start, int end) = Enclosing(lines, guarded, blockStart, blockEnd);

        foreach (OrderedRuns.Run run in OrderedRuns.Find(lines, guarded))
        {
            if (run.Repeats || run.Lines[0] > end || run.Lines[^1] < start)
            {
                continue;
            }

            // A run the block begins is a new list, nested inside the item above it, so it starts
            // again at one. A run the block merely joins carries on from where it already was.
            int number = run.Lines[0] >= blockStart && run.Lines[0] <= blockEnd ? 1 : run.Start;

            foreach (int line in run.Lines)
            {
                if (ListStructure.TryRead(lines[line], out ListStructure.ListItem item))
                {
                    lines[line] = string.Concat(
                        lines[line][..item.IndentWidth],
                        number.ToString(CultureInfo.InvariantCulture),
                        item.Delimiter.ToString(),
                        lines[line][(item.IndentWidth + item.Marker.Length)..]);
                }

                number++;
            }
        }
    }

    /// <summary>The span of the list block the moved lines sit in.</summary>
    private static (int Start, int End) Enclosing(List<string> lines, bool[] guarded, int first, int last)
    {
        int start = first;
        int blanks = 0;

        for (int i = first - 1; i >= 0; i--)
        {
            if (!Continues(lines, guarded, i, ref blanks))
            {
                break;
            }

            start = i;
        }

        int end = last;
        blanks = 0;

        for (int i = last + 1; i < lines.Count; i++)
        {
            if (!Continues(lines, guarded, i, ref blanks))
            {
                break;
            }

            end = i;
        }

        return (start, end);
    }

    /// <summary>
    /// Whether a line carries the list on: an item, something indented under one, or a single
    /// blank between two of them.
    /// </summary>
    private static bool Continues(List<string> lines, bool[] guarded, int i, ref int blanks)
    {
        string text = lines[i];

        if (text.Trim().Length == 0)
        {
            return ++blanks <= 1;
        }

        blanks = 0;

        return !guarded[i] && (ListStructure.IsItem(text) || IndentWidth(text) > 0);
    }

    /// <summary>One whole-line edit per line that really changed, and the selection moved with them.</summary>
    private static EditResult Diff(EditContext context, List<string> lines, TextRange selection)
    {
        List<TextEdit> edits = [];
        int startDelta = 0;
        int endDelta = 0;

        for (int i = 0; i < lines.Count; i++)
        {
            string before = context.Lines[i];

            if (lines[i] == before)
            {
                continue;
            }

            int line = context.FirstLine + i;

            edits.Add(new TextEdit(Selections.WholeLine(line, before), lines[i]));

            int delta = lines[i].Length - before.Length;

            if (line == selection.Start.Line)
            {
                startDelta = delta;
            }

            if (line == selection.End.Line)
            {
                endDelta = delta;
            }
        }

        if (edits.Count == 0)
        {
            return EditResult.None;
        }

        // Carry the selection along by however much its own lines moved, so it still covers the
        // same words and the command can be pressed twice running.
        var moved = new TextRange(
            new TextPosition(selection.Start.Line, Math.Max(0, selection.Start.Column + startDelta)),
            new TextPosition(selection.End.Line, Math.Max(0, selection.End.Column + endDelta)));

        return new EditResult(edits, moved);
    }

    private static bool Guarded(EditContext context, bool[] guarded, int line)
    {
        int i = line - context.FirstLine;

        return i >= 0 && i < guarded.Length && guarded[i];
    }

    private static int IndentWidth(string text) => text.Length - text.TrimStart(' ', '\t').Length;
}
