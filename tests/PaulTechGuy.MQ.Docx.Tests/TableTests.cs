// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Docx.Tests;

/// <summary>
/// Tables, in both the shapes markdown writes them.
///
/// Three of these are about things Word does silently rather than about appearance. A cell
/// with no paragraph in it makes the file unreadable; two tables with nothing between them
/// are merged into one; and a grid whose columns do not add up to the table's stated width is
/// re-fitted in a way that matches neither. None of the three is visible in the XML unless
/// somebody is looking for it.
/// </summary>
public partial class TableTests
{
    [Fact]
    public async Task A_pipe_table_becomes_a_table()
    {
        using var exported = await ExportedDocument.FromAsync(
            "| Name | Count |\n| --- | --- |\n| Apples | 3 |\n| Pears | 12 |\n");

        string xml = exported.DocumentXml();

        xml.ShouldContain("<w:tbl>");
        xml.ShouldContain("MarqoraTable");
        xml.ShouldContain("Apples");
        xml.ShouldContain("12");
    }

    [Fact]
    public async Task The_header_row_repeats_across_a_page_break()
    {
        using var exported = await ExportedDocument.FromAsync(
            "| Name | Count |\n| --- | --- |\n| Apples | 3 |\n");

        string xml = exported.DocumentXml();

        xml.ShouldContain("tblHeader");
        xml.ShouldContain("cantSplit");
    }

    [Fact]
    public async Task Column_alignment_reaches_every_cell_in_the_column()
    {
        using var exported = await ExportedDocument.FromAsync(
            "| Left | Middle | Right |\n| :--- | :---: | ---: |\n| a | b | c |\n");

        string xml = exported.DocumentXml();

        xml.ShouldContain("w:val=\"center\"");
        xml.ShouldContain("w:val=\"right\"");
    }

    /// <summary>
    /// Word merges two tables that touch. Without a paragraph between them a document with
    /// two tables becomes a document with one, and the join is not obvious to look at.
    /// </summary>
    [Fact]
    public async Task Two_tables_in_a_row_stay_two_tables()
    {
        using var exported = await ExportedDocument.FromAsync(
            "| a |\n| --- |\n| 1 |\n\n| b |\n| --- |\n| 2 |\n");

        string xml = exported.DocumentXml();

        CountOf(xml, "<w:tbl>").ShouldBe(2);
        xml.ShouldNotContain("</w:tbl><w:tbl>");
    }

    /// <summary>
    /// The table fills the measure and Word shares it between the columns.
    ///
    /// Two questions, answered in two places. The width is stated, because the preview states
    /// it too - a table spans the text column there, so one that came out a third of the page
    /// wide in Word looked wrong beside the same table in a PDF. The proportions are Word's,
    /// because markdown says nothing about them and measuring the content is exactly the work
    /// the browser does for the preview.
    /// </summary>
    [Fact]
    public async Task A_table_fills_the_measure_and_Word_shares_it_between_the_columns()
    {
        using var exported = await ExportedDocument.FromAsync(
            "| Short | A rather longer column of prose | Mid |\n"
            + "| --- | --- | --- |\n| a | b | c |\n");

        string xml = exported.DocumentXml();

        // Five thousand fiftieths of a percent is the whole of it.
        xml.ShouldContain("w:w=\"5000\"");
        xml.ShouldContain("w:type=\"pct\"");

        // The columns are not given widths of their own, so nothing overrides the sharing.
        xml.ShouldContain("w:type=\"auto\"");
        xml.ShouldNotContain("w:type=\"dxa\"");

        // The grid still has to be there - the schema requires it - but it is an estimate
        // Word is free to replace, not a width it must honour.
        GridWidths(xml).Count.ShouldBe(3);
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// A table with almost nothing in it is the case that showed the problem: content-fitting
    /// alone left it a fraction of the page wide next to a PDF that spanned it.
    /// </summary>
    [Fact]
    public async Task Even_a_nearly_empty_table_spans_the_page()
    {
        using var exported = await ExportedDocument.FromAsync("| a | b |\n| - | - |\n| 1 | 2 |\n");

        exported.DocumentXml().ShouldContain("w:w=\"5000\"");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// A pipe table says nothing about how wide its columns should be, so an even split is
    /// the obvious answer - and a poor one. The prose column here should come out wider than
    /// the two short ones.
    /// </summary>
    [Fact]
    public async Task A_pipe_tables_columns_are_weighted_by_what_is_in_them()
    {
        using var exported = await ExportedDocument.FromAsync(
            "| ID | Description of the thing being described at some length | OK |\n"
            + "| --- | --- | --- |\n| 1 | x | y |\n");

        IReadOnlyList<int> columns = GridWidths(exported.DocumentXml());

        columns[1].ShouldBeGreaterThan(columns[0]);
        columns[1].ShouldBeGreaterThan(columns[2]);
    }

    [Fact]
    public async Task A_short_row_is_padded_so_the_grid_still_lines_up()
    {
        using var exported = await ExportedDocument.FromAsync(
            "| a | b | c |\n| --- | --- | --- |\n| 1 |\n");

        exported.ValidationErrors().ShouldBeEmpty();

        // Three cells in each row, including the one the author only gave one cell.
        CountOf(exported.DocumentXml(), "<w:tc>").ShouldBe(6);
    }

    [Fact]
    public async Task A_cell_can_hold_more_than_a_line_of_text()
    {
        using var exported = await ExportedDocument.FromAsync(
            "| Thing | Detail |\n| --- | --- |\n| One | `code` and **bold** |\n");

        string xml = exported.DocumentXml();

        xml.ShouldContain("MarqoraCodeChar");
        xml.ShouldContain("<w:b ");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_grid_table_uses_the_widths_its_author_drew()
    {
        using var exported = await ExportedDocument.FromAsync(
            "+--------------+-----+\n| Wide         | Thin|\n+==============+=====+\n"
            + "| a            | b   |\n+--------------+-----+\n");

        IReadOnlyList<int> columns = GridWidths(exported.DocumentXml());

        columns.Count.ShouldBe(2);
        columns[0].ShouldBeGreaterThan(columns[1]);
        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task Tables_validate()
    {
        using var exported = await ExportedDocument.FromAsync(
            "| a | b |\n| :-: | --: |\n| 1 | 2 |\n\nText.\n\n"
            + "| c |\n| --- |\n| 3 |\n");

        exported.ValidationErrors().ShouldBeEmpty();
    }

    private static IReadOnlyList<int> GridWidths(string xml)
    {
        Match grid = GridPattern().Match(xml);

        return grid.Success
            ? [.. ColumnPattern().Matches(grid.Value)
                .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))]
            : [];
    }

    private static int CountOf(string haystack, string needle)
    {
        int count = 0;
        int at = 0;

        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }

    [GeneratedRegex("<w:tblGrid>.*?</w:tblGrid>", RegexOptions.Singleline)]
    private static partial Regex GridPattern();

    [GeneratedRegex("<w:gridCol w:w=\"(\\d+)\"")]
    private static partial Regex ColumnPattern();
}
