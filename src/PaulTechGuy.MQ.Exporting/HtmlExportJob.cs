// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Exporting;

/// <summary>What an HTML export is of, and where it goes.</summary>
/// <param name="MeasurePixels">The page width, the reader's own <c>PreviewMaxWidth</c>; zero for none.</param>
public sealed record HtmlExportRequest(
    RenderRequest Document,
    string OutputPath,
    string Title,
    DocumentImages Images,
    int MeasurePixels);

/// <summary>
/// Exports one document as a self-contained HTML file: the renderer's finished markup, packaged
/// by <see cref="IHtmlExporter"/>. The app and marqora-cli both run this, so the file cannot
/// differ between them (CLI-ARC-04).
///
/// Awaited on the caller's context, as every job here is. The app calls from the UI thread,
/// where the preview has to be spoken to; the CLI has no context, so it costs it nothing.
/// </summary>
public sealed class HtmlExportJob(IHtmlExporter exporter)
{
    /// <summary>The problem an HTML export's row names, for a picture it linked instead of embedding.</summary>
    public const string TooLargeToEmbed = "Too large to embed";

    /// <returns>
    /// Nothing written when the renderer did not hand the markup over, as an export of nothing is
    /// worse than none. Otherwise the file, and a row for each picture too large to embed, which
    /// stays linked to where it is on this machine.
    /// </returns>
    public async Task<ExportOutcome> RunAsync(
        IDocumentRenderer renderer,
        HtmlExportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(request);

        BridgeOutcome<RenderedDocument> rendered = await renderer
            .RenderAsync(request.Document, cancellationToken)
            .ConfigureAwait(true);

        if (!rendered.IsRendered || string.IsNullOrWhiteSpace(rendered.Value?.Html))
        {
            return ExportOutcome.NotRendered(rendered.IsRendered ? BridgeResult.Failed : rendered.Result);
        }

        IReadOnlyList<string> skipped = await exporter
            .WriteAsync(
                request.OutputPath,
                request.Title,
                rendered.Value.Html,
                request.Images,
                request.MeasurePixels,
                cancellationToken)
            .ConfigureAwait(true);

        return ExportOutcome.Wrote(
            request.OutputPath,
            [.. skipped.Select(item => new ExportIssue { Problem = TooLargeToEmbed, Item = item })]);
    }
}
