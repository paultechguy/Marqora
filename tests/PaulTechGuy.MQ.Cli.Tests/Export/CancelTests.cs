// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Cli.Rendering;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Export;

/// <summary>CLI-RPT-08 for exports: Ctrl+C leaves no partial output, no browser and no data folder, and exits 130.</summary>
[Collection(nameof(BrowserRunsAlone))]
public sealed class CancelTests
{
    [Fact]
    public async Task Ctrl_c_during_an_export_leaves_no_partial_output_and_no_browser()
    {
        using var workspace = new Workspace();
        string body = string.Concat(Enumerable.Range(1, 400).Select(i => $"## Section {i}\n\nSome text for section {i}, long enough to fill a line or two of a page.\n\n"));

        for (int i = 0; i < 10; i++)
        {
            workspace.Text($"doc{i}.md", $"# Document {i}\n\n{body}");
        }

        RendererFactory? made = null;

        // Canceled as Ctrl+C cancels a run: through the token the signal handler trips. Late enough
        // that the browser is up and a document is part way through.
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancel.CancelAfter(TimeSpan.FromSeconds(2.5));

        CliResult result = await CliRunner.RunAsync(
            ["export", "pdf", "--out-dir", "dist", "*.md"],
            currentDirectory: workspace.Root,
            renderers: setup => made = new RendererFactory(setup, RendererFactory.InstalledRuntime),
            cancellationToken: cancel.Token);

        result.ExitCode.ShouldBe(130, result.ToString());
        made.ShouldNotBeNull();
        made.BrowsersStarted.ShouldNotBeEmpty("canceled before the browser started, so the test proves nothing");

        foreach (int id in made.BrowsersStarted)
        {
            RendererFactoryTests.Running(id).ShouldBeFalse($"browser {id} outlived the run");
        }

        Directory.Exists(made.DataFolder).ShouldBeFalse("the data folder was left behind");

        // Every file left is a whole PDF under its own name; nothing half-written, under any name.
        string[] left = Directory.Exists(workspace.At("dist")) ? Directory.GetFiles(workspace.At("dist")) : [];

        left.Length.ShouldBeLessThan(10, "the run finished before it was canceled, so the test proves nothing");

        foreach (string file in left)
        {
            Path.GetFileName(file).ShouldMatch(@"^doc\d\.pdf$");
            File.ReadAllText(file).TrimEnd().ShouldEndWith("%%EOF");
        }
    }
}
