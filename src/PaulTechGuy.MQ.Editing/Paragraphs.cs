// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Markdown;

namespace PaulTechGuy.MQ.Editing;

/// <summary>
/// The block a caret is sitting in, for the snippets that take text with them.
///
/// Deliberately narrow: a paragraph or a heading, and nothing else. A caret in a list item, a
/// table row or a fenced block gets no answer, and the snippet goes in empty the way it always
/// has. The reasoning is about surprise rather than markdown — every one of those would wrap
/// correctly inside a callout, but clicking Note with the caret somewhere in a forty-line table
/// and watching the whole table move is not what the click looked like it would do. Selecting
/// the table first still wraps it, because a selection says what it means.
///
/// This is not a markdown parser and does not try to be. It reads one line at a time and asks
/// which construct it opens, which is enough for "where does this paragraph start and stop"
/// and would not be enough for anything more.
/// </summary>
internal static partial class Paragraphs
{
    /// <summary>
    /// How a line reads on its own, as far as finding a paragraph's edges needs to know.
    /// </summary>
    private enum LineKind
    {
        /// <summary>Nothing but whitespace: the separator between blocks.</summary>
        Blank,

        /// <summary>Prose, and so part of a paragraph.</summary>
        Paragraph,

        /// <summary>An ATX heading, which is a block of exactly one line.</summary>
        Heading,

        /// <summary>
        /// A construct that takes the line below it as its own continuation: a list item, a
        /// blockquote or a table row. Prose underneath one of these belongs to it rather than
        /// being a paragraph in its own right.
        /// </summary>
        Absorbing,

        /// <summary>
        /// A run of <c>=</c> or <c>-</c>, which directly under a paragraph is the underline of a
        /// setext heading rather than the line after it.
        /// </summary>
        Setext,

        /// <summary>Something that is none of the above and ends a paragraph: a rule, a fence,
        /// raw HTML, a link or footnote definition, indented code.</summary>
        Other,
    }

    /// <summary>
    /// The paragraph or heading containing <paramref name="line"/>, or null when there is not
    /// one — a blank line, a list, a table, anything inside a fenced block or front matter, or a
    /// line that is really the continuation of the construct above it.
    /// </summary>
    /// <param name="guarded">
    /// One flag per line of <paramref name="context"/>, as
    /// <see cref="MarkdownRegionScanner.FindProtectedLines"/> returns them. Passed in rather than
    /// computed here because the caller needs the same array for its own check and the scan runs
    /// the length of the document.
    /// </param>
    public static TextRange? Around(EditContext context, int line, bool[] guarded)
    {
        if (context.LineAt(line) is not { } text)
        {
            return null;
        }

        LineKind kind = KindAt(context, guarded, line);

        // A heading is one line by definition, so there is nothing to walk.
        if (kind == LineKind.Heading)
        {
            return Selections.WholeLine(line, text);
        }

        if (kind != LineKind.Paragraph)
        {
            return null;
        }

        int start = line;
        while (start > context.FirstLine && KindAt(context, guarded, start - 1) == LineKind.Paragraph)
        {
            start--;
        }

        // Stopping on a line that swallows the one below it means this run is that line's
        // continuation rather than a paragraph: the second line of a lazily continued list item
        // reads as prose on its own and is not prose on its own.
        if (start > context.FirstLine && KindAt(context, guarded, start - 1) == LineKind.Absorbing)
        {
            return null;
        }

        int end = line;
        while (end + 1 < context.EndLine && KindAt(context, guarded, end + 1) == LineKind.Paragraph)
        {
            end++;
        }

        // A run of = or - immediately under the paragraph makes the whole thing a setext
        // heading, so the underline comes too. With a blank line in between it is a rule, and
        // the walk above has already stopped on the blank.
        if (end + 1 < context.EndLine && KindAt(context, guarded, end + 1) == LineKind.Setext)
        {
            end++;
        }

        return new TextRange(new TextPosition(start, 0), new TextPosition(end, context.LineAt(end)?.Length ?? 0));
    }

    /// <summary>
    /// A line's kind, with anything the region scanner protects reading as
    /// <see cref="LineKind.Other"/>. Prose inside a fenced block is not prose.
    /// </summary>
    private static LineKind KindAt(EditContext context, bool[] guarded, int line)
    {
        if (context.LineAt(line) is not { } text)
        {
            return LineKind.Other;
        }

        int index = line - context.FirstLine;

        if (index >= 0 && index < guarded.Length && guarded[index])
        {
            return LineKind.Other;
        }

        return Classify(text);
    }

    private static LineKind Classify(string text)
    {
        string trimmed = text.TrimStart(' ', '\t');

        if (trimmed.Length == 0)
        {
            return LineKind.Blank;
        }

        // Four columns in is an indented code block rather than prose. A fenced one is already
        // guarded; this is the other kind.
        if (text.Length - trimmed.Length >= 4)
        {
            return LineKind.Other;
        }

        if (Heading().IsMatch(text))
        {
            return LineKind.Heading;
        }

        // Ahead of the thematic break, because "---" is both and under a paragraph the heading
        // is what it means.
        if (Setext().IsMatch(text))
        {
            return LineKind.Setext;
        }

        if (trimmed[0] is '>' or '|' || ListStructure.IsItem(text))
        {
            return LineKind.Absorbing;
        }

        if (trimmed[0] == '<' || ThematicBreak().IsMatch(text) || LinkReference().IsMatch(text))
        {
            return LineKind.Other;
        }

        return LineKind.Paragraph;
    }

    [GeneratedRegex(@"^[ \t]{0,3}#{1,6}([ \t]|$)")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^[ \t]{0,3}(=+|-+)[ \t]*$")]
    private static partial Regex Setext();

    [GeneratedRegex(@"^[ \t]{0,3}((\*[ \t]*){3,}|(-[ \t]*){3,}|(_[ \t]*){3,})$")]
    private static partial Regex ThematicBreak();

    /// <summary>A link or footnote definition: <c>[label]:</c> at the head of a line.</summary>
    [GeneratedRegex(@"^[ \t]{0,3}\[[^\]]+\]:")]
    private static partial Regex LinkReference();
}
