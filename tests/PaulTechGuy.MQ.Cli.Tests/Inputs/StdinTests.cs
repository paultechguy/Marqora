// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Text;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Inputs;

/// <summary>CLI-IN-02 and CLI-IN-03: "-" reads standard input, alone, with links resolved against --base-dir.</summary>
public sealed class StdinTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Standard_input_is_read_and_the_result_written_to_stdout()
    {
        CliResult result = await CliRunner.RunAsync(["number", "-"], standardInput: Encoding.UTF8.GetBytes("# A\n## B\n"), cancellationToken: Ct);

        result.ExitCode.ShouldBe(0);
        result.Out.ShouldBe("# 1  A\n## 1.1  B\n");
    }

    /// <summary>The manual's own example, through a real PowerShell pipe.</summary>
    [Fact]
    public async Task Get_content_piped_to_format_writes_the_formatted_text()
    {
        using var workspace = new Workspace();
        workspace.Text("x.md", "# Title\nText   \n\n\n\nMore\n");

        var start = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = workspace.Root,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add($"[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false); Get-Content x.md -Raw | & '{Repo.CliExecutable}' format -");

        using Process process = Process.Start(start)!;
        string output = await process.StandardOutput.ReadToEndAsync(Ct);
        await process.WaitForExitAsync(Ct);

        process.ExitCode.ShouldBe(0);
        output.ShouldContain("# Title\n\nText\n\nMore\n");
    }

    [Fact]
    public async Task Standard_input_with_a_file_is_a_usage_error()
    {
        CliResult result = await CliRunner.RunAsync(["format", "-", "x.md"], cancellationToken: Ct);

        result.ExitCode.ShouldBe(2);
    }

    [Fact]
    public async Task Write_with_standard_input_is_a_usage_error()
    {
        CliResult result = await CliRunner.RunAsync(["number", "--write", "-"], standardInput: "# A\n"u8.ToArray(), cancellationToken: Ct);

        result.ExitCode.ShouldBe(2);
        result.Error.ShouldContain("standard input has nowhere to be written back");
    }

    [Fact]
    public async Task Json_for_standard_input_names_its_base_folder()
    {
        using var workspace = new Workspace();
        Directory.CreateDirectory(workspace.At("docs"));

        CliResult result = await CliRunner.RunAsync(
            ["number", "--json", "--base-dir", "docs", "-"],
            currentDirectory: workspace.Root,
            standardInput: "# A\n"u8.ToArray(),
            cancellationToken: Ct);

        result.Out.ShouldContain($"\"baseDir\":\"{workspace.At("docs").Replace("\\", "\\\\", StringComparison.Ordinal)}\"");
    }
}
