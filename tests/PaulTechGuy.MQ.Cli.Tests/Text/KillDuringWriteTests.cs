// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Text;

/// <summary>
/// CLI-OUT-03 and CLI-RPT-08: a run ended part way through --write - killed, the harshest way a
/// process can end - never leaves a file truncated. Each file is either as it was or as it was
/// going to be.
///
/// Not timed to land in the middle of a write; it does not need to be. The process is killed at a
/// spread of moments across a run over many large files, and the assertion holds wherever each kill
/// lands, so the test cannot pass by luck or fail by it.
/// </summary>
public sealed class KillDuringWriteTests
{
    [Fact]
    public async Task Every_file_is_original_or_complete_however_the_run_is_cut_short()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        // Headings throughout, so every file changes and every file is written.
        string original = string.Concat(Enumerable.Range(1, 20_000).Select(i => $"## Heading {i}\n\nSome text for heading {i}.\n\n"));

        using var reference = new Workspace();
        reference.Text("ref.md", original);
        string numbered = (await CliRunner.RunAsync(["number", reference.At("ref.md")], cancellationToken: ct)).Out;

        foreach (int delay in new[] { 150, 300, 500, 800 })
        {
            using var workspace = new Workspace();

            for (int i = 0; i < 12; i++)
            {
                workspace.Text($"doc{i:00}.md", original);
            }

            var start = new ProcessStartInfo(Repo.CliExecutable)
            {
                WorkingDirectory = workspace.Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (string arg in new[] { "number", "--write", "--quiet", "*.md" })
            {
                start.ArgumentList.Add(arg);
            }

            using Process process = Process.Start(start)!;
            await Task.Delay(delay, ct);

            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // It finished first; every file should then be complete, which the check below says.
            }

            await process.WaitForExitAsync(ct);

            for (int i = 0; i < 12; i++)
            {
                string after = workspace.Read($"doc{i:00}.md");

                (after == original || after == numbered).ShouldBeTrue($"doc{i:00}.md was left neither original nor complete after a kill at {delay} ms ({after.Length} characters).");
            }
        }
    }
}
