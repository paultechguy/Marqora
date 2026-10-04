// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using System.Text;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Themes.Tests;

/// <summary>
/// Every theme that ships: complete, readable and legible, in both modes. A new theme file is
/// picked up by these without being listed anywhere - which is the point of them.
/// </summary>
public sealed class ShippedThemeTests
{
    public static TheoryData<string> ThemeIds()
    {
        TheoryData<string> ids = [];

        foreach (ThemeSource source in ThemeCatalog.EmbeddedSources())
        {
            ids.Add(source.Id);
        }

        return ids;
    }

    public static TheoryData<string, PaletteMode> ThemesAndModes()
    {
        TheoryData<string, PaletteMode> cases = [];

        foreach (ThemeSource source in ThemeCatalog.EmbeddedSources())
        {
            cases.Add(source.Id, PaletteMode.Light);
            cases.Add(source.Id, PaletteMode.Dark);
        }

        return cases;
    }

    /// <summary>Every slot named once per mode, every value six hex digits, and nothing extra.</summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void A_shipped_theme_reads_without_a_single_problem(string id)
    {
        ThemeSource source = ThemeCatalog.EmbeddedSources().Single(s => s.Id == id);

        ThemeReadResult result = ThemeReader.Read(source.Json, source.Id);

        result.Theme.ShouldNotBeNull();
        result.Problems.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void A_shipped_theme_has_a_name_and_a_description(string id)
    {
        ColorTheme theme = ThemeCatalog.Load().Find(id);

        theme.Id.ShouldBe(id);
        theme.Name.ShouldNotBeNullOrWhiteSpace();
        theme.Description.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Each text slot against what it is read on, at the bar its slot names. Every failure in a
    /// palette is reported at once, with the ratio it reached, so a theme can be fixed in one
    /// pass rather than one slot per run.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemesAndModes))]
    public void Every_text_slot_stands_out_from_what_it_is_read_on(string id, PaletteMode mode)
    {
        ThemePalette palette = ThemeCatalog.Load().Find(id).PaletteFor(mode);
        (string page, string bodyText) = Repository.Neutrals(mode);
        List<string> failures = [];

        foreach (ThemeSlot slot in ThemeSlots.All)
        {
            if (slot.Contrast is not { } rule || !palette.TryGet(slot.Id, out string foreground))
            {
                continue;
            }

            string background = rule.Against switch
            {
                ThemeSlots.Page => page,
                ThemeSlots.BodyText => bodyText,
                _ => palette[rule.Against],
            };

            // mark-fill is the background, and the body text is what is read on it.
            (string text, string under) = rule.Against == ThemeSlots.BodyText
                ? (background, foreground)
                : (foreground, background);

            double ratio = Contrast.Ratio(text, under);

            if (ratio < rule.Minimum)
            {
                failures.Add($"{slot.Id} {text} on {rule.Against} {under}: {ratio:0.00}:1, needs {rule.Minimum:0.0}:1");
            }
        }

        failures.ShouldBeEmpty($"{id} ({mode})");
    }

    [Fact]
    public void The_catalog_reads_the_shipped_themes_without_a_problem() =>
        ThemeCatalog.Load().Problems.ShouldBeEmpty();

    [Fact]
    public void Default_comes_first_and_the_rest_follow_by_name()
    {
        IReadOnlyList<ColorTheme> themes = ThemeCatalog.Load().Themes;

        themes[0].Id.ShouldBe(ThemeCatalog.DefaultId);
        themes.Skip(1).Select(t => t.Name)
            .ShouldBe(themes.Skip(1).Select(t => t.Name).Order(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Default keeps mermaid's own themes. Its diagrams are today's diagrams; mermaid's stock
    /// palettes compute too much to be restated through the base theme's few variables.
    /// </summary>
    [Fact]
    public void Default_keeps_stock_diagrams() =>
        ThemeCatalog.Load().Default.UsesStockDiagrams.ShouldBeTrue();

    /// <summary>
    /// Default, frozen. It is today's preview flattened to opaque hex, with the nine colors that
    /// fell short of AA darkened on purpose, and a stray edit to it changes every unthemed
    /// document Marqora shows. Changing it deliberately means updating the hash below - the
    /// failure message carries the new one.
    /// </summary>
    [Fact]
    public void Default_has_not_changed_by_accident()
    {
        ColorTheme theme = ThemeCatalog.Load().Default;
        StringBuilder text = new();

        foreach (PaletteMode mode in new[] { PaletteMode.Light, PaletteMode.Dark })
        {
            foreach (ThemeSlot slot in ThemeSlots.All)
            {
                if (theme.PaletteFor(mode).TryGet(slot.Id, out string hex))
                {
                    text.Append(mode).Append('.').Append(slot.Id).Append('=').Append(hex).Append('\n');
                }
            }
        }

        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));

        hash.ShouldBe(
            "254c06b90e0c3f2a9376f993d3df1f423c6f00092cab0d3cc6b4d576c4277a2c",
            customMessage: "Default's colors changed. If that was meant, put the new hash in this test.");
    }
}
