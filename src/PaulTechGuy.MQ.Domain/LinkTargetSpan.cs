// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// Where the target sits inside a markdown reference, so it can be replaced without disturbing
/// anything around it.
///
/// Only the target is ever rewritten. The label an author wrote is theirs: pointing a dead
/// "[the release notes](v3.md)" at v2.md must not turn it into "[v2.md](v2.md)", which is what
/// replacing the whole reference would do. A title is left alone for the same reason.
/// </summary>
public static class LinkTargetSpan
{
    /// <summary>
    /// The half-open span of the target inside <paramref name="line"/>, or null when the text
    /// between <paramref name="start"/> and <paramref name="end"/> is not a reference this can
    /// safely rewrite.
    ///
    /// Works from the closing bracket inwards rather than from the front, because the front is
    /// the part that varies: an image opens with "!", a label can contain brackets of its own,
    /// and a reference-style link has no parentheses at all.
    /// </summary>
    public static (int Start, int End)? Find(string line, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (start < 0 || end > line.Length || end - start < 4)
        {
            return null;
        }

        // The reference must actually end where it claims to. Anything else means the line has
        // moved on since the check ran and the decoration is pointing at something else now.
        if (line[end - 1] != ')')
        {
            return null;
        }

        int open = line.LastIndexOf('(', end - 1);

        if (open <= start || line[open - 1] != ']')
        {
            return null;
        }

        int targetStart = open + 1;
        int targetEnd = end - 1;

        // A title after the target - (url "the title") - is part of the reference and not part
        // of the address, so it stays. Only an unquoted run is trimmed off the end.
        int quote = line.IndexOf('"', targetStart);

        if (quote >= 0 && quote < targetEnd)
        {
            targetEnd = quote;

            while (targetEnd > targetStart && line[targetEnd - 1] == ' ')
            {
                targetEnd--;
            }
        }

        return targetEnd < targetStart ? null : (targetStart, targetEnd);
    }
}
