// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using PaulTechGuy.MQ.Cli.Text;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Text;

/// <summary>CLI-OUT-05: --dry-run's diff applies with git apply to the original, and an unchanged file prints nothing.</summary>
public sealed class DryRunDiffTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> Documents => new()
    {
        { "lf", "# Guide\n\nIntro.\n\n## Install\n\nRun it.\n\n## Use\n\nUse it.\n" },
        { "crlf", "# Guide\r\n\r\nIntro.\r\n\r\n## Install\r\n" },
        { "no final newline", "# Guide\n\n## Install" },
        { "long", string.Concat(Enumerable.Range(1, 60).Select(i => i % 15 == 0 ? $"## Part {i}\n\n" : $"Line {i}.\n\n")) },
    };

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task The_diff_applies_cleanly_with_git_apply(string name, string text)
    {
        using var workspace = new Workspace();
        workspace.Text("doc.md", text);

        CliResult dry = await CliRunner.RunAsync(["number", "--dry-run", "doc.md"], currentDirectory: workspace.Root, cancellationToken: Ct);
        dry.ExitCode.ShouldBe(0, name);
        workspace.Bytes("doc.diff", dry.OutBytes);

        (await GitApplyAsync(workspace.Root, "doc.diff")).ShouldBe(0, $"git apply refused the {name} diff:\n{dry.Out}");

        CliResult direct = await CliRunner.RunAsync(["number", "doc.md"], currentDirectory: workspace.Root, cancellationToken: Ct);
        workspace.Read("doc.md").ShouldBe(direct.Out, $"the {name} diff, applied, should give what the verb prints");
    }

    [Fact]
    public async Task An_unchanged_file_prints_nothing()
    {
        using var workspace = new Workspace();
        workspace.Text("doc.md", "# 1  Guide\n## 1.1  Install\n");

        CliResult dry = await CliRunner.RunAsync(["number", "--dry-run", "doc.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        dry.ExitCode.ShouldBe(0);
        dry.Out.ShouldBeEmpty();
    }

    [Fact]
    public void Equal_texts_have_no_diff()
    {
        UnifiedDiff.Create("a\nb\n", "a\nb\n", "x.md").ShouldBeEmpty();
    }

    /// <summary>Applies the diff in the workspace; git apply works outside a repository.</summary>
    private static async Task<int> GitApplyAsync(string folder, string diff)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = folder,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        start.ArgumentList.Add("apply");
        start.ArgumentList.Add("--whitespace=nowarn");
        start.ArgumentList.Add(diff);

        using Process process = Process.Start(start)!;
        await process.StandardError.ReadToEndAsync(Ct);
        await process.WaitForExitAsync(Ct);

        return process.ExitCode;
    }
}
