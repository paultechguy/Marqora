// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

/// <summary>
/// Table rules carried onto the elements, for the one destination that will not read them from
/// a style block.
///
/// The stylesheet below is the shape app.css has after the packager has resolved its custom
/// properties and flattened its translucency - the two-line selector for the shared cell rule
/// included, since that spelling is exactly what the lookup has to cope with.
/// </summary>
public sealed class InlineTableStylesTests
{
    private const string Styles = """
        .mq-preview table {
          border-collapse: collapse;
          width: 100%;
          font-size: 0.94em;
        }

        .mq-preview th,
        .mq-preview td {
          padding: 0.5em 0.85em;
          border: 1px solid #e2e2e2;
          text-align: left;
        }

        .mq-preview thead th {
          background: #e4eff1;
          border-bottom: 2px solid #3f8f98;
          font-weight: 620;
        }

        .mq-preview tbody tr:nth-child(even) { background: #fafafa; }
        """;

    private const string Table = """
        <table>
        <thead>
        <tr><th>Name</th><th>Size</th></tr>
        </thead>
        <tbody>
        <tr><td>first</td><td>1</td></tr>
        <tr><td>second</td><td>2</td></tr>
        <tr><td>third</td><td>3</td></tr>
        <tr><td>fourth</td><td>4</td></tr>
        </tbody>
        </table>
        """;

    /// <summary>The whole point: the tint reaches the cell, where Word reads shading from.</summary>
    [Fact]
    public void A_header_cell_carries_the_accent_tint()
    {
        string stamped = InlineTableStyles.Stamp(Table, Styles);

        stamped.ShouldContain("background: #e4eff1");
        stamped.ShouldContain("border-bottom: 2px solid #3f8f98");
    }

    /// <summary>
    /// The shared rule comes first so the head's heavier bottom border is the later declaration
    /// and still wins, which is what keeps the head visibly off the data.
    /// </summary>
    [Fact]
    public void A_header_cell_takes_the_shared_rule_before_its_own()
    {
        string stamped = InlineTableStyles.Stamp("<table><thead><tr><th>N</th></tr></thead></table>", Styles);

        stamped.ShouldContain(
            "<th style=\"padding: 0.5em 0.85em; border: 1px solid #e2e2e2; text-align: left; "
            + "background: #e4eff1; border-bottom: 2px solid #3f8f98; font-weight: 620\">");
    }

    /// <summary>A body cell gets the borders and the padding, and no tint.</summary>
    [Fact]
    public void A_body_cell_takes_only_the_shared_rule()
    {
        string stamped = InlineTableStyles.Stamp(Table, Styles);

        stamped.ShouldContain("<td style=\"padding: 0.5em 0.85em; border: 1px solid #e2e2e2; text-align: left\">");
        stamped.ShouldNotContain("<td style=\"padding: 0.5em 0.85em; border: 1px solid #e2e2e2; text-align: left; background");
    }

    [Fact]
    public void The_table_itself_takes_its_own_rule() =>
        InlineTableStyles.Stamp(Table, Styles)
            .ShouldContain("<table style=\"border-collapse: collapse; width: 100%; font-size: 0.94em\">");

    /// <summary>
    /// nth-child counts from one and Word does not implement it at all, so the stripe has to be
    /// worked out here. The second and fourth body rows are the even ones.
    /// </summary>
    [Fact]
    public void Only_the_even_body_rows_are_striped()
    {
        string[] rows = InlineTableStyles.Stamp(Table, Styles)
            .Split("<tr", StringSplitOptions.RemoveEmptyEntries);

        // The head's row, then four body rows: no, yes, no, yes.
        rows[1].ShouldNotContain("#fafafa");
        rows[2].ShouldNotContain("#fafafa");
        rows[3].ShouldContain("background: #fafafa");
        rows[4].ShouldNotContain("#fafafa");
        rows[5].ShouldContain("background: #fafafa");
    }

    /// <summary>A row header outside the head is a cell, not a heading, and is not tinted.</summary>
    [Fact]
    public void A_header_cell_outside_the_head_is_not_tinted() =>
        InlineTableStyles.Stamp("<table><tbody><tr><th>Row</th></tr></tbody></table>", Styles)
            .ShouldNotContain("#e4eff1");

    /// <summary>
    /// A nested table starts its own count rather than continuing the outer one, which is what
    /// the selector it stands in for would have done.
    /// </summary>
    [Fact]
    public void A_nested_table_restarts_the_stripe_count()
    {
        const string nested = """
            <table><tbody>
            <tr><td><table><tbody><tr><td>inner one</td></tr></tbody></table></td></tr>
            </tbody></table>
            """;

        // The inner table's only row is its first, so nothing in the whole fragment is striped.
        InlineTableStyles.Stamp(nested, Styles).ShouldNotContain("#fafafa");
    }

    /// <summary>
    /// The document may have styled the cell itself. The stamp goes in front, so what was
    /// already there is the later declaration and still applies.
    /// </summary>
    [Fact]
    public void An_existing_style_attribute_wins()
    {
        string stamped = InlineTableStyles.Stamp(
            "<table><tbody><tr><td style=\"text-align: right\">9</td></tr></tbody></table>", Styles);

        stamped.ShouldContain("text-align: left; text-align: right\"");
    }

    /// <summary>
    /// A selector that stops matching leaves the element alone rather than guessing. That puts
    /// a renamed rule back to today's behavior instead of a wrong color.
    /// </summary>
    [Fact]
    public void A_stylesheet_with_no_table_rules_changes_nothing() =>
        InlineTableStyles.Stamp(Table, ".mq-preview pre { background: #f0f0f0; }").ShouldBe(Table);

    /// <summary>
    /// Only rules at the left margin count. The dark theme and the print block are indented
    /// inside an at-rule in app.css, and a fragment is always a light screen document.
    /// </summary>
    [Fact]
    public void An_indented_override_is_not_read()
    {
        const string withDarkBlock = $$"""
            {{Styles}}

            @media (prefers-color-scheme: dark) {
              .mq-preview thead th {
                background: #123456;
              }
            }
            """;

        string stamped = InlineTableStyles.Stamp(Table, withDarkBlock);

        stamped.ShouldContain("#e4eff1");
        stamped.ShouldNotContain("#123456");
    }

    [Fact]
    public void Markup_with_no_table_is_returned_as_it_came()
    {
        const string html = "<p>Nothing tabular about this at all.</p>";

        InlineTableStyles.Stamp(html, Styles).ShouldBe(html);
    }

    [Fact]
    public void Empty_markup_is_returned_as_it_came() =>
        InlineTableStyles.Stamp(string.Empty, Styles).ShouldBe(string.Empty);
}
