// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using PaulTechGuy.MQ.Cli.Rendering;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Export;

/// <summary>
/// CLI-EX-01: one browser per run, and none for a run that does not export. The browser half
/// starts a real one.
/// </summary>
[Collection(nameof(BrowserRunsAlone))]
public sealed class RendererFactoryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_format_run_never_asks_for_a_renderer()
    {
        using var workspace = new Workspace();
        string path = workspace.Text("a.md", "# A\n");
        var fake = new FakeRenderers();

        CliResult result = await CliRunner.RunAsync(["format", path], renderers: fake.Make, cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());
        fake.Made.ShouldBe(0);
    }

    [Fact]
    public async Task Ten_exports_start_one_browser_and_leave_none_running()
    {
        using var workspace = new Workspace();

        for (int i = 0; i < 10; i++)
        {
            workspace.Text($"doc{i}.md", $"# Document {i}\n\nSome text.\n");
        }

        RendererFactory? made = null;

        CliResult result = await CliRunner.RunAsync(
            ["export", "pdf", "--out-dir", "dist", "*.md"],
            currentDirectory: workspace.Root,
            renderers: setup => made = new RendererFactory(setup, RendererFactory.InstalledRuntime),
            cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());
        Directory.GetFiles(workspace.At("dist"), "*.pdf").Length.ShouldBe(10);

        made.ShouldNotBeNull();
        made.BrowsersStarted.Count.ShouldBe(1);
        Running(made.BrowsersStarted[0]).ShouldBeFalse("the run's browser outlived it");
    }

    internal static bool Running(int id)
    {
        try
        {
            using Process process = Process.GetProcessById(id);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
