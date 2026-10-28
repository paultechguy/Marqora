// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;

namespace PaulTechGuy.MQ.Cli.Text;

/// <summary>
/// What --dry-run prints: a unified diff from a file's text to what a verb would make of it,
/// written so that git apply takes it (CLI-OUT-05).
///
/// Lines are compared with their endings, so a change that is only an ending shows, and each line
/// is printed with the ending it has: a CRLF file's diff carries CRLF, as git's own does. A last
/// line with no newline gets git's "\ No newline at end of file" marker.
///
/// Myers' algorithm, after the common start and end are set aside. A difference too large to trace
/// in reasonable memory is printed as one hunk replacing everything between them, which is still a
/// correct diff, only not the smallest.
/// </summary>
internal static class UnifiedDiff
{
    private const int Context = 3;

    /// <summary>Beyond this many edits the trace is not kept, and the middle is replaced whole.</summary>
    private const int MaxEdits = 4000;

    private enum Kind
    {
        Same,
        Removed,
        Added,
    }

    private readonly record struct Edit(Kind Kind, int Old, int New);

    /// <returns>The diff, or an empty string when the two are equal.</returns>
    public static string Create(string oldText, string newText, string path)
    {
        ArgumentNullException.ThrowIfNull(oldText);
        ArgumentNullException.ThrowIfNull(newText);
        ArgumentNullException.ThrowIfNull(path);

        if (oldText == newText)
        {
            return string.Empty;
        }

        string[] a = SplitKeepingEndings(oldText);
        string[] b = SplitKeepingEndings(newText);

        List<Edit> edits = Edits(a, b);
        var output = new StringBuilder();
        string name = path.Replace('\\', '/');

        output.Append("--- a/").Append(name).Append('\n');
        output.Append("+++ b/").Append(name).Append('\n');

        foreach ((int start, int end) in Hunks(edits))
        {
            WriteHunk(output, edits, start, end, a, b);
        }

        return output.ToString();
    }

    /// <summary>Lines with their own endings attached; the last has none when the text has no final newline.</summary>
    private static string[] SplitKeepingEndings(string text)
    {
        var lines = new List<string>();
        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                lines.Add(text[start..(i + 1)]);
                start = i + 1;
            }
            else if (text[i] == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n'))
            {
                lines.Add(text[start..(i + 1)]);
                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            lines.Add(text[start..]);
        }

        return [.. lines];
    }

    private static List<Edit> Edits(string[] a, string[] b)
    {
        int prefix = 0;

        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix])
        {
            prefix++;
        }

        int suffix = 0;

        while (suffix < a.Length - prefix && suffix < b.Length - prefix && a[^(suffix + 1)] == b[^(suffix + 1)])
        {
            suffix++;
        }

        var edits = new List<Edit>(a.Length + b.Length);

        for (int i = 0; i < prefix; i++)
        {
            edits.Add(new Edit(Kind.Same, i, i));
        }

        edits.AddRange(Middle(a, b, prefix, a.Length - suffix, b.Length - suffix));

        for (int i = 0; i < suffix; i++)
        {
            edits.Add(new Edit(Kind.Same, a.Length - suffix + i, b.Length - suffix + i));
        }

        return edits;
    }

    /// <summary>The shortest edit script between a[offset..aEnd] and b[offset..bEnd].</summary>
    private static List<Edit> Middle(string[] a, string[] b, int offset, int aEnd, int bEnd)
    {
        int n = aEnd - offset;
        int m = bEnd - offset;
        int max = n + m;

        if (max == 0)
        {
            return [];
        }

        var v = new int[(2 * max) + 2];
        var trace = new List<int[]>();

        for (int d = 0; d <= max; d++)
        {
            if (d > MaxEdits)
            {
                return ReplaceWhole(offset, aEnd, bEnd);
            }

            trace.Add((int[])v.Clone());

            for (int k = -d; k <= d; k += 2)
            {
                int x = k == -d || (k != d && v[max + k - 1] < v[max + k + 1])
                    ? v[max + k + 1]
                    : v[max + k - 1] + 1;
                int y = x - k;

                while (x < n && y < m && a[offset + x] == b[offset + y])
                {
                    x++;
                    y++;
                }

                v[max + k] = x;

                if (x >= n && y >= m)
                {
                    return Backtrack(trace, max, n, m, offset);
                }
            }
        }

        return ReplaceWhole(offset, aEnd, bEnd);
    }

    private static List<Edit> Backtrack(List<int[]> trace, int max, int n, int m, int offset)
    {
        var edits = new List<Edit>();
        int x = n;
        int y = m;

        for (int d = trace.Count - 1; d >= 0; d--)
        {
            int[] v = trace[d];
            int k = x - y;
            int previousK = k == -d || (k != d && v[max + k - 1] < v[max + k + 1]) ? k + 1 : k - 1;
            int previousX = v[max + previousK];
            int previousY = previousX - previousK;

            while (x > previousX && y > previousY)
            {
                edits.Add(new Edit(Kind.Same, offset + x - 1, offset + y - 1));
                x--;
                y--;
            }

            if (d > 0)
            {
                edits.Add(x == previousX
                    ? new Edit(Kind.Added, offset + x, offset + y - 1)
                    : new Edit(Kind.Removed, offset + x - 1, offset + y));
            }

            x = previousX;
            y = previousY;
        }

        edits.Reverse();
        return edits;
    }

    private static List<Edit> ReplaceWhole(int offset, int aEnd, int bEnd)
    {
        var edits = new List<Edit>();

        for (int i = offset; i < aEnd; i++)
        {
            edits.Add(new Edit(Kind.Removed, i, offset));
        }

        for (int j = offset; j < bEnd; j++)
        {
            edits.Add(new Edit(Kind.Added, aEnd, j));
        }

        return edits;
    }

    /// <summary>Runs of edits that change something, each widened by the context around it and merged with any it then touches.</summary>
    private static IEnumerable<(int Start, int End)> Hunks(List<Edit> edits)
    {
        int i = 0;

        while (i < edits.Count)
        {
            while (i < edits.Count && edits[i].Kind == Kind.Same)
            {
                i++;
            }

            if (i == edits.Count)
            {
                yield break;
            }

            int start = Math.Max(0, i - Context);
            int end = i;

            while (true)
            {
                while (end < edits.Count && edits[end].Kind != Kind.Same)
                {
                    end++;
                }

                int same = end;

                while (same < edits.Count && edits[same].Kind == Kind.Same)
                {
                    same++;
                }

                // Close enough to the next change that their context would overlap: one hunk.
                if (same < edits.Count && same - end <= 2 * Context)
                {
                    end = same;
                    continue;
                }

                end = Math.Min(edits.Count, end + Context);
                break;
            }

            yield return (start, end);
            i = end;
        }
    }

    private static void WriteHunk(StringBuilder output, List<Edit> edits, int start, int end, string[] a, string[] b)
    {
        int oldStart = -1;
        int newStart = -1;
        int oldCount = 0;
        int newCount = 0;

        for (int i = start; i < end; i++)
        {
            Edit edit = edits[i];

            if (edit.Kind != Kind.Added)
            {
                oldCount++;
                oldStart = oldStart < 0 ? edit.Old : oldStart;
            }

            if (edit.Kind != Kind.Removed)
            {
                newCount++;
                newStart = newStart < 0 ? edit.New : newStart;
            }
        }

        // An empty side is numbered by the line before it, as diff does: "-0,0" at the very top.
        oldStart = oldCount == 0 ? edits[start].Old : oldStart + 1;
        newStart = newCount == 0 ? edits[start].New : newStart + 1;

        output.Append(CultureInfo.InvariantCulture, $"@@ -{Range(oldStart, oldCount)} +{Range(newStart, newCount)} @@\n");

        for (int i = start; i < end; i++)
        {
            Edit edit = edits[i];

            (char sign, string line) = edit.Kind switch
            {
                Kind.Removed => ('-', a[edit.Old]),
                Kind.Added => ('+', b[edit.New]),
                _ => (' ', a[edit.Old]),
            };

            output.Append(sign).Append(line);

            if (!line.EndsWith('\n') && !line.EndsWith('\r'))
            {
                output.Append("\n\\ No newline at end of file\n");
            }
        }
    }

    private static string Range(int start, int count) =>
        count == 1 ? start.ToString(CultureInfo.InvariantCulture) : string.Create(CultureInfo.InvariantCulture, $"{start},{count}");
}
