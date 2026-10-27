// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Run;

/// <summary>CLI-RPT-06 and CLI-RPT-07: how much stderr says, and when it is colored.</summary>
public sealed class ReporterTests
{
    private const string Escape = "\u001b[";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Quiet_on_a_clean_write_prints_nothing()
    {
        using var workspace = new Workspace();
        string path = workspace.Text("a.md", "# A\n");

        CliResult result = await CliRunner.RunAsync(["number", "--write", "--quiet", path], cancellationToken: Ct);

        result.ExitCode.ShouldBe(0);
        result.Out.ShouldBeEmpty();
        result.Error.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_quiet_each_file_gets_a_summary_line()
    {
        using var workspace = new Workspace();
        string path = workspace.Text("a.md", "# A\n");

        CliResult result = await CliRunner.RunAsync(["number", "--write", path], cancellationToken: Ct);

        result.Error.ShouldContain("written (1 heading numbered).");
    }

    [Fact]
    public async Task Verbose_names_the_ending_a_mixed_file_would_be_written_with()
    {
        using var workspace = new Workspace();
        string path = workspace.Text("a.md", "text\r\nmore\nend\r\n");

        CliResult result = await CliRunner.RunAsync(["number", "--verbose", path], cancellationToken: Ct);

        result.Error.ShouldContain("Mixed line endings");
    }

    [Fact]
    public async Task Auto_colors_a_terminal()
    {
        CliResult result = await CliRunner.RunAsync(["fromat"], errorIsTerminal: true, cancellationToken: Ct);

        result.Error.ShouldContain(Escape);
    }

    [Fact]
    public async Task Auto_does_not_color_redirected_stderr()
    {
        CliResult result = await CliRunner.RunAsync(["fromat"], errorIsTerminal: false, cancellationToken: Ct);

        result.Error.ShouldNotContain(Escape);
    }

    [Fact]
    public async Task No_color_turns_auto_off()
    {
        CliResult result = await CliRunner.RunAsync(
            ["number", "--check", "missing.md"],
            errorIsTerminal: true,
            variables: new Dictionary<string, string> { ["NO_COLOR"] = "1" },
            cancellationToken: Ct);

        result.Error.ShouldNotContain(Escape);
    }

    [Fact]
    public async Task Always_overrides_no_color()
    {
        CliResult result = await CliRunner.RunAsync(
            ["number", "--color", "always", "missing.md"],
            errorIsTerminal: false,
            variables: new Dictionary<string, string> { ["NO_COLOR"] = "1" },
            cancellationToken: Ct);

        result.Error.ShouldContain(Escape);
    }

    /// <summary>A real process with stderr redirected, as a CI log or a pipe has it.</summary>
    [Fact]
    public async Task A_real_redirected_stderr_carries_no_escapes()
    {
        CliResult result = await CliRunner.RunProcessAsync(["number", "missing.md"], cancellationToken: Ct);

        result.ExitCode.ShouldBe(3);
        result.Error.ShouldContain("missing.md");
        result.Error.ShouldNotContain(Escape);
    }

    [Fact]
    public async Task Json_lines_are_never_colored()
    {
        CliResult result = await CliRunner.RunAsync(["number", "--json", "--color", "always", "missing.md"], cancellationToken: Ct);

        result.Out.ShouldNotContain(Escape);
        result.Out.ShouldContain("\"outcome\":\"failed\"");
    }
}
