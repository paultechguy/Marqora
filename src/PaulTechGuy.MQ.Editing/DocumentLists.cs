// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Editing;

/// <summary>What renumbering every list in a document did.</summary>
/// <param name="Text">The document, with lines joined by LF.</param>
/// <param name="Changed">How many lists changed.</param>
/// <param name="LeftAlone">
/// One-based lines of the lists asked for zeros that cannot take them: Renumber List offers zeros
/// only for a list that follows a break, because otherwise "0." would read as paragraph text.
/// </param>
public sealed record ListsRenumbered(string Text, int Changed, IReadOnlyList<int> LeftAlone);

/// <summary>
/// Renumber List, run on every numbered list in a document in turn: marqora-cli's
/// renumber-lists (CLI-VB-REN-01).
///
/// Each list is renumbered exactly as the command would with the caret on its first line, and in
/// document order, so the result is the one a person would get doing them one at a time. Without
/// a numbering each list gets the one the command would suggest for it: a list counting up becomes
/// the 1. 1. 1. shorthand and the shorthand is counted back up, so a list that repeats 1. on
/// purpose is never quietly renumbered by a default.
/// </summary>
public static class DocumentLists
{
    /// <param name="numbering">The numbering for every list, or null for each list's own suggestion.</param>
    public static ListsRenumbered RenumberAll(string markdown, ListNumbering? numbering)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        List<string> lines = [.. markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n')];
        var leftAlone = new List<int>();
        int changed = 0;

        // Where the lists start is read again after each one, though renumbering never adds or
        // removes a line: a wider marker pushes the item's own lines right, and the next list is
        // read from the document as it now is rather than as it was.
        for (int index = 0; ; index++)
        {
            IReadOnlyList<int> starts = ListRenumber.ListStarts(lines);

            if (index >= starts.Count)
            {
                break;
            }

            int line = starts[index];
            var context = new EditContext(lines, 0, TextRange.At(new TextPosition(line, 0)));

            if (ListRenumber.Describe(context) is not { } summary)
            {
                continue;
            }

            ListNumbering chosen = numbering ?? summary.Suggested;

            if (chosen == ListNumbering.Zeros && !summary.CanStartAtZero)
            {
                leftAlone.Add(line + 1);
                continue;
            }

            EditResult result = ListRenumber.Apply(context, chosen);

            if (!result.IsEmpty)
            {
                Apply(lines, result.Edits);
                changed++;
            }
        }

        return new ListsRenumbered(string.Join('\n', lines), changed, leftAlone);
    }

    /// <summary>
    /// The edits, each within one line, applied from the last to the first so an earlier one's
    /// columns still mean what they did when it was made.
    /// </summary>
    private static void Apply(List<string> lines, IReadOnlyList<TextEdit> edits)
    {
        foreach (TextEdit edit in edits.OrderByDescending(e => e.Range.Start.Line).ThenByDescending(e => e.Range.Start.Column))
        {
            TextPosition start = edit.Range.Start;
            TextPosition end = edit.Range.End;

            if (start.Line != end.Line)
            {
                throw new InvalidOperationException("Renumber List made an edit across lines, which it never does.");
            }

            string text = lines[start.Line];
            lines[start.Line] = text[..start.Column] + edit.Text + text[end.Column..];
        }
    }
}
