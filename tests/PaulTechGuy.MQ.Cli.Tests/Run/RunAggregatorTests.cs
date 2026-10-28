// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Run;

/// <summary>CLI-RPT-03: a run carries on past a failure and ends with the worst it met; --fail-fast stops at the first.</summary>
public sealed class RunAggregatorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Codes_rank_canceled_render_output_input_usage_findings_success()
    {
        ExitCode[] ranked = [ExitCode.Success, ExitCode.Findings, ExitCode.Usage, ExitCode.Input, ExitCode.Output, ExitCode.Render, ExitCode.Canceled];

        for (int i = 0; i < ranked.Length; i++)
        {
            for (int j = 0; j < ranked.Length; j++)
            {
                ExitCodes.Worst(ranked[i], ranked[j]).ShouldBe(ranked[Math.Max(i, j)]);
            }
        }
    }

    [Fact]
    public async Task One_input_error_and_one_finding_exit_3_and_both_are_reported()
    {
        using var workspace = new Workspace();
        string changes = workspace.Text("changes.md", "# A\n");

        CliResult result = await CliRunner.RunAsync(["number", "--check", workspace.At("missing.md"), changes], cancellationToken: Ct);

        result.ExitCode.ShouldBe(3);
        result.Error.ShouldContain("missing.md");
        result.Error.ShouldContain("changes.md: would change");
    }

    [Fact]
    public async Task Fail_fast_stops_at_the_first_failure()
    {
        using var workspace = new Workspace();
        workspace.Bytes("a.md", [0x23, 0x20, 0xE9, 0x0A]);
        workspace.Text("b.md", "# B\n");

        CliResult result = await CliRunner.RunAsync(["number", "--write", "--fail-fast", "--json", "*.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(3);
        result.Out.ShouldContain("\"path\":\"b.md\",\"verb\":\"number\",\"outcome\":\"skipped\"");
        workspace.Read("b.md").ShouldBe("# B\n");
    }

    [Fact]
    public async Task Without_fail_fast_the_rest_are_processed()
    {
        using var workspace = new Workspace();
        workspace.Bytes("a.md", [0x23, 0x20, 0xE9, 0x0A]);
        workspace.Text("b.md", "# B\n");

        CliResult result = await CliRunner.RunAsync(["number", "--write", "*.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(3);
        workspace.Read("b.md").ShouldBe("# 1  B\n");
    }
}
