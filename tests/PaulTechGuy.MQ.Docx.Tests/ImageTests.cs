// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Docx.Tests;

/// <summary>
/// Pictures, and what happens when there is not one.
///
/// A markdown document refers to its images by relative path, so these tests write a real PNG
/// next to a real markdown file and export from there - the resolution is half of what is
/// being tested, and a fake would skip it. The PNG is four bytes of pixel data with a proper
/// header, which is all the size reader looks at.
/// </summary>
public class ImageTests : IDisposable
{
    private readonly string _folder;

    public ImageTests()
    {
        _folder = Path.Combine(
            Path.GetTempPath(), "marqora-tests", Guid.NewGuid().ToString("n"));

        Directory.CreateDirectory(_folder);
    }

    [Fact]
    public async Task A_local_image_is_embedded_in_the_file()
    {
        WritePng("picture.png", 400, 300);

        using var exported = await ExportAsync("![A picture](picture.png)\n");

        exported.DocumentXml().ShouldContain("<w:drawing>");
        exported.Skipped.ShouldBeEmpty();
        ImagePartCount(exported.Path).ShouldBe(1);
    }

    /// <summary>
    /// The drawing states its size in two places - the inline extent and the shape's own
    /// extents - and Word draws nothing at all if they disagree.
    /// </summary>
    [Fact]
    public async Task The_two_places_a_drawing_states_its_size_agree()
    {
        WritePng("picture.png", 96, 48);

        using var exported = await ExportAsync("![](picture.png)\n");

        string xml = exported.DocumentXml();

        // 96 pixels at 96 DPI is one inch, which is 914400 EMU.
        xml.ShouldContain("cx=\"914400\"");
        xml.ShouldContain("cy=\"457200\"");
    }

    [Fact]
    public async Task An_image_wider_than_the_page_is_scaled_to_fit_and_keeps_its_shape()
    {
        // 2000 x 1000 is far wider than the 6.5in text column of a Letter page at Word's
        // Normal margins.
        WritePng("wide.png", 2000, 1000);

        using var exported = await ExportAsync("![](wide.png)\n");

        string xml = exported.DocumentXml();

        // 12240 less an inch each side is 9360 twips, which is 5943600 EMU - and half that
        // for the height, because the proportions are kept.
        xml.ShouldContain("cx=\"5943600\"");
        xml.ShouldContain("cy=\"2971800\"");
    }

    [Fact]
    public async Task The_same_picture_used_twice_is_stored_once()
    {
        WritePng("logo.png", 64, 64);

        using var exported = await ExportAsync("![](logo.png)\n\nText.\n\n![](logo.png)\n");

        ImagePartCount(exported.Path).ShouldBe(1);
        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task Alt_text_becomes_the_description_a_screen_reader_reads()
    {
        WritePng("chart.png", 100, 100);

        using var exported = await ExportAsync("![Quarterly revenue](chart.png)\n");

        exported.DocumentXml().ShouldContain("Quarterly revenue");
    }

    /// <summary>
    /// Marqora makes no network calls at runtime, so a remote image cannot be fetched. The
    /// document gets the alt text and the caller is told, rather than the picture silently
    /// going missing.
    /// </summary>
    [Fact]
    public async Task A_remote_image_is_reported_rather_than_fetched()
    {
        using var exported = await ExportAsync("![Remote](https://example.com/x.png)\n");

        exported.Skipped.Count.ShouldBe(1);
        exported.Skipped[0].ShouldContain("Remote");
        exported.PlainText().ShouldContain("Remote");
        exported.DocumentXml().ShouldNotContain("<w:drawing>");
    }

    [Fact]
    public async Task A_missing_image_is_reported_and_leaves_its_alt_text_behind()
    {
        using var exported = await ExportAsync("![The diagram](nowhere.png)\n");

        exported.Skipped.Count.ShouldBe(1);
        exported.PlainText().ShouldContain("The diagram");
    }

    /// <summary>
    /// The same containment rule the preview applies when it serves an image: a path that
    /// climbs out of the document's folder is refused rather than followed.
    /// </summary>
    [Fact]
    public async Task An_image_outside_the_documents_folder_is_refused()
    {
        using var exported = await ExportAsync("![Escape](../secrets.png)\n");

        exported.Skipped.Count.ShouldBe(1);
        exported.DocumentXml().ShouldNotContain("<w:drawing>");
    }

    [Fact]
    public async Task Images_validate()
    {
        WritePng("a.png", 200, 100);

        using var exported = await ExportAsync("# Title\n\n![One](a.png)\n\n| x |\n| --- |\n| ![Two](a.png) |\n");

        exported.ValidationErrors().ShouldBeEmpty();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A held file should not fail the run over cleanup.
        }

        GC.SuppressFinalize(this);
    }

    private async Task<ExportedDocument> ExportAsync(string markdown)
    {
        string source = Path.Combine(_folder, "document.md");

        await File.WriteAllTextAsync(source, markdown, TestContext.Current.CancellationToken);

        return await ExportedDocument.FromAsync(markdown, sourceDocumentPath: source);
    }

    private static int ImagePartCount(string path)
    {
        using WordprocessingDocument file = WordprocessingDocument.Open(path, false);

        return file.MainDocumentPart!.ImageParts.Count();
    }

    /// <summary>
    /// A real PNG: the signature, then an IHDR chunk carrying the dimensions. Nothing reads
    /// past the header, so the pixel data is left out.
    /// </summary>
    private void WritePng(string name, uint width, uint height)
    {
        byte[] png = new byte[33];

        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(png);

        // Chunk length (13) and type "IHDR".
        png[8] = 0; png[9] = 0; png[10] = 0; png[11] = 13;
        png[12] = (byte)'I'; png[13] = (byte)'H'; png[14] = (byte)'D'; png[15] = (byte)'R';

        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20), height);

        png[24] = 8;  // bit depth
        png[25] = 6;  // truecolor with alpha

        File.WriteAllBytes(Path.Combine(_folder, name), png);
    }
}
