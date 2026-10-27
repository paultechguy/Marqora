// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Verbs;

/// <summary>
/// CLI-FO-05 and CLI-TH-06: folio --dry-run prints each line of the app's preflight, in text and in
/// the JSON, names the theme and where it came from, and writes nothing.
/// </summary>
public sealed class FolioDryRunTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_preflight_line_is_printed_and_nothing_is_written()
    {
        using var workspace = new Workspace();
        FolioSet.WriteTo(workspace);

        CliResult result = await CliRunner.RunAsync(["folio", "--dry-run", "-o", "Set.zip", "docs/guide.md", "docs/setup.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());

        result.Out.ShouldContain("Folio:      Set.zip, a zip");
        result.Out.ShouldContain("Theme:      Default, from the default");
        result.Out.ShouldContain("Documents:  2");
        result.Out.ShouldContain("  guide.md");
        result.Out.ShouldContain("  setup.md");
        result.Out.ShouldContain("Pictures:   1");
        result.Out.ShouldContain("Missing:    2 pictures not on this machine");
        result.Out.ShouldContain("Outside:    1 link pointing outside the Folio");
        result.Out.ShouldContain("Web:        1 picture left on the web");
        result.Out.ShouldContain("Estimate:   about ");

        File.Exists(workspace.At("Set.zip")).ShouldBeFalse();
    }

    [Fact]
    public async Task Each_preflight_line_is_in_the_json()
    {
        using var workspace = new Workspace();
        FolioSet.WriteTo(workspace);

        CliResult result = await CliRunner.RunAsync(["folio", "--dry-run", "--json", "-o", "Set.zip", "docs/guide.md", "docs/setup.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());

        JsonElement set = FolioSet.JsonLines(result).Single(l => l.GetProperty("path").GetString() == "Set.zip");
        JsonElement preflight = set.GetProperty("preflight");

        set.GetProperty("outcome").GetString().ShouldBe("printed");
        preflight.GetProperty("form").GetString().ShouldBe("zip");
        preflight.GetProperty("documents").EnumerateArray().Select(d => d.GetString()).ShouldBe(["guide.md", "setup.md"]);
        preflight.GetProperty("pictures").GetInt32().ShouldBe(1);
        preflight.GetProperty("missingPictures").GetInt32().ShouldBe(2);
        preflight.GetProperty("outsideLinks").GetInt32().ShouldBe(1);
        preflight.GetProperty("webPictures").GetInt32().ShouldBe(1);
        preflight.GetProperty("estimatedBytes").GetInt64().ShouldBeGreaterThan(0);
        preflight.TryGetProperty("sites", out _).ShouldBeFalse("the sites are listed only under --fetch-web-images");
        set.GetProperty("theme").GetProperty("source").GetString().ShouldBe("default");
    }

    [Fact]
    public async Task A_theme_named_on_the_command_line_says_so()
    {
        using var workspace = new Workspace();
        FolioSet.WriteTo(workspace);

        CliResult text = await CliRunner.RunAsync(["folio", "--dry-run", "--theme", "vintage-press", "-o", "Set.zip", "docs/guide.md"], currentDirectory: workspace.Root, cancellationToken: Ct);
        CliResult json = await CliRunner.RunAsync(["folio", "--dry-run", "--json", "--theme", "vintage-press", "-o", "Set.zip", "docs/guide.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        text.Out.ShouldContain("from --theme");

        JsonElement theme = FolioSet.JsonLines(json).Single(l => l.GetProperty("path").GetString() == "Set.zip").GetProperty("theme");
        theme.GetProperty("id").GetString().ShouldBe("vintage-press");
        theme.GetProperty("source").GetString().ShouldBe("option");
    }

    [Fact]
    public async Task Shrinking_is_named_when_it_is_on()
    {
        using var workspace = new Workspace();
        FolioSet.WriteTo(workspace);

        CliResult result = await CliRunner.RunAsync(["folio", "--dry-run", "--shrink-images", "1200", "-o", "Set/", "docs/guide.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.Out.ShouldContain("Folio:      Set/, a folder");
        result.Out.ShouldContain("Shrink:     pictures wider than 1200 px");
    }
}
