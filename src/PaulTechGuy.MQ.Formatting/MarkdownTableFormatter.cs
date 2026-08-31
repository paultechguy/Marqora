// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using static PaulTechGuy.MQ.Formatting.MarkdownFormatter;

namespace PaulTechGuy.MQ.Formatting;

/// <summary>
/// Pads table cells so the pipes line up.
///
/// Alignment colons in the divider row are preserved exactly — they are the only part of a
/// table that changes what it renders to, so they are read, kept, and written back rather
/// than regenerated from a guess.
/// </summary>
internal static class MarkdownTableFormatter
{
    private enum Alignment
    {
        None,
        Left,
        Centre,
        Right,
    }

    public static void Apply(List<Line> lines)
    {
        int i = 0;

        while (i < lines.Count)
        {
            if (!IsCandidate(lines, i))
            {
                i++;
                continue;
            }

            int end = i + 1;

            while (end + 1 < lines.Count && IsBodyRow(lines[end + 1]))
            {
                end++;
            }

            FormatTable(lines, i, end);
            i = end + 1;
        }
    }

    /// <summary>A header row is only a table when the line under it is a divider.</summary>
    private static bool IsCandidate(List<Line> lines, int index)
    {
        if (index + 1 >= lines.Count)
        {
            return false;
        }

        Line header = lines[index];
        Line divider = lines[index + 1];

        if (!header.IsText || !divider.IsText || header.Frozen || divider.Frozen)
        {
            return false;
        }

        return MarkdownLineRules.IsTableRow(header.Text) && IsDivider(divider.Text);
    }

    private static bool IsBodyRow(Line line) =>
        line.IsText && !line.Frozen && MarkdownLineRules.IsTableRow(line.Text);

    private static bool IsDivider(string line)
    {
        string[] cells = SplitCells(line);

        if (cells.Length == 0)
        {
            return false;
        }

        foreach (string cell in cells)
        {
            string c = cell.Trim();

            if (c.Length == 0)
            {
                return false;
            }

            int dashes = 0;

            for (int i = 0; i < c.Length; i++)
            {
                char ch = c[i];

                if (ch == '-')
                {
                    dashes++;
                }
                else if (ch != ':')
                {
                    return false;
                }
                else if (i != 0 && i != c.Length - 1)
                {
                    // A colon is only meaningful at one end or the other.
                    return false;
                }
            }

            if (dashes == 0)
            {
                return false;
            }
        }

        return true;
    }

    private static void FormatTable(List<Line> lines, int start, int end)
    {
        var rows = new List<string[]>();

        for (int i = start; i <= end; i++)
        {
            rows.Add(SplitCells(lines[i].Text));
        }

        Alignment[] alignments = ReadAlignments(rows[1]);

        int columns = Math.Max(alignments.Length, rows.Max(r => r.Length));

        // The divider is rebuilt rather than measured; its dashes are padding, not content.
        var widths = new int[columns];

        for (int r = 0; r < rows.Count; r++)
        {
            if (r == 1)
            {
                continue;
            }

            for (int c = 0; c < rows[r].Length && c < columns; c++)
            {
                widths[c] = Math.Max(widths[c], rows[r][c].Trim().Length);
            }
        }

        for (int c = 0; c < columns; c++)
        {
            // Room for the alignment colons, and never narrower than "---".
            widths[c] = Math.Max(widths[c], 3);
        }

        string indent = lines[start].Text[..(lines[start].Text.Length - lines[start].Text.TrimStart().Length)];

        for (int r = 0; r < rows.Count; r++)
        {
            lines[start + r].Text = r == 1
                ? BuildDivider(indent, widths, alignments, columns)
                : BuildRow(indent, rows[r], widths, alignments, columns);
        }
    }

    private static Alignment[] ReadAlignments(string[] dividerCells)
    {
        var alignments = new Alignment[dividerCells.Length];

        for (int i = 0; i < dividerCells.Length; i++)
        {
            string c = dividerCells[i].Trim();
            bool left = c.StartsWith(':');
            bool right = c.EndsWith(':');

            alignments[i] = (left, right) switch
            {
                (true, true) => Alignment.Centre,
                (true, false) => Alignment.Left,
                (false, true) => Alignment.Right,
                _ => Alignment.None,
            };
        }

        return alignments;
    }

    private static string BuildDivider(string indent, int[] widths, Alignment[] alignments, int columns)
    {
        var builder = new StringBuilder(indent).Append('|');

        for (int c = 0; c < columns; c++)
        {
            Alignment a = c < alignments.Length ? alignments[c] : Alignment.None;
            int width = widths[c];

            string body = a switch
            {
                Alignment.Left => ':' + new string('-', width - 1),
                Alignment.Right => new string('-', width - 1) + ':',
                Alignment.Centre => ':' + new string('-', width - 2) + ':',
                _ => new string('-', width),
            };

            builder.Append(' ').Append(body).Append(" |");
        }

        return builder.ToString();
    }

    private static string BuildRow(string indent, string[] cells, int[] widths, Alignment[] alignments, int columns)
    {
        var builder = new StringBuilder(indent).Append('|');

        for (int c = 0; c < columns; c++)
        {
            string value = c < cells.Length ? cells[c].Trim() : string.Empty;
            Alignment a = c < alignments.Length ? alignments[c] : Alignment.None;
            int width = widths[c];
            int slack = width - value.Length;

            string padded = a switch
            {
                Alignment.Right => new string(' ', slack) + value,
                Alignment.Centre => new string(' ', slack / 2) + value + new string(' ', slack - (slack / 2)),
                _ => value + new string(' ', slack),
            };

            builder.Append(' ').Append(padded).Append(" |");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Splits a row into cells on unescaped pipes.
    ///
    /// A pipe inside a code span or preceded by a backslash belongs to the cell's content;
    /// splitting on it would tear the row apart.
    /// </summary>
    private static string[] SplitCells(string line)
    {
        string t = line.Trim();

        if (t.StartsWith('|'))
        {
            t = t[1..];
        }

        if (t.EndsWith('|') && !t.EndsWith("\\|", StringComparison.Ordinal))
        {
            t = t[..^1];
        }

        var cells = new List<string>();
        var current = new StringBuilder();
        int openFence = 0;

        for (int i = 0; i < t.Length; i++)
        {
            char ch = t[i];

            if (ch == '`')
            {
                // A code span is delimited by a run of backticks and closed by a run of the
                // same length, so `` a ` b `` is one span rather than two.
                int run = 0;

                while (i + run < t.Length && t[i + run] == '`')
                {
                    run++;
                }

                if (openFence == 0)
                {
                    openFence = run;
                }
                else if (openFence == run)
                {
                    openFence = 0;
                }

                current.Append(t, i, run);
                i += run - 1;
                continue;
            }

            // Backslash escapes are inert inside a code span. Honouring one there was enough
            // to swallow the closing backtick of a cell ending in a path separator, after
            // which every pipe on the line looked like code and the row never split.
            if (ch == '\\' && openFence == 0 && i + 1 < t.Length)
            {
                current.Append(ch).Append(t[i + 1]);
                i++;
                continue;
            }

            if (ch == '|' && openFence == 0)
            {
                cells.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        cells.Add(current.ToString());

        return [.. cells];
    }
}
