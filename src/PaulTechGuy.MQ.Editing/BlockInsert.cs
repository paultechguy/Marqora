// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Editing;

/// <summary>
/// Whole blocks dropped in at the caret: fences, tables and rules, each with the blank
/// lines markdown needs around them.
/// </summary>
internal static class BlockInsert
{
    private const string _fence = "```";

    public static EditResult CodeBlock(EditContext context)
    {
        TextRange selection = Selections.Normalize(context);

        // With something selected the fence goes around it, which is the usual reason for
        // reaching for this on an existing document.
        if (!selection.IsEmpty)
        {
            string? last = context.LineAt(selection.End.Line);
            string? above = selection.Start.Line > 0 ? context.LineAt(selection.Start.Line - 1) : null;
            string? below = context.LineAt(selection.End.Line + 1);

            string opening = (NeedsAir(above) ? "\n" : string.Empty) + _fence + "\n";
            string closing = "\n" + _fence + (NeedsAir(below) ? "\n" : string.Empty);

            return new EditResult(
                [
                    new TextEdit(TextRange.At(new TextPosition(selection.Start.Line, 0)), opening),
                    new TextEdit(
                        TextRange.At(new TextPosition(selection.End.Line, last?.Length ?? selection.End.Column)),
                        closing),
                ],
                null);
        }

        // Otherwise an empty fence, with the caret on the line between the two rails.
        return Insert(
            context,
            selection.Start.Line,
            [_fence, string.Empty, _fence],
            body => TextRange.At(new TextPosition(body + 1, 0)));
    }

    public static EditResult Table(EditContext context)
    {
        TextRange selection = Selections.Normalize(context);

        // Deliberately loose: Format Document's table rule lines the columns up properly,
        // so there is no point doing it twice.
        string[] block =
        [
            "| Column 1 | Column 2 | Column 3 |",
            "| --- | --- | --- |",
            "|  |  |  |",
        ];

        // Leave the first heading selected so it can be typed straight over.
        return Insert(
            context,
            selection.Start.Line,
            block,
            body => new TextRange(new TextPosition(body, 2), new TextPosition(body, 10)));
    }

    public static EditResult HorizontalRule(EditContext context) =>
        Insert(context, Selections.Normalize(context).Start.Line, ["---"], _ => null);

    /// <summary>
    /// Puts <paramref name="block"/> in as whole lines ahead of <paramref name="at"/>,
    /// padding with blank lines wherever it would otherwise weld itself to a neighbour.
    /// The callback receives the document line the block itself starts on, once any
    /// padding above has been accounted for.
    /// </summary>
    internal static EditResult Insert(
        EditContext context,
        int at,
        IReadOnlyList<string> block,
        Func<int, TextRange?> selection)
    {
        List<string> lines = [];

        if (at > 0 && NeedsAir(context.LineAt(at - 1)))
        {
            lines.Add(string.Empty);
        }

        int offset = lines.Count;
        lines.AddRange(block);

        if (NeedsAir(context.LineAt(at)))
        {
            lines.Add(string.Empty);
        }

        return new EditResult(
            [new TextEdit(TextRange.At(new TextPosition(at, 0)), string.Join("\n", lines) + "\n")],
            selection(at + offset));
    }

    /// <summary>
    /// True when a neighbouring line holds text, and so needs a blank line between it and
    /// the block. A line off the end of the window is the end of the document, which needs
    /// nothing.
    /// </summary>
    private static bool NeedsAir(string? line) => line is not null && line.Trim().Length > 0;
}
