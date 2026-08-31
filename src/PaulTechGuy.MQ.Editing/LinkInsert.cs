// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Editing;

/// <summary>
/// Builds a link around whatever the caret has hold of, putting the caret wherever there
/// is still something left to type.
/// </summary>
internal static partial class LinkInsert
{
    private const string Placeholder = "url";

    public static EditResult Apply(EditContext context)
    {
        TextRange selection = Selections.Normalize(context);

        // A link label can run across lines, so a multi-line selection gets bracketed at
        // each end rather than refused.
        if (!selection.IsSingleLine)
        {
            var tail = new TextPosition(selection.End.Line, selection.End.Column + 2);

            return new EditResult(
                [
                    new TextEdit(TextRange.At(selection.Start), "["),
                    new TextEdit(TextRange.At(selection.End), $"]({Placeholder})"),
                ],
                new TextRange(tail, tail with { Column = tail.Column + Placeholder.Length }));
        }

        int line = selection.Start.Line;
        if (context.LineAt(line) is not { } text)
        {
            return EditResult.None;
        }

        int start = Math.Clamp(selection.Start.Column, 0, text.Length);
        int end = Math.Clamp(selection.End.Column, 0, text.Length);
        string inner = text[start..end];

        // A selected URL becomes the destination and the label is what is missing; any
        // other selection becomes the label and the destination is what is missing.
        // Either way the caret lands on the empty half.
        if (inner.Length == 0 || LooksLikeUrl(inner))
        {
            var caret = new TextPosition(line, start + 1);

            return new EditResult(
                [new TextEdit(new TextRange(new TextPosition(line, start), new TextPosition(line, end)), $"[]({inner})")],
                TextRange.At(caret));
        }

        int destination = start + inner.Length + 3;

        return new EditResult(
            [new TextEdit(new TextRange(new TextPosition(line, start), new TextPosition(line, end)), $"[{inner}]({Placeholder})")],
            new TextRange(
                new TextPosition(line, destination),
                new TextPosition(line, destination + Placeholder.Length)));
    }

    private static bool LooksLikeUrl(string text) =>
        UrlLike().IsMatch(text.Trim());

    [GeneratedRegex(@"^([a-zA-Z][a-zA-Z0-9+.\-]*:|www\.)", RegexOptions.CultureInvariant)]
    private static partial Regex UrlLike();
}
