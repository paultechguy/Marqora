// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Commands;

/// <summary>
/// CLI-CMD-04 and CLI-CMD-05: a command line that cannot be run as given exits 2 with one
/// sentence on stderr pointing at help, before any file is touched, and nothing on stdout.
/// </summary>
public sealed class UsageErrorTests
{
    public static TheoryData<string, string> Conflicts => new()
    {
        { "format --write --check x.md", "cannot be combined" },
        { "format --write --dry-run x.md", "cannot be combined" },
        { "format --check --dry-run x.md", "cannot be combined" },
        { "format - x.md", "'-' reads standard input" },
        { "--quiet --verbose format x.md", "--quiet and --verbose" },
        { "check --strict x.md", "--strict needs --style" },
        { "toc --write x.md", "does not write one" },
        { "replace --with y x.md", "--find" },
        { "format", "Required argument missing" },
        { "number --from h9 x.md", "h1, h2, h3" },
        { "--color sometimes format x.md", "auto, always, never" },
    };

    [Theory]
    [MemberData(nameof(Conflicts))]
    public async Task A_conflict_or_missing_option_exits_2_with_one_sentence(string commandLine, string expected)
    {
        CliResult result = await CliRunner.RunAsync(commandLine.Split(' '), cancellationToken: TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(2, result.ToString());
        result.Out.ShouldBeEmpty();
        result.Error.ShouldContain(expected);
        result.Error.ShouldContain("--help'.");
        result.Error.TrimEnd().Split('\n').Length.ShouldBe(1, "one sentence, on one line");
    }

    public static TheoryData<string, string> Typos => new()
    {
        { "fromat x.md", "Did you mean 'format'?" },
        { "chek x.md", "Did you mean 'check'?" },
        { "themes lst", "Did you mean 'list'?" },
        { "format --writ x.md", "Did you mean '--write'?" },
        { "format --verbos x.md", "Did you mean '--verbose'?" },
        { "number --dryrun x.md", "Did you mean '--dry-run'?" },
    };

    [Theory]
    [MemberData(nameof(Typos))]
    public async Task A_close_misspelling_suggests_the_right_word(string commandLine, string suggestion)
    {
        CliResult result = await CliRunner.RunAsync(commandLine.Split(' '), cancellationToken: TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(2, result.ToString());
        result.Error.ShouldContain(suggestion);
    }

    [Fact]
    public async Task A_word_like_no_verb_gets_no_suggestion()
    {
        CliResult result = await CliRunner.RunAsync(["zebra", "x.md"], cancellationToken: TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(2);
        result.Error.ShouldContain("'zebra' is not a marqora-cli command.");
        result.Error.ShouldNotContain("Did you mean");
    }

    /// <summary>
    /// CLI-CMD-05: "@file" is a file name. A response file holding --check would otherwise turn
    /// this into a check run; instead the name is looked for, and not found.
    /// </summary>
    [Fact]
    public async Task A_response_file_is_not_expanded()
    {
        using var workspace = new Workspace();
        workspace.Text("args.rsp", "--check\n");

        CliResult result = await CliRunner.RunAsync(["format", "@args.rsp"], currentDirectory: workspace.Root, cancellationToken: TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(3, result.ToString());
        result.Error.ShouldContain("@args.rsp");
    }
}
