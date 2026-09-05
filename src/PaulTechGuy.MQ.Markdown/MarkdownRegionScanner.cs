// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Markdown;

/// <summary>
/// Marks the lines a text rule must keep out of: the inside of fenced code blocks, their fence
/// lines, YAML front matter, and — on request — four-space indented code blocks.
///
/// Two callers, and they want different answers. The style checks in Analysis have always
/// ignored indented blocks, because that is what the formatter protects and the two must agree.
/// Spell checking cannot afford to: an indented code sample would be underlined end to end. So
/// indented blocks are opt-in rather than a change of behaviour for the existing caller.
///
/// The formatter carries the same knowledge in its own line classifier, but that one is
/// internal to its assembly and welded to a mutable line type it needs for rewriting. This is a
/// second, read-only implementation rather than a refactoring of that one, because reshaping the
/// formatter's internals without a test suite behind it would put its promise — that formatting
/// never changes what a document renders to — at risk for no gain here. The two should be folded
/// together once the formatter has tests of its own, and this project is where that should land.
/// </summary>
public static class MarkdownRegionScanner
{
    /// <summary>
    /// One flag per line: true where a rule should keep out. Links are not filtered here
    /// because a link inside a fence never becomes a link in the first place — the parser does
    /// not look inside one.
    /// </summary>
    /// <param name="includeIndentedCode">
    /// Also protect four-space indented code blocks. Off by default, which is what the style
    /// checks have always seen.
    ///
    /// The rule is deliberately conservative: a block starts only where an indented line follows
    /// a blank one, which is what stops a lazy list continuation being read as code. The cost is
    /// the reverse case — a list item's second paragraph, indented and blank-line separated, is
    /// read as code and goes unchecked. That errs towards silence, which is the right direction
    /// for a spell checker: a missed squiggle is cheaper than a false one.
    /// </param>
    public static bool[] FindProtectedLines(IReadOnlyList<string> lines, bool includeIndentedCode = false)
    {
        ArgumentNullException.ThrowIfNull(lines);

        bool[] result = new bool[lines.Count];
        string? fence = null;
        bool inFrontMatter = false;
        bool inIndentedCode = false;

        // True at the top of the file, so a document opening with an indented line reads as a
        // code block, exactly as it renders.
        bool previousWasBlank = true;

        for (int i = 0; i < lines.Count; i++)
        {
            string trimmed = lines[i].TrimStart();

            // Front matter only counts as front matter at the very top of the file.
            if (i == 0 && trimmed == "---")
            {
                inFrontMatter = true;
                result[i] = true;
                previousWasBlank = false;

                continue;
            }

            if (inFrontMatter)
            {
                result[i] = true;
                inFrontMatter = trimmed is not ("---" or "...");
                previousWasBlank = false;

                continue;
            }

            if (fence is null)
            {
                if (TryReadFence(trimmed, out string? opened))
                {
                    fence = opened;
                    result[i] = true;
                    inIndentedCode = false;
                    previousWasBlank = false;

                    continue;
                }

                if (includeIndentedCode)
                {
                    ScanIndentedCode(lines[i], trimmed, result, i, ref inIndentedCode, ref previousWasBlank);
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

            previousWasBlank = false;
        }

        return result;
    }

    /// <summary>
    /// Carries the indented-block state across one line. A blank line does not end a block —
    /// only the next line that carries content at less than four columns does — so a code sample
    /// with a gap in it stays one block rather than two.
    /// </summary>
    private static void ScanIndentedCode(
        string line,
        string trimmed,
        bool[] result,
        int index,
        ref bool inIndentedCode,
        ref bool previousWasBlank)
    {
        bool blank = trimmed.Length == 0;
        bool indented = IsIndented(line);

        if (inIndentedCode)
        {
            if (blank || indented)
            {
                result[index] = true;
            }
            else
            {
                inIndentedCode = false;
            }
        }
        else if (indented && previousWasBlank)
        {
            inIndentedCode = true;
            result[index] = true;
        }

        previousWasBlank = blank;
    }

    /// <summary>
    /// Whether a line carries content starting at the fourth column or later. A tab counts as
    /// four, which is what CommonMark does. A line of nothing but whitespace is not indented
    /// code; it is blank.
    /// </summary>
    private static bool IsIndented(string line)
    {
        int width = 0;

        foreach (char c in line)
        {
            if (c == ' ')
            {
                width++;
            }
            else if (c == '\t')
            {
                width += 4;
            }
            else
            {
                return width >= 4;
            }
        }

        return false;
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
