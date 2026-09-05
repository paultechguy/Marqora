// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;

namespace PaulTechGuy.MQ.Markdown;

/// <summary>
/// Blanks the runs of a line that are not prose, so that what is left can be handed to something
/// that only understands words.
///
/// The one rule everything here depends on: <b>a mask overwrites in place and never deletes</b>.
/// Callers report positions back against the original line — the spell engine is given a masked
/// line and returns offsets into it — so a mask that changed the length would put every one of
/// them in the wrong place.
///
/// Order matters. Each rule runs over the previous rule's output, which is what stops a URL
/// inside a code span being matched a second time as a URL.
///
/// This does not try to be a parser. It is a set of cheap, conservative rules whose failure mode
/// is leaving something unmasked rather than eating prose. The known gaps are named on the rules
/// that have them.
/// </summary>
public static partial class LineMasker
{
    /// <summary>
    /// What a masked run is filled with.
    ///
    /// A full stop rather than a space, and the difference is not cosmetic. A space says nothing
    /// was ever here, which joins the words either side of a mask together: "and `^` and" became
    /// "and     and", and a spell engine reasonably reported the second "and" as a word typed
    /// twice. It was reading across a code span it could not see.
    ///
    /// Any character that is neither a word character nor whitespace would do. A full stop is
    /// the least surprising of them if a masked line is ever printed while debugging, and it
    /// keeps the length identical, which is the promise everything here depends on.
    /// </summary>
    private const char Separator = '.';

    /// <summary>
    /// Everything that is not prose, blanked: code spans, HTML tags, link targets, URLs, maths,
    /// footnote markers, entities and emoji shortcodes. The link text and the alt text of an
    /// image survive, because a reader sees them.
    ///
    /// Fenced blocks, front matter and indented code are not handled here — they are whole-line
    /// concerns, and <see cref="MarkdownRegionScanner.FindProtectedLines"/> answers them.
    /// </summary>
    public static string MaskNonProse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (line.Length == 0)
        {
            return line;
        }

        string masked = MaskCodeSpans(line);

        // Angle brackets first: this is both the autolink form <https://example.com> and every
        // HTML tag with its attributes. The text between two tags is prose and is left alone.
        masked = Blank(masked, AngleSpan());

        // Both definition forms are line-anchored, and the footnote one has to go first: its
        // label starts with a caret, and the reference rule would otherwise swallow the first
        // word of the footnote's text along with the label.
        masked = Blank(masked, FootnoteDefinition());
        masked = Blank(masked, ReferenceDefinition());

        masked = Blank(masked, LinkTarget());
        masked = Blank(masked, ReferenceLabel());
        masked = Blank(masked, FootnoteReference());
        masked = Blank(masked, BareUrl());
        masked = Blank(masked, DisplayMath());
        masked = Blank(masked, InlineMath());
        masked = Blank(masked, HtmlEntity());
        masked = Blank(masked, EmojiShortcode());

        return masked;
    }

    /// <summary>
    /// Replaces the contents of inline code spans with <see cref="Separator"/>, keeping every
    /// other character where it was so match positions still line up with the real line.
    /// </summary>
    public static string MaskCodeSpans(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (!line.Contains('`', StringComparison.Ordinal))
        {
            return line;
        }

        char[] masked = line.ToCharArray();
        int i = 0;

        while (i < masked.Length)
        {
            if (masked[i] != '`')
            {
                i++;

                continue;
            }

            int open = i;
            while (i < masked.Length && masked[i] == '`')
            {
                i++;
            }

            int runLength = i - open;
            int close = FindClosingRun(masked, i, runLength);

            if (close < 0)
            {
                // No partner, so the backticks are just characters. Nothing to mask.
                return new string(masked);
            }

            for (int j = open; j < close + runLength && j < masked.Length; j++)
            {
                masked[j] = Separator;
            }

            i = close + runLength;
        }

        return new string(masked);
    }

    private static int FindClosingRun(char[] text, int from, int runLength)
    {
        for (int i = from; i < text.Length; i++)
        {
            if (text[i] != '`')
            {
                continue;
            }

            int start = i;
            while (i < text.Length && text[i] == '`')
            {
                i++;
            }

            if (i - start == runLength)
            {
                return start;
            }

            i--;
        }

        return -1;
    }

    /// <summary>
    /// Every match blanked, the rest untouched. Matching runs against the string as it arrived
    /// while the writes go to a copy, so the positions stay valid for the whole sweep.
    /// </summary>
    private static string Blank(string line, Regex pattern)
    {
        Match match = pattern.Match(line);

        if (!match.Success)
        {
            return line;
        }

        char[] buffer = line.ToCharArray();

        while (match.Success)
        {
            for (int i = match.Index; i < match.Index + match.Length; i++)
            {
                buffer[i] = Separator;
            }

            match = match.NextMatch();
        }

        return new string(buffer);
    }

    /// <summary>An autolink or an HTML tag. Unclosed brackets match nothing, which is the safe way round.</summary>
    [GeneratedRegex(@"<[^<>]*>")]
    private static partial Regex AngleSpan();

    /// <summary>A footnote definition's marker only: "[^note]:". What follows is the footnote, and is prose.</summary>
    [GeneratedRegex(@"^[ \t]*\[\^[^\]]*\]:")]
    private static partial Regex FootnoteDefinition();

    /// <summary>
    /// A link reference definition: the label and the destination, but not the optional title
    /// after it, which is prose the reader can see. The label may not open with a caret — that
    /// is a footnote, handled above.
    /// </summary>
    [GeneratedRegex(@"^[ \t]*\[[^\]^][^\]]*\]:[ \t]*\S+")]
    private static partial Regex ReferenceDefinition();

    /// <summary>
    /// An inline link or image destination: "](…)". The "[text]" before it is left alone.
    ///
    /// Gap: a destination carrying balanced parentheses stops at the first ")", leaving the tail
    /// of the URL unmasked. Rare, and the leftovers are usually caught by the skip rules.
    /// </summary>
    [GeneratedRegex(@"\]\([^)]*\)")]
    private static partial Regex LinkTarget();

    /// <summary>A reference-style link's label: "][ref]", including the collapsed "[]" form.</summary>
    [GeneratedRegex(@"\]\[[^\]]*\]")]
    private static partial Regex ReferenceLabel();

    /// <summary>A footnote marker in running text: "[^1]".</summary>
    [GeneratedRegex(@"\[\^[^\]]*\]")]
    private static partial Regex FootnoteReference();

    /// <summary>A bare URL, with or without a scheme. GFM auto-links both.</summary>
    [GeneratedRegex(@"(?:https?://|ftp://|www\.)\S+", RegexOptions.IgnoreCase)]
    private static partial Regex BareUrl();

    [GeneratedRegex(@"\$\$[^$]*\$\$")]
    private static partial Regex DisplayMath();

    /// <summary>
    /// Inline maths. The delimiters must hug their content — no space after the opening "$" nor
    /// before the closing one — which is the same heuristic KaTeX's auto-render uses, and what
    /// stops "$5 and $10" being read as a formula with "and" inside it.
    /// </summary>
    [GeneratedRegex(@"\$(?!\s)[^$]*?(?<!\s)\$")]
    private static partial Regex InlineMath();

    /// <summary>"&amp;amp;" and friends, whose names are not words.</summary>
    [GeneratedRegex(@"&(?:#\d+|#[xX][0-9a-fA-F]+|[a-zA-Z][a-zA-Z0-9]*);")]
    private static partial Regex HtmlEntity();

    /// <summary>
    /// An emoji shortcode such as ":sparkles:". Two characters minimum, so a lone ":" pair in
    /// prose is not mistaken for one.
    /// </summary>
    [GeneratedRegex(@":[a-z0-9_+-]{2,}:", RegexOptions.IgnoreCase)]
    private static partial Regex EmojiShortcode();
}
