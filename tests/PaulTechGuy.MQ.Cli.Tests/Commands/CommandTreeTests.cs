// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using System.Xml.Linq;
using PaulTechGuy.MQ.Cli.Commands;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Commands;

/// <summary>CLI-CMD-01: the verb shape, and the library it is built on, pinned where it should be.</summary>
public sealed class CommandTreeTests
{
    /// <summary>Every verb in the requirements' §3.1, by its canonical name.</summary>
    public static TheoryData<string> Verbs =>
    [
        "format", "number", "unnumber", "replace", "renumber-lists", "diagram-layout",
        "check", "toc", "outline", "unpack", "folio", "version",
        "themes list", "export html", "export pdf", "export word",
    ];

    [Theory]
    [MemberData(nameof(Verbs))]
    public async Task Every_verb_is_reachable_by_its_canonical_name(string verb)
    {
        CliResult result = await CliRunner.RunAsync([.. verb.Split(' '), "--help"], cancellationToken: TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(0, result.ToString());
        result.Out.ShouldContain($"marqora-cli {verb}");
    }

    [Fact]
    public void The_tree_holds_exactly_the_verbs_of_the_requirements()
    {
        CommandTree tree = CommandTree.Create(TestEnvironment.Discarding());

        string[] verbs = [.. tree.AllCommands.Where(c => c.Subcommands.Count == 0).Select(CommandShared.HelpPath).Order(StringComparer.Ordinal)];

        verbs.ShouldBe(
        [
            "marqora-cli check",
            "marqora-cli diagram-layout",
            "marqora-cli export html",
            "marqora-cli export pdf",
            "marqora-cli export word",
            "marqora-cli folio",
            "marqora-cli format",
            "marqora-cli number",
            "marqora-cli outline",
            "marqora-cli renumber-lists",
            "marqora-cli replace",
            "marqora-cli themes list",
            "marqora-cli toc",
            "marqora-cli unnumber",
            "marqora-cli unpack",
            "marqora-cli version",
        ]);
    }

    /// <summary>The version is set once, in Directory.Packages.props, and held to 2.0.x.</summary>
    [Fact]
    public void System_CommandLine_is_pinned_centrally_to_2_0()
    {
        XDocument packages = XDocument.Load(Repo.At("Directory.Packages.props"));
        string? version = packages.Descendants("PackageVersion")
            .SingleOrDefault(p => (string?)p.Attribute("Include") == "System.CommandLine")?
            .Attribute("Version")?.Value;

        version.ShouldNotBeNull();
        version.ShouldStartWith("2.0.");

        XDocument project = XDocument.Load(Repo.At("src", "PaulTechGuy.MQ.Cli", "PaulTechGuy.MQ.Cli.csproj"));

        project.Descendants("PackageReference")
            .Single(p => (string?)p.Attribute("Include") == "System.CommandLine")
            .Attribute("Version").ShouldBeNull();
    }

    /// <summary>The exports were kept out of help until they matched the app's files; since M2-14 they are listed.</summary>
    [Fact]
    public async Task The_export_verbs_are_in_help()
    {
        CliResult result = await CliRunner.RunAsync(["--help"], cancellationToken: TestContext.Current.CancellationToken);

        // A line of the command list, not the word: the root's description mentions exporting.
        result.Out.ReplaceLineEndings("\n").Split('\n')
            .ShouldContain(line => line.TrimStart().StartsWith("export ", StringComparison.Ordinal));

        CliResult export = await CliRunner.RunAsync(["export", "--help"], cancellationToken: TestContext.Current.CancellationToken);

        foreach (string format in new[] { "html", "pdf", "word" })
        {
            export.Out.ReplaceLineEndings("\n").Split('\n')
                .ShouldContain(line => line.TrimStart().StartsWith(format + " ", StringComparison.Ordinal), $"export {format}");
        }
    }
}
