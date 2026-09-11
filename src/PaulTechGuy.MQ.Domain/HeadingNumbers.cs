// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// Turns a document's heading levels into the section numbers shown beside them.
///
/// A pure function of the levels in document order, which is all the rule needs: a heading's
/// number depends on how many headings of its own level have been seen since the last one
/// above it, and on nothing else about the document. That is what lets the renderer and the
/// outline panel agree without either of them asking the other.
///
/// The rule is not a CSS counter chain, and cannot be. A document that skips a level - a
/// "###" directly under a "#" with no "##" between them - is ordinary rather than exotic,
/// and a counter has to render the missing level as something, which is how a heading comes
/// out as "9.0.1". Here the missing level is a zero that is dropped, so the same heading
/// reads "9.1".
/// </summary>
public static class HeadingNumbers
{
    /// <summary>Markdown has six heading levels, and the counters are one per level.</summary>
    private const int MaxLevel = 6;

    /// <summary>
    /// The number for each heading, in the order the levels were given. A heading that is
    /// not numbered - because numbering is off, or because it sits above the level the count
    /// starts at - gets an empty string rather than a gap, so the result stays index-for-index
    /// with the input.
    /// </summary>
    public static IReadOnlyList<string> Compute(IReadOnlyList<int> levels, HeadingNumbering numbering)
    {
        ArgumentNullException.ThrowIfNull(levels);

        string[] numbers = new string[levels.Count];
        Array.Fill(numbers, string.Empty);

        // The enum's members are the heading levels themselves, and Off is zero.
        int start = (int)numbering;

        if (start is < 1 or > MaxLevel)
        {
            return numbers;
        }

        // One counter per level, so a heading only ever has to look at its own and its
        // parents'.
        Span<int> counters = stackalloc int[MaxLevel];
        var builder = new StringBuilder();

        for (int i = 0; i < levels.Count; i++)
        {
            // Clamped rather than rejected. Nothing outside one to six can come from a
            // markdown parser, and a numbering helper is the wrong place for a render to
            // die if something ever does.
            int level = Math.Clamp(levels[i], 1, MaxLevel);

            if (level < start)
            {
                // Above the numbered range: left unnumbered, but it still begins a new
                // section, so everything below it starts again. Without this the deeper
                // numbers run on across a document's chapters instead of restarting.
                for (int r = start - 1; r < MaxLevel; r++)
                {
                    counters[r] = 0;
                }

                continue;
            }

            counters[level - 1]++;

            for (int d = level; d < MaxLevel; d++)
            {
                counters[d] = 0;
            }

            builder.Clear();

            for (int c = start - 1; c < level; c++)
            {
                // A level the document skipped is still zero here. Dropping those leading
                // zeros is what turns "0.1" into "1" for a heading whose parent level was
                // never used.
                if (counters[c] == 0 && builder.Length == 0)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append('.');
                }

                builder.Append(counters[c]);
            }

            if (builder.Length == 0)
            {
                continue;
            }

            numbers[i] = builder.ToString();
        }

        return numbers;
    }
}
