// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Inputs;

/// <summary>
/// CLI-IN-04 and CLI-OUT-06: UTF-8 read strictly, and a file's byte order mark, line endings and
/// final newline kept through --write, except where format's own rules decide them (A7).
/// </summary>
public sealed class TextFileTests
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Utf8_with_or_without_a_byte_order_mark_formats(bool withBom)
    {
        using var workspace = new Workspace();
        byte[] body = "# Title\nText   \n"u8.ToArray();
        string path = workspace.Bytes("a.md", withBom ? [.. Bom, .. body] : body);

        CliResult result = await CliRunner.RunAsync(["format", "--write", path], cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());
        byte[] after = workspace.ReadBytes("a.md");
        after.AsSpan().StartsWith(Bom).ShouldBe(withBom);
    }

    [Fact]
    public async Task A_windows_1252_file_exits_3_and_is_left_exactly_as_it_was()
    {
        using var workspace = new Workspace();

        // "café" in Windows-1252: the é is one byte, 0xE9, which is not valid UTF-8.
        byte[] original = [0x23, 0x20, 0x63, 0x61, 0x66, 0xE9, 0x0A];
        string path = workspace.Bytes("a.md", original);

        CliResult result = await CliRunner.RunAsync(["format", "--write", path], cancellationToken: Ct);

        result.ExitCode.ShouldBe(3);
        result.Error.ShouldContain("is not valid UTF-8");
        workspace.ReadBytes("a.md").ShouldBe(original);
    }

    [Fact]
    public async Task A_crlf_file_with_a_bom_is_still_crlf_with_a_bom_after_format_write()
    {
        using var workspace = new Workspace();
        string path = workspace.Bytes("a.md", [.. Bom, .. "# Title\r\nText   \r\n\r\n\r\nMore\r\n"u8.ToArray()]);

        (await CliRunner.RunAsync(["format", "--write", path], cancellationToken: Ct)).ExitCode.ShouldBe(0);

        byte[] after = workspace.ReadBytes("a.md");
        after.AsSpan().StartsWith(Bom).ShouldBeTrue();
        string text = System.Text.Encoding.UTF8.GetString(after, 3, after.Length - 3);
        text.ShouldBe("# Title\r\n\r\nText\r\n\r\nMore\r\n");
    }

    [Fact]
    public async Task An_lf_file_without_a_final_newline_keeps_it_that_way_under_number_write()
    {
        using var workspace = new Workspace();
        string path = workspace.Text("a.md", "# A\n## B");

        (await CliRunner.RunAsync(["number", "--write", path], cancellationToken: Ct)).ExitCode.ShouldBe(0);

        workspace.Read("a.md").ShouldBe("# 1  A\n## 1.1  B");
    }

    [Fact]
    public async Task A_crlf_file_keeps_crlf_under_number_write()
    {
        using var workspace = new Workspace();
        string path = workspace.Text("a.md", "# A\r\n## B\r\n");

        (await CliRunner.RunAsync(["number", "--write", path], cancellationToken: Ct)).ExitCode.ShouldBe(0);

        workspace.Read("a.md").ShouldBe("# 1  A\r\n## 1.1  B\r\n");
    }

    [Fact]
    public async Task Format_check_on_an_already_formatted_crlf_file_exits_0()
    {
        using var workspace = new Workspace();
        string path = workspace.Text("a.md", "# Title\r\n\r\nText\r\n");

        CliResult result = await CliRunner.RunAsync(["format", "--check", path], cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());
    }

    /// <summary>A mixed file the verb does not change is not "changed" by having its endings tidied.</summary>
    [Fact]
    public async Task A_mixed_file_the_verb_leaves_alone_is_not_reported_as_changed()
    {
        using var workspace = new Workspace();
        string path = workspace.Text("a.md", "text\r\nmore\nend\r\n");

        CliResult result = await CliRunner.RunAsync(["number", "--check", path], cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());
    }

    [Fact]
    public async Task Stdout_carries_no_byte_order_mark_and_the_inputs_endings()
    {
        using var workspace = new Workspace();
        string path = workspace.Bytes("a.md", [.. Bom, .. "# A\r\n"u8.ToArray()]);

        CliResult result = await CliRunner.RunAsync(["number", path], cancellationToken: Ct);

        result.OutBytes.AsSpan().StartsWith(Bom).ShouldBeFalse();
        result.Out.ShouldBe("# 1  A\r\n");
    }
}
