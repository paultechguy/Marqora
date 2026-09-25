// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Markdown;

namespace PaulTechGuy.MQ.Editing;

/// <summary>
/// Renumber List: rewrites one numbered list as <c>0. 0. 0.</c>, <c>1. 1. 1.</c> or
/// <c>1. 2. 3.</c>.
///
/// Which list is decided the same way for a caret and a selection. Every line the selection
/// touches is matched to the deepest run it sits inside — an item of the run, a continuation or
/// nested line under one of its items, or a blank between them — and the shallowest of those
/// wins. A caret is a selection of one line, so it picks the list it is in; a selection that
/// covers a whole list, sub-lists and all, picks the outer one. Two separate lists at the same
/// level both get renumbered, which is what selecting both says.
///
/// Only that level's markers change. A nested list is its own run and keeps its numbers: the
/// command is about the list the author pointed at, and a sub-list numbered differently on
/// purpose is none of its business.
///
/// Runs come from <see cref="OrderedRuns"/>, the same reading the formatter and the indent
/// command use, so "which lines are one list" never gets two answers in the same document.
/// </summary>
internal static class ListRenumber
{
    /// <summary>What <see cref="Apply"/> would act on, or null when the caret is not in a numbered list.</summary>
    public static OrderedListSummary? Describe(EditContext context)
    {
        List<OrderedRuns.Run> runs = Picked(context, out bool[] guarded);

        if (runs.Count == 0)
        {
            return null;
        }

        // Offer whatever would change something. A list that counts up is turned into the
        // shorthand; one already in the shorthand is counted back up.
        ListNumbering suggested = runs[0].Repeats ? ListNumbering.Sequential : ListNumbering.Ones;

        return new OrderedListSummary(
            runs.Sum(r => r.Lines.Count),
            suggested,
            runs.All(run => FollowsABreak(context.Lines, guarded, run.Lines[0])));
    }

    /// <summary>Renumbers the picked lists. Returns no edits when they already read that way.</summary>
    public static EditResult Apply(EditContext context, ListNumbering numbering)
    {
        List<OrderedRuns.Run> runs = Picked(context, out bool[] guarded);

        // Refused rather than trusted to the prompt, which grays the choice out: writing it would
        // turn the list into paragraph text, and nothing afterwards would say why.
        if (runs.Count == 0
            || (numbering == ListNumbering.Zeros && !runs.All(run => FollowsABreak(context.Lines, guarded, run.Lines[0]))))
        {
            return EditResult.None;
        }

        List<TextEdit> edits = [];

        foreach (OrderedRuns.Run run in runs)
        {
            int end = End(context.Lines, guarded, run);

            for (int k = 0; k < run.Lines.Count; k++)
            {
                int line = run.Lines[k];
                string text = context.Lines[line];

                if (!ListStructure.TryRead(text, out ListStructure.ListItem item))
                {
                    continue;
                }

                int number = numbering switch
                {
                    ListNumbering.Zeros => 0,
                    ListNumbering.Ones => 1,
                    _ => k + 1,
                };

                string digits = number.ToString(CultureInfo.InvariantCulture);
                int oldDigits = item.Marker.Length - 1;

                if (digits == text.Substring(item.IndentWidth, oldDigits))
                {
                    continue;
                }

                int documentLine = context.FirstLine + line;

                // Only the digits are replaced, so a caret sitting in the item's text is carried
                // along by the editor rather than thrown to the end of a rewritten line.
                edits.Add(new TextEdit(
                    new TextRange(
                        new TextPosition(documentLine, item.IndentWidth),
                        new TextPosition(documentLine, item.IndentWidth + oldDigits)),
                    digits));

                // A wider marker moves the item's content column right, and anything under the
                // item that sat exactly on the old column would fall out of it - a nested list
                // would become a sibling, a continuation paragraph would leave the list. So what
                // belongs to the item moves with it. A narrower marker needs nothing: a child
                // one or two columns past the content column is still inside the item.
                int grow = digits.Length - oldDigits;

                if (grow > 0)
                {
                    int last = k + 1 < run.Lines.Count ? run.Lines[k + 1] - 1 : end;

                    Push(context, guarded, edits, line + 1, last, item.ContentColumn, grow);
                }
            }
        }

        return edits.Count == 0 ? EditResult.None : new EditResult(edits, null);
    }

    /// <summary>Indents the lines an item owns by <paramref name="by"/> columns.</summary>
    private static void Push(
        EditContext context,
        bool[] guarded,
        List<TextEdit> edits,
        int first,
        int last,
        int column,
        int by)
    {
        string spaces = new(' ', by);

        for (int i = first; i <= last; i++)
        {
            string text = context.Lines[i];

            // A line inside a fence moves too, however little it is indented: the fence it
            // belongs to is moving, and the code must keep its shape relative to it.
            if (text.Trim().Length == 0 || (IndentOf(text) < column && !guarded[i]))
            {
                continue;
            }

            TextPosition at = new(context.FirstLine + i, 0);
            edits.Add(new TextEdit(new TextRange(at, at), spaces));
        }
    }

    /// <summary>
    /// The runs the selection points at, in document order. Empty when it points at none.
    /// </summary>
    private static List<OrderedRuns.Run> Picked(EditContext context, out bool[] guarded)
    {
        guarded = MarkdownRegionScanner.FindProtectedLines(context.Lines);

        bool[] fenced = guarded;
        List<OrderedRuns.Run> runs = Join(context.Lines, fenced, OrderedRuns.Find(context.Lines, fenced));

        if (runs.Count == 0)
        {
            return [];
        }

        int[] ends = [.. runs.Select(run => End(context.Lines, fenced, run))];
        TextRange selection = Selections.Normalize(context);

        int from = Math.Max(0, selection.Start.Line - context.FirstLine);
        int to = Math.Min(context.Lines.Count - 1, selection.End.Line - context.FirstLine);

        var touched = new List<int>();

        for (int line = from; line <= to; line++)
        {
            int deepest = -1;

            for (int r = 0; r < runs.Count; r++)
            {
                if (Contains(context.Lines, fenced, runs[r], ends[r], line)
                    && (deepest < 0 || runs[r].Indent > runs[deepest].Indent))
                {
                    deepest = r;
                }
            }

            if (deepest >= 0 && !touched.Contains(deepest))
            {
                touched.Add(deepest);
            }
        }

        if (touched.Count == 0)
        {
            return [];
        }

        int level = touched.Min(r => runs[r].Indent);

        return [.. touched.Where(r => runs[r].Indent == level).Order().Select(r => runs[r])];
    }

    /// <summary>
    /// Whether <paramref name="line"/> belongs to <paramref name="run"/>: one of its items, or
    /// anything between its first item and its end that is indented under it.
    /// </summary>
    private static bool Contains(IReadOnlyList<string> lines, bool[] guarded, OrderedRuns.Run run, int end, int line)
    {
        if (line < run.Lines[0] || line > end)
        {
            return false;
        }

        string text = lines[line];

        return run.Lines.Contains(line) || guarded[line] || text.Trim().Length == 0 || IndentOf(text) > run.Indent;
    }

    /// <summary>
    /// Puts back together a list that <see cref="OrderedRuns"/> cut at a fence inside one of its
    /// items.
    ///
    /// The reader ends every run at a protected line, and so does the formatter. That is safe for
    /// them, because each keeps whatever number a run starts at, so the halves stay as written.
    /// It is not safe here: counting each half from one would write 1, 2, 1, 2 down a list of
    /// four steps with a code sample in the second. So a run that picks up at the same indent and
    /// delimiter, with nothing between but lines indented under the item above - a fence among
    /// them - is read as the same list carrying on.
    /// </summary>
    private static List<OrderedRuns.Run> Join(
        IReadOnlyList<string> lines,
        bool[] guarded,
        IReadOnlyList<OrderedRuns.Run> runs)
    {
        List<OrderedRuns.Run> joined = [];

        foreach (OrderedRuns.Run run in runs)
        {
            int previous = joined.FindLastIndex(r => r.Indent == run.Indent);

            if (previous >= 0
                && joined[previous].Delimiter == run.Delimiter
                && Continues(lines, guarded, joined[previous], run.Lines[0]))
            {
                OrderedRuns.Run before = joined[previous];

                joined[previous] = before with
                {
                    Lines = [.. before.Lines, .. run.Lines],
                    Numbers = [.. before.Numbers, .. run.Numbers],
                };

                continue;
            }

            joined.Add(run);
        }

        return joined;
    }

    /// <summary>
    /// Whether everything between <paramref name="run"/>'s last item and <paramref name="next"/>
    /// hangs under that item, with a fence among it. Without the fence the reader had some other
    /// reason to end the run - a delimiter change, a number jumping forward after a blank - and
    /// that reason stands.
    /// </summary>
    private static bool Continues(IReadOnlyList<string> lines, bool[] guarded, OrderedRuns.Run run, int next)
    {
        bool fence = false;
        int blanks = 0;

        for (int i = run.Lines[^1] + 1; i < next; i++)
        {
            string text = lines[i];

            if (guarded[i])
            {
                // The fence's own lines have to be inside the item; the code between them may sit
                // anywhere.
                if (IsFenceDelimiter(text) && IndentOf(text) <= run.Indent)
                {
                    return false;
                }

                fence = true;
                blanks = 0;
                continue;
            }

            if (text.Trim().Length == 0)
            {
                if (++blanks > 1)
                {
                    return false;
                }

                continue;
            }

            blanks = 0;

            if (IndentOf(text) <= run.Indent)
            {
                return false;
            }
        }

        return fence;
    }

    private static bool IsFenceDelimiter(string text)
    {
        string trimmed = text.TrimStart();

        return trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal);
    }

    /// <summary>
    /// The last line of a run, counting what hangs under its last item: continuation lines and
    /// nested lists indented past the run's markers, across single blank lines.
    /// </summary>
    private static int End(IReadOnlyList<string> lines, bool[] guarded, OrderedRuns.Run run)
    {
        int end = run.Lines[^1];
        bool inFence = false;

        for (int i = end + 1; i < lines.Count; i++)
        {
            string text = lines[i];

            // A fence the item owns opens indented under it, and everything up to its close is
            // the item's, however little of the code itself is indented.
            if (inFence || guarded[i])
            {
                bool delimiter = IsFenceDelimiter(text);

                if (!inFence && (!delimiter || IndentOf(text) <= run.Indent))
                {
                    break;
                }

                inFence = !inFence ? true : !delimiter;
                end = i;
                continue;
            }

            if (text.Trim().Length == 0)
            {
                // A blank only belongs to the item if something indented under it follows.
                if (i + 1 < lines.Count && lines[i + 1].Trim().Length > 0 && IndentOf(lines[i + 1]) > run.Indent)
                {
                    continue;
                }

                break;
            }

            if (IndentOf(text) <= run.Indent)
            {
                break;
            }

            end = i;
        }

        return end;
    }

    /// <summary>
    /// Whether the line above a list's first item leaves it free to start at any number: nothing
    /// above it, a blank, a heading, a thematic break, or the closing line of a fence. Any other
    /// text is read as a paragraph the list would be interrupting.
    ///
    /// CommonMark lets a list interrupt a paragraph only when it starts at one. A list directly
    /// under a line of text - including a sub-list directly under its parent item's text, which
    /// is the usual way to write one - stops being a list the moment its first number becomes 0,
    /// and its lines join the paragraph above.
    /// </summary>
    private static bool FollowsABreak(IReadOnlyList<string> lines, bool[] guarded, int first)
    {
        if (first == 0)
        {
            return true;
        }

        string above = lines[first - 1];
        string trimmed = above.Trim();

        if (trimmed.Length == 0 || guarded[first - 1])
        {
            return true;
        }

        // An empty list item above has no paragraph for this one to interrupt.
        if (ListStructure.TryRead(above, out ListStructure.ListItem item))
        {
            return item.TaskBox.Length + item.Content.Trim().Length == 0;
        }

        if (IndentOf(above) < 4 && trimmed[0] == '#')
        {
            string hashes = trimmed.TrimStart('#');

            return trimmed.Length - hashes.Length <= 6 && (hashes.Length == 0 || hashes[0] is ' ' or '\t');
        }

        return trimmed.Length >= 3
            && trimmed[0] is '-' or '*' or '_'
            && trimmed.All(c => c == trimmed[0] || c is ' ' or '\t')
            && trimmed.Count(c => c == trimmed[0]) >= 3;
    }

    private static int IndentOf(string text) => text.Length - text.TrimStart().Length;
}
