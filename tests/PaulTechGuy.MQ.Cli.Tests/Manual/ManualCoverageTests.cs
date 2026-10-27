// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using System.Reflection;
using System.Text.RegularExpressions;
using PaulTechGuy.MQ.Cli.Commands;
using PaulTechGuy.MQ.Cli.Reporting;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Manual;

/// <summary>
/// CLI-DOC-01: everything a user can type is in the manual, checked against the parser itself, so
/// an option added to the code without a word in the manual fails the build. Hidden commands - the
/// exports, which refuse to run until M2 - are left out until they are shown.
/// </summary>
public sealed class ManualCoverageTests
{
    private static readonly CommandTree Tree = CommandTree.Create(TestEnvironment.Discarding());

    private static IEnumerable<Command> Verbs => Tree.AllCommands.Where(c => c.Subcommands.Count == 0 && !c.Hidden && !HasHiddenParent(c));

    [Fact]
    public void Every_command_has_a_section_of_its_own()
    {
        string[] missing = [.. Verbs.Select(Name).Where(name => Manual.Cli.Section($"### `{name}`") is null)];

        missing.ShouldBeEmpty("Each command needs a heading of the form ### `name`.");
    }

    [Fact]
    public void Every_alias_is_named()
    {
        string[] missing = [.. Verbs
            .SelectMany(c => c.Aliases)
            .Where(alias => !Manual.Cli.Text.Contains($"`{alias}`", StringComparison.Ordinal))];

        missing.ShouldBeEmpty();
    }

    [Fact]
    public void Every_option_is_in_its_commands_section()
    {
        List<string> missing = [];

        foreach (Command verb in Verbs)
        {
            string section = Manual.Cli.Section($"### `{Name(verb)}`") ?? string.Empty;

            missing.AddRange(verb.Options
                .Where(o => !o.Recursive && !o.Hidden)
                .SelectMany(Names)
                .Where(name => !Backticked(section, name))
                .Select(name => $"{Name(verb)} {name}"));
        }

        missing.ShouldBeEmpty();
    }

    /// <summary>The options every command takes, and --help and --version, which the root carries.</summary>
    [Fact]
    public void Every_global_option_is_in_the_global_options()
    {
        string section = Manual.Cli.Section("### Global options") ?? string.Empty;

        // System.CommandLine also accepts /h and /?; the manual teaches the forms with dashes.
        string[] missing = [.. Tree.Root.Options
            .Where(o => !o.Hidden)
            .SelectMany(Names)
            .Where(name => !name.StartsWith('/'))
            .Where(name => !Backticked(section, name))];

        missing.ShouldBeEmpty();
    }

    [Fact]
    public void Every_example_in_the_help_is_in_the_manual()
    {
        string[] missing = [.. Tree.Shared.Examples
            .Where(pair => !pair.Key.Hidden && !HasHiddenParent(pair.Key))
            .SelectMany(pair => pair.Value)
            .Where(example => !Manual.Cli.Lines.Contains(example))];

        missing.ShouldBeEmpty("Each example in a command's --help appears in the manual, on a line of its own.");
    }

    [Fact]
    public void Every_json_field_is_named_in_the_json_section()
    {
        string section = Manual.Cli.Section("## JSON output") ?? string.Empty;
        string[] fields = File.ReadAllLines(Repo.At("tests", "PaulTechGuy.MQ.Cli.Tests", "Golden", "json", "schema-v1.txt"))
            .Where(line => line.Length > 0)
            .Select(line => line.Split('.')[^1])
            .Distinct()
            .ToArray();

        fields.Where(field => !section.Contains($"`{field}`", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public void Every_outcome_is_named_in_the_json_section()
    {
        string section = Manual.Cli.Section("## JSON output") ?? string.Empty;
        string[] outcomes = [.. typeof(Outcomes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)];

        outcomes.ShouldNotBeEmpty();
        outcomes.Where(outcome => !section.Contains($"`{outcome}`", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    /// <summary>CLI-DOC-02 and A14: a pre-commit hook, and no recipe for GitHub Actions or any other CI service, in the manual or the recipes.</summary>
    [Fact]
    public void The_manual_has_a_hook_and_no_ci_workflow()
    {
        Manual.Cli.Section("## A pre-commit hook").ShouldNotBeNull().ShouldContain("format --check");

        string[] workflow = ["runs-on:", ".github/workflows", "uses: actions/", "azure-pipelines", ".gitlab-ci"];

        Manual.All
            .SelectMany(manual => workflow.Where(marker => manual.Text.Contains(marker, StringComparison.OrdinalIgnoreCase)).Select(marker => $"{manual}: {marker}"))
            .ShouldBeEmpty();
    }

    private static string Name(Command command) => CommandShared.HelpPath(command)["marqora-cli ".Length..];

    private static IEnumerable<string> Names(Option option) => [option.Name, .. option.Aliases];

    private static bool HasHiddenParent(Command command) =>
        command.Parents.OfType<Command>().Any(parent => parent.Hidden || HasHiddenParent(parent));

    /// <summary>The name in backticks, alone or followed by its value: `--find` or `--find &lt;text&gt;`.</summary>
    private static bool Backticked(string text, string name) =>
        Regex.IsMatch(text, "`" + Regex.Escape(name) + "[` ]");
}
