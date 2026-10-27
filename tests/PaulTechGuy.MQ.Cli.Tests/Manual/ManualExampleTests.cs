// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Manual;

/// <summary>
/// CLI-DOC-01: every example in the manual and the recipes runs and gives the output it shows. Each
/// runs in a fresh copy of docs/cli-tour, the folder the tour tells the reader to copy, so an
/// example that writes cannot change what the next one sees.
/// </summary>
public sealed class ManualExampleTests
{
    public static TheoryData<string> Documents => [.. Manual.All.Select(m => m.RelativePath)];

    public static TheoryData<string, int> ExampleLines =>
    [
        .. Manual.All.SelectMany(m => m.Examples().Where(e => !e.NoOutput).Select(e => (m.RelativePath, e.Command.Line))),
    ];

    [Theory]
    [MemberData(nameof(Documents))]
    public void Every_example_shows_its_output_or_says_it_shows_none(string document)
    {
        string[] untagged = [.. Manual.Named(document).Examples()
            .Where(e => !e.NoOutput && e.Output is null)
            .Select(e => $"line {e.Command.Line}: {e.Command.Lines[0]}")];

        untagged.ShouldBeEmpty("An example needs a ```text block after it, or a cli-example: no-output comment before it.");
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void A_shown_example_is_one_command(string document)
    {
        string[] several = [.. Manual.Named(document).Examples()
            .Where(e => !e.NoOutput && e.Command.Lines.Count != 1)
            .Select(e => $"line {e.Command.Line}")];

        several.ShouldBeEmpty("An example with output runs one command; list several under cli-example: no-output.");
    }

    /// <summary>A tag before anything but an example is a tag that tests nothing: most likely the example moved.</summary>
    [Theory]
    [MemberData(nameof(Documents))]
    public void Every_tag_is_on_an_example(string document)
    {
        Manual manual = Manual.Named(document);
        string[] stray = [.. manual.Blocks()
            .Where(b => manual.TagBefore(b) is not null && !Manual.IsExample(b))
            .Select(b => $"line {b.Line}: {manual.TagBefore(b)}")];

        stray.ShouldBeEmpty();
    }

    [Fact]
    public void There_are_examples_to_run()
    {
        Manual.Cli.Examples().Count(e => !e.NoOutput).ShouldBeGreaterThan(5);
    }

    [Theory]
    [MemberData(nameof(ExampleLines))]
    public async Task The_example_gives_the_output_shown(string document, int line)
    {
        ManualExample example = Manual.Named(document).Examples().Single(e => e.Command.Line == line);
        string[] args = Manual.Arguments(example.Command.Lines[0])[1..];

        using var workspace = new Workspace();
        CopyTour(workspace);

        CliResult result = await CliRunner.RunAsync(args, currentDirectory: workspace.Root, cancellationToken: TestContext.Current.CancellationToken);

        string shown = Manual.Normalize(result.Out + result.Error);

        shown.ShouldBe(Manual.Normalize(example.Output!.Text), $"{document} line {line}: {example.Command.Lines[0]}");
        result.ExitCode.ShouldBe(example.ExitCode, result.ToString());
    }

    private static void CopyTour(Workspace workspace)
    {
        string tour = Repo.At(Manual.TourFolder);

        foreach (string file in Directory.EnumerateFiles(tour, "*", SearchOption.AllDirectories))
        {
            workspace.Copy(Path.GetRelativePath(Repo.Root, file), Path.GetRelativePath(tour, file));
        }
    }
}
