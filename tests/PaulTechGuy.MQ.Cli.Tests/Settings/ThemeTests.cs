// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using PaulTechGuy.MQ.Cli.Settings;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Themes;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Settings;

/// <summary>CLI-TH-01, TH-02, TH-03 and TH-05: naming a theme, which one wins, an unknown one, and the list.</summary>
public sealed class ThemeTests
{
    private static readonly ThemeCatalog Catalog = ThemeCatalog.Load();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SettingsSource With(string? screen = null, string? export = null) =>
        new(new AppSettings { ColorTheme = screen, ExportColorTheme = export }, "settings file test.json");

    [Theory]
    [InlineData("vintage-press")]
    [InlineData("Vintage Press")]
    [InlineData("VINTAGE-PRESS")]
    public void A_theme_is_named_by_id_or_gallery_name_ignoring_case(string named)
    {
        ThemeResolver.Resolve(named, SettingsSource.BuiltIn, Catalog).Theme.Id.ShouldBe("vintage-press");
    }

    [Fact]
    public async Task A_text_verb_does_not_take_a_theme()
    {
        CliResult result = await CliRunner.RunAsync(["format", "--theme", "ocean", "a.md"], cancellationToken: Ct);

        result.ExitCode.ShouldBe(2);
    }

    [Fact]
    public void The_option_wins_over_everything()
    {
        ResolvedTheme resolved = ThemeResolver.Resolve("ocean", With(screen: "forest", export: "ember"), Catalog);

        resolved.Theme.Id.ShouldBe("ocean");
        resolved.Source.ShouldBe(ThemeSourceKind.Option);
    }

    [Fact]
    public void Then_the_settings_export_theme()
    {
        ResolvedTheme resolved = ThemeResolver.Resolve(null, With(screen: "forest", export: "ember"), Catalog);

        resolved.Theme.Id.ShouldBe("ember");
        resolved.Source.ShouldBe(ThemeSourceKind.SettingsExport);
    }

    [Fact]
    public void Then_the_settings_screen_theme()
    {
        ResolvedTheme resolved = ThemeResolver.Resolve(null, With(screen: "forest"), Catalog);

        resolved.Theme.Id.ShouldBe("forest");
        resolved.Source.ShouldBe(ThemeSourceKind.SettingsScreen);
    }

    [Fact]
    public void Then_default()
    {
        ResolvedTheme resolved = ThemeResolver.Resolve(null, SettingsSource.BuiltIn, Catalog);

        resolved.Theme.Id.ShouldBe(ThemeCatalog.DefaultId);
        resolved.Source.ShouldBe(ThemeSourceKind.Default);
    }

    [Fact]
    public void An_unknown_theme_suggests_the_nearest_and_lists_every_id()
    {
        UsageException error = Should.Throw<UsageException>(() => ThemeResolver.Resolve("Oceanic", SettingsSource.BuiltIn, Catalog));

        error.Message.ShouldContain("'Oceanic'");
        error.Message.ShouldContain("Did you mean 'ocean'?");

        foreach (ColorTheme theme in Catalog.Themes)
        {
            error.Message.ShouldContain(theme.Id);
        }
    }

    /// <summary>A settings file naming a theme that is gone fails too, rather than falling back as the app does.</summary>
    [Fact]
    public void An_unknown_theme_in_the_settings_fails_too()
    {
        UsageException error = Should.Throw<UsageException>(() => ThemeResolver.Resolve(null, With(export: "neon"), Catalog));

        error.Message.ShouldContain("test.json");
        error.Message.ShouldContain("'neon'");
    }

    [Fact]
    public async Task Themes_list_is_the_catalog_in_gallery_order()
    {
        CliResult result = await CliRunner.RunAsync(["themes", "list"], cancellationToken: Ct);

        string[] ids = [.. result.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split(' ')[0])];

        ids.ShouldBe(Catalog.Themes.Select(theme => theme.Id));
        ids[0].ShouldBe(ThemeCatalog.DefaultId);
    }

    [Fact]
    public async Task Themes_list_json_is_one_array_of_the_catalog()
    {
        CliResult result = await CliRunner.RunAsync(["themes", "list", "--json"], cancellationToken: Ct);

        using JsonDocument parsed = JsonDocument.Parse(result.Out);

        parsed.RootElement.EnumerateArray().Select(theme => theme.GetProperty("id").GetString())
            .ShouldBe(Catalog.Themes.Select(theme => theme.Id));
    }

    /// <summary>A theme file the app gains is a row in the list, with nothing changed in the CLI.</summary>
    [Fact]
    public async Task An_added_theme_file_is_an_added_row()
    {
        string json = ThemeCatalog.EmbeddedSources().Single(source => source.Id == "ocean").Json
            .Replace("\"Ocean\"", "\"Test Lagoon\"", StringComparison.Ordinal);

        ThemeCatalog extended = ThemeCatalog.FromSources([.. ThemeCatalog.EmbeddedSources(), new ThemeSource("test-lagoon", json)]);

        var output = new StringWriter();
        var environment = TestEnvironment.Discarding() with { Out = output };
        environment = environment with { Themes = () => extended };

        int code = await CliApp.RunAsync(["themes", "list"], environment, Ct);

        code.ShouldBe(0);
        output.ToString().ShouldContain("test-lagoon");
    }
}
