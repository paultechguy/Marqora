// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Cli.Inputs;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Inputs;

/// <summary>CLI-IN-01 and CLI-IN-06: names and patterns made into files, once each, in a stable order.</summary>
public sealed class InputSetTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void A_recursive_pattern_finds_every_level_in_ordinal_order()
    {
        using var workspace = new Workspace();
        workspace.Text("docs/b.md", "b");
        workspace.Text("docs/a.md", "a");
        workspace.Text("docs/deep/c.md", "c");
        workspace.Text("docs/notes.txt", "not markdown");

        (IReadOnlyList<InputFile> files, IReadOnlyList<InputProblem> problems) = InputSet.Resolve(["docs/**/*.md"], workspace.Root);

        problems.ShouldBeEmpty();
        files.Select(f => f.Display.Replace('\\', '/')).ShouldBe(["docs/a.md", "docs/b.md", "docs/deep/c.md"]);
    }

    [Fact]
    public void A_file_named_twice_or_matched_twice_is_processed_once()
    {
        using var workspace = new Workspace();
        workspace.Text("a.md", "a");

        (IReadOnlyList<InputFile> files, _) = InputSet.Resolve(["a.md", "./a.md", "*.md", "A.MD"], workspace.Root);

        files.Count.ShouldBe(1);
    }

    [Fact]
    public void Sorted_is_the_default_whatever_order_the_names_came_in()
    {
        using var workspace = new Workspace();
        workspace.Text("b.md", "b");
        workspace.Text("a.md", "a");

        (IReadOnlyList<InputFile> files, _) = InputSet.Resolve(["b.md", "a.md"], workspace.Root);

        files.Select(f => f.Display).ShouldBe(["a.md", "b.md"]);
    }

    /// <summary>A15: folio's order is the reading order, so it is the order the documents were named in.</summary>
    [Fact]
    public void As_named_keeps_the_order_given_with_each_patterns_matches_sorted_where_it_stands()
    {
        using var workspace = new Workspace();
        workspace.Text("z.md", "z");
        workspace.Text("parts/b.md", "b");
        workspace.Text("parts/a.md", "a");
        workspace.Text("intro.md", "intro");

        (IReadOnlyList<InputFile> files, _) = InputSet.Resolve(["z.md", "parts/*.md", "intro.md", "parts/a.md"], workspace.Root, InputOrder.AsNamed);

        files.Select(f => f.Display.Replace('\\', '/')).ShouldBe(["z.md", "parts/a.md", "parts/b.md", "intro.md"]);
    }

    [Fact]
    public async Task A_pattern_matching_nothing_is_an_input_error_naming_it()
    {
        using var workspace = new Workspace();

        CliResult result = await CliRunner.RunAsync(["number", "*.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(3);
        result.Error.ShouldContain("*.md: No file matches the pattern.");
    }

    [Fact]
    public async Task One_missing_file_among_three_the_others_are_processed_and_the_run_exits_3()
    {
        using var workspace = new Workspace();
        workspace.Text("a.md", "# A\n");
        workspace.Text("c.md", "# C\n");

        CliResult result = await CliRunner.RunAsync(["number", "--write", "a.md", "b.md", "c.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(3);
        result.Error.ShouldContain("b.md: No such file.");
        workspace.Read("a.md").ShouldBe("# 1  A\n");
        workspace.Read("c.md").ShouldBe("# 1  C\n");
    }

    [Fact]
    public async Task A_folder_is_refused_with_the_pattern_that_would_find_its_files()
    {
        using var workspace = new Workspace();
        workspace.Text("docs/a.md", "a");

        CliResult result = await CliRunner.RunAsync(["number", "docs"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(3);
        result.Error.ShouldContain("docs/**/*.md");
    }
}
