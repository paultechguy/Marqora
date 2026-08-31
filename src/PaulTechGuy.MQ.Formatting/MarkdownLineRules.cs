// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.RegularExpressions;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Formatting;

/// <summary>
/// The single-line rules, kept apart from the block machinery so each one can be read, and
/// tested, on its own.
/// </summary>
internal static partial class MarkdownLineRules
{
    /// <summary><c>#Heading</c> becomes <c># Heading</c>. Up to three spaces of indent is still a heading.</summary>
    public static string AddHeadingSpace(string line)
    {
        Match m = HeadingWithoutSpace().Match(line);

        return m.Success
            ? $"{m.Groups["indent"].Value}{m.Groups["hashes"].Value} {m.Groups["rest"].Value}"
            : line;
    }

    [GeneratedRegex(@"^(?<indent>\s{0,3})(?<hashes>#{1,6})(?<rest>[^\s#].*)$")]
    private static partial Regex HeadingWithoutSpace();

    /// <summary><c>&gt;quote</c> becomes <c>&gt; quote</c>, at any nesting depth.</summary>
    public static string AddBlockquoteSpace(string line)
    {
        Match m = BlockquoteWithoutSpace().Match(line);

        return m.Success
            ? $"{m.Groups["indent"].Value}{m.Groups["marks"].Value} {m.Groups["rest"].Value}"
            : line;
    }

    [GeneratedRegex(@"^(?<indent>\s{0,3})(?<marks>>+)(?<rest>[^\s>].*)$")]
    private static partial Regex BlockquoteWithoutSpace();

    /// <summary>
    /// <c>-item</c> becomes <c>- item</c>.
    ///
    /// The asterisk needs care: <c>*emphasis*</c> at the start of a line looks exactly like a
    /// bullet with a missing space. A line whose asterisk has a partner later on is treated
    /// as emphasis and left alone, which is the reading a renderer gives it too.
    /// </summary>
    public static string AddListMarkerSpace(string line)
    {
        Match ordered = OrderedWithoutSpace().Match(line);

        if (ordered.Success)
        {
            return $"{ordered.Groups["indent"].Value}{ordered.Groups["number"].Value}{ordered.Groups["delim"].Value} {ordered.Groups["rest"].Value}";
        }

        Match m = BulletWithoutSpace().Match(line);

        if (!m.Success)
        {
            return line;
        }

        string marker = m.Groups["marker"].Value;
        string rest = m.Groups["rest"].Value;

        // A second asterisk means emphasis, not a list.
        if (marker == "*" && rest.Contains('*', StringComparison.Ordinal))
        {
            return line;
        }

        return $"{m.Groups["indent"].Value}{marker} {rest}";
    }

    // The negative lookahead keeps thematic breaks (---, ***) and bold (**text**) out.
    [GeneratedRegex(@"^(?<indent>\s*)(?<marker>[-*+])(?![-*+\s])(?<rest>\S.*)$")]
    private static partial Regex BulletWithoutSpace();

    [GeneratedRegex(@"^(?<indent>\s*)(?<number>\d{1,9})(?<delim>[.)])(?<rest>\S.*)$")]
    private static partial Regex OrderedWithoutSpace();

    /// <summary>Rewrites the bullet character, leaving indent and content alone.</summary>
    public static string NormalizeBullet(string line, BulletMarker bullet)
    {
        Match m = BulletItem().Match(line);

        if (!m.Success || IsThematicBreak(line))
        {
            return line;
        }

        char target = bullet switch
        {
            BulletMarker.Asterisk => '*',
            BulletMarker.Plus => '+',
            _ => '-',
        };

        return $"{m.Groups["indent"].Value}{target}{m.Groups["rest"].Value}";
    }

    [GeneratedRegex(@"^(?<indent>\s*)[-*+](?<rest>\s+\S.*)$")]
    private static partial Regex BulletItem();

    public static bool IsListItem(string line) =>
        (BulletItem().IsMatch(line) && !IsThematicBreak(line)) || OrderedItem().IsMatch(line);

    [GeneratedRegex(@"^(?<indent>\s*)(?<number>\d{1,9})(?<delim>[.)])\s+(?<rest>.*)$")]
    private static partial Regex OrderedItem();

    public static bool TryReadOrderedItem(
        string line,
        out int indent,
        out int number,
        out char delimiter,
        out string rest)
    {
        indent = 0;
        number = 0;
        delimiter = '.';
        rest = string.Empty;

        Match m = OrderedItem().Match(line);

        if (!m.Success)
        {
            return false;
        }

        indent = m.Groups["indent"].Value.Length;
        number = int.Parse(m.Groups["number"].Value, System.Globalization.CultureInfo.InvariantCulture);
        delimiter = m.Groups["delim"].Value[0];
        rest = m.Groups["rest"].Value;

        return true;
    }

    /// <summary>Three or more of the same character, with only spaces between them.</summary>
    public static bool IsThematicBreak(string line)
    {
        string t = line.Trim();

        if (t.Length < 3)
        {
            return false;
        }

        char c = t[0];

        if (c is not ('-' or '*' or '_'))
        {
            return false;
        }

        int count = 0;

        foreach (char ch in t)
        {
            if (ch == c)
            {
                count++;
            }
            else if (ch != ' ' && ch != '\t')
            {
                return false;
            }
        }

        return count >= 3;
    }

    public static bool IsTableRow(string line)
    {
        string t = line.Trim();

        return t.StartsWith('|') && t.Length > 1;
    }

    /// <summary><c>[text] (url)</c> becomes <c>[text](url)</c>; the same for reference links.</summary>
    public static string TidyLinkSyntax(string segment) =>
        LinkGap().Replace(segment, "]");

    [GeneratedRegex(@"\][ \t]+(?=[(\[])")]
    private static partial Regex LinkGap();

    /// <summary>
    /// Settles on one pair of characters for italic and bold.
    ///
    /// Underscores inside words are left alone: <c>snake_case_name</c> is not emphasis in
    /// CommonMark, and rewriting it would corrupt identifiers.
    /// </summary>
    public static string UnifyEmphasis(string segment, EmphasisStyle style)
    {
        if (style == EmphasisStyle.Asterisk)
        {
            segment = UnderscoreBold().Replace(segment, "**$1**");
            segment = UnderscoreItalic().Replace(segment, "*$1*");
        }
        else
        {
            segment = AsteriskBold().Replace(segment, "__$1__");
            segment = AsteriskItalic().Replace(segment, "_$1_");
        }

        return segment;
    }

    [GeneratedRegex(@"(?<![\w\\_])__(?=\S)(.+?)(?<=\S)__(?![\w_])")]
    private static partial Regex UnderscoreBold();

    [GeneratedRegex(@"(?<![\w\\_])_(?=\S)([^_]+?)(?<=\S)_(?![\w_])")]
    private static partial Regex UnderscoreItalic();

    [GeneratedRegex(@"(?<![\w\\*])\*\*(?=\S)(.+?)(?<=\S)\*\*(?![\w*])")]
    private static partial Regex AsteriskBold();

    [GeneratedRegex(@"(?<![\w\\*])\*(?=\S)([^*]+?)(?<=\S)\*(?![\w*])")]
    private static partial Regex AsteriskItalic();

    /// <summary>
    /// Strips trailing spaces and tabs.
    ///
    /// Two trailing spaces are a hard line break, which is content rather than untidiness, so
    /// when the following line would continue the paragraph they are kept — normalised to
    /// exactly two, since three or more are just as invisible and no more meaningful.
    /// </summary>
    public static string TrimTrailing(string line, bool keepHardBreak)
    {
        string trimmed = line.TrimEnd(' ', '\t');

        if (!keepHardBreak || trimmed.Length == 0 || trimmed.Length == line.Length)
        {
            return trimmed;
        }

        // Only spaces count; a tab does not make a hard break.
        int spaces = 0;

        for (int i = line.Length - 1; i >= 0 && line[i] == ' '; i--)
        {
            spaces++;
        }

        return spaces >= 2 ? trimmed + "  " : trimmed;
    }

    /// <summary>
    /// Applies a transform to the parts of a line that are not inside a code span.
    ///
    /// Without this, a rule would happily rewrite the contents of <c>`a_b_c`</c> or
    /// <c>`[x] (y)`</c> — text that is shown literally and must not be touched.
    /// </summary>
    public static string OutsideCodeSpans(string line, Func<string, string> transform)
    {
        if (!line.Contains('`', StringComparison.Ordinal))
        {
            return transform(line);
        }

        var builder = new StringBuilder(line.Length);
        int i = 0;

        while (i < line.Length)
        {
            int tick = line.IndexOf('`', i);

            if (tick < 0)
            {
                builder.Append(transform(line[i..]));
                break;
            }

            builder.Append(transform(line[i..tick]));

            // The closing run has to be the same length as the opening one.
            int run = 0;
            while (tick + run < line.Length && line[tick + run] == '`')
            {
                run++;
            }

            string fence = new('`', run);
            int close = line.IndexOf(fence, tick + run, StringComparison.Ordinal);

            if (close < 0)
            {
                // Unbalanced: treat the rest as ordinary text rather than swallowing it.
                builder.Append(transform(line[tick..]));
                break;
            }

            builder.Append(line, tick, close + run - tick);
            i = close + run;
        }

        return builder.ToString();
    }
}
