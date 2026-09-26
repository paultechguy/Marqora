// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Folio.Tests;

/// <summary>
/// A review resumed from its page, in a Folio: set down as a stand-in holding only what the page
/// carried, and planned so that its text - which came from a file somebody sent - cannot gather
/// anything else from the reader's disk.
/// </summary>
public sealed class FolioStandInTests : IDisposable
{
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 5];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "marqora-standin-tests", Guid.NewGuid().ToString("n"));

    public FolioStandInTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temp folder that outlives the test is not a failed test.
        }
    }

    private static Dictionary<string, ReviewAsset> Assets(params (string Key, byte[] Bytes)[] assets) =>
        assets.ToDictionary(a => a.Key, a => new ReviewAsset("image/png", a.Bytes), StringComparer.OrdinalIgnoreCase);

    // ---- the planner, for a contained-only source

    [Fact]
    public void An_absolute_path_in_a_resumed_review_is_not_collected()
    {
        using var workspace = new FolioWorkspace();
        string secret = workspace.File("elsewhere/secret.png", "private");

        workspace.StandIn("stand-in/1/notes.md", $"![]({secret.Replace('\\', '/')})\n");

        FolioPlan plan = workspace.Plan();

        plan.Assets.ShouldBeEmpty();
        plan.Warnings.Single().Kind.ShouldBe(FolioWarningKind.MissingImage);
        plan.Warnings.Single().Message.ShouldContain("not in the review page");
    }

    [Fact]
    public void A_climb_out_of_a_resumed_review_is_not_collected()
    {
        using var workspace = new FolioWorkspace();
        workspace.File("elsewhere/secret.png", "private");

        workspace.StandIn("stand-in/1/notes.md", "![](../../elsewhere/secret.png)\n");

        workspace.Plan().Assets.ShouldBeEmpty();
    }

    [Fact]
    public void A_path_into_another_stand_in_is_not_collected()
    {
        using var workspace = new FolioWorkspace();
        workspace.File("stand-in/2/theirs.png", "another review");

        workspace.StandIn("stand-in/1/notes.md", "![](../2/theirs.png)\n");

        workspace.Plan().Assets.ShouldBeEmpty();
    }

    [Fact]
    public void A_picture_the_page_carried_is_collected_as_written()
    {
        using var workspace = new FolioWorkspace();
        workspace.File("stand-in/1/img/a+b.png", "carried");

        workspace.StandIn("stand-in/1/notes.md", "![](img/a+b.png)\n");

        FolioPlan plan = workspace.Plan();

        plan.Warnings.ShouldBeEmpty();
        plan.Assets.Single().EntryName.ShouldBe("img/a+b.png");
    }

    /// <summary>The same reference from a saved document still reaches outside its folder, as it always has.</summary>
    [Fact]
    public void An_ordinary_document_still_gathers_from_elsewhere()
    {
        using var workspace = new FolioWorkspace();
        string picture = workspace.File("elsewhere/logo.png", "the-logo");

        workspace.Document("docs/guide.md", $"![]({picture.Replace('\\', '/')})\n");

        workspace.Plan().Assets.Count.ShouldBe(1);
    }

    // ---- writing the stand-in

    [Fact]
    public void The_pictures_are_written_where_the_document_names_them()
    {
        FolioStandInResult result = FolioStandIn.Write(_root, "notes.md", Assets(("img/a b.png", Png), ("img/a+b.png", Png)));

        string folder = Path.GetDirectoryName(result.DocumentPath)!;

        Path.GetFileName(result.DocumentPath).ShouldBe("notes.md");
        File.Exists(result.DocumentPath).ShouldBeFalse();
        File.ReadAllBytes(Path.Combine(folder, "img", "a b.png")).ShouldBe(Png);
        File.Exists(Path.Combine(folder, "img", "a+b.png")).ShouldBeTrue();
        result.Skipped.ShouldBeEmpty();
    }

    /// <summary>A page cannot leave something that merely starts the way a PNG does under another name.</summary>
    [Theory]
    [InlineData("evil.hta")]
    [InlineData("desktop.ini")]
    [InlineData("x.url")]
    [InlineData("picture.gif")]
    public void A_picture_whose_name_is_not_what_its_bytes_are_is_not_written(string key)
    {
        FolioStandInResult result = FolioStandIn.Write(_root, "notes.md", Assets((key, Png)));

        result.Skipped.ShouldBe([key]);
        File.Exists(Path.Combine(Path.GetDirectoryName(result.DocumentPath)!, key)).ShouldBeFalse();
    }

    [Fact]
    public void A_jpeg_may_be_named_either_way()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1];

        FolioStandIn.Write(_root, "notes.md", Assets(("a.jpeg", jpeg), ("b.jpg", jpeg))).Skipped.ShouldBeEmpty();
    }

    /// <summary>One key naming a file where another needs a folder: the one that cannot be written is skipped, not the share.</summary>
    [Fact]
    public void A_picture_that_cannot_be_written_is_skipped_alone()
    {
        byte[] noExtension = Png;

        FolioStandInResult result = FolioStandIn.Write(_root, "notes.md", Assets(("img.png", noExtension), ("img.png/a.png", Png)));

        result.Skipped.ShouldBe(["img.png/a.png"]);
    }

    [Fact]
    public void Nothing_is_written_outside_the_root()
    {
        FolioStandInResult result = FolioStandIn.Write(_root, "notes.md", Assets(("../../escaped.png", Png), ($"{new string('a', 300)}.png", Png)));

        result.Skipped.Count.ShouldBe(2);
        Directory.EnumerateFiles(Path.GetDirectoryName(_root)!, "escaped.png", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("CON.md", "review.md")]
    [InlineData("nul", "review.md")]
    [InlineData("notes.", "notes")]
    [InlineData("CON", "review.md")]
    [InlineData(".md", "review.md")]
    [InlineData("notes.md", "notes.md")]
    public void A_file_name_Windows_would_change_is_replaced(string given, string expected)
    {
        FolioStandIn.SafeName(given).ShouldBe(expected);
    }

    [Fact]
    public void Each_stand_in_has_a_folder_of_its_own()
    {
        string first = FolioStandIn.Write(_root, "notes.md", Assets()).DocumentPath;
        string second = FolioStandIn.Write(_root, "notes.md", Assets()).DocumentPath;

        Path.GetDirectoryName(first).ShouldNotBe(Path.GetDirectoryName(second));
    }
}
