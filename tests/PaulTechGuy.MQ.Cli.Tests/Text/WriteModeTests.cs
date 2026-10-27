// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Text;

/// <summary>CLI-OUT-01 to CLI-OUT-04 and CLI-IN-05: what each mode touches, and what it leaves alone.</summary>
public sealed class WriteModeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTime LongAgo = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Already numbered. Two headings, because one heading reading "1  A" is not taken for numbering
    /// by the app's detector - it could as well be a title that starts with a number.
    /// </summary>
    private const string Numbered = "# 1  A\n## 1.1  B\n";

    [Fact]
    public async Task Without_write_the_file_and_its_time_are_unchanged()
    {
        using var workspace = new Workspace();
        string path = workspace.Text("a.md", "# A\n");
        File.SetLastWriteTimeUtc(path, LongAgo);

        CliResult result = await CliRunner.RunAsync(["number", path], cancellationToken: Ct);

        result.Out.ShouldBe("# 1  A\n");
        workspace.Read("a.md").ShouldBe("# A\n");
        File.GetLastWriteTimeUtc(path).ShouldBe(LongAgo);
    }

    [Fact]
    public async Task Write_leaves_an_unchanged_file_alone_with_its_time()
    {
        using var workspace = new Workspace();
        string path = workspace.Text("a.md", Numbered);
        File.SetLastWriteTimeUtc(path, LongAgo);

        CliResult result = await CliRunner.RunAsync(["number", "--write", path], cancellationToken: Ct);

        result.ExitCode.ShouldBe(0);
        result.Error.ShouldContain("unchanged");
        File.GetLastWriteTimeUtc(path).ShouldBe(LongAgo);
    }

    [Fact]
    public async Task Write_refuses_a_run_with_a_read_only_file_and_changes_none()
    {
        using var workspace = new Workspace();
        string a = workspace.Text("a.md", "# A\n");
        string b = workspace.Text("b.md", "# B\n");
        string locked = workspace.Text("locked.md", "# L\n");
        File.SetAttributes(locked, FileAttributes.ReadOnly);

        CliResult result = await CliRunner.RunAsync(["number", "--write", a, b, locked], cancellationToken: Ct);

        result.ExitCode.ShouldBe(3);
        result.Error.ShouldContain("locked.md: is read-only");
        workspace.Read("a.md").ShouldBe("# A\n");
        workspace.Read("b.md").ShouldBe("# B\n");
        workspace.Read("locked.md").ShouldBe("# L\n");
    }

    [Fact]
    public async Task Without_write_a_read_only_file_is_read_normally()
    {
        using var workspace = new Workspace();
        string locked = workspace.Text("locked.md", "# L\n");
        File.SetAttributes(locked, FileAttributes.ReadOnly);

        CliResult result = await CliRunner.RunAsync(["number", locked], cancellationToken: Ct);

        result.ExitCode.ShouldBe(0);
        result.Out.ShouldBe("# 1  L\n");
    }

    [Fact]
    public async Task Check_with_everything_in_shape_exits_0_and_says_nothing()
    {
        using var workspace = new Workspace();
        string path = workspace.Text("a.md", Numbered);

        CliResult result = await CliRunner.RunAsync(["number", "--check", path], cancellationToken: Ct);

        result.ExitCode.ShouldBe(0);
        result.Out.ShouldBeEmpty();
        result.Error.ShouldBeEmpty();
    }

    [Fact]
    public async Task Check_with_one_file_to_change_exits_1_naming_it_on_stderr()
    {
        using var workspace = new Workspace();
        string done = workspace.Text("done.md", Numbered);
        string todo = workspace.Text("todo.md", "# A\n");

        CliResult result = await CliRunner.RunAsync(["number", "--check", done, todo], cancellationToken: Ct);

        result.ExitCode.ShouldBe(1);
        result.Out.ShouldBeEmpty();
        result.Error.ShouldContain("todo.md: would change.");
        result.Error.ShouldNotContain("done.md");
        workspace.Read("todo.md").ShouldBe("# A\n");
    }

    [Fact]
    public async Task Several_files_with_no_mode_exit_2_suggesting_write_or_check()
    {
        using var workspace = new Workspace();
        string a = workspace.Text("a.md", "# A\n");
        string b = workspace.Text("b.md", "# B\n");

        CliResult result = await CliRunner.RunAsync(["format", a, b], cancellationToken: Ct);

        result.ExitCode.ShouldBe(2);
        result.Out.ShouldBeEmpty();
        result.Error.ShouldContain("--write");
        result.Error.ShouldContain("--check");
    }
}
