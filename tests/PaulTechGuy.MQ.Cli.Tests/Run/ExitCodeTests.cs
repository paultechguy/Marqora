// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Run;

/// <summary>
/// CLI-RPT-01: each exit code, produced by a real invocation. Render (5) arrives with the exports
/// in M2; its test is added there.
/// </summary>
public sealed class ExitCodeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Success_is_0()
    {
        using var workspace = new Workspace();
        string path = workspace.Text("a.md", "# A\n");

        (await CliRunner.RunAsync(["number", path], cancellationToken: Ct)).ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task A_file_check_says_would_change_is_1()
    {
        using var workspace = new Workspace();
        string path = workspace.Text("a.md", "# A\n");

        (await CliRunner.RunAsync(["number", "--check", path], cancellationToken: Ct)).ExitCode.ShouldBe(1);
    }

    [Fact]
    public async Task A_usage_error_is_2()
    {
        (await CliRunner.RunAsync(["format", "--write", "--check", "a.md"], cancellationToken: Ct)).ExitCode.ShouldBe(2);
    }

    [Fact]
    public async Task A_missing_input_is_3()
    {
        using var workspace = new Workspace();

        (await CliRunner.RunAsync(["format", workspace.At("missing.md")], cancellationToken: Ct)).ExitCode.ShouldBe(3);
    }

    /// <summary>An output that cannot be written: the file is held open with no sharing while --write tries to replace it.</summary>
    [Fact]
    public async Task An_output_that_cannot_be_written_is_4()
    {
        using var workspace = new Workspace();
        string path = workspace.Text("a.md", "# A\n");

        CliResult result;

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            result = await CliRunner.RunAsync(["number", "--write", path], cancellationToken: Ct);
        }

        result.ExitCode.ShouldBe(4, result.ToString());
        result.Error.ShouldContain("Could not be written");
        workspace.Read("a.md").ShouldBe("# A\n");
    }

    [Fact]
    public async Task Canceled_is_130()
    {
        using var workspace = new Workspace();
        string path = workspace.Text("a.md", "# A\n");
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        CliResult result = await CliRunner.RunAsync(["number", "--write", path], cancellationToken: cancel.Token);

        result.ExitCode.ShouldBe(130);
        workspace.Read("a.md").ShouldBe("# A\n");
    }
}
