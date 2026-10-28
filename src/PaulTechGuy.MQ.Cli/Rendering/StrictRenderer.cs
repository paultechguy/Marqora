// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Cli.Rendering;

/// <summary>
/// The run's renderer, with a render that does not finish made a failure of the file (CLI-EX-05).
///
/// The export jobs are the app's, and the app degrades: a PDF prints whatever the preview shows
/// when its render does not answer, because a reader is there to see it. A script is not, and a
/// PDF missing half its diagrams that exits 0 is found only after it has been sent. So the CLI
/// stops the job at the render rather than changing what the job does for the app.
/// </summary>
internal sealed class StrictRenderer(IDocumentRenderer inner, TimeSpan timeout) : IDocumentRenderer
{
    public async Task<BridgeOutcome<RenderedDocument>> RenderAsync(RenderRequest request, CancellationToken cancellationToken = default) =>
        Required(await inner.RenderAsync(request, cancellationToken).ConfigureAwait(true));

    public async Task<BridgeOutcome<string>> RenderDetachedAsync(string html, CancellationToken cancellationToken = default) =>
        Required(await inner.RenderDetachedAsync(html, cancellationToken).ConfigureAwait(true));

    public Task<byte[]?> DiagramPngAsync(string hash, CancellationToken cancellationToken = default) =>
        inner.DiagramPngAsync(hash, cancellationToken);

    public Task<string?> DiagramSvgAsync(string hash, CancellationToken cancellationToken = default) =>
        inner.DiagramSvgAsync(hash, cancellationToken);

    public Task<MathPicture?> MathPngAsync(int line, int ordinal, CancellationToken cancellationToken = default) =>
        inner.MathPngAsync(line, ordinal, cancellationToken);

    public Task<PdfExportOutcome> PrintPdfAsync(string path, PdfPageSetup setup, string title, PaperFurniture furniture, CancellationToken cancellationToken = default) =>
        inner.PrintPdfAsync(path, setup, title, furniture, cancellationToken);

    private BridgeOutcome<T> Required<T>(BridgeOutcome<T> outcome) => outcome.Result switch
    {
        BridgeResult.Rendered => outcome,
        BridgeResult.TimedOut => throw new RenderFailedException(string.Create(
            CultureInfo.InvariantCulture,
            $"The document did not finish rendering within {timeout.TotalSeconds:0} seconds. Pass --timeout to allow longer.")),
        _ => throw new RenderFailedException($"The document could not be rendered: {outcome.Error?.Message ?? "the renderer did not answer"}.", outcome.Error),
    };
}
