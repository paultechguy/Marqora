// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

/// <summary>
/// The one lookup every export asks for a picture: inside the document's folder and never out
/// of it, or from the pictures a review page carried and never from disk.
/// </summary>
public sealed class DocumentImagesTests : IDisposable
{
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 7];

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "marqora-tests", Guid.NewGuid().ToString("n"));

    public DocumentImagesTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Document => Path.Combine(_folder, "notes.md");

    private string Write(string relative)
    {
        string full = Path.GetFullPath(Path.Combine(_folder, relative));

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, Png);

        return full;
    }

    // ---- the folder

    [Fact]
    public void A_picture_beside_the_document_is_found_by_path()
    {
        string full = Write("img/a.png");

        DocumentImage image = DocumentImages.FromDocument(Document).Find("img/a.png");

        image.Status.ShouldBe(DocumentImageStatus.Found);
        image.FilePath.ShouldBe(full);
        image.Asset.ShouldBeNull();
    }

    [Fact]
    public void A_reference_that_climbs_out_is_never_followed()
    {
        DocumentImages.FromDocument(Document).Find("../outside.png").Status.ShouldBe(DocumentImageStatus.Outside);
    }

    [Fact]
    public void A_missing_picture_is_not_found()
    {
        DocumentImages.FromDocument(Document).Find("img/none.png").Status.ShouldBe(DocumentImageStatus.NotFound);
    }

    /// <summary>A saved document whose folder has gone still has a source: its pictures are missing, not unsaved.</summary>
    [Fact]
    public void A_document_whose_folder_has_gone_reports_not_found()
    {
        string gone = Path.Combine(_folder, "gone", "notes.md");

        DocumentImages images = DocumentImages.FromDocument(gone);

        images.HasSource.ShouldBeTrue();
        images.Find("a.png").Status.ShouldBe(DocumentImageStatus.NotFound);
    }

    [Fact]
    public void A_percent_encoded_space_is_decoded()
    {
        Write("img/a b.png");

        DocumentImages.FromDocument(Document).Find("img/a%20b.png").Status.ShouldBe(DocumentImageStatus.Found);
    }

    /// <summary>A plus is a plus in a URL, so a file named with one is found as it is.</summary>
    [Fact]
    public void A_plus_in_a_file_name_is_kept()
    {
        string full = Write("img/a+b.png");

        DocumentImages.FromDocument(Document).Find("img/a+b.png").FilePath.ShouldBe(full);
    }

    /// <summary>The HTML exports used to read '+' as a space, and a document that relied on that keeps working.</summary>
    [Fact]
    public void A_plus_standing_for_a_space_is_found_when_nothing_else_is()
    {
        string full = Write("img/a b.png");

        DocumentImage image = DocumentImages.FromDocument(Document).Find("img/a+b.png");

        image.Status.ShouldBe(DocumentImageStatus.Found);
        image.FilePath.ShouldBe(full);
    }

    [Fact]
    public void An_unsaved_document_has_nowhere_to_look()
    {
        DocumentImages.FromDocument(null).ShouldBeSameAs(DocumentImages.None);
        DocumentImages.None.HasSource.ShouldBeFalse();
        DocumentImages.None.Find("a.png").Status.ShouldBe(DocumentImageStatus.NoSource);
    }

    // ---- a page

    [Fact]
    public void A_page_answers_from_its_pictures_however_the_path_is_written()
    {
        var asset = new ReviewAsset("image/png", Png);
        DocumentImages images = DocumentImages.FromPage(new Dictionary<string, ReviewAsset>(StringComparer.OrdinalIgnoreCase)
        {
            ["img/a b.png"] = asset,
        });

        images.Find("./img/a%20b.png").Asset.ShouldBeSameAs(asset);
        images.Find("IMG/A B.PNG").Status.ShouldBe(DocumentImageStatus.Found);
        images.Find("img/other.png").Status.ShouldBe(DocumentImageStatus.NotFound);
        images.IsPage.ShouldBeTrue();
    }

    /// <summary>A resumed review is never shown an unrelated file that happens to sit where its picture once did.</summary>
    [Fact]
    public void A_page_never_looks_on_disk()
    {
        string onDisk = Write("img/a.png");

        DocumentImage image = DocumentImages.FromPage(new Dictionary<string, ReviewAsset>()).Find(onDisk);

        image.Status.ShouldBe(DocumentImageStatus.NotFound);
        image.FilePath.ShouldBeNull();
    }
}
