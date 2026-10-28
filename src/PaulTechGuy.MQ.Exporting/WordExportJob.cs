// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Themes;

namespace PaulTechGuy.MQ.Exporting;

/// <summary>What a Word export is of, and how it is laid out.</summary>
/// <param name="Theme">The color theme the document is written in, light palette; null for Default.</param>
public sealed record WordExportRequest(
    RenderRequest Document,
    string OutputPath,
    string Title,
    DocxExportSetup Setup,
    ExportLayout Layout,
    DocumentImages Images,
    ColorTheme? Theme);

/// <summary>
/// Exports one document as a Word file. The markdown is walked into Word's own constructs by
/// <see cref="IDocxExporter"/>; the renderer is asked only for what a browser alone can make:
/// the finished markup (diagram hashes, KaTeX's MathML, highlight.js's colors), each diagram's
/// picture and light SVG, and a picture of an equation Word cannot write.
///
/// Which is why it cannot refuse the way the HTML export does. A render that did not answer
/// costs the file its colors and its diagrams; it does not stop one being written.
/// </summary>
public sealed class WordExportJob(IDocxExporter exporter, ILogger<WordExportJob> logger)
{
    /// <returns>The file, and everything that could not be carried into it, in document order.</returns>
    public async Task<ExportOutcome> RunAsync(
        IDocumentRenderer renderer,
        WordExportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(request);

        // Timed end to end. Everything from here until the report window appears is dead air to
        // whoever is watching - the file dialog has closed and nothing else has happened yet -
        // so when that wait is long enough to notice, the log has to say which part was slow.
        var elapsed = Stopwatch.StartNew();

        // Whatever the renderer has, or nothing at all. A document that has never been
        // previewed, or one typed into a second ago, still exports.
        BridgeOutcome<RenderedDocument> outcome = await renderer
            .RenderAsync(request.Document, cancellationToken)
            .ConfigureAwait(true);

        string rendered = outcome.IsRendered ? outcome.Value?.Html ?? string.Empty : string.Empty;
        long renderedMs = elapsed.ElapsedMilliseconds;

        // The exporter runs on a worker: a .docx is heavier than an HTML file - there are images
        // to re-encode and a whole part graph to assemble - and freezing the window on a
        // picture-heavy document is the mistake not to repeat. What it asks the renderer for
        // comes back to the caller's context, where the renderer lives.
        var ask = new CallerContext(SynchronizationContext.Current);
        RenderRequest document = request.Document;

        IReadOnlyList<ExportIssue> issues = await Task.Run(
            () => exporter.WriteAsync(
                request.OutputPath,
                request.Title,
                document.Markdown,
                request.Setup,
                request.Layout,
                document.Numbering,
                document.DocumentPath,
                rendered,
                hash => ask.Run(
                    () => renderer.DiagramPngAsync(hash, cancellationToken),
                    ex => logger.LogWarning(ex, "A diagram could not be rasterized for the Word export.")),
                request.Images,
                request.Theme,
                hash => ask.Run(
                    () => renderer.DiagramSvgAsync(hash, cancellationToken),
                    ex => logger.LogWarning(ex, "A diagram's SVG could not be had for the Word export; it stays a PNG.")),
                (line, ordinal) => ask.Run(
                    () => renderer.MathPngAsync(line, ordinal, cancellationToken),
                    ex => logger.LogWarning(ex, "An equation could not be drawn for the Word export; its source goes in.")),
                cancellationToken),
            cancellationToken)
            .ConfigureAwait(true);

        logger.LogInformation(
            "Word export of {Document} took {Total} ms: {Preview} ms collecting the preview, "
            + "{Export} ms in the exporter ({Characters} characters, {Rendered} of preview HTML).",
            request.Title,
            elapsed.ElapsedMilliseconds,
            renderedMs,
            elapsed.ElapsedMilliseconds - renderedMs,
            document.Markdown.Length,
            rendered.Length);

        return ExportOutcome.Wrote(request.OutputPath, issues);
    }

    /// <summary>
    /// Runs a renderer call on the context the job was started from, from whatever thread the
    /// exporter is on. The live preview belongs to the UI thread and cannot be spoken to from a
    /// worker, so the call goes back there and the answer comes home through a completion
    /// source. With no context there is nothing to go back to, and the call is simply made.
    ///
    /// Null on any failure, logged, which the exporter treats as "write the source instead": a
    /// document is never left with a gap where a picture was.
    /// </summary>
    private sealed class CallerContext(SynchronizationContext? context)
    {
        public Task<T?> Run<T>(Func<Task<T?>> call, Action<Exception> failed)
            where T : class
        {
            var completion = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (context is null)
            {
                _ = AnswerAsync();
            }
            else
            {
                context.Post(_ => _ = AnswerAsync(), null);
            }

            return completion.Task;

            // Nothing is thrown out of here: on the UI thread the continuation is the
            // dispatcher's, and an exception escaping it would take the window down rather than
            // the export.
            async Task AnswerAsync()
            {
                try
                {
                    completion.SetResult(await call().ConfigureAwait(true));
                }
                catch (Exception ex)
                {
                    failed(ex);
                    completion.SetResult(null);
                }
            }
        }
    }
}
