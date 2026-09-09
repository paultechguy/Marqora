// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Folio;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>One document, rendered and ready to be placed in a Folio.</summary>
/// <param name="Plan">Its entry in the plan, which is where its name inside the Folio comes from.</param>
/// <param name="Title">What the contents list calls it: its first heading, or its file name.</param>
/// <param name="Html">Finished markup - Markdig, then mermaid, KaTeX and highlighting.</param>
/// <param name="HeadingSlugs">
/// The anchors Markdig minted for this document, which are the ones that have to be renamed:
/// twelve documents in one page will contain more than one "#introduction".
/// </param>
public sealed record FolioRenderedDocument(
    FolioDocumentPlan Plan,
    string Title,
    string Html,
    IReadOnlyList<string> HeadingSlugs);

/// <summary>
/// Assembles a Folio's reading copy: every document in one page that opens in any browser,
/// offline, with nothing installed.
///
/// The page carries no script of its own. Moving between documents is an ordinary anchor and
/// the contents list is plain markup, which is what keeps a Folio past the mail gateways that
/// strip active content - and what lets it still work in a browser nobody has thought about yet.
///
/// Styles come from <see cref="RenderedHtmlPackager"/>, so a Folio and the preview cannot drift
/// apart, and the KaTeX and highlight themes are carried only when some document actually needs
/// them.
/// </summary>
public sealed class FolioHtmlWriter(RenderedHtmlPackager packager, ILogger<FolioHtmlWriter> logger)
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Writes the Folio. Returns the entry names of images that were referenced and could not
    /// be embedded, which the caller reports rather than hiding.
    /// </summary>
    public async Task<IReadOnlyList<string>> WriteAsync(
        FolioPlan plan,
        IReadOnlyList<FolioRenderedDocument> documents,
        string outputPath,
        string title,
        int measurePixels = 0,
        string? appVersion = null,
        string? machineName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        Dictionary<string, string> assets = plan.Assets.ToDictionary(
            a => a.EntryName,
            a => a.SourcePath,
            StringComparer.OrdinalIgnoreCase);

        // Entry name to the anchor its document will answer to, so a link written between two
        // documents becomes a jump within one page.
        Dictionary<string, string> anchors = documents
            .Select((d, i) => (d.Plan.EntryName, Anchor: FolioLinks.Anchor(i)))
            .ToDictionary(x => x.EntryName, x => x.Anchor, StringComparer.OrdinalIgnoreCase);

        var body = new StringBuilder();
        List<string> unresolved = [];

        for (int i = 0; i < documents.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            FolioRenderedDocument document = documents[i];
            string html = FolioLinks.Namespace(document.Html, document.HeadingSlugs, FolioLinks.Anchor(i));

            html = FolioLinks.Relink(html, anchors, FolioLinks.Anchor(i), document.HeadingSlugs);
            html = packager.EmbedFolioAssets(html, assets, out IReadOnlyList<string> missing);

            unresolved.AddRange(missing);

            body.AppendLine(CultureInfo.InvariantCulture,
                $"<article class=\"mq-preview mq-folio-doc\" id=\"{FolioLinks.Anchor(i)}\">");
            body.AppendLine(html);
            body.AppendLine("</article>");
        }

        string content = body.ToString();

        var page = new StringBuilder();

        page.AppendLine("<!DOCTYPE html>");
        page.AppendLine("<html lang=\"en\" data-theme=\"light\">");
        page.AppendLine("<head>");
        page.AppendLine("<meta charset=\"utf-8\" />");
        page.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        page.AppendLine(CultureInfo.InvariantCulture, $"<title>{WebUtility.HtmlEncode(title)}</title>");
        page.AppendLine("<meta name=\"generator\" content=\"Marqora\" />");

        // The marker that makes recognizing a Folio a four-kilobyte read rather than a scan of
        // the whole file for a payload that sits at the end of it.
        page.AppendLine(FolioMarkup.MarkerMeta);

        page.AppendLine("<style>");
        page.AppendLine(packager.ReadStyles(content));
        page.AppendLine(ExportLayout.PageCss(measurePixels));
        page.AppendLine(FolioStyles(measurePixels));
        page.AppendLine("</style>");
        page.AppendLine("</head>");
        page.AppendLine("<body>");

        if (documents.Count > 1)
        {
            page.AppendLine(Contents(documents, title));
        }

        page.Append(content);

        /*
            The sources, so that the one file anybody can read is also the documents it was made
            from. A browser neither runs nor renders an unknown script type, so to a reader this
            is invisible; drag the same file onto Marqora and it unpacks.

            Last, after the content, because that is where the images it refers to already are -
            and because a reader who saves the page has the whole document before this begins.
        */
        page.AppendLine(FolioPayload.Encode(new FolioPayload
        {
            AppVersion = appVersion,
            ExportedUtc = DateTimeOffset.UtcNow,
            ExportedFrom = machineName,
            Documents = [.. documents.Select(d => new FolioPayloadDocument
            {
                Entry = d.Plan.EntryName,
                Text = d.Plan.Text,
            })],
            Assets = [.. plan.Assets.Select(a => new FolioManifestAsset
            {
                Entry = a.EntryName,
                Relocated = a.Relocated,
            })],
        }));

        page.AppendLine("</body>");
        page.AppendLine("</html>");

        await File.WriteAllTextAsync(outputPath, page.ToString(), Utf8NoBom, cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation(
            "Wrote a Folio of {Documents} document(s) to {Path}.", documents.Count, outputPath);

        return unresolved;
    }

    /// <summary>
    /// The contents list. Plain anchors and a <c>details</c> element, so it needs no script and
    /// still collapses.
    /// </summary>
    private static string Contents(IReadOnlyList<FolioRenderedDocument> documents, string title)
    {
        var builder = new StringBuilder();

        builder.AppendLine("<nav class=\"mq-folio-contents\">");
        builder.AppendLine("<details open>");
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"<summary>{WebUtility.HtmlEncode(title)}</summary>");
        builder.AppendLine("<ol>");

        for (int i = 0; i < documents.Count; i++)
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"<li><a href=\"#{FolioLinks.Anchor(i)}\">{WebUtility.HtmlEncode(documents[i].Title)}</a></li>");
        }

        builder.AppendLine("</ol>");
        builder.AppendLine("</details>");
        builder.AppendLine("</nav>");

        return builder.ToString();
    }

    /// <summary>
    /// What a page of many documents needs and a single export does not: a rule between
    /// documents, a contents list, and a preview that is a page rather than a pane.
    /// </summary>
    private static string FolioStyles(int measurePixels) => $$"""
        .mq-folio-contents {
        {{Measure(measurePixels)}}
          margin-bottom: 2.5rem;
          font-size: 15px;
        }

        .mq-folio-contents summary {
          cursor: pointer;
          font-weight: 600;
          font-size: 17px;
        }

        .mq-folio-contents ol { margin: 0.75rem 0 0; padding-left: 1.5rem; }

        .mq-folio-contents li { margin: 0.2rem 0; }

        .mq-folio-doc + .mq-folio-doc {
          margin-top: 3rem;
          padding-top: 3rem;
          border-top: 1px solid var(--mq-border);
        }

        @media print {
          .mq-folio-doc + .mq-folio-doc {
            border-top: 0;
            page-break-before: always;
          }
        }
        """;

    /// <summary>
    /// The contents list is held to the same measure as the documents it lists, so the two are
    /// not centred on different axes. Written the same way <see cref="ExportLayout"/> writes it.
    /// </summary>
    private static string Measure(int measurePixels) => measurePixels > 0
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"  max-width: {measurePixels}px;{Environment.NewLine}  margin-inline: auto;")
        : "  max-width: none;\n  margin-inline: 0;";
}
