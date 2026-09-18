// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Rendering.Tests;

/// <summary>
/// The sectioning a rendered table comes out with, which two other things quietly depend on.
///
/// <c>app.css</c> tints the header with <c>thead th</c> rather than <c>th</c>, so that a row
/// header down the left of a table is not painted like a heading. <c>InlineTableStyles</c> then
/// carries that same rule onto the cells for the rich-text clipboard, and decides which cells
/// are in the head by watching for the <c>thead</c> tag as it walks the markup.
///
/// Both are reading a promise made by Markdig's table renderer rather than by anything in this
/// repository. Nothing asserted it until a pasted table came out with no header at all and the
/// question of what the markup actually looks like turned out to be unanswered.
/// </summary>
public class TableMarkupShapeTests
{
    private static readonly MarkdigMarkdownRenderer Renderer =
        new(NullLogger<MarkdigMarkdownRenderer>.Instance);

    private const string PipeTable = """
        | Name | Size |
        | --- | --- |
        | first | 1 |
        | second | 2 |
        """;

    [Fact]
    public void A_pipe_table_is_rendered_with_a_head_and_a_body()
    {
        string html = Renderer.Render(PipeTable).Html;

        html.ShouldContain("<thead>");
        html.ShouldContain("<tbody>");
    }

    /// <summary>
    /// The header cells are inside the head, not merely first. This is the part the clipboard
    /// stamp relies on: a <c>th</c> it meets before any <c>thead</c> gets no tint.
    /// </summary>
    [Fact]
    public void The_header_cells_are_inside_the_head()
    {
        string html = Renderer.Render(PipeTable).Html;

        // Searched for with the bracket, since "<th" is also the front of "<thead".
        int cell = FirstHeaderCell(html);

        cell.ShouldBeGreaterThan(html.IndexOf("<thead>", StringComparison.Ordinal));
        cell.ShouldBeLessThan(html.IndexOf("</thead>", StringComparison.Ordinal));
    }

    /// <summary>
    /// Body rows are ordinary cells. If these came out as <c>th</c> the whole table would wear
    /// the header tint once the stamp had been applied.
    /// </summary>
    [Fact]
    public void Body_rows_are_data_cells()
    {
        string html = Renderer.Render(PipeTable).Html;
        int body = html.IndexOf("<tbody>", StringComparison.Ordinal);

        html[body..].ShouldContain("<td");
        FirstHeaderCell(html[body..]).ShouldBe(-1);
    }

    /// <summary>
    /// Where the first real header cell starts, opening tag only. Markdig writes <c>&lt;th&gt;</c>
    /// with no attributes for an unaligned column and <c>&lt;th style=...&gt;</c> for an aligned
    /// one, and both have to be found without <c>&lt;thead&gt;</c> answering instead.
    /// </summary>
    private static int FirstHeaderCell(string html)
    {
        int plain = html.IndexOf("<th>", StringComparison.Ordinal);
        int withAttributes = html.IndexOf("<th ", StringComparison.Ordinal);

        return plain < 0 ? withAttributes
            : withAttributes < 0 ? plain
            : Math.Min(plain, withAttributes);
    }
}
