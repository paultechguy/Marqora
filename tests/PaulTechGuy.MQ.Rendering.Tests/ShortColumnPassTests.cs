// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Rendering.Tests;

/// <summary>
/// Which table columns the preview keeps from wrapping.
///
/// The complaint this answers is a short header - "Due date" - breaking over two lines because
/// a column of prose beside it took the room, and the <c>&amp;nbsp;</c> people type into the
/// header to stop it. Asserted on the rendered markup, since the class is the whole of the
/// contract with <c>app.css</c>.
/// </summary>
public class ShortColumnPassTests
{
    private static readonly MarkdigMarkdownRenderer Renderer =
        new(NullLogger<MarkdigMarkdownRenderer>.Instance);

    /// <summary>Spelled out rather than read from the pass: it is the contract with app.css.</summary>
    private const string NoWrapClass = "mq-nowrap";

    private const string Prose =
        "A sentence long enough that the browser would rather wrap it than anything else";

    [Fact]
    public void A_short_column_beside_prose_is_kept_on_one_line()
    {
        string html = Render(
            "| Due date | Notes |",
            "| --- | --- |",
            $"| 2026-09-22 | {Prose} |");

        Cells(html).ShouldBe(
        [
            ("th", true), ("th", false),
            ("td", true), ("td", false),
        ]);
    }

    /// <summary>
    /// Nothing is competing for the room, so the browser would not have wrapped anything - and
    /// marking every column would take away the one freedom it has if the pane is narrow.
    /// </summary>
    [Fact]
    public void A_table_of_nothing_but_short_columns_is_left_alone()
    {
        string html = Render(
            "| Name | Size |",
            "| --- | --- |",
            "| first | 1 |");

        html.ShouldNotContain(NoWrapClass);
    }

    /// <summary>
    /// Columns that cannot wrap cannot give up room either, so the shortest go first and the
    /// rest stay free once the budget is spent. Four columns of twenty characters is eighty,
    /// past the sixty a table may hold unwrapped, so the fourth is left to wrap.
    /// </summary>
    [Fact]
    public void Past_the_budget_the_remaining_short_columns_may_still_wrap()
    {
        string twenty = new('x', 20);
        string nineteen = new('y', 19);

        string html = Render(
            "| a | b | c | d | e |",
            "| --- | --- | --- | --- | --- |",
            $"| {nineteen} | {twenty} | {twenty} | {twenty} | {Prose} |");

        Cells(html).Where(cell => cell.Tag == "td").Select(cell => cell.NoWrap).ShouldBe(
            [true, true, true, false, false]);
    }

    /// <summary>
    /// The measure is what a reader sees. A link counts its label, not its address, and an
    /// entity is the one character it stands for rather than the six it is written with.
    /// </summary>
    [Fact]
    public void A_cell_is_measured_by_what_it_shows_rather_than_how_it_is_written()
    {
        string html = Render(
            "| Owner | Notes |",
            "| --- | --- |",
            $"| [Paul](https://example.com/a/very/long/address/indeed) | {Prose} |",
            $"| Q&amp;A&nbsp;team | {Prose} |");

        Cells(html).Where(cell => cell.Tag == "td").Select(cell => cell.NoWrap).ShouldBe(
            [true, false, true, false]);
    }

    [Fact]
    public void A_column_with_one_long_cell_is_not_short()
    {
        string html = Render(
            "| Status | Notes |",
            "| --- | --- |",
            $"| Done | {Prose} |",
            $"| Waiting on the vendor's reply | {Prose} |");

        html.ShouldNotContain(NoWrapClass);
    }

    private static string Render(params string[] lines) =>
        Renderer.Render(string.Join('\n', lines) + '\n').Html;

    /// <summary>Every cell in document order: its tag, and whether it carries the class.</summary>
    private static (string Tag, bool NoWrap)[] Cells(string html) =>
        [.. Regex.Matches(html, @"<(?<tag>th|td)\b(?<attributes>[^>]*)>")
            .Select(match => (
                match.Groups["tag"].Value,
                match.Groups["attributes"].Value.Contains(NoWrapClass, StringComparison.Ordinal)))];
}
