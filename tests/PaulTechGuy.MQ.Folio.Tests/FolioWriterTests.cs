// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Folio.Tests;

/// <summary>
/// The writer, against a plan built from real documents: what lands on disk, and what refuses
/// to be written over.
/// </summary>
public sealed class FolioWriterTests
{
    private static readonly FolioWriter Writer = new(NullLogger<FolioWriter>.Instance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_folder_form_writes_documents_images_and_a_manifest()
    {
        using var workspace = new FolioWorkspace();

        workspace.File("docs/logo.png", "the-logo");
        workspace.File("elsewhere/chart.png", "a-chart");
        workspace.Document("docs/guide.md", "![](logo.png) ![](../elsewhere/chart.png)");

        FolioPlan plan = workspace.Plan();
        string target = Path.Combine(workspace.Elsewhere, "out", "handbook");

        await Writer.WriteFolderAsync(plan, FolioManifest.For(plan, "1.0", "TESTBOX"), target, Ct);

        File.Exists(Path.Combine(target, "guide.md")).ShouldBeTrue();
        File.Exists(Path.Combine(target, "logo.png")).ShouldBeTrue();
        File.Exists(Path.Combine(target, "media", "chart.png")).ShouldBeTrue();
        File.Exists(Path.Combine(target, FolioManifest.FileName)).ShouldBeTrue();

        // The repointed reference is what actually reaches the disk, not the original.
        string written = await File.ReadAllTextAsync(Path.Combine(target, "guide.md"), Ct);

        written.ShouldBe("![](logo.png) ![](media/chart.png)");
    }

    [Fact]
    public async Task The_folder_form_refuses_to_write_into_somewhere_that_holds_work()
    {
        using var workspace = new FolioWorkspace();

        workspace.Document("docs/guide.md", "hello");

        FolioPlan plan = workspace.Plan();
        string target = Path.Combine(workspace.Elsewhere, "busy");
        string bystander = Path.Combine(target, "important.md");

        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(bystander, "do not lose me", Ct);

        await Should.ThrowAsync<IOException>(
            Writer.WriteFolderAsync(plan, FolioManifest.For(plan, "1.0", "TESTBOX"), target, Ct));

        string survivor = await File.ReadAllTextAsync(bystander, Ct);

        survivor.ShouldBe("do not lose me");
    }

    [Fact]
    public async Task The_zip_form_holds_every_entry_the_plan_named()
    {
        using var workspace = new FolioWorkspace();

        workspace.File("docs/images/logo.png", "the-logo");
        workspace.File("elsewhere/chart.png", "a-chart");
        workspace.Document("docs/guide.md", "![](images/logo.png) ![](../elsewhere/chart.png)");
        workspace.Document("docs/setup.md", "Setup.");

        FolioPlan plan = workspace.Plan();
        string zipPath = Path.Combine(workspace.Elsewhere, "handbook.zip");

        await Writer.WriteZipAsync(plan, FolioManifest.For(plan, "1.0", "TESTBOX"), zipPath, Ct);

        using ZipArchive archive = ZipFile.OpenRead(zipPath);

        archive.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ShouldBe(
        [
            "folio-manifest.json",
            "guide.md",
            "images/logo.png",
            "media/chart.png",
            "setup.md",
        ]);
    }

    /// <summary>
    /// A Folio is a file that gets emailed, and an absolute path carries the author's user name
    /// and how they organize their disk. The entry names are enough to unpack from.
    /// </summary>
    [Fact]
    public async Task The_manifest_records_no_absolute_paths()
    {
        using var workspace = new FolioWorkspace();

        workspace.File("elsewhere/chart.png", "a-chart");
        workspace.Document("docs/guide.md", "![](../elsewhere/chart.png)");

        FolioPlan plan = workspace.Plan();
        string target = Path.Combine(workspace.Elsewhere, "out");

        await Writer.WriteFolderAsync(plan, FolioManifest.For(plan, "1.0", "TESTBOX"), target, Ct);

        string manifest = await File.ReadAllTextAsync(
            Path.Combine(target, FolioManifest.FileName), Ct);

        manifest.ShouldNotContain(workspace.Docs);
        manifest.ShouldNotContain(workspace.Elsewhere);
        manifest.ShouldContain("marqora-folio");
        manifest.ShouldContain("media/chart.png");
    }

    [Fact]
    public void The_suggested_name_follows_the_folder_when_several_documents_share_one()
    {
        using var workspace = new FolioWorkspace();

        workspace.Document("docs/one.md", "one");
        workspace.Document("docs/two.md", "two");

        FolioManifest.SuggestedName(workspace.Plan(), DateTimeOffset.Now).ShouldBe("docs");
    }

    [Fact]
    public void The_suggested_name_follows_the_document_when_there_is_only_one()
    {
        using var workspace = new FolioWorkspace();

        workspace.Document("docs/team-handbook.md", "hello");

        FolioManifest.SuggestedName(workspace.Plan(), DateTimeOffset.Now).ShouldBe("team-handbook");
    }
}
