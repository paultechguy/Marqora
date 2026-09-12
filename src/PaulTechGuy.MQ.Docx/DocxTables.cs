// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using DocumentFormat.OpenXml.Wordprocessing;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkdigTable = Markdig.Extensions.Tables.Table;
using MarkdigTableCell = Markdig.Extensions.Tables.TableCell;
using MarkdigTableRow = Markdig.Extensions.Tables.TableRow;
using WordTable = DocumentFormat.OpenXml.Wordprocessing.Table;

namespace PaulTechGuy.MQ.Docx;

/// <summary>
/// Column widths, and the few table properties that are not in the style.
///
/// The widths are the interesting part. Markdig reports a percentage per column for a grid
/// table, because the author drew the rules and their positions mean something; for a pipe
/// table it reports nothing at all, because <c>| a | b |</c> says nothing about how wide
/// either column should be. Splitting a pipe table evenly is the obvious answer and a poor
/// one - a column of "Yes" and "No" gets the same room as a column of prose - so the widths
/// are weighted by what is actually in each column and then clamped, which keeps a long
/// column from swallowing the page and a short one from disappearing.
/// </summary>
internal static class DocxTables
{
    /// <summary>Table width as fiftieths of a percent, so five thousand is the full measure.</summary>
    private const int FullWidthPercent = 5000;

    /// <summary>No column may be narrower than this fraction of an even share.</summary>
    private const double MinimumShare = 0.45;

    /// <summary>Nor wider than this multiple of one.</summary>
    private const double MaximumShare = 2.2;

    /// <summary>
    /// Word decides the proportions; the table fills the measure. Word calls this combination
    /// AutoFit to Window, and it is the closest thing Word has to what the preview does.
    ///
    /// Two separate questions hide in "how wide should this table be". How wide is the table -
    /// and markdown does not say, but the preview answers it the same way every time, because
    /// the stylesheet gives a table the full measure. And how the width is shared between the
    /// columns - which markdown also does not say, and which is genuinely a measurement
    /// question about the content.
    ///
    /// An earlier version answered both by guessing, from how much text each column held, and
    /// writing fixed widths down. The guess was usually wrong and, written down, stopped Word
    /// improving on it. Handing the second question to Word fixed that and lost the first: a
    /// table with little in it came out a third of the page wide next to a PDF where the same
    /// table spanned it. Stating the width and letting Word share it out answers both.
    /// </summary>
    public static TableProperties Properties() => new(
        new TableStyle { Val = StyleIds.Table },
        new TableWidth { Width = FullWidthPercent.ToString(Invariant), Type = TableWidthUnitValues.Pct },
        new TableLayout { Type = TableLayoutValues.Autofit },

        // Switches on the conditional formatting the style defines for the first row, and
        // switches off the banding it does not define. Without it the header is not shaded,
        // however carefully the style describes it.
        new TableLook
        {
            Val = "04A0",
            FirstRow = true,
            LastRow = false,
            FirstColumn = false,
            LastColumn = false,
            NoHorizontalBand = false,
            NoVerticalBand = true,
        });

    public static TableGrid Grid(IReadOnlyList<int> widths)
    {
        var grid = new TableGrid();

        foreach (int width in widths)
        {
            grid.AppendChild(new GridColumn { Width = width.ToString(Invariant) });
        }

        return grid;
    }

    /// <summary>
    /// How wide each column should be, in twips, totalling exactly the usable width.
    ///
    /// The total matters: a fixed-layout table whose grid does not add up to its stated width
    /// is re-fitted by Word in a way that ignores both, so all the rounding is absorbed into
    /// the last column rather than left scattered.
    /// </summary>
    public static int[] Widths(MarkdigTable table, int usableTwips)
    {
        ArgumentNullException.ThrowIfNull(table);

        int columns = ColumnCount(table);

        if (columns == 0)
        {
            return [];
        }

        double[] shares = ProportionalShares(table, columns) ?? MeasuredShares(table, columns);

        var widths = new int[columns];
        double total = shares.Sum();

        for (int i = 0; i < columns; i++)
        {
            widths[i] = (int)Math.Round(usableTwips * (shares[i] / total));
        }

        widths[^1] = usableTwips - widths[..^1].Sum();

        return widths;
    }

    /// <summary>
    /// The widths a grid table stated, or null when the table did not state any - which is
    /// every pipe table, and is reported as a width of zero rather than as an absence.
    /// </summary>
    private static double[]? ProportionalShares(MarkdigTable table, int columns)
    {
        if (table.ColumnDefinitions.Count < columns)
        {
            return null;
        }

        var shares = new double[columns];
        double total = 0;

        for (int i = 0; i < columns; i++)
        {
            shares[i] = table.ColumnDefinitions[i].Width;
            total += shares[i];
        }

        return total > 0.01 ? shares : null;
    }

    /// <summary>
    /// Shares weighted by the widest plain-text cell in each column, clamped so that neither
    /// extreme can take over. Measured in characters, which is crude and is the right amount
    /// of effort: the answer only has to be better than splitting evenly.
    /// </summary>
    private static double[] MeasuredShares(MarkdigTable table, int columns)
    {
        var longest = new double[columns];

        foreach (MarkdigTableRow row in table.OfType<MarkdigTableRow>())
        {
            int index = 0;

            foreach (MarkdigTableCell cell in row.OfType<MarkdigTableCell>())
            {
                int at = cell.ColumnIndex >= 0 ? cell.ColumnIndex : index;

                if (at < columns)
                {
                    longest[at] = Math.Max(longest[at], TextLengthOf(cell));
                }

                index += Math.Max(1, cell.ColumnSpan);
            }
        }

        double average = longest.Sum() / columns;

        if (average <= 0)
        {
            Array.Fill(longest, 1);
            average = 1;
        }

        for (int i = 0; i < columns; i++)
        {
            longest[i] = Math.Clamp(longest[i], average * MinimumShare, average * MaximumShare);
        }

        return longest;
    }

    /// <summary>
    /// How much text a cell holds, counted in characters and ignoring every kind of markup
    /// around it. A cell holding a bold word is exactly as wide as one holding the same word
    /// plain, which is what a column-width estimate wants to know.
    /// </summary>
    private static int TextLengthOf(MarkdigTableCell cell)
    {
        int length = 0;

        foreach (LiteralInline literal in cell.Descendants<LiteralInline>())
        {
            length += literal.Content.Length;
        }

        return length;
    }

    /// <summary>
    /// How many columns the table has. The column definitions are authoritative when they
    /// exist; otherwise the widest row decides, because a markdown table is allowed to have a
    /// short row and Word is not.
    /// </summary>
    public static int ColumnCount(MarkdigTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        int widest = 0;

        foreach (MarkdigTableRow row in table.OfType<MarkdigTableRow>())
        {
            int columns = row.OfType<MarkdigTableCell>().Sum(cell => Math.Max(1, cell.ColumnSpan));

            widest = Math.Max(widest, columns);
        }

        return Math.Max(widest, table.ColumnDefinitions.Count);
    }

    /// <summary>
    /// How a column's cells are aligned, or null when the author said nothing and Word's own
    /// default is the right answer.
    /// </summary>
    public static JustificationValues? AlignmentOf(MarkdigTable table, int column)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (column >= table.ColumnDefinitions.Count)
        {
            return null;
        }

        return table.ColumnDefinitions[column].Alignment switch
        {
            TableColumnAlign.Center => JustificationValues.Center,
            TableColumnAlign.Right => JustificationValues.Right,
            TableColumnAlign.Left => JustificationValues.Left,
            _ => null,
        };
    }

    public static WordTable Empty() => new(Properties());

    private static CultureInfo Invariant => CultureInfo.InvariantCulture;
}
