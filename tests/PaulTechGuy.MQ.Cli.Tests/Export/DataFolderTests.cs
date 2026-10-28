// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Cli.Rendering;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Export;

/// <summary>CLI-EX-02: a data folder of the run's own, gone when it ends, and none left by a run that could not clean up.</summary>
[Collection(nameof(BrowserRunsAlone))]
public sealed class DataFolderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task No_data_folder_remains_after_a_run_and_a_stale_one_is_swept()
    {
        using var workspace = new Workspace();
        workspace.Text("a.md", "# A\n");

        // A run that was killed: no process 999999 started at tick 1.
        string stale = Path.Combine(RunFolder.Root, "999999-1-deadbeef");
        Directory.CreateDirectory(Path.Combine(stale, "WebView2"));

        RendererFactory? made = null;

        CliResult result = await CliRunner.RunAsync(
            ["export", "pdf", "-o", "a.pdf", "a.md"],
            currentDirectory: workspace.Root,
            renderers: setup => made = new RendererFactory(setup, RendererFactory.InstalledRuntime),
            cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());
        made!.DataFolder.ShouldNotBeNull();
        Directory.Exists(made.DataFolder).ShouldBeFalse("the run's data folder was left behind");
        Directory.Exists(stale).ShouldBeFalse("a stale run's folder was not swept");
    }

    [Fact]
    public async Task Two_runs_at_the_same_time_both_succeed_and_leave_no_folder()
    {
        using var workspace = new Workspace();
        workspace.Text("a.md", "# A\n\nFirst.\n");
        workspace.Text("b.md", "# B\n\nSecond.\n");

        // Separate processes, as two terminals would start them.
        Task<CliResult> first = CliRunner.RunProcessAsync(["export", "pdf", "-o", "a.pdf", "a.md"], currentDirectory: workspace.Root, cancellationToken: Ct);
        Task<CliResult> second = CliRunner.RunProcessAsync(["export", "pdf", "-o", "b.pdf", "b.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        CliResult[] results = await Task.WhenAll(first, second);

        results[0].ExitCode.ShouldBe(0, results[0].ToString());
        results[1].ExitCode.ShouldBe(0, results[1].ToString());
        File.Exists(workspace.At("a.pdf")).ShouldBeTrue();
        File.Exists(workspace.At("b.pdf")).ShouldBeTrue();

        // This test process's own runs are the only folders allowed to be there.
        string own = $"{Environment.ProcessId}-";
        string[] left = Directory.Exists(RunFolder.Root)
            ? [.. Directory.EnumerateDirectories(RunFolder.Root).Select(Path.GetFileName).Where(name => !name!.StartsWith(own, StringComparison.Ordinal))!]
            : [];

        left.ShouldBeEmpty();
    }
}
