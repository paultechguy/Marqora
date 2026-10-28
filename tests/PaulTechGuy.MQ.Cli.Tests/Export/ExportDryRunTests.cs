// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Export;

/// <summary>CLI-EX-04: --dry-run starts no browser and writes nothing, and says what it would do.</summary>
public sealed class ExportDryRunTests
{
    [Fact]
    public async Task A_dry_run_starts_no_browser_writes_nothing_and_prints_the_plan()
    {
        using var workspace = new Workspace();
        workspace.Text("guide.md", "# Guide\n\n![The chart](chart.png)\n\nSee [the notes](notes.md).\n");
        var fake = new FakeRenderers();

        CliResult result = await CliRunner.RunAsync(
            ["export", "pdf", "--dry-run", "--theme", "forest", "--out-dir", "dist", "guide.md"],
            currentDirectory: workspace.Root,
            renderers: fake.Make,
            cancellationToken: TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(0, result.ToString());
        fake.Made.ShouldBe(0);
        Directory.Exists(workspace.At("dist")).ShouldBeFalse();

        result.Out.ShouldContain($"Export:     guide.md -> {workspace.At(Path.Combine("dist", "guide.pdf"))}");
        result.Out.ShouldContain("Theme:      Forest, from --theme");
        result.Out.ShouldContain("Page:       Letter, portrait, normal margins");
        result.Error.ShouldContain("guide.md:3:");
        result.Error.ShouldContain("[missing-image]");
        result.Error.ShouldContain("guide.md:5:");
        result.Error.ShouldContain("[broken-link]");
    }
}
