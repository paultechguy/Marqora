// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Export;

/// <summary>CLI-EX-05: a render that never finishes fails its file with exit 5, and the run moves on.</summary>
public sealed class RenderTimeoutTests
{
    [Fact]
    public async Task A_render_that_never_finishes_fails_its_file_with_exit_5_and_the_next_is_exported()
    {
        using var workspace = new Workspace();
        workspace.Text("a.md", $"# A\n\n{FakeRenderers.NeverFinishes}\n");
        workspace.Text("b.md", "# B\n");
        var fake = new FakeRenderers();

        CliResult result = await CliRunner.RunAsync(
            ["export", "pdf", "--timeout", "7", "--out-dir", "dist", "a.md", "b.md"],
            currentDirectory: workspace.Root,
            renderers: fake.Make,
            cancellationToken: TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(5, result.ToString());
        result.Error.ShouldContain("a.md: The document did not finish rendering within 7 seconds. Pass --timeout to allow longer.");
        fake.Setup!.Timeout.ShouldBe(TimeSpan.FromSeconds(7));

        Directory.GetFiles(workspace.At("dist")).Select(Path.GetFileName).ShouldBe(["b.pdf"]);
    }

    [Fact]
    public async Task A_timeout_of_zero_is_a_usage_error()
    {
        CliResult result = await CliRunner.RunAsync(["export", "pdf", "--timeout", "0", "-o", "x.pdf", "x.md"], cancellationToken: TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(2, result.ToString());
    }
}
