// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;

namespace PaulTechGuy.MQ.Domain;

/// <summary>The styles a run of comment text can carry. Any combination, except that code stands alone.</summary>
[Flags]
public enum CommentStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Underline = 4,
    Highlight = 8,
    Code = 16,
}

/// <summary>A run of comment text in one style.</summary>
public sealed record CommentSpan(string Text, CommentStyle Style);

/// <summary>
/// The little markup a review comment may carry: five styles, written the way the preview already
/// reads them, so a comment says the same thing in the sidebar, on the shared page, and in the
/// CriticMarkup copy an AI is given.
///
/// <code>
/// **bold**   *italic*   ++underline++   ==highlight==   `code`
/// </code>
///
/// <c>++</c> is Markdig's inserted text, which the preview draws underlined. Everything else -
/// links, headings, lists - stays the characters that were typed.
///
/// One parser, here, and both places that show a comment ask it: the sidebar builds its runs
/// from <see cref="Parse"/>, and the shared page takes <see cref="ToHtml"/>, which the shell
/// inserts rather than parsing again. Two readers of the same markup written in two languages
/// would be two answers to "is this bold?".
///
/// Deliberately forgiving. A marker with no partner is text, not an error - a comment that says
/// "2 * 3" or "a++" means exactly that - and a pair must hold something, so "****" is four
/// asterisks. Styles nest, except inside code, where every character is literal.
/// </summary>
public static class CommentMarkup
{
    /// <summary>The five markers, longest first: "**" must be tried before "*".</summary>
    private static readonly (string Marker, CommentStyle Style)[] Markers =
    [
        ("**", CommentStyle.Bold),
        ("++", CommentStyle.Underline),
        ("==", CommentStyle.Highlight),
        ("`", CommentStyle.Code),
        ("*", CommentStyle.Italic),
    ];

    /// <summary>The marker that writes a style, for the shortcut keys that toggle it.</summary>
    public static string MarkerOf(CommentStyle style) => style switch
    {
        CommentStyle.Bold => "**",
        CommentStyle.Italic => "*",
        CommentStyle.Underline => "++",
        CommentStyle.Highlight => "==",
        CommentStyle.Code => "`",
        _ => throw new ArgumentOutOfRangeException(nameof(style), style, "One style at a time."),
    };

    /// <summary>
    /// The comment as runs of text and style, in order. Line breaks are kept in the text as
    /// <c>\n</c>; a caller that lays out paragraphs splits on them.
    /// </summary>
    public static IReadOnlyList<CommentSpan> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var spans = new List<CommentSpan>();
        Parse(text.Replace("\r\n", "\n", StringComparison.Ordinal), CommentStyle.None, spans);

        return Merge(spans);
    }

    private static void Parse(string text, CommentStyle outer, List<CommentSpan> spans)
    {
        var plain = new StringBuilder();
        int i = 0;

        while (i < text.Length)
        {
            if (OpeningAt(text, i) is { } found)
            {
                (string marker, CommentStyle style, int close) = found;

                if (plain.Length > 0)
                {
                    spans.Add(new CommentSpan(plain.ToString(), outer));
                    plain.Clear();
                }

                string inner = text[(i + marker.Length)..close];

                if (style == CommentStyle.Code)
                {
                    spans.Add(new CommentSpan(inner, outer | CommentStyle.Code));
                }
                else
                {
                    Parse(inner, outer | style, spans);
                }

                i = close + marker.Length;
                continue;
            }

            plain.Append(text[i]);
            i++;
        }

        if (plain.Length > 0)
        {
            spans.Add(new CommentSpan(plain.ToString(), outer));
        }
    }

    /// <summary>
    /// A marker opening at <paramref name="at"/> that has a partner, and where the partner is.
    ///
    /// The partner is the next occurrence of the same marker on the same line with something
    /// between them. A lone "*" is only italic if its partner is not the first half of a "**",
    /// so "*a **b** c*" is italic around bold rather than italic "a " and stray asterisks.
    /// </summary>
    private static (string Marker, CommentStyle Style, int Close)? OpeningAt(string text, int at)
    {
        foreach ((string marker, CommentStyle style) in Markers)
        {
            if (string.CompareOrdinal(text, at, marker, 0, marker.Length) != 0)
            {
                continue;
            }

            int from = at + marker.Length;

            // The first half of a "**" that found no partner is not an italic opening: "****"
            // is four asterisks, not italic around two of them.
            if (marker == "*" && from < text.Length && text[from] == '*')
            {
                continue;
            }

            for (int close = text.IndexOf(marker, from, StringComparison.Ordinal);
                close >= 0;
                close = text.IndexOf(marker, close + 1, StringComparison.Ordinal))
            {
                if (text.AsSpan(from, close - from).Contains('\n'))
                {
                    break;
                }

                bool doubled = marker == "*"
                    && close + 1 < text.Length && text[close + 1] == '*';

                if (doubled)
                {
                    // Part of a "**": step over the pair so it is not read as the italic's end.
                    close++;
                    continue;
                }

                if (close > from)
                {
                    return (marker, style, close);
                }

                break;
            }

            // This marker has no partner; a shorter one starting here may ("**" alone can
            // still open an italic "*" whose partner comes later).
        }

        return null;
    }

    /// <summary>Joins neighbours in the same style, so a caller draws one run where it can.</summary>
    private static List<CommentSpan> Merge(List<CommentSpan> spans)
    {
        var merged = new List<CommentSpan>(spans.Count);

        foreach (CommentSpan span in spans.Where(s => s.Text.Length > 0))
        {
            if (merged.Count > 0 && merged[^1].Style == span.Style)
            {
                merged[^1] = merged[^1] with { Text = merged[^1].Text + span.Text };
            }
            else
            {
                merged.Add(span);
            }
        }

        return merged;
    }

    /// <summary>
    /// The comment as HTML for a margin note on the shared page: each run encoded and wrapped in
    /// its element, a blank line starting a new paragraph (<c>mq-sidenote-para</c>) and a single
    /// line break kept as <c>&lt;br&gt;</c>. Underline is <c>&lt;u&gt;</c> and highlight
    /// <c>&lt;mark&gt;</c>, which the page's stylesheet already draws the way the preview does.
    /// </summary>
    public static string ToHtml(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string[] paragraphs = text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim()
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var html = new StringBuilder();

        for (int p = 0; p < paragraphs.Length; p++)
        {
            if (p > 0)
            {
                html.Append("<span class=\"mq-sidenote-para\">");
            }

            foreach (CommentSpan span in Parse(paragraphs[p]))
            {
                html.Append(Open(span.Style));
                html.Append(WebUtility.HtmlEncode(span.Text).Replace("\n", "<br>", StringComparison.Ordinal));
                html.Append(Close(span.Style));
            }

            if (p > 0)
            {
                html.Append("</span>");
            }
        }

        return html.ToString();
    }

    private static readonly (CommentStyle Style, string Tag)[] Tags =
    [
        (CommentStyle.Highlight, "mark"),
        (CommentStyle.Bold, "strong"),
        (CommentStyle.Italic, "em"),
        (CommentStyle.Underline, "u"),
        (CommentStyle.Code, "code"),
    ];

    private static string Open(CommentStyle style) =>
        string.Concat(Tags.Where(t => style.HasFlag(t.Style)).Select(t => "<" + t.Tag + ">"));

    private static string Close(CommentStyle style) =>
        string.Concat(Tags.Where(t => style.HasFlag(t.Style)).Reverse().Select(t => "</" + t.Tag + ">"));

    /// <summary>
    /// A shortcut key's edit: wraps the selection in the style's marker, or takes the marker off
    /// if the selection already wears it - just inside the selection or just outside it, since a
    /// double-click selects the word but not the asterisks around it. With nothing selected it
    /// puts an empty pair down and the caret between them, ready to type into.
    /// </summary>
    /// <returns>The new text and the selection to put back, still around the same words.</returns>
    public static (string Text, int Start, int Length) Toggle(string text, int start, int length, CommentStyle style)
    {
        ArgumentNullException.ThrowIfNull(text);

        string marker = MarkerOf(style);
        int m = marker.Length;
        start = Math.Clamp(start, 0, text.Length);
        length = Math.Clamp(length, 0, text.Length - start);

        string selected = text.Substring(start, length);

        // Wearing it inside the selection: "**word**" selected whole.
        if (length >= 2 * m + 1 && selected.StartsWith(marker, StringComparison.Ordinal)
            && selected.EndsWith(marker, StringComparison.Ordinal))
        {
            string inner = selected[m..^m];
            return (text[..start] + inner + text[(start + length)..], start, inner.Length);
        }

        // Wearing it just outside: "word" selected inside "**word**".
        if (length > 0 && start >= m && start + length + m <= text.Length
            && string.CompareOrdinal(text, start - m, marker, 0, m) == 0
            && string.CompareOrdinal(text, start + length, marker, 0, m) == 0)
        {
            return (text[..(start - m)] + selected + text[(start + length + m)..], start - m, length);
        }

        return (text[..start] + marker + selected + marker + text[(start + length)..], start + m, length);
    }
}
