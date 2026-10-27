// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Inputs;

/// <summary>
/// CLI-IN-01: the same quoted pattern finds the same files from PowerShell and from cmd, because the
/// CLI expands it and neither shell does. bash is checked by hand and recorded in the plan's §9.3,
/// since not every machine that runs these tests has it.
/// </summary>
public sealed class ShellGlobTests
{
    [Fact]
    public async Task Powershell_and_cmd_give_the_same_files()
    {
        using var workspace = new Workspace();
        workspace.Text("docs/a.md", "# A\n");
        workspace.Text("docs/deep/b.md", "# B\n");
        workspace.Text("other.md", "# O\n");

        string exe = Repo.CliExecutable;

        string[] pwsh = await RunAsync("pwsh", $"-NoProfile -Command \"& '{exe}' number --check --json 'docs/**/*.md'\"", workspace.Root);

        // cmd reads its own command line, not the escaped one .NET builds from a list: /s keeps the
        // outer quotes for itself and passes everything inside them through untouched.
        string[] cmd = await RunAsync("cmd.exe", $"/d /s /c \"\"{exe}\" number --check --json \"docs/**/*.md\"\"", workspace.Root);

        pwsh.ShouldBe(cmd);
        pwsh.Length.ShouldBe(2);
        pwsh.ShouldAllBe(line => line.Contains("docs"));
    }

    private static async Task<string[]> RunAsync(string shell, string arguments, string folder)
    {
        var start = new ProcessStartInfo(shell, arguments)
        {
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using Process process = Process.Start(start)!;
        string output = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        return [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Replace("\\\\", "/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];
    }
}
