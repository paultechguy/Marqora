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

    /// <summary>
    /// The lines a command should act on: those the selection touches, minus the blank ones,
    /// which separate blocks rather than belonging to them. A selection that is entirely blank
    /// falls back to the caret's own line, so the command still does something in an empty
    /// document.
    /// </summary>
    public static List<(int Line, string Text)> Targets(EditContext context, out TextRange selection)
    {
        selection = Normalize(context);

        List<(int Line, string Text)> targets = [];
        for (int i = selection.Start.Line; i <= selection.End.Line; i++)
        {
            if (context.LineAt(i) is { } text)
            {
                targets.Add((i, text));
            }
        }

        if (targets.Count == 0)
        {
            return targets;
        }

        List<(int Line, string Text)> content = targets.FindAll(t => t.Text.Trim().Length > 0);

        return content.Count > 0 ? content : [targets[0]];
    }
}
