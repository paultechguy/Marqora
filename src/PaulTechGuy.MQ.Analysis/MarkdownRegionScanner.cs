// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Analysis;

/// <summary>
/// Marks the lines that style rules must not touch: the inside of fenced code blocks, their
/// fence lines, and YAML front matter.
///
/// The formatter carries the same knowledge in its own line classifier, but that one is
/// internal to its assembly and welded to a mutable line type it needs for rewriting. This
/// is a second, read-only implementation rather than a refactoring of that one, because
/// reshaping the formatter's internals without a test suite behind it would put its promise
/// — that formatting never changes what a document renders to — at risk for no gain here.
/// The two should be folded together once the formatter has tests of its own.
///
/// Only fenced blocks are recognised, not four-space indented ones, which matches what the
/// formatter protects.
/// </summary>
public static class MarkdownRegionScanner
{
    /// <summary>
    /// One flag per line: true where a style rule should keep out. Links are not filtered
    /// here because a link inside a fence never becomes a link in the first place — the
    /// parser does not look inside one.
    /// </summary>
    public static bool[] FindProtectedLines(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        bool[] result = new bool[lines.Count];
        string? fence = null;
        bool inFrontMatter = false;

        for (int i = 0; i < lines.Count; i++)
        {
            string trimmed = lines[i].TrimStart();

            // Front matter only counts as front matter at the very top of the file.
            if (i == 0 && trimmed == "---")
            {
                inFrontMatter = true;
                result[i] = true;

                continue;
            }

            if (inFrontMatter)
            {
                result[i] = true;
                inFrontMatter = trimmed is not ("---" or "...");

                continue;
            }

            if (fence is null)
            {
                if (TryReadFence(trimmed, out string? opened))
                {
                    fence = opened;
                    result[i] = true;
                }

                continue;
            }

            result[i] = true;

            // A closing fence has to be the same character, at least as long, and carry no
            // info string of its own.
            if (TryReadFence(trimmed, out string? closer)
                && closer![0] == fence[0]
                && closer.Length >= fence.Length
                && trimmed.TrimEnd().Length == closer.Length)
            {
                fence = null;
            }
        }

        return result;
    }

    /// <summary>
    /// Reads a run of at least three backticks or tildes from the start of a line. Backtick
    /// fences may not carry a backtick in their info string, which is what stops an inline
    /// code span being mistaken for one.
    /// </summary>
    private static bool TryReadFence(string trimmed, out string? fence)
    {
        fence = null;

        if (trimmed.Length < 3 || (trimmed[0] != '`' && trimmed[0] != '~'))
        {
            return false;
        }

        char marker = trimmed[0];
        int length = 0;

        while (length < trimmed.Length && trimmed[length] == marker)
        {
            length++;
        }

        if (length < 3)
        {
            return false;
        }

        if (marker == '`' && trimmed[length..].Contains('`', StringComparison.Ordinal))
        {
            return false;
        }

        fence = trimmed[..length];

        return true;
    }
}
