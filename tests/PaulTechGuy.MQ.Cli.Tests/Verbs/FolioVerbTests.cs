// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.IO.Compression;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Verbs;

/// <summary>
/// CLI-FO-01 (folder, zip): what folio writes. Equality with the app's own Folios is the M0-05
/// baseline's to prove (plan §9.3); these prove the shape, and that nothing is left beside it.
/// </summary>
public sealed class FolioVerbTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_folder_form_carries_the_documents_their_pictures_and_a_manifest()
    {
        using var workspace = new Workspace();
        FolioSet.WriteTo(workspace);

        CliResult result = await CliRunner.RunAsync(["folio", "-o", "Set/", "docs/guide.md", "docs/setup.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());
        result.Error.ShouldContain("shared 2 documents and 1 picture as a folder.");

        File.Exists(workspace.At("Set/guide.md")).ShouldBeTrue();
        File.Exists(workspace.At("Set/setup.md")).ShouldBeTrue();
        File.Exists(workspace.At("Set/folio-manifest.json")).ShouldBeTrue();
        Directory.GetFiles(workspace.At("Set"), "*.png", SearchOption.AllDirectories).Length.ShouldBe(1);
        Leftovers(workspace).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_zip_form_holds_the_same_entries()
    {
        using var workspace = new Workspace();
        FolioSet.WriteTo(workspace);

        CliResult result = await CliRunner.RunAsync(["folio", "-o", "Set.zip", "docs/guide.md", "docs/setup.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());

        using ZipArchive zip = ZipFile.OpenRead(workspace.At("Set.zip"));
        string[] entries = [.. zip.Entries.Select(e => e.FullName)];

        entries.ShouldContain("guide.md");
        entries.ShouldContain("setup.md");
        entries.ShouldContain("folio-manifest.json");
        entries.Count(e => e.EndsWith(".png", StringComparison.Ordinal)).ShouldBe(1);
        Leftovers(workspace).ShouldBeEmpty();
    }

    /// <summary>A15: the documents are in the order they were named, as the app keeps the order they were ticked.</summary>
    [Fact]
    public async Task The_documents_keep_the_order_they_were_named_in()
    {
        using var workspace = new Workspace();
        FolioSet.WriteTo(workspace);

        CliResult preflight = await CliRunner.RunAsync(["folio", "-o", "Set.zip", "--dry-run", "docs/setup.md", "docs/guide.md"], currentDirectory: workspace.Root, cancellationToken: Ct);
        CliResult result = await CliRunner.RunAsync(["folio", "-o", "Set.zip", "docs/setup.md", "docs/guide.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());
        preflight.Out.IndexOf("  setup.md", StringComparison.Ordinal).ShouldBeLessThan(preflight.Out.IndexOf("  guide.md", StringComparison.Ordinal));

        using ZipArchive zip = ZipFile.OpenRead(workspace.At("Set.zip"));
        string[] documents = [.. zip.Entries.Select(e => e.FullName).Where(e => e.EndsWith(".md", StringComparison.Ordinal))];

        documents.ShouldBe(["setup.md", "guide.md"]);
    }

    [Fact]
    public async Task A_named_input_that_is_missing_makes_no_folio()
    {
        using var workspace = new Workspace();
        FolioSet.WriteTo(workspace);

        CliResult result = await CliRunner.RunAsync(["folio", "-o", "Set.zip", "docs/guide.md", "docs/absent.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(3);
        File.Exists(workspace.At("Set.zip")).ShouldBeFalse();
    }

    /// <summary>The sibling a Folio is written to before it is moved into place.</summary>
    private static string[] Leftovers(Workspace workspace) =>
        [.. Directory.GetFileSystemEntries(workspace.Root).Select(Path.GetFileName).Where(n => n!.EndsWith(".tmp", StringComparison.Ordinal) || n.EndsWith(".old", StringComparison.Ordinal))!];
}
