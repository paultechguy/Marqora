// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PaulTechGuy.MQ.Themes;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Docx.Tests;

/// <summary>
/// A Word export in the color theme the preview is showing, in its light palette.
///
/// Forest is the theme under test because it differs from Default almost everywhere, so a
/// color that reaches the file from the wrong place - a constant left behind, a scheme
/// reference that outranks the literal beside it - shows up as the wrong value rather than
/// as the right one by coincidence.
/// </summary>
public sealed class ColorThemeTests
{
    private static readonly ColorTheme Forest = ThemeCatalog.Load().Find("forest");

    private static string Rgb(string slot) => Forest.Light[slot].TrimStart('#').ToUpperInvariant();

    private static Task<ExportedDocument> ExportAsync(string markdown) =>
        ExportedDocument.FromAsync(markdown, colorTheme: Forest);

    private static Style StyleOf(ExportedDocument exported, string styleId)
    {
        using WordprocessingDocument file = WordprocessingDocument.Open(exported.Path, false);

        Style style = file.MainDocumentPart!.StyleDefinitionsPart!.Styles!
            .Elements<Style>()
            .Single(s => s.StyleId == styleId);

        return (Style)style.CloneNode(true);
    }

    [Fact]
    public void The_theme_under_test_is_not_Default() =>
        Forest.Id.ShouldNotBe(ThemeCatalog.DefaultId);

    /// <summary>
    /// Each heading in its own color, written as a plain value. A theme reference would win
    /// over the literal - themeColor outranks val - and put accent 1 back over the theme.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    public async Task A_heading_wears_the_theme_color_for_its_level(int level)
    {
        using ExportedDocument exported = await ExportAsync("# One\n\n### Three\n\n###### Six\n");

        Color color = StyleOf(exported, $"Heading{level}").StyleRunProperties!.Color!;

        color.Val!.Value.ShouldBe(Rgb($"heading-{level}"));
        color.ThemeColor.ShouldBeNull();
        color.ThemeShade.ShouldBeNull();
        color.ThemeTint.ShouldBeNull();
        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_level_two_heading_has_the_theme_rule_under_it()
    {
        using ExportedDocument exported = await ExportAsync("## Section\n");

        StyleOf(exported, "Heading2").StyleParagraphProperties!.ParagraphBorders!.BottomBorder!.Color!.Value
            .ShouldBe(Rgb("heading-rule"));
        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_callout_wears_its_bar_fill_and_title_colors()
    {
        using ExportedDocument exported = await ExportAsync("> [!WARNING]\n> Mind the step.\n");

        Style panel = StyleOf(exported, "MarqoraCalloutWarning");

        panel.StyleParagraphProperties!.ParagraphBorders!.LeftBorder!.Color!.Value.ShouldBe(Rgb("callout-warning-bar"));
        panel.StyleParagraphProperties.Shading!.Fill!.Value.ShouldBe(Rgb("callout-warning-fill"));

        StyleOf(exported, "MarqoraCalloutWarningTitle").StyleRunProperties!.Color!.Val!.Value
            .ShouldBe(Rgb("callout-warning-title"));

        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>The header fill, ink and rule, and the stripe as the second band - the preview's table.</summary>
    [Fact]
    public async Task A_table_wears_the_theme_header_and_stripe()
    {
        using ExportedDocument exported = await ExportAsync("| A | B |\n|---|---|\n| 1 | 2 |\n| 3 | 4 |\n");

        Style table = StyleOf(exported, "MarqoraTable");

        TableStyleProperties header = table.Elements<TableStyleProperties>()
            .Single(p => p.Type! == TableStyleOverrideValues.FirstRow);
        TableStyleConditionalFormattingTableCellProperties cell =
            header.GetFirstChild<TableStyleConditionalFormattingTableCellProperties>()!;

        cell.GetFirstChild<Shading>()!.Fill!.Value.ShouldBe(Rgb("table-header-fill"));
        cell.GetFirstChild<TableCellBorders>()!.BottomBorder!.Color!.Value.ShouldBe(Rgb("table-header-rule"));
        // Read back as the SDK's base run properties type, not the one it was written as, so the
        // color is found by element rather than by its parent's type.
        header.Descendants<Color>().Single().Val!.Value.ShouldBe(Rgb("table-header-text"));

        table.Elements<TableStyleProperties>()
            .Single(p => p.Type! == TableStyleOverrideValues.Band2Horizontal)
            .GetFirstChild<TableStyleConditionalFormattingTableCellProperties>()!
            .GetFirstChild<Shading>()!.Fill!.Value.ShouldBe(Rgb("table-stripe"));

        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_quote_wears_the_theme_bar_ink_and_fill()
    {
        using ExportedDocument exported = await ExportAsync("> Quoted.\n");

        Style quote = StyleOf(exported, "Quote");

        quote.StyleParagraphProperties!.ParagraphBorders!.LeftBorder!.Color!.Value.ShouldBe(Rgb("quote-bar"));
        quote.StyleParagraphProperties.Shading!.Fill!.Value.ShouldBe(Rgb("quote-fill"));
        quote.StyleRunProperties!.Color!.Val!.Value.ShouldBe(Rgb("quote-text"));
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>Default's quote fill is the page, and painting the page white is no fill at all.</summary>
    [Fact]
    public async Task A_quote_fill_that_is_the_page_is_left_out()
    {
        using ExportedDocument exported = await ExportedDocument.FromAsync("> Quoted.\n");

        StyleOf(exported, "Quote").StyleParagraphProperties!.Shading.ShouldBeNull();
    }

    /// <summary>
    /// Bold wears the theme's bold color in running text - and keeps the color of what holds it
    /// inside a quote, and a link's color inside a link, as it does in the preview.
    /// </summary>
    [Fact]
    public async Task Bold_takes_the_theme_color_only_where_nothing_else_colors_it()
    {
        using ExportedDocument exported = await ExportAsync(
            "Plain **body** text.\n\n> Quoted **bold**.\n\nA **[bold link](https://example.com/)** here.\n");

        using WordprocessingDocument file = WordprocessingDocument.Open(exported.Path, false);
        Body body = file.MainDocumentPart!.Document!.Body!;
        string strong = Rgb("strong");

        Run RunWith(string text) => body.Descendants<Run>().Single(r => r.InnerText == text);

        RunWith("body").RunProperties!.Color!.Val!.Value.ShouldBe(strong);
        RunWith("bold").RunProperties!.Color.ShouldBeNull();

        Run link = RunWith("bold link");

        link.RunProperties!.RunStyle!.Val!.Value.ShouldBe("Hyperlink");
        link.RunProperties.Color.ShouldBeNull();
    }

    [Fact]
    public async Task List_markers_wear_the_theme_marker_color()
    {
        using ExportedDocument exported = await ExportAsync("- one\n- two\n\n1. first\n2. second\n");

        exported.NumberingXml().ShouldContain($"w:val=\"{Rgb("list-marker")}\"");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>What Word's own galleries start from: the link as accent 1 and as the hyperlink.</summary>
    [Fact]
    public async Task The_document_theme_takes_the_theme_link_color()
    {
        using ExportedDocument exported = await ExportAsync("Text.\n");

        // Whitespace out: the part is saved indented, with a line break between each element.
        string theme = string.Concat(exported.ThemeXml().Where(c => !char.IsWhiteSpace(c)));

        theme.ShouldContain($"<a:accent1><a:srgbClrval=\"{Rgb("link")}\"");
        theme.ShouldContain($"<a:hlink><a:srgbClrval=\"{Rgb("link")}\"");
        theme.ShouldContain($"<a:accent6><a:srgbClrval=\"{Rgb("callout-caution-bar")}\"");
    }

    [Fact]
    public async Task Code_wears_the_theme_code_colors()
    {
        using ExportedDocument exported = await ExportAsync("Some `inline` code.\n\n```\nblock\n```\n");

        StyleOf(exported, "MarqoraCodeChar").StyleRunProperties!.Color!.Val!.Value.ShouldBe(Rgb("code-inline-text"));
        StyleOf(exported, "MarqoraCode").StyleParagraphProperties!.Shading!.Fill!.Value.ShouldBe(Rgb("code-block-fill"));
        exported.ValidationErrors().ShouldBeEmpty();
    }
}
