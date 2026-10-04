// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Themes.Tests;

/// <summary>
/// Reading a theme file, and the catalog's forgiveness for one that is not quite right. Each
/// test starts from a copy of Default and spoils one thing.
/// </summary>
public sealed class ThemeReaderTests
{
    private static string DefaultJson() =>
        ThemeCatalog.EmbeddedSources().Single(s => s.Id == ThemeCatalog.DefaultId).Json;

    /// <summary>Default, re-identified as a theme of its own with its own diagram colors, then edited.</summary>
    private static string Variant(string id, Action<JsonObject>? edit = null)
    {
        JsonObject theme = JsonNode.Parse(DefaultJson())!.AsObject();

        theme["id"] = id;
        theme["name"] = id;
        theme.Remove("diagrams");

        foreach (string mode in new[] { "light", "dark" })
        {
            JsonObject palette = theme[mode]!.AsObject();

            foreach (ThemeSlot slot in ThemeSlots.All.Where(s => s.Group == ThemeSlots.DiagramsGroup))
            {
                palette[slot.Id] = "#123456";
            }
        }

        edit?.Invoke(theme);

        return theme.ToJsonString();
    }

    private static ThemeReadResult Read(string json, string id = "probe") => ThemeReader.Read(json, id);

    [Fact]
    public void A_complete_theme_reads_cleanly() =>
        Read(Variant("probe")).Problems.ShouldBeEmpty();

    [Fact]
    public void Capitals_are_accepted_and_kept_lowercase()
    {
        ThemeReadResult result = Read(Variant("probe", t => t["light"]!["link"] = "#2F7680"));

        result.Problems.ShouldBeEmpty();
        result.Theme!.Light["link"].ShouldBe("#2f7680");
    }

    [Fact]
    public void A_missing_slot_is_reported_and_the_theme_is_still_built()
    {
        ThemeReadResult result = Read(Variant("probe", t => t["dark"]!.AsObject().Remove("rule")));

        result.Problems.ShouldHaveSingleItem().ShouldContain("probe.dark: \"rule\" is missing");
        result.Theme.ShouldNotBeNull();
    }

    [Fact]
    public void A_slot_nobody_has_heard_of_is_reported() =>
        Read(Variant("probe", t => t["light"]!["heading-7"] = "#000000"))
            .Problems.ShouldHaveSingleItem().ShouldContain("\"heading-7\" is not a slot");

    [Theory]
    [InlineData("#abc")]
    [InlineData("rgba(0, 0, 0, 0.5)")]
    [InlineData("#12345g")]
    [InlineData("teal")]
    public void A_color_that_is_not_six_hex_digits_is_reported(string value) =>
        Read(Variant("probe", t => t["light"]!["link"] = value))
            .Problems.ShouldContain(p => p.Contains("which is not #rrggbb", StringComparison.Ordinal));

    [Fact]
    public void An_id_that_disagrees_with_the_file_name_is_reported() =>
        Read(Variant("other"), id: "probe")
            .Problems.ShouldHaveSingleItem().ShouldContain("must match the file name");

    [Fact]
    public void A_file_that_is_not_json_is_no_theme()
    {
        ThemeReadResult result = Read("{ \"id\": ");

        result.Theme.ShouldBeNull();
        result.Problems.ShouldHaveSingleItem().ShouldContain("not valid JSON");
    }

    [Fact]
    public void A_file_without_a_name_is_no_theme() =>
        Read(Variant("probe", t => t.Remove("name"))).Theme.ShouldBeNull();

    [Fact]
    public void A_stock_diagram_theme_that_also_names_diagram_colors_is_reported() =>
        Read(Variant("probe", t => t["diagrams"] = "stock"))
            .Problems.ShouldContain(p => p.Contains("keeps stock diagrams", StringComparison.Ordinal));

    [Fact]
    public void Diagrams_may_only_be_stock() =>
        Read(Variant("probe", t => t["diagrams"] = "themed"))
            .Problems.ShouldContain(p => p.Contains("may only be", StringComparison.Ordinal));

    // ------------------------------------------------------------------ catalog

    private static ThemeCatalog Catalog(params ThemeSource[] extra) =>
        ThemeCatalog.FromSources([new ThemeSource(ThemeCatalog.DefaultId, DefaultJson()), .. extra]);

    [Fact]
    public void An_unknown_or_absent_id_finds_Default()
    {
        ThemeCatalog catalog = Catalog();

        catalog.Find("no-such-theme").Id.ShouldBe(ThemeCatalog.DefaultId);
        catalog.Find(null).Id.ShouldBe(ThemeCatalog.DefaultId);
        catalog.Contains("no-such-theme").ShouldBeFalse();
    }

    [Fact]
    public void A_missing_slot_takes_Default_color_and_is_logged()
    {
        ThemeCatalog catalog = Catalog(new ThemeSource("probe", Variant("probe", t => t["light"]!.AsObject().Remove("link"))));

        catalog.Find("probe").Light["link"].ShouldBe(catalog.Default.Light["link"]);
        catalog.Problems.ShouldHaveSingleItem().ShouldContain("\"link\" is missing");
    }

    /// <summary>Default has no diagram colors to lend, so the theme keeps mermaid's own instead.</summary>
    [Fact]
    public void A_theme_missing_a_diagram_color_keeps_stock_diagrams()
    {
        ThemeCatalog catalog = Catalog(new ThemeSource("probe", Variant("probe", t => t["dark"]!.AsObject().Remove("diagram-line"))));

        ColorTheme theme = catalog.Find("probe");

        theme.UsesStockDiagrams.ShouldBeTrue();
        theme.Light.TryGet("diagram-primary", out _).ShouldBeFalse();
    }

    [Fact]
    public void A_themed_theme_keeps_its_diagram_colors()
    {
        ColorTheme theme = Catalog(new ThemeSource("probe", Variant("probe"))).Find("probe");

        theme.UsesStockDiagrams.ShouldBeFalse();
        theme.Dark["diagram-primary"].ShouldBe("#123456");
    }

    [Fact]
    public void An_unreadable_theme_is_left_out_and_logged()
    {
        ThemeCatalog catalog = Catalog(new ThemeSource("broken", "not json"));

        catalog.Themes.ShouldHaveSingleItem();
        catalog.Problems.ShouldHaveSingleItem().ShouldStartWith("broken:");
    }

    [Fact]
    public void A_second_theme_with_the_same_id_is_ignored()
    {
        ThemeCatalog catalog = Catalog(
            new ThemeSource("probe", Variant("probe")),
            new ThemeSource("probe", Variant("probe", t => t["name"] = "Impostor")));

        catalog.Find("probe").Name.ShouldBe("probe");
        catalog.Problems.ShouldHaveSingleItem().ShouldContain("second theme");
    }

    [Fact]
    public void The_rest_are_ordered_by_name_after_Default()
    {
        ThemeCatalog catalog = Catalog(
            new ThemeSource("zebra", Variant("zebra")),
            new ThemeSource("apple", Variant("apple")));

        catalog.Themes.Select(t => t.Id).ShouldBe([ThemeCatalog.DefaultId, "apple", "zebra"]);
    }

    [Fact]
    public void Without_Default_there_is_no_catalog() =>
        Should.Throw<InvalidOperationException>(() =>
            ThemeCatalog.FromSources([new ThemeSource("probe", Variant("probe"))]));
}
