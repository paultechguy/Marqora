// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.RegularExpressions;

namespace PaulTechGuy.MQ.Docx;

/// <summary>One cell of a raw HTML table: its words, and where it sits in the grid.</summary>
internal sealed record HtmlTableCell(string Text, bool IsHeader, int ColumnSpan, int RowSpan, string? Alignment);

/// <summary>One row of a raw HTML table, and whether it came from the table's head.</summary>
internal sealed record HtmlTableRow(IReadOnlyList<HtmlTableCell> Cells, bool IsHead);

/// <summary>A raw HTML table read for Word: its caption and its rows.</summary>
internal sealed record HtmlTable(string? Caption, IReadOnlyList<HtmlTableRow> Rows);

/// <summary>
/// A raw HTML <c>&lt;table&gt;</c> block, read into rows and cells so Word can draw it as a
/// table.
///
/// It was written as its text, row by row - every word there and none of the shape, so the
/// fixture's table with a merged cell read as a column of loose figures. A table is the one
/// piece of raw HTML whose structure is plain from the markup alone and that Word has a
/// direct equivalent for: <c>colspan</c> is a grid span, <c>rowspan</c> a vertical merge.
///
/// Only a block that is one table and nothing else, with no table nested inside it. Anything
/// else returns null and keeps the plain-text floor, so a shape this does not understand is
/// never drawn wrong.
/// </summary>
internal static partial class HtmlTables
{
    public static HtmlTable? Read(string html)
    {
        string trimmed = html.Trim();

        if (!trimmed.StartsWith("<table", StringComparison.OrdinalIgnoreCase)
            || !trimmed.EndsWith("</table>", StringComparison.OrdinalIgnoreCase)
            || TableOpen().Count(trimmed) != 1)
        {
            return null;
        }

        string? caption = Caption().Match(trimmed) is { Success: true } found
            ? HtmlBlockText.PlainText(found.Groups[1].Value)
            : null;

        // Rows in the head are the header rows Word repeats on each page. A tfoot's rows stay
        // where the markup put them - last - which is also where the browser draws them.
        string head = Head().Match(trimmed) is { Success: true } inHead ? inHead.Value : string.Empty;

        var rows = new List<HtmlTableRow>();

        foreach (Match row in Row().Matches(trimmed))
        {
            var cells = new List<HtmlTableCell>();

            foreach (Match cell in Cell().Matches(row.Groups[1].Value))
            {
                string attributes = cell.Groups[2].Value;

                cells.Add(new HtmlTableCell(
                    HtmlBlockText.PlainText(cell.Groups[3].Value),
                    cell.Groups[1].Value.Equals("th", StringComparison.OrdinalIgnoreCase),
                    Span(attributes, "colspan"),
                    Span(attributes, "rowspan"),
                    TextAlign().Match(attributes) is { Success: true } align ? align.Groups[1].Value.ToLowerInvariant() : null));
            }

            if (cells.Count > 0)
            {
                rows.Add(new HtmlTableRow(cells, head.Length > 0 && head.Contains(row.Value, StringComparison.Ordinal)));
            }
        }

        return rows.Count > 0 ? new HtmlTable(caption, rows) : null;
    }

    private static int Span(string attributes, string name)
    {
        Match match = Regex.Match(attributes, name + @"\s*=\s*[""']?(\d+)", RegexOptions.IgnoreCase);

        return match.Success && int.TryParse(match.Groups[1].Value, out int span) && span > 1 ? Math.Min(span, 63) : 1;
    }

    [GeneratedRegex("<table\\b", RegexOptions.IgnoreCase)]
    private static partial Regex TableOpen();

    [GeneratedRegex("<caption\\b[^>]*>(.*?)</caption>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Caption();

    [GeneratedRegex("<thead\\b[^>]*>.*?</thead>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Head();

    [GeneratedRegex("<tr\\b[^>]*>(.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Row();

    [GeneratedRegex("<(td|th)\\b([^>]*)>(.*?)</\\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Cell();

    [GeneratedRegex("text-align\\s*:\\s*(left|center|right)", RegexOptions.IgnoreCase)]
    private static partial Regex TextAlign();
}
