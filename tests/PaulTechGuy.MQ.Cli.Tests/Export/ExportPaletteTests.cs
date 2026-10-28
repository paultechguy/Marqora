// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using PaulTechGuy.MQ.Cli.Rendering;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using PaulTechGuy.MQ.Themes;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Export;

/// <summary>
/// CLI-TH-04 and CLI-EX-02: what leaves is light, in one theme, and is all that is left. Every
/// theme's HTML export carries its light palette and nothing of its dark one; a Folio's documents
/// share one palette; and an export run adds its outputs to the folder and nothing else.
/// </summary>
[Collection(nameof(BrowserRunsAlone))]
public sealed partial class ExportPaletteTests
{
    /// <summary>Something of every kind a theme colors: headings, a link, a quote, a table, a callout, code, a diagram.</summary>
    private const string Document = "# Palette\n\n## Second\n\nSee [the guide](https://example.com/), **strong** and *emphasis*.\n\n> A quote.\n\n| A | B |\n| - | - |\n| 1 | 2 |\n\n> [!WARNING]\n> A callout.\n\n```csharp\nreturn \"text\"; // comment\n```\n\n```mermaid\nflowchart LR\n  A --> B\n```\n";

    private static readonly ThemeCatalog Catalog = ThemeCatalog.Load();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> ThemeIds => [.. Catalog.Themes.Select(t => t.Id)];

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public async Task An_html_export_carries_its_themes_light_palette_and_nothing_of_its_dark_one(string themeId)
    {
        using var workspace = new Workspace();
        workspace.Text("doc.md", Document);

        CliResult result = await CliRunner.RunAsync(["export", "html", "--theme", themeId, "doc.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());

        ColorTheme theme = Catalog.Find(themeId);
        string page = workspace.Read("doc.html");

        page.ShouldContain("<html lang=\"en\" data-theme=\"light\">");

        string styles = string.Join('\n', StyleBlock().Matches(page).Select(m => m.Groups[1].Value)).ToLowerInvariant();

        // app.css's rules for the dark screen are left behind, all three shapes of them; the
        // print block they shared a selector list with still applies to the light root.
        styles.ShouldNotContain(":root[data-theme=\"dark\"]");
        styles.ShouldNotContain("var(--mq-dark-fill)");
        Regex.IsMatch(styles, @"@media print\s*\{\s*:root\s*\{\s*--mq-bg: #ffffff;").ShouldBeTrue("the print block, on the light root alone");

        foreach ((string slot, string hex) in theme.Light.Colors)
        {
            styles.ShouldContain($"--mq-theme-{slot}: {hex.ToLowerInvariant()};", Case.Sensitive);
        }

        // The shell's stylesheets declare no theme color (CLAUDE.md, Color themes), so a dark value
        // found in the page that they do not hold can only have come from the theme's dark
        // palette. One they do hold is theirs: app.css's light grays share some themes' dark
        // values (Default's dark heading-6 is app.css's light tertiary text, #8a8a8a).
        HashSet<string> own = [.. ShellColors(), .. theme.Light.Colors.Values.Select(v => v.ToLowerInvariant())];
        string[] leaked = [.. theme.Dark.Colors
            .Where(c => !own.Contains(c.Value.ToLowerInvariant()) && styles.Contains(c.Value.ToLowerInvariant(), StringComparison.Ordinal))
            .Select(c => $"{c.Key} {c.Value}")];

        leaked.ShouldBeEmpty($"{themeId}'s dark palette in its HTML export");
    }

    [Fact]
    public async Task A_folios_documents_share_the_one_theme_of_the_run()
    {
        using var workspace = new Workspace();
        workspace.Text("a.md", Document);
        workspace.Text("b.md", "# Second document\n\n> [!NOTE]\n> Another callout.\n");

        CliResult result = await CliRunner.RunAsync(["folio", "--theme", "ember", "-o", "Set.html", "a.md", "b.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());

        string page = workspace.Read("Set.html");
        ColorTheme ember = Catalog.Find("ember");

        // One palette for the page, so no document can wear another: each slot is declared once,
        // with Ember's light value.
        foreach ((string slot, string hex) in ember.Light.Colors)
        {
            Regex.Matches(page, $"--mq-theme-{Regex.Escape(slot)}: ([^;]+);").Select(m => m.Groups[1].Value.ToLowerInvariant())
                .ShouldBe([hex.ToLowerInvariant()], $"the {slot} slot");
        }
    }

    [Theory]
    [InlineData("html", ".html")]
    [InlineData("pdf", ".pdf")]
    [InlineData("word", ".docx")]
    public async Task An_export_adds_its_output_and_nothing_else(string format, string extension)
    {
        using var workspace = new Workspace();
        workspace.Text("docs/doc.md", Document);
        workspace.Bytes("docs/images/logo.png", FolioSet.Png);

        string[] before = Files(workspace.Root);
        RendererFactory? made = null;

        CliResult result = await CliRunner.RunAsync(
            ["export", format, "docs/doc.md"],
            currentDirectory: workspace.Root,
            renderers: setup => made = new RendererFactory(setup, RendererFactory.InstalledRuntime),
            cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());
        Files(workspace.Root).Except(before).ShouldBe([Path.Combine("docs", "doc" + extension)]);
        Directory.Exists(made!.DataFolder).ShouldBeFalse("the run's data folder was left behind");
    }

    [Fact]
    public async Task A_single_page_folio_adds_its_page_and_nothing_else()
    {
        using var workspace = new Workspace();
        FolioSet.WriteTo(workspace);

        string[] before = Files(workspace.Root);

        CliResult result = await CliRunner.RunAsync(["folio", "-o", "Handbook.html", "docs/guide.md", "docs/setup.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());
        Files(workspace.Root).Except(before).ShouldBe(["Handbook.html"]);
    }

    private static string[] Files(string root) =>
        [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(root, f)).Order(StringComparer.Ordinal)];

    [GeneratedRegex("<style[^>]*>(.*?)</style>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex StyleBlock();

    /// <summary>Every hex color the shell's own stylesheets write, in lower case.</summary>
    private static IEnumerable<string> ShellColors() =>
        Directory.EnumerateFiles(Path.Combine(Repo.Root, "webshell"), "*.css")
            .SelectMany(file => HexColor().Matches(File.ReadAllText(file)).Select(m => m.Value.ToLowerInvariant()));

    [GeneratedRegex("#[0-9a-fA-F]{6}\\b")]
    private static partial Regex HexColor();
}
