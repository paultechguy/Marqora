// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Abstractions.Rendering;

/// <summary>
/// A document to render for export.
/// </summary>
/// <param name="Markdown">The document's text.</param>
/// <param name="DocumentPath">
/// Where it is on disk, which its relative pictures resolve against; null for a document that
/// has not been saved.
/// </param>
/// <param name="Numbering">How its headings are numbered, as the document has it.</param>
/// <param name="ColorThemeId">The color theme it leaves in, always in its light palette.</param>
public sealed record RenderRequest(string Markdown, string? DocumentPath, HeadingNumbering Numbering, string ColorThemeId);

/// <summary>
/// A document's finished markup: diagrams drawn as inline SVG, math laid out, code highlighted.
/// </summary>
public sealed record RenderedDocument(string Html);

/// <summary>
/// What an export asks of a renderer, carved out of <see cref="IPreviewHost"/> so that the app
/// and marqora-cli export through the same calls (CLI-ARC-04, docs/CLI-Implementation-Plan.md
/// F20). Two implementations:
///
/// <list type="bullet">
///   <item>
///     The live preview, which answers for the document on screen. That is the one the app
///     exports, so it already holds what <see cref="RenderAsync"/> asks for: the request's text
///     and theme are what it is showing, and are not rendered a second time.
///   </item>
///   <item>
///     The headless host, the same shell in a WebView2 nobody sees, which opens the request's
///     document as its one tab and keeps it there until the next.
///   </item>
/// </list>
///
/// Diagrams, math and the PDF are asked of the document last rendered. Nothing here degrades:
/// a request the shell did not answer comes back as that, and each caller decides what an
/// unanswered request means for its file.
/// </summary>
public interface IDocumentRenderer
{
    /// <summary>Renders the document and answers with its finished markup.</summary>
    Task<BridgeOutcome<RenderedDocument>> RenderAsync(RenderRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// One diagram of the rendered document as PNG bytes, its light drawing tightly cropped, or
    /// null when it could not be produced.
    /// </summary>
    Task<byte[]?> DiagramPngAsync(string hash, CancellationToken cancellationToken = default);

    /// <summary>
    /// One diagram's light drawing as SVG markup, the one Copy as SVG takes, or null when it
    /// could not be produced.
    /// </summary>
    Task<string?> DiagramSvgAsync(string hash, CancellationToken cancellationToken = default);

    /// <summary>
    /// One equation of the rendered document as a picture, found by its block's zero-based
    /// line and its place among that block's equations, or null when there is nothing to draw.
    /// </summary>
    Task<MathPicture?> MathPngAsync(int line, int ordinal, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finishes markup Markdig has produced for a document that is not the one rendered: a
    /// Folio's other documents. The work happens in a container of the shell's own, so the
    /// rendered document is not disturbed.
    /// </summary>
    Task<BridgeOutcome<string>> RenderDetachedAsync(string html, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the rendered document to a PDF file.
    ///
    /// Through the paged engine - the document laid out into pages in a view of its own, with
    /// the cover, contents, running header and page numbers it asks for, and footnotes at the
    /// page foot - unless the setup asks for the classic engine, or the paged engine cannot
    /// finish, in which case the rendered page is printed as it stands, as every PDF was
    /// before. Answers which engine wrote the file and what the PDF lost that the document
    /// asked for: diagrams that could not be drawn, footnotes made endnotes.
    /// </summary>
    /// <param name="title">
    /// What the PDF calls itself: the front matter's title, or the file name. It goes into the
    /// file's Title property, and a full path there travels with the file to whoever it is sent.
    /// </param>
    /// <param name="furniture">The cover, contents and header and footer, resolved for this document.</param>
    Task<PdfExportOutcome> PrintPdfAsync(
        string path,
        PdfPageSetup setup,
        string title,
        PaperFurniture furniture,
        CancellationToken cancellationToken = default);
}
