// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Editing;

/// <summary>Shared selection tidying every command wants before it starts.</summary>
internal static class Selections
{
    /// <summary>
    /// Puts the selection in document order and drops a trailing line that holds none of
    /// it.
    ///
    /// Dragging down through a line and releasing at the start of the next one leaves a
    /// selection ending at column 0. That line contains nothing the user picked, so
    /// prefixing or wrapping it would act on a line they never touched.
    /// </summary>
    public static TextRange Normalize(EditContext context)
    {
        TextRange selection = context.Selection.Ordered;

        if (selection.IsEmpty || selection.End.Column != 0 || selection.End.Line <= selection.Start.Line)
        {
            return selection;
        }

        int previous = selection.End.Line - 1;

        return new TextRange(selection.Start, new TextPosition(previous, context.LineAt(previous)?.Length ?? 0));
    }

    /// <summary>The span covering a whole line, for commands that rewrite one outright.</summary>
    public static TextRange WholeLine(int line, string text) =>
        new(new TextPosition(line, 0), new TextPosition(line, text.Length));
}
