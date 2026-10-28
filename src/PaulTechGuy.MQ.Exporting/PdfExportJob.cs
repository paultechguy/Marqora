// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Analysis;
using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Exporting;

/// <summary>What a PDF export is of, and how it is laid out on paper.</summary>
/// <param name="Title">What the PDF calls itself: the front matter's title, or the file's name. Never the path.</param>
public sealed record PdfExportRequest(
    RenderRequest Document,
    string OutputPath,
    string Title,
    PdfPageSetup Setup,
    PaperFurniture Furniture);

/// <summary>
/// Exports one document as a PDF: rendered, then printed by the renderer's paged engine, with
/// the classic engine as its fallback. The report rows are the renderer's (a diagram that did
/// not draw, the endnotes advisory, both read from the markup it printed) and the pictures the
/// PDF leaves out, which this job finds from the text.
/// </summary>
public sealed class PdfExportJob(IMarkdownRenderer markdown, IMarkdownAnalyzer analyzer, ILogger<PdfExportJob> logger)
{
    /// <returns>The file, the engine that printed it, and its rows in line order.</returns>
    public async Task<ExportOutcome> RunAsync(
        IDocumentRenderer renderer,
        PdfExportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(request);

        // The headless renderer prints what it last rendered, so it has to be this. The live
        // preview already shows it and prints whatever it shows, as the app always has: a render
        // that did not answer is the renderer's to refuse at the print, not this job's.
        BridgeOutcome<RenderedDocument> rendered = await renderer
            .RenderAsync(request.Document, cancellationToken)
            .ConfigureAwait(true);

        if (!rendered.IsRendered)
        {
            logger.LogInformation("The render before the PDF did not finish ({Result}); printing what the renderer has.", rendered.Result);
        }

        PdfExportOutcome printed = await renderer
            .PrintPdfAsync(request.OutputPath, request.Setup, request.Title, request.Furniture, cancellationToken)
            .ConfigureAwait(true);

        IReadOnlyList<ExportIssue> issues =
        [
            .. printed.Issues,
            .. await PictureIssuesAsync(request.Document.Markdown, request.Document.DocumentPath).ConfigureAwait(true),
        ];

        return ExportOutcome.Wrote(request.OutputPath, [.. issues.OrderBy(i => i.Line)]) with { Engine = printed.Engine };
    }

    /// <summary>
    /// The pictures a PDF of this text leaves out, as export report rows: the ones addressed on
    /// the web, which the renderer never fetches; the ones not found; and the ones kept outside
    /// the document's folder, which the renderer will not serve (docs/Export-Alignment-Plan.md,
    /// §6.5).
    ///
    /// The editor's own link check, run once more over the exported text with only the picture
    /// rules on - so the report and the underlines in the editor agree about what is missing.
    /// The wording is Word's where Word has the same row.
    /// </summary>
    private async Task<IReadOnlyList<ExportIssue>> PictureIssuesAsync(string text, string? documentPath)
    {
        try
        {
            AnalysisResult found = await Task.Run(() =>
            {
                RenderedMarkdown rendered = markdown.Render(text);

                return analyzer.Analyze(new AnalysisRequest
                {
                    Text = text,
                    DocumentPath = documentPath,
                    Links = rendered.Links,
                    Outline = rendered.Outline,
                    Anchors = rendered.Anchors,
                    CheckImageAltText = false,
                    CheckBlockedImages = true,
                });
            }).ConfigureAwait(true);

            return
            [
                .. found.LinkFindings
                    .Where(f => f.Kind is LinkFindingKind.MissingImage
                        or LinkFindingKind.RemoteMedia
                        or LinkFindingKind.OutsideFolder)
                    .Select(f => new ExportIssue
                    {
                        Line = f.Line + 1,
                        Problem = f.Kind switch
                        {
                            LinkFindingKind.MissingImage => "Not found",
                            LinkFindingKind.RemoteMedia => "Not on this machine",
                            _ => "Outside the document's folder, so not shown",
                        },
                        Item = f.Url.Length <= 80 ? f.Url : f.Url[..80] + "…",
                    }),
            ];
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The report is a courtesy; the PDF is already written.
            logger.LogWarning(ex, "Could not check the exported PDF's pictures.");
            return [];
        }
    }
}
