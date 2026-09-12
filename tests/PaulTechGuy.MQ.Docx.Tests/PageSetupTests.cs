// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Docx.Tests;

/// <summary>
/// Page setup reaching the file, and the file being one Word will open without complaint.
///
/// The A4 test is here because the obvious implementation gets it wrong. Marqora states A4
/// as 8.27 x 11.69 inches, and converting that to twips gives 11909 - close enough to look
/// right in a diff, and not A4 as far as Word is concerned. It shows "Custom size" in Page
/// Setup and the printer then disagrees with the document about what is in the tray.
/// </summary>
public class PageSetupTests
{
    [Fact]
    public async Task An_exported_document_has_no_schema_errors()
    {
        using var exported = await ExportedDocument.FromAsync("# Hello\n\nSome prose.\n");

        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task Letter_portrait_is_the_size_Word_expects()
    {
        using var exported = await ExportedDocument.FromAsync("Text.");

        string xml = exported.DocumentXml();

        xml.ShouldContain("w:w=\"12240\"");
        xml.ShouldContain("w:h=\"15840\"");
    }

    [Fact]
    public async Task A4_is_the_millimetre_definition_rather_than_the_rounded_inches()
    {
        using var exported = await ExportedDocument.FromAsync(
            "Text.",
            new DocxExportSetup { Paper = PaperSize.A4 });

        string xml = exported.DocumentXml();

        // 210mm x 297mm exactly. Converting 8.27in would give 11909 and Word would call it
        // a custom size.
        xml.ShouldContain("w:w=\"11906\"");
        xml.ShouldContain("w:h=\"16838\"");
        xml.ShouldNotContain("w:w=\"11909\"");
    }

    [Fact]
    public async Task Landscape_swaps_the_edges()
    {
        using var exported = await ExportedDocument.FromAsync(
            "Text.",
            new DocxExportSetup { Orientation = PageOrientation.Landscape });

        string xml = exported.DocumentXml();

        xml.ShouldContain("w:w=\"15840\"");
        xml.ShouldContain("w:h=\"12240\"");
    }

    [Fact]
    public async Task Margins_reach_the_page_in_twips()
    {
        using var exported = await ExportedDocument.FromAsync(
            "Text.",
            new DocxExportSetup { Margin = PageMargin.Wide });

        // One inch on every side.
        exported.DocumentXml().ShouldContain("w:top=\"1440\"");
    }

    [Fact]
    public async Task A_document_with_no_margin_keeps_the_header_distance_inside_it()
    {
        using var exported = await ExportedDocument.FromAsync(
            "Text.",
            new DocxExportSetup { Margin = PageMargin.None });

        // Nothing to clamp to, so the header cannot sit anywhere; it must not become a
        // distance larger than the margin that contains it.
        exported.DocumentXml().ShouldContain("w:header=\"0\"");
        exported.ValidationErrors().ShouldBeEmpty();
    }
}
