// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Abstractions.Editing;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Editing.Tests;

/// <summary>
/// Runs a command the way the editor does and hands back the resulting document.
///
/// The apply step here is a small stand-in for the one in the web shell: edits are
/// addressed against the document as it was before any of them ran, so they are applied
/// back to front. Going through it in the tests means the edits are proven to compose,
/// not just to look right individually.
/// </summary>
internal static class Edits
{
    private static readonly IMarkdownEditor Editor = new MarkdownEditor();

    public static EditContext Caret(string document, int line, int column) =>
        Selection(document, line, column, line, column);

    public static EditContext Selection(string document, int startLine, int startColumn, int endLine, int endColumn) =>
        new(
            document.Split('\n'),
            0,
            new TextRange(new TextPosition(startLine, startColumn), new TextPosition(endLine, endColumn)));

    /// <summary>The document after running <paramref name="command"/>.</summary>
    public static string Run(EditContext context, MarkdownEditCommand command) =>
        Apply(context, Editor.Apply(command, context));

    /// <summary>The document after inserting a snippet.</summary>
    public static string RunSnippet(EditContext context, string body) =>
        Apply(context, Editor.Insert(body, context));

    /// <summary>What the toolbar would show for this selection.</summary>
    public static MarkdownMarkState Describe(EditContext context) => Editor.Describe(context);

    /// <summary>The text the command left selected, for checking where the caret ended up.</summary>
    public static string Selected(EditContext context, MarkdownEditCommand command) =>
        Selected(context, Editor.Apply(command, context));

    /// <summary>The same, for a snippet.</summary>
    public static string SelectedAfterSnippet(EditContext context, string body) =>
        Selected(context, Editor.Insert(body, context));

    /// <summary>
    /// The document and selection a command produced, together, so a test can carry on
    /// from where it left the caret — which is what pressing a toggle twice does.
    /// </summary>
    public static EditContext Then(EditContext context, MarkdownEditCommand command)
    {
        EditResult result = Editor.Apply(command, context);
        string text = Apply(context, result);

        return new EditContext(text.Split('\n'), 0, result.Selection ?? context.Selection);
    }

    private static string Selected(EditContext context, EditResult result)
    {
        if (result.Selection is not { } selection)
        {
            return string.Empty;
        }

        string[] lines = Apply(context, result).Split('\n');
        TextRange ordered = selection.Ordered;

        if (ordered.Start.Line != ordered.End.Line)
        {
            return string.Join("\n", lines[ordered.Start.Line..(ordered.End.Line + 1)]);
        }

        string line = lines[ordered.Start.Line];

        return line[Math.Min(ordered.Start.Column, line.Length)..Math.Min(ordered.End.Column, line.Length)];
    }

    private static string Apply(EditContext context, EditResult result)
    {
        List<string> lines = [.. context.Lines];

        IEnumerable<TextEdit> ordered = result.Edits
            .OrderByDescending(e => e.Range.Ordered.Start.Line)
            .ThenByDescending(e => e.Range.Ordered.Start.Column);

        foreach (TextEdit edit in ordered)
        {
            TextRange range = edit.Range.Ordered;
            string head = lines[range.Start.Line][..range.Start.Column];
            string tail = lines[range.End.Line][range.End.Column..];

            lines.RemoveRange(range.Start.Line, range.End.Line - range.Start.Line + 1);
            lines.InsertRange(range.Start.Line, (head + edit.Text + tail).Split('\n'));
        }

        return string.Join("\n", lines);
    }
}
