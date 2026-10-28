// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Commands;

/// <summary>CLI-CMD-02: the menu's own names are accepted, and do exactly what the short ones do.</summary>
public sealed class AliasTests
{
    private const string Document = "# Title\n\n## Install\n\n## Use\n";

    public static TheoryData<string, string> Aliases => new()
    {
        { "number", "number-headings" },
        { "unnumber", "remove-heading-numbers" },
    };

    [Theory]
    [MemberData(nameof(Aliases))]
    public async Task An_alias_gives_the_same_output_as_its_verb(string verb, string alias)
    {
        using var workspace = new Workspace();
        string path = workspace.Text("guide.md", Document);

        CliResult canonical = await CliRunner.RunAsync([verb, path], cancellationToken: TestContext.Current.CancellationToken);
        CliResult aliased = await CliRunner.RunAsync([alias, path], cancellationToken: TestContext.Current.CancellationToken);

        aliased.ExitCode.ShouldBe(canonical.ExitCode);
        aliased.OutBytes.ShouldBe(canonical.OutBytes);
    }

    [Theory]
    [MemberData(nameof(Aliases))]
    public async Task Help_for_the_verb_names_its_alias(string verb, string alias)
    {
        CliResult help = await CliRunner.RunAsync([verb, "--help"], cancellationToken: TestContext.Current.CancellationToken);

        help.ExitCode.ShouldBe(0);
        help.Out.ShouldContain(alias);
    }
}
