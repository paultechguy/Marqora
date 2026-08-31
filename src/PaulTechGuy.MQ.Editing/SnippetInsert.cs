// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Editing;

/// <summary>
/// Drops a snippet in at the caret.
///
/// A snippet is plain markdown, not a template language. The single concession is an
/// optional <c>$0</c> marking where the caret should land; without one it lands at the end
/// of what was inserted. That keeps a file someone drops into the snippets folder readable
/// as ordinary markdown, which is the whole point of a folder of markdown files.
/// </summary>
internal static class SnippetInsert
{
    private const string CaretMarker = "$0";
    private const string EscapedCaret = "$$0";

    public static EditResult Apply(EditContext context, string body)
    {
        (string text, int caretLine, int caretColumn) = Prepare(body);

        if (text.Length == 0)
        {
            return EditResult.None;
        }

        TextRange selection = Selections.Normalize(context);

        // A snippet with a line break in it is a block, and wants the blank lines markdown
        // needs around it. A single line goes in where the caret is, like typed text.
        if (!text.Contains('\n', StringComparison.Ordinal))
        {
            return Inline(selection, text, caretColumn);
        }

        return BlockInsert.Insert(
            context,
            selection.Start.Line,
            text.Split('\n'),
            start => TextRange.At(new TextPosition(start + caretLine, caretColumn)));
    }

    private static EditResult Inline(TextRange selection, string text, int caretColumn) =>
        new(
            [new TextEdit(new TextRange(selection.Start, selection.End), text)],
            TextRange.At(new TextPosition(selection.Start.Line, selection.Start.Column + caretColumn)));

    /// <summary>
    /// Cleans the body up and works out where the caret belongs, walking it once so the
    /// position accounts for every substitution made along the way.
    /// </summary>
    private static (string Text, int CaretLine, int CaretColumn) Prepare(string body)
    {
        string source = Normalize(body);
        var builder = new StringBuilder(source.Length);

        int caretLine = -1;
        int caretColumn = -1;
        int line = 0;
        int column = 0;
        int i = 0;

        while (i < source.Length)
        {
            // "$$0" is how a snippet asks for a literal "$0". Shell scripts and regular
            // expressions contain one often enough to be worth the escape.
            if (source.AsSpan(i).StartsWith(EscapedCaret, StringComparison.Ordinal))
            {
                builder.Append(CaretMarker);
                column += CaretMarker.Length;
                i += EscapedCaret.Length;

                continue;
            }

            if (source.AsSpan(i).StartsWith(CaretMarker, StringComparison.Ordinal))
            {
                // Every marker is taken out, but only the first moves the caret. A stray
                // "$0" left behind in the document is worse than one that disappears.
                if (caretLine < 0)
                {
                    caretLine = line;
                    caretColumn = column;
                }

                i += CaretMarker.Length;

                continue;
            }

            char current = source[i];
            builder.Append(current);

            if (current == '\n')
            {
                line++;
                column = 0;
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
