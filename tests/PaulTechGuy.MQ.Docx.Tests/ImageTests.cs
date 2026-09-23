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
    /// Word export asks nobody whether to fetch, so a remote image is not fetched here. The
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

    /// <summary>
    /// The picture itself is never fetched, but the address is just text - sending the reader
    /// to it costs Marqora no network call, so the alt-text placeholder is a real hyperlink
    /// rather than inert text.
    /// </summary>
    [Fact]
    public async Task A_remote_image_placeholder_links_to_the_original_address()
    {
        using var exported = await ExportAsync("![Remote](https://example.com/x.png)\n");

        exported.DocumentXml().ShouldContain("<w:hyperlink");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// "[![alt](img)](page)" - a remote image standing in as the label of an outer link. The
    /// outer Hyperlink is already being built one level up (see WriteInto), and w:hyperlink
    /// cannot nest inside w:hyperlink, so the placeholder for the inner image has to fall back
    /// to a plain run even though it would otherwise qualify for one of its own.
    /// </summary>
    [Fact]
    public async Task A_remote_image_labelling_an_outer_link_does_not_nest_hyperlinks()
    {
        using var exported = await ExportAsync(
            "[![Remote](https://example.com/x.png)](https://example.com/page)\n");

        // OpenXmlValidator does not flag w:hyperlink nested inside w:hyperlink - Word does, with
        // an "unreadable content" repair prompt - so the count is the real check here, not
        // ValidationErrors.
        exported.DocumentXml().Split("<w:hyperlink").Length.ShouldBe(2);
        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_missing_image_is_reported_and_leaves_its_alt_text_behind()
    {
        using var exported = await ExportAsync("![The diagram](nowhere.png)\n");

        exported.Skipped.Count.ShouldBe(1);
        exported.PlainText().ShouldContain("The diagram");
    }

    /// <summary>
    /// Markdig decodes an entity into its own inline rather than a literal, and the alt text
    /// used to be read from the literals alone - so "Q&amp;amp;A" stood in for the picture as
    /// "[QA]".
    /// </summary>
    [Fact]
    public async Task An_entity_in_the_alt_text_survives_into_the_placeholder()
    {
        using var exported = await ExportAsync("![Q&amp;A&nbsp;chart](nowhere.png)\n");

        exported.PlainText().ShouldContain("[Q&A chart]");
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

    /// <summary>
    /// A picture written into the markdown itself rather than beside it.
    ///
    /// It used to fall through to the relative-path branch, where no file of that name exists,
    /// and be reported as *not found* - a wrong reason as well as a missing picture. Nothing
    /// was missing: the bytes were in the document, and embedding them needs no network call,
    /// which is the whole reason a remote image cannot be embedded and this one can.
    /// </summary>
    [Fact]
    public async Task A_picture_written_into_the_markdown_is_embedded()
    {
        using var exported = await ExportAsync($"![Inline picture]({DataUri(96, 48)})\n");

        string xml = exported.DocumentXml();

        xml.ShouldContain("<w:drawing>");

        // Read out of the decoded bytes: 96 pixels at 96 DPI is an inch, which is 914400 EMU.
        xml.ShouldContain("cx=\"914400\"");
        xml.ShouldContain("cy=\"457200\"");
        exported.Skipped.ShouldBeEmpty();
        ImagePartCount(exported.Path).ShouldBe(1);
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// Keyed on the bytes, not on the URL: one icon pasted through a document is one part. The
    /// middle one is wrapped in a link, which is the shape that made three separate lines of
    /// the same picture in the export report.
    /// </summary>
    [Fact]
    public async Task The_same_embedded_picture_three_times_is_stored_once()
    {
        string uri = DataUri(96, 48);

        using var exported = await ExportAsync(
            $"![One]({uri})\n\n[![Two]({uri})](https://example.com)\n\n![Three]({uri})\n");

        ImagePartCount(exported.Path).ShouldBe(1);
        exported.Skipped.ShouldBeEmpty();
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// An embedded picture does not need a folder to be found in, so it is answered before the
    /// document is asked where it lives.
    /// </summary>
    [Fact]
    public async Task An_embedded_picture_survives_a_document_that_has_never_been_saved()
    {
        using var exported = await ExportedDocument.FromAsync(
            $"![Inline picture]({DataUri(96, 48)})\n");

        ImagePartCount(exported.Path).ShouldBe(1);
        exported.Skipped.ShouldBeEmpty();
    }

    /// <summary>
    /// Word has drawn SVG since 2016 but wants a raster to fall back on, and producing one
    /// would mean rendering it. What matters is that the reason is the true one: it is a
    /// picture Word will not draw, not a file somebody has moved.
    /// </summary>
    [Fact]
    public async Task An_embedded_picture_Word_cannot_draw_says_so_rather_than_not_found()
    {
        using var exported = await ExportAsync(
            "![Blue circle](data:image/svg+xml;base64,PHN2Zy8+)\n");

        exported.Skipped.Count.ShouldBe(1);
        exported.Skipped[0].ShouldContain("not a picture Word can show");
        exported.Skipped[0].ShouldNotContain("not found");
        exported.PlainText().ShouldContain("Blue circle");
    }

    /// <summary>
    /// The export report is read by a person in a dialog, and a data URI is thousands of
    /// characters of base64. Without a label of its own, a picture with no alt text would
    /// paste the entire thing into it.
    /// </summary>
    [Fact]
    public async Task An_embedded_picture_that_cannot_be_shown_is_named_rather_than_quoted()
    {
        using var exported = await ExportAsync("![](data:image/svg+xml;base64,PHN2Zy8+)\n");

        exported.Skipped.Count.ShouldBe(1);
        exported.Skipped[0].ShouldContain("an embedded image");
        exported.Skipped[0].ShouldNotContain("PHN2Zy8+");
    }

    /// <summary>
    /// Where, not only what.
    ///
    /// Being told that a picture is missing is half of it; finding it in a document of two
    /// thousand lines is the other half, and the report used to leave that half to the reader.
    /// Counted from one, the way an editor counts.
    /// </summary>
    [Fact]
    public async Task A_missing_picture_says_which_line_it_was_on()
    {
        using var exported = await ExportAsync(
            "First line.\n\nSecond line.\n\n![A picture](no-such-file.png)\n");

        exported.Issues.Count.ShouldBe(1);
        exported.Issues[0].Line.ShouldBe(5);
        exported.Issues[0].Problem.ShouldBe("Not found");
        exported.Issues[0].Item.ShouldBe("A picture");
    }

    /// <summary>
    /// One row per place, not per sentence. Three copies of one picture are three things to
    /// fix, and a reader working down the list has to be taken to each of them.
    /// </summary>
    [Fact]
    public async Task The_same_missing_picture_twice_is_two_rows_at_two_lines()
    {
        using var exported = await ExportAsync(
            "![Gone](no-such-file.png)\n\n![Gone](no-such-file.png)\n");

        exported.Issues.Count.ShouldBe(2);
        exported.Issues[0].Line.ShouldBe(1);
        exported.Issues[1].Line.ShouldBe(3);
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
    private static byte[] PngBytes(uint width, uint height)
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

        return png;
    }

    private void WritePng(string name, uint width, uint height) =>
        File.WriteAllBytes(Path.Combine(_folder, name), PngBytes(width, height));

    /// <summary>The same picture, written into the markdown instead of beside it.</summary>
    private static string DataUri(uint width, uint height) =>
        "data:image/png;base64," + Convert.ToBase64String(PngBytes(width, height));
}
