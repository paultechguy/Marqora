// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Exporting;

/// <summary>What a Folio's single-page form is of, and where it goes.</summary>
/// <param name="Numbering">
/// Each document's heading numbering, keyed by the path the plan knows it by. Settled before the
/// first render, so a preference changed during a long export cannot number half a collection.
/// </param>
/// <param name="DefaultNumbering">For a planned document <paramref name="Numbering"/> does not name.</param>
public sealed record FolioPageRequest(
    FolioPlan Plan,
    string OutputPath,
    string Title,
    IReadOnlyDictionary<string, HeadingNumbering> Numbering,
    HeadingNumbering DefaultNumbering,
    int MeasurePixels,
    string AppVersion,
    string MachineName);

/// <summary>
/// Builds a Folio's reading copy: every planned document rendered into one page.
///
/// Each document is rendered here rather than taken from a preview, because eleven of twelve
/// are not on screen. Markdig runs here and the renderer finishes the job - mermaid, KaTeX,
/// highlighting - detached, so a preview the reader is looking at is never disturbed.
///
/// One document at a time, deliberately. The shell has a single off-screen mermaid frame and
/// concurrent renders would race over it.
/// </summary>
public sealed class FolioPageJob(IMarkdownRenderer markdown, FolioHtmlWriter writer, ILogger<FolioPageJob> logger)
{
    /// <param name="progress">A status line per step, for whoever is watching.</param>
    /// <returns>
    /// The page. Its report rows are the plan's, which the caller already holds; a reference the
    /// page could not embed is one of those, so it is logged here rather than reported twice.
    /// </returns>
    public async Task<ExportOutcome> RunAsync(
        IDocumentRenderer renderer,
        FolioPageRequest request,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(request);

        FolioPlan plan = request.Plan;
        List<FolioRenderedDocument> rendered = [];

        for (int i = 0; i < plan.Documents.Count; i++)
        {
            FolioDocumentPlan document = plan.Documents[i];

            progress?.Report($"Rendering {document.EntryName} ({i + 1} of {plan.Documents.Count})...");

            // The planned text, not the buffer: its references have already been repointed at
            // what the Folio will actually contain. Numbered as the preview is, since a Folio is
            // the document as the author reads it.
            HeadingNumbering numbering =
                request.Numbering.TryGetValue(document.SourcePath, out HeadingNumbering chosen)
                    ? chosen
                    : request.DefaultNumbering;

            RenderedMarkdown page = await Task
                .Run(() => markdown.Render(document.Text, numbering), cancellationToken)
                .ConfigureAwait(true);

            BridgeOutcome<string> finished = await renderer
                .RenderDetachedAsync(page.Html, cancellationToken)
                .ConfigureAwait(true);

            // Unanswered, the Markdig markup goes in unfinished: a Folio missing one document's
            // diagrams beats a Folio missing the document.
            string html = finished.IsRendered ? finished.Value ?? string.Empty : page.Html;

            rendered.Add(new FolioRenderedDocument(
                document,
                page.Outline.FirstOrDefault(h => h.Level == 1)?.Text
                    ?? Path.GetFileNameWithoutExtension(document.EntryName),
                html,
                [.. page.Outline.Select(h => h.Slug)]));
        }

        progress?.Report("Writing the Folio...");

        IReadOnlyList<string> unresolved = await writer
            .WriteAsync(
                plan,
                rendered,
                request.OutputPath,
                request.Title,
                request.MeasurePixels,
                request.AppVersion,
                request.MachineName,
                cancellationToken)
            .ConfigureAwait(true);

        if (unresolved.Count > 0)
        {
            // Logged rather than shown. Every one of these is a local reference the page could
            // not match to a planned asset - remote addresses are skipped by the embedder - so
            // the planner has already reported it as a missing image, and the report says so
            // with a line number and a way to get there.
            logger.LogInformation(
                "The Folio page left {Count} reference(s) unembedded: {Entries}.",
                unresolved.Count,
                string.Join(", ", unresolved.Distinct(StringComparer.OrdinalIgnoreCase).Take(10)));
        }

        return ExportOutcome.Wrote(request.OutputPath, []);
    }
}
