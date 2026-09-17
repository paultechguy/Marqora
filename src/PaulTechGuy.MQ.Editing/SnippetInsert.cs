// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Markdown;

namespace PaulTechGuy.MQ.Editing;

/// <summary>
/// Drops a snippet in at the caret, and — for a snippet that asks — takes the text around the
/// caret in with it.
///
/// A snippet is plain markdown, not a template language. The two concessions are the markers in
/// <see cref="SnippetMarkers"/>: <c>$0</c> for where the caret lands, and <c>$SEL</c> for where
/// the captured text goes. A file someone drops into the snippets folder still reads as ordinary
/// markdown, which is the whole point of a folder of markdown files.
///
/// What gets captured, in the order the user's own actions decide it:
///
/// <list type="bullet">
/// <item>A selection is taken exactly as it stands, even a few words out of the middle of a
/// paragraph. What was on either side of it becomes a paragraph of its own, so a callout made
/// from one sentence leaves the rest of that paragraph reading correctly above and below it.</item>
/// <item>With nothing selected, the paragraph or heading the caret is in — see
/// <see cref="Paragraphs"/> for why it stops there.</item>
/// <item>Anywhere else, nothing: the marker comes out and the snippet goes in empty, which is
/// what it did before there was a marker at all.</item>
/// </list>
/// </summary>
internal static class SnippetInsert
{
    public static EditResult Apply(EditContext context, string body)
    {
        // Normalized once, up front: what gets captured depends on whether the body is a block,
        // and a body is only a block after its line endings have been folded and the trailing
        // newline a text editor left on the file has been dropped.
        string source = Normalize(body);

        // Resolved before the text is built rather than after, because what it takes is part of
        // the text: the captured lines are written into the body at the marker, carrying the
        // marker line's own prefix so a multi-line capture stays inside the block it landed in.
        Capture capture = Resolve(context, source);

        (string text, int caretLine, int caretColumn) = Prepare(source, capture.Lines);

        if (text.Length == 0)
        {
            return EditResult.None;
        }

        if (capture.Lines.Count == 0)
        {
            TextRange selection = Selections.Normalize(context);

            // A snippet with a line break in it is a block, and wants the blank lines markdown
            // needs around it. A single line goes in where the caret is, like typed text.
            return text.Contains('\n', StringComparison.Ordinal)
                ? BlockInsert.Insert(
                    context,
                    selection.Start.Line,
                    text.Split('\n'),
                    start => TextRange.At(new TextPosition(start + caretLine, caretColumn)))
                : Inline(selection, text, caretColumn);
        }

        // The same division, asked of the result rather than of the body: a one-line snippet that
        // captured a few words out of one line is still inline text, and giving it blank lines
        // and a paragraph of its own would be wrong.
        return text.Contains('\n', StringComparison.Ordinal) || !capture.Range.IsSingleLine
            ? Replace(context, capture.Range, text, caretLine, caretColumn)
            : Inline(capture.Range, text, caretColumn);
    }

    /// <summary>The text a snippet takes with it, and the span of the document it came out of.</summary>
    private readonly record struct Capture(TextRange Range, IReadOnlyList<string> Lines)
    {
        public static Capture None { get; } = new(default, []);
    }

    private static Capture Resolve(EditContext context, string source)
    {
        // No marker, nothing to capture. The window check is the other half of that: finding a
        // paragraph's edges, and telling a fenced block from prose, are both read from the top of
        // the file, so a context starting partway down cannot answer either. The view model asks
        // for a document-scoped one whenever the body carries the marker - see
        // EditContextScopes.ForSnippet - and this is what happens if some other caller does not.
        if (!SnippetMarkers.HasSelection(source) || context.FirstLine != 0)
        {
            return Capture.None;
        }

        bool[] guarded = MarkdownRegionScanner.FindProtectedLines(context.Lines);
        TextRange selection = Selections.Normalize(context);

        TextRange? found = selection.IsEmpty
            ? Paragraphs.Around(context, selection.Start.Line, guarded)

            // Only a block takes the spaces around the selection with it. An inline snippet
            // leaves the words either side of it where they are, and the space between them and
            // what it captured is still wanted.
            : source.Contains('\n', StringComparison.Ordinal) ? Widen(context, selection) : selection;

        if (found is not { } range || Straddles(guarded, range))
        {
            return Capture.None;
        }

        List<string> lines = Slice(context, range);

        // Whitespace is not worth breaking a paragraph in half for.
        return lines.TrueForAll(line => line.Trim().Length == 0) ? Capture.None : new Capture(range, lines);
    }

    /// <summary>
    /// Grows a selection out over the spaces and tabs on either side of it, so the gap the
    /// captured words leave behind goes with them rather than stranding a double space in what is
    /// left of the line. Whitespace running all the way to a line's edge means the whole line
    /// goes, rather than an indent being left behind above the block.
    /// </summary>
    private static TextRange Widen(EditContext context, TextRange selection)
    {
        string startText = context.LineAt(selection.Start.Line) ?? string.Empty;
        string endText = context.LineAt(selection.End.Line) ?? string.Empty;

        int startColumn = Math.Min(selection.Start.Column, startText.Length);
        while (startColumn > 0 && startText[startColumn - 1] is ' ' or '\t')
        {
            startColumn--;
        }

        int endColumn = Math.Min(selection.End.Column, endText.Length);
        while (endColumn < endText.Length && endText[endColumn] is ' ' or '\t')
        {
            endColumn++;
        }

        if (startColumn > 0 && startText[..startColumn].Trim().Length == 0)
        {
            startColumn = 0;
        }

        if (endText[endColumn..].Trim().Length == 0)
        {
            endColumn = endText.Length;
        }

        return new TextRange(
            new TextPosition(selection.Start.Line, startColumn),
            new TextPosition(selection.End.Line, endColumn));
    }

    /// <summary>The text inside <paramref name="range"/>, one entry per line it covers.</summary>
    private static List<string> Slice(EditContext context, TextRange range)
    {
        List<string> lines = [];

        for (int i = range.Start.Line; i <= range.End.Line; i++)
        {
            string text = context.LineAt(i) ?? string.Empty;
            int from = i == range.Start.Line ? Math.Min(range.Start.Column, text.Length) : 0;
            int to = i == range.End.Line ? Math.Min(range.End.Column, text.Length) : text.Length;

            lines.Add(text[Math.Min(from, to)..to]);
        }

        // A capture that starts partway along a line starts at a word, not at the space in front
        // of it. One that starts at column zero keeps its indent, because there the indent is
        // part of the block rather than a gap between it and the words before it.
        if (range.Start.Column > 0)
        {
            lines[0] = lines[0].TrimStart(' ', '\t');
        }

        // Trailing whitespace on the last line has nothing left to mean - two spaces at the end
        // of a paragraph are not a line break.
        lines[^1] = lines[^1].TrimEnd();

        return lines;
    }

    /// <summary>
    /// Whether a capture would begin or end partway through a fenced block or front matter,
    /// which would leave both halves broken. Opening on the fence line and closing on the other
    /// one is the whole block, and is fine.
    /// </summary>
    private static bool Straddles(bool[] guarded, TextRange range)
    {
        int start = range.Start.Line;
        int end = range.End.Line;

        if (start < 0 || end >= guarded.Length)
        {
            return true;
        }

        return (guarded[start] && start > 0 && guarded[start - 1])
            || (guarded[end] && end + 1 < guarded.Length && guarded[end + 1]);
    }

    private static EditResult Inline(TextRange range, string text, int caretColumn) =>
        new(
            [new TextEdit(new TextRange(range.Start, range.End), text)],
            TextRange.At(new TextPosition(range.Start.Line, range.Start.Column + caretColumn)));

    /// <summary>
    /// Puts a block where the captured text was, giving whatever is left of the line on either
    /// side a paragraph of its own.
    ///
    /// Unlike <see cref="BlockInsert.Insert"/> this replaces rather than inserts, and the blank
    /// lines are decided by what is left behind as well as by the neighboring lines: words before
    /// the capture end a paragraph the block must not weld itself to, and with nothing left on
    /// that side the line above decides, exactly as it does for a plain insert.
    /// </summary>
    private static EditResult Replace(
        EditContext context,
        TextRange range,
        string text,
        int caretLine,
        int caretColumn)
    {
        string startText = context.LineAt(range.Start.Line) ?? string.Empty;
        string endText = context.LineAt(range.End.Line) ?? string.Empty;

        string head = startText[..Math.Min(range.Start.Column, startText.Length)];
        string tail = endText[Math.Min(range.End.Column, endText.Length)..];

        string above = head.Length > 0
            ? "\n\n"
            : range.Start.Line > 0 && BlockInsert.NeedsAir(context.LineAt(range.Start.Line - 1))
                ? "\n"
                : string.Empty;

        string below = tail.Length > 0
            ? "\n\n"
            : BlockInsert.NeedsAir(context.LineAt(range.End.Line + 1))
                ? "\n"
                : string.Empty;

        // Every line of the block starts at column zero - either the range did, or the padding
        // above ends in a newline - so the caret's column inside the block is its column in the
        // document. Its line moves down by however many newlines the padding added.
        return new EditResult(
            [new TextEdit(range, above + text + below)],
            TextRange.At(new TextPosition(range.Start.Line + above.Length + caretLine, caretColumn)));
    }

    /// <summary>
    /// Cleans the body up, writes the captured text in at its marker, and works out where the
    /// caret belongs — walking the body once so the position accounts for every substitution
    /// made along the way, the captured text included.
    ///
    /// One walk rather than a substitution pass followed by a marker pass, because the captured
    /// text is the user's own document and may well contain a <c>$0</c> of its own. Appending it
    /// here means it is never scanned for markers.
    /// </summary>
    private static (string Text, int CaretLine, int CaretColumn) Prepare(
        string source,
        IReadOnlyList<string> captured)
    {
        var builder = new StringBuilder(source.Length);

        int caretLine = -1;
        int caretColumn = -1;
        int line = 0;
        int column = 0;
        int lineStart = 0;
        int i = 0;

        while (i < source.Length)
        {
            ReadOnlySpan<char> rest = source.AsSpan(i);

            // Each escape is tested before the marker it escapes, or "$$0" reads as a dollar
            // followed by a caret marker.
            if (rest.StartsWith(SnippetMarkers.EscapedSelection, StringComparison.Ordinal))
            {
                builder.Append(SnippetMarkers.Selection);
                column += SnippetMarkers.Selection.Length;
                i += SnippetMarkers.EscapedSelection.Length;

                continue;
            }

            if (rest.StartsWith(SnippetMarkers.Selection, StringComparison.Ordinal))
            {
                i += SnippetMarkers.Selection.Length;

                // Nothing was captured, so the marker comes out the way a spare $0 does. This is
                // the caret-in-a-table case, and the snippet goes in empty.
                if (captured.Count == 0)
                {
                    continue;
                }

                Substitute(builder, captured, ref line, ref column, ref lineStart);

                continue;
            }

            if (rest.StartsWith(SnippetMarkers.EscapedCaret, StringComparison.Ordinal))
            {
                builder.Append(SnippetMarkers.Caret);
                column += SnippetMarkers.Caret.Length;
                i += SnippetMarkers.EscapedCaret.Length;

                continue;
            }

            if (rest.StartsWith(SnippetMarkers.Caret, StringComparison.Ordinal))
            {
                // Every marker is taken out, but only the first moves the caret. A stray "$0"
                // left behind in the document is worse than one that disappears.
                if (caretLine < 0)
                {
                    caretLine = line;
                    caretColumn = column;
                }

                i += SnippetMarkers.Caret.Length;

                continue;
            }

            char current = source[i];
            builder.Append(current);

            if (current == '\n')
            {
                line++;
                column = 0;
                lineStart = builder.Length;
            }
            else
            {
                column++;
            }

            i++;
        }

        // With no marker at all, the caret ends up after what was inserted.
        return (builder.ToString(), caretLine < 0 ? line : caretLine, caretColumn < 0 ? column : caretColumn);
    }

    /// <summary>
    /// Writes the captured lines in at the marker. The first continues the line the marker was
    /// on; every line after it repeats that line's prefix, so a paragraph captured into
    /// <c>&gt; $SEL</c> comes out quoted all the way down rather than escaping the callout on its
    /// second line.
    /// </summary>
    private static void Substitute(
        StringBuilder builder,
        IReadOnlyList<string> captured,
        ref int line,
        ref int column,
        ref int lineStart)
    {
        string prefix = Continuation(builder.ToString(lineStart, builder.Length - lineStart));

        builder.Append(captured[0]);
        column += captured[0].Length;

        for (int c = 1; c < captured.Count; c++)
        {
            // A blank line inside the capture - the gap between two selected paragraphs - takes
            // the prefix without its trailing space, so an otherwise empty line is not left
            // carrying trailing whitespace.
            string written = captured[c].Trim().Length == 0
                ? prefix.TrimEnd()
                : prefix + captured[c];

            builder.Append('\n').Append(written);

            line++;
            lineStart = builder.Length - written.Length;
            column = written.Length;
        }
    }

    /// <summary>
    /// What the marker's line has to repeat on the lines below it: its blockquote markers and
    /// whitespace as they stand, and everything else as a space of the same width.
    ///
    /// That one rule covers the three shapes a marker can sit in. <c>&gt; $SEL</c> repeats
    /// "&gt; " and stays a blockquote. <c>- $SEL</c> becomes two spaces, which is what a list
    /// item's continuation is indented by. A marker alone on its line repeats nothing.
    /// </summary>
    private static string Continuation(string prefix)
    {
        char[] characters = prefix.ToCharArray();

        for (int i = 0; i < characters.Length; i++)
        {
            if (characters[i] != '>' && !char.IsWhiteSpace(characters[i]))
            {
                characters[i] = ' ';
            }
        }

        return new string(characters);
    }

    /// <summary>
    /// Strips a byte-order mark, folds every line ending to a plain newline, and drops the
    /// one trailing newline a text editor leaves at the end of a file.
    ///
    /// The line endings matter more than they look: the shell turns every newline it
    /// receives into the document's own ending, so a carriage return arriving from a
    /// Windows-authored snippet would come out doubled on every line.
    /// </summary>
    private static string Normalize(string body)
    {
        string text = body.TrimStart('﻿')
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);

        return text.EndsWith('\n') ? text[..^1] : text;
    }
}
