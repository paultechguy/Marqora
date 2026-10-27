// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using PaulTechGuy.MQ.Cli.Commands;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Manual;

/// <summary>
/// CLI-DOC-01 for the commands the example runs cannot reach: the ones inside pipelines, loops and
/// scripts, which are most of the recipes. Each is parsed as marqora-cli would parse it, so an
/// option renamed or removed in the code fails here until the recipe that uses it is changed.
/// What the shell does around them was run by hand; the plan's records say when.
/// </summary>
public sealed class ManualCommandTests
{
    private static readonly CommandTree Tree = CommandTree.Create(TestEnvironment.Discarding());

    public static TheoryData<string> Documents => [.. Manual.All.Select(m => m.RelativePath)];

    [Theory]
    [MemberData(nameof(Documents))]
    public void Every_command_in_a_shell_block_parses(string document)
    {
        List<string> failures = [];

        foreach (ShellCommand command in Commands(Manual.Named(document)))
        {
            ParseResult parse = Tree.Root.Parse(command.Arguments.ToArray(), new ParserConfiguration { ResponseFileTokenReplacer = null });

            // A wrapper such as "function mq { marqora-cli --settings app @args }" is finished by
            // whoever calls it, so the command and files it lacks are not its own to give.
            IEnumerable<string> errors = parse.Errors
                .Select(e => e.Message)
                .Where(message => !command.Forwards || !message.StartsWith("Required", StringComparison.Ordinal));

            failures.AddRange(errors.Select(message => $"line {command.Line}: {command.Source}\n    {message}"));
        }

        failures.ShouldBeEmpty();
    }

    /// <summary>So that the test above cannot pass by finding nothing to parse.</summary>
    [Fact]
    public void The_recipes_have_commands_in_every_shell()
    {
        Dictionary<string, int> perShell = Manual.Recipes.Blocks()
            .Where(ShellCommands.IsShell)
            .GroupBy(b => b.Info.Trim())
            .ToDictionary(g => g.Key, g => g.Sum(b => ShellCommands.In(b).Count()));

        perShell.ShouldContainKey("powershell");
        perShell.ShouldContainKey("bat");
        perShell.ShouldContainKey("bash");
        perShell.Values.ShouldAllBe(count => count > 5);
    }

    [Fact]
    public void A_pipeline_is_read_up_to_where_the_shell_takes_over()
    {
        var block = new FencedBlock(1, "bash", [
            "git diff --name-only -z -- '*.md' | xargs -0 -r marqora-cli format --check || exit 1",
            "marqora-cli replace --regex --find 'a (\\d+)' --with 'b $1' \"docs/**/*.md\" 2>&1 | grep x",
            "toc=$(marqora-cli toc README.md)",
            "echo \"marqora-cli format --write <file>\" >&2",
        ]);

        ShellCommand[] commands = [.. ShellCommands.In(block)];

        commands.Select(c => string.Join(' ', c.Arguments)).ShouldBe([
            "format --check FILE.md",
            "replace --regex --find a (\\d+) --with b $1 docs/**/*.md",
            "toc README.md",
        ]);
    }

    [Fact]
    public void The_manual_points_to_the_recipes()
    {
        Manual.Cli.Text.ShouldContain("(CLI-Recipes.md)");
        Manual.Recipes.Text.ShouldContain("(CLI.md)");
    }

    private static IEnumerable<ShellCommand> Commands(Manual manual) =>
        manual.Blocks().SelectMany(ShellCommands.In);
}
