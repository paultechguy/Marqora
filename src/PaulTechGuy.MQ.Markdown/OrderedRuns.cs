// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Markdown;

/// <summary>
/// Finds the runs of ordered list items in a document: which lines belong to one numbered
/// sequence, and what the author numbered them.
///
/// A run is not simply "every ordered item at this indent". Four things end one, and leaving any
/// of them out makes a caller disagree with the formatter about the same document:
///
/// <list type="bullet">
/// <item>Two blank lines in a row. One is a loose list; two is the end of it.</item>
/// <item>A non-list line at the left margin. Indented under the item it is a continuation.</item>
/// <item>A change of delimiter. CommonMark starts a new list when <c>1.</c> becomes <c>1)</c>.</item>
/// <item>A number that jumps forward after a blank line — the author starting a new list rather
/// than losing count, so the sequence restarts from whatever they wrote.</item>
/// </list>
///
/// The formatter carries the same knowledge in <c>RenumberOrderedLists</c>, welded to the mutable
/// line type it needs for rewriting. This is the read-only half, in the foundation project where
/// both can eventually share it; see the note on <see cref="MarkdownRegionScanner"/>.
/// </summary>
public static class OrderedRuns
{
    /// <summary>One numbered sequence, in document order.</summary>
    /// <param name="Indent">The indent width every item in the run shares.</param>
    /// <param name="Delimiter">The <c>.</c> or <c>)</c> they all carry.</param>
    /// <param name="Lines">The line number of each item.</param>
    /// <param name="Numbers">The number each item was written with.</param>
    public sealed record Run(
        int Indent,
        char Delimiter,
        IReadOnlyList<int> Lines,
        IReadOnlyList<int> Numbers)
    {
        /// <summary>
        /// Whether every item repeats one number — the <c>1. 1. 1.</c> shorthand, which a
        /// renumbering caller must leave exactly as it found it.
        ///
        /// A run that agrees only in part, <c>1. 1. 2.</c>, is half fixed by hand and gets
        /// finished rather than frozen where it was left.
        ///
        /// A run of one item is never this. There is nothing for it to agree with, and reading it
        /// as the shorthand would freeze the number of every list that happens to have a single
        /// entry — including the one a freshly nested item starts.
        /// </summary>
        public bool Repeats => Numbers.Count > 1 && Numbers.All(n => n == Numbers[0]);

        /// <summary>The number the run starts from.</summary>
        public int Start => Numbers.Count > 0 ? Numbers[0] : 1;
    }

    /// <summary>
    /// Every ordered run in <paramref name="lines"/>, in the order they start.
    /// </summary>
    /// <param name="protectedLines">
    /// Lines to read straight past, from <see cref="MarkdownRegionScanner.FindProtectedLines"/>.
    /// A numbered line inside a fenced block is a line of code, and it ends any run above it the
    /// same way a left-margin line does.
    /// </param>
    public static IReadOnlyList<Run> Find(IReadOnlyList<string> lines, bool[]? protectedLines = null)
    {
        ArgumentNullException.ThrowIfNull(lines);

        // Indent width to the next number expected at that depth, to the run that expectation
        // belongs to, and to the delimiter it is using. Runs are counted off rather than keyed by
        // depth, so a depth returned to after a gap is a new run and not the old one carrying on.
        var counters = new Dictionary<int, int>();
        var runAt = new Dictionary<int, int>();
        var delimiterAt = new Dictionary<int, char>();

        var order = new List<int>();
        var meta = new Dictionary<int, (int Indent, char Delimiter)>();
        var found = new Dictionary<int, (List<int> Lines, List<int> Numbers)>();

        int nextRun = 0;
        int blanks = 0;
        bool blankBefore = false;

        void EndEveryRun()
        {
            counters.Clear();
            runAt.Clear();
            delimiterAt.Clear();
        }

        for (int i = 0; i < lines.Count; i++)
        {
            if (protectedLines is not null && i < protectedLines.Length && protectedLines[i])
            {
                EndEveryRun();
                continue;
            }

            string text = lines[i];

            if (text.Trim().Length == 0)
            {
                blankBefore = true;

                if (++blanks > 1)
                {
                    EndEveryRun();
                }

                continue;
            }

            blanks = 0;
            bool hadBlank = blankBefore;
            blankBefore = false;

            if (!ListStructure.TryRead(text, out ListStructure.ListItem item) || !item.Ordered)
            {
                if (!char.IsWhiteSpace(text[0]))
                {
                    EndEveryRun();
                }

                continue;
            }

            int indent = item.IndentWidth;

            // A deeper list restarts; stepping back out drops the deeper counters.
            foreach (int deeper in counters.Keys.Where(k => k > indent).ToList())
            {
                counters.Remove(deeper);
                runAt.Remove(deeper);
                delimiterAt.Remove(deeper);
            }

            bool known = counters.TryGetValue(indent, out int expected);

            if (known && delimiterAt.TryGetValue(indent, out char delimiter) && delimiter != item.Delimiter)
            {
                known = false;
            }

            if (known && hadBlank && item.Number > expected)
            {
                known = false;
            }

            if (!known)
            {
                runAt[indent] = nextRun;
                meta[nextRun] = (indent, item.Delimiter);
                found[nextRun] = ([], []);
                order.Add(nextRun);
                nextRun++;
            }

            delimiterAt[indent] = item.Delimiter;

            int next = known ? expected : item.Number;
            counters[indent] = next + 1;

            int run = runAt[indent];
            found[run].Lines.Add(i);
            found[run].Numbers.Add(item.Number);
        }

        return [.. order.Select(id => new Run(meta[id].Indent, meta[id].Delimiter, found[id].Lines, found[id].Numbers))];
    }
}
