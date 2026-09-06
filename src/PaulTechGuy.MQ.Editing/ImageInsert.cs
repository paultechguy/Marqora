// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Editing;

/// <summary>
/// Drops an image reference in at the caret, leaving the caret where the alt text goes.
///
/// No placeholder word. Writing "![alt](shot.png)" would leave every untouched image labelled
/// "alt" - a non-empty alt text, so the check that looks for a missing one stays quiet and the
/// document ships that way. An empty pair of brackets with the caret inside them is what the
/// check is looking for, and typing fills it in just as readily.
/// </summary>
internal static class ImageInsert
{
    public static EditResult Apply(EditContext context, IReadOnlyList<string> references)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(references);

        if (references.Count == 0)
        {
            return EditResult.None;
        }

        TextRange selection = Selections.Normalize(context);
        int line = selection.Start.Line;

        if (context.LineAt(line) is not { } text)
        {
            return EditResult.None;
        }

        int start = Math.Clamp(selection.Start.Column, 0, text.Length);
        int end = Math.Clamp(selection.End.Column, start, text.Length);

        // Whatever was selected becomes the alt text of the first image. Selecting a caption and
        // pasting a screenshot over it is the one case where the author has already said what
        // the picture is.
        string alt = selection.IsSingleLine ? text[start..end] : string.Empty;

        var range = new TextRange(
            new TextPosition(line, start),
            new TextPosition(line, selection.IsSingleLine ? end : start));

        // More than one at a time only happens when several files were copied together, and
        // they are separate blocks rather than a run of images on one line.
        string body = string.Join(
            "\n\n",
            references.Select((reference, index) =>
                $"![{(index == 0 ? alt : string.Empty)}]({reference})"));

        return new EditResult(
            [new TextEdit(range, body)],
            CaretFor(line, start, alt));
    }

    /// <summary>
    /// Where the caret lands: inside the first image's brackets when there is nothing to say
    /// yet, and after the whole reference when the selection already said it.
    ///
    /// Only ever on the first line, because that is where the first image is; a caret parked at
    /// the end of a block of four is a caret nobody asked for.
    /// </summary>
    private static TextRange CaretFor(int line, int start, string alt)
    {
        int column = alt.Length == 0
            ? start + 2
            : start + alt.Length + 2;

        var caret = new TextPosition(line, column);

        return TextRange.At(caret);
    }
}
