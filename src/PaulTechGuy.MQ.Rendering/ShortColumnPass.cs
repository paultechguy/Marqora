// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Markdig.Extensions.Tables;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using MarkdigDocument = Markdig.Syntax.MarkdownDocument;

namespace PaulTechGuy.MQ.Rendering;

/// <summary>
/// Keeps a table's short columns from wrapping in the preview.
///
/// A table gets the full measure and the browser shares it out, and when one column holds
/// prose the browser pays for it by squeezing the others down to their longest single word -
/// so a "Due date" header breaks over two lines beside a column of sentences. Markdown has no
/// way to say how wide a column is, and the workaround people reach for is a run of
/// <c>&amp;nbsp;</c> in the header. The author should not have to: a column whose every cell is
/// short is marked here, and <c>app.css</c> stops it wrapping.
///
/// The budget is what keeps this from making things worse. A column that cannot wrap cannot
/// give up any room either, so a table of nothing but short columns could be marked into
/// overflowing a narrow pane - or, in print, the page, where there is no scroll bar to rescue
/// it. The shortest columns are marked first, and marking stops once their combined text
/// would pass the budget, which leaves the rest free to wrap the way they always have.
///
/// Preview only. The Word export shares the columns out itself, with its own floor on how
/// narrow one may get; see <c>DocxTables</c>.
/// </summary>
internal static class ShortColumnPass
{
    /// <summary>The class <c>app.css</c> gives <c>white-space: nowrap</c>.</summary>
    public const string NoWrapClass = "mq-nowrap";

    /// <summary>A column is short when no cell in it, header included, is longer than this.</summary>
    internal const int ShortCellCharacters = 20;

    /// <summary>The most text, summed across the marked columns, one table may hold unwrapped.</summary>
    internal const int TableBudgetCharacters = 60;

    public static void Apply(MarkdigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (Table table in document.Descendants<Table>())
        {
            Apply(table);
        }
    }

    private static void Apply(Table table)
    {
        // Longest cell per column. A cell spanning several columns says nothing about any one
        // of them, so it is neither measured nor marked.
        var longest = new Dictionary<int, int>();

        foreach (var (cell, column) in SingleColumnCells(table))
        {
            longest[column] = Math.Max(longest.GetValueOrDefault(column), TableCellText.LengthOf(cell));
        }

        var marked = new HashSet<int>();
        int spent = 0;

        foreach (var (column, length) in longest
            .Where(entry => entry.Value <= ShortCellCharacters)
            .OrderBy(entry => entry.Value)
            .ThenBy(entry => entry.Key))
        {
            if (spent + length > TableBudgetCharacters)
            {
                break;
            }

            spent += length;
            marked.Add(column);
        }

        // Every column fitting means nothing was competing for the room, and the browser would
        // not have wrapped any of them. Marking the lot would only take away its freedom to.
        if (marked.Count == 0 || marked.Count == longest.Count)
        {
            return;
        }

        foreach (var (cell, column) in SingleColumnCells(table))
        {
            if (marked.Contains(column))
            {
                cell.GetAttributes().AddClass(NoWrapClass);
            }
        }
    }

    /// <summary>
    /// Each cell that occupies exactly one column, with that column's index. Markdig fills in
    /// <see cref="TableCell.ColumnIndex"/> for a grid table; a pipe table leaves it unset and
    /// the position in the row is the answer.
    /// </summary>
    private static IEnumerable<(TableCell Cell, int Column)> SingleColumnCells(Table table)
    {
        foreach (TableRow row in table.OfType<TableRow>())
        {
            int index = 0;

            foreach (TableCell cell in row.OfType<TableCell>())
            {
                int at = cell.ColumnIndex >= 0 ? cell.ColumnIndex : index;
                int span = Math.Max(1, cell.ColumnSpan);

                if (span == 1)
                {
                    yield return (cell, at);
                }

                index += span;
            }
        }
    }
}
