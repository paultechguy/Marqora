// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using PaulTechGuy.MQ.Cli.Commands;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Commands;

/// <summary>CLI-CMD-03: every verb's help ends with examples, and a verb without any fails the build.</summary>
public sealed class HelpExamplesTests
{
    public static TheoryData<string> VerbPaths
    {
        get
        {
            CommandTree tree = CommandTree.Create(TestEnvironment.Discarding());

            return [.. tree.AllCommands.Where(c => c.Subcommands.Count == 0).Select(c => CommandShared.HelpPath(c)["marqora-cli ".Length..])];
        }
    }

    [Fact]
    public void Every_verb_has_examples()
    {
        CommandTree tree = CommandTree.Create(TestEnvironment.Discarding());

        Command[] without = [.. tree.AllCommands
            .Where(c => c.Subcommands.Count == 0)
            .Where(c => !tree.Shared.Examples.TryGetValue(c, out string[]? examples) || examples.Length == 0)];

        without.Select(CommandShared.HelpPath).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(VerbPaths))]
    public async Task A_verbs_help_shows_its_examples(string verb)
    {
        CliResult help = await CliRunner.RunAsync([.. verb.Split(' '), "--help"], cancellationToken: TestContext.Current.CancellationToken);

        help.ExitCode.ShouldBe(0);
        help.Out.ShouldContain("Examples:");
        help.Out.ShouldContain($"marqora-cli {verb}");
    }

    /// <summary>Every example names the verb it belongs to, so none was pasted under the wrong one.</summary>
    [Fact]
    public void Every_example_starts_with_its_own_verb()
    {
        CommandTree tree = CommandTree.Create(TestEnvironment.Discarding());

        foreach ((Command command, string[] examples) in tree.Shared.Examples)
        {
            foreach (string example in examples)
            {
                string path = CommandShared.HelpPath(command);
                bool own = example == path
                    || example.StartsWith(path + " ", StringComparison.Ordinal)
                    || command.Name == "version" && example == "marqora-cli --version";

                own.ShouldBeTrue($"'{example}' is listed under {CommandShared.HelpPath(command)}.");
            }
        }
    }
}
