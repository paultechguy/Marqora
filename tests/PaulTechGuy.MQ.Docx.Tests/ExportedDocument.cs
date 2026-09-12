// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Docx.Tests;

/// <summary>
/// A throwaway .docx written from markdown, opened and ready to assert on.
///
/// The exporter writes to a path rather than a stream, because that is what the app asks of
/// it, so the tests need somewhere to put files. The folder goes under the temp directory
/// and is deleted on dispose - the same shape as DocumentFolder in Analysis.Tests and
/// FolioWorkspace in Folio.Tests.
/// </summary>
internal sealed class ExportedDocument : IDisposable
{
    private readonly string _root;

    private ExportedDocument(string root, string path)
    {
        _root = root;
        Path = path;
    }

    /// <summary>Where the file was written, for a test that wants to reopen it itself.</summary>
    public string Path { get; }

    /// <summary>What the exporter said it could not carry into the file.</summary>
    public IReadOnlyList<string> Skipped { get; private init; } = [];

    public static async Task<ExportedDocument> FromAsync(
        string markdown,
        DocxExportSetup? setup = null,
        HeadingNumbering headingNumbering = HeadingNumbering.Off,
        string? renderedPreviewHtml = null,
        string? sourceDocumentPath = null,
        Func<string, Task<byte[]?>>? diagramPng = null)
    {
        string root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "marqora-tests", Guid.NewGuid().ToString("n"));

        Directory.CreateDirectory(root);

        string path = System.IO.Path.Combine(root, "export.docx");

        var exporter = new DocxExporter(NullLogger<DocxExporter>.Instance);

        IReadOnlyList<string> skipped = await exporter.WriteAsync(
            path,
            "Test document",
            markdown,
            setup ?? DocxExportSetup.Default,
            headingNumbering,
            sourceDocumentPath,
            renderedPreviewHtml,
            diagramPng).ConfigureAwait(false);

        return new ExportedDocument(root, path) { Skipped = skipped };
    }

    /// <summary>The whole document part as XML, which is what most assertions read.</summary>
    public string DocumentXml()
    {
        using WordprocessingDocument file = WordprocessingDocument.Open(Path, false);

        return file.MainDocumentPart!.Document!.OuterXml;
    }

    /// <summary>The style definitions, for assertions about colors and spacing.</summary>
    public string StylesXml()
    {
        using WordprocessingDocument file = WordprocessingDocument.Open(Path, false);

        return file.MainDocumentPart!.StyleDefinitionsPart?.Styles?.OuterXml ?? string.Empty;
    }

    /// <summary>
    /// The footnotes, or an empty string for a document that has none - which is the answer
    /// a document with no notes should give, since it gets no part at all.
    /// </summary>
    public string FootnotesXml()
    {
        using WordprocessingDocument file = WordprocessingDocument.Open(Path, false);

        return file.MainDocumentPart!.FootnotesPart?.Footnotes?.OuterXml ?? string.Empty;
    }

    /// <summary>
    /// What the footnotes say, with the markup taken out.
    ///
    /// Worth having separately from the XML: a colored code run is several elements rather
    /// than one, so a line of code never appears as a contiguous string in the markup even
    /// though that is exactly how a reader sees it.
    /// </summary>
    public string FootnotesText()
    {
        using WordprocessingDocument file = WordprocessingDocument.Open(Path, false);

        return file.MainDocumentPart!.FootnotesPart?.Footnotes?.InnerText ?? string.Empty;
    }

    /// <summary>
    /// The numbering definitions, or an empty string for a document with no lists in it.
    /// </summary>
    public string NumberingXml()
    {
        using WordprocessingDocument file = WordprocessingDocument.Open(Path, false);

        return file.MainDocumentPart!.NumberingDefinitionsPart?.Numbering?.OuterXml
            ?? string.Empty;
    }

    /// <summary>
    /// Everything the document says, with the markup taken out - for the assertions that are
    /// about what a reader sees rather than about which element carries it.
    /// </summary>
    public string PlainText()
    {
        using WordprocessingDocument file = WordprocessingDocument.Open(Path, false);

        return file.MainDocumentPart!.Document!.Body!.InnerText;
    }

    /// <summary>
    /// Every schema complaint the SDK can find, which should always be none.
    ///
    /// Office2019 rather than the newest: it is the oldest version Marqora claims to write
    /// for, and validating against it catches an element a later Word would tolerate.
    /// </summary>
    /// <remarks>
    /// Strings rather than the SDK's own error type, because the point of this method is what
    /// a failing test prints. ValidationErrorInfo has no useful ToString, so asserting on a
    /// list of them says "expected empty, was 3 items" and leaves you opening the .docx by
    /// hand to find out which three.
    /// </remarks>
    public IReadOnlyList<string> ValidationErrors()
    {
        using WordprocessingDocument file = WordprocessingDocument.Open(Path, false);

        var validator = new OpenXmlValidator(FileFormatVersions.Office2019);

        return
        [
            .. validator.Validate(file)
                .Select(error => $"{error.Path?.XPath}: {error.Description}")
        ];
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A test that left the file open should not fail the run over the cleanup.
        }
    }
}
