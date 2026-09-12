// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Markdig;
using Markdig.Syntax;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Rendering;
using MarkdigDocument = Markdig.Syntax.MarkdownDocument;
using MarkdigFootnote = Markdig.Extensions.Footnotes.Footnote;
using WordPageMargin = DocumentFormat.OpenXml.Wordprocessing.PageMargin;

namespace PaulTechGuy.MQ.Docx;

/// <summary>
/// Writes a markdown document out as a .docx.
///
/// The pipeline is built once and reused, as it is in the preview renderer and for the same
/// reason: a Markdig pipeline is immutable and thread-safe after Build.
/// </summary>
public sealed class DocxExporter : IDocxExporter
{
    private readonly MarkdownPipeline _pipeline;
    private readonly ILogger<DocxExporter> _logger;

    public DocxExporter(ILogger<DocxExporter> logger)
    {
        _logger = logger;

        // The same extensions the preview reads documents with. The source-line extension is
        // not added: it exists to stamp an HTML attribute, and this walker reads Block.Line
        // off the tree directly, which is the number that attribute would have carried.
        _pipeline = MarqoraMarkdownPipeline.CreateBuilder().Build();
    }

    public async Task<IReadOnlyList<string>> WriteAsync(
        string outputPath,
        string title,
        string markdown,
        DocxExportSetup setup,
        HeadingNumbering headingNumbering,
        string? sourceDocumentPath,
        string? renderedPreviewHtml,
        Func<string, Task<byte[]?>>? diagramPng = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(outputPath);
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(setup);

        cancellationToken.ThrowIfCancellationRequested();

        MarkdigDocument document = Markdig.Markdown.Parse(markdown, _pipeline);

        _logger.LogDebug(
            "Exporting {Characters} characters of markdown to {Path}.",
            markdown.Length,
            outputPath);

        var report = new ExportReport();

        PreviewHarvest preview = PreviewHarvest.From(renderedPreviewHtml);

        // Fetched before the walk rather than during it, because the walk is synchronous and
        // each picture is a round trip to the shell. Asking for them together also matters:
        // the bridge allows twenty seconds per diagram, and a document with a dozen of them
        // would otherwise be able to spend four minutes waiting one at a time.
        IReadOnlyDictionary<string, byte[]> diagrams =
            await FetchDiagramsAsync(document, preview, diagramPng, report).ConfigureAwait(false);

        using (WordprocessingDocument file =
            WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document))
        {
            MainDocumentPart main = file.AddMainDocumentPart();
            var body = new Body();
            main.Document = new Document(body);

            var numbering = new NumberingPlan();

            numbering.Plan(document);

            // Planned before the styles are written, because the heading styles have to name
            // the numbering instance they belong to - that link is what makes Word renumber a
            // document after somebody inserts a section into it.
            int? headingNumberId = numbering.PlanHeadings(headingNumbering);

            // The theme first: the styles below reference it for every color and face, and a
            // reference into a part that is not there resolves to nothing.
            DocxTheme.Write(main.AddNewPart<ThemePart>());
            DocxStyles.Write(
                main.AddNewPart<StyleDefinitionsPart>(),
                headingNumbering,
                headingNumberId);

            // Asked of the tree rather than discovered while walking it, because the settings
            // part is written before the body and has to name the separator notes if there
            // are going to be any.
            bool hasFootnotes = document.Descendants<MarkdigFootnote>().Any();

            DocxSettings.Write(
                main.AddNewPart<DocumentSettingsPart>(),
                updateFieldsOnOpen: setup.IncludeTableOfContents,
                hasFootnotes);

            // Only when there is something to number: an empty numbering part is one more
            // thing for Word to object to, and a document of prose has no lists at all.
            if (!numbering.IsEmpty)
            {
                numbering.Write(main.AddNewPart<NumberingDefinitionsPart>());
            }

            var renderer = new BlockRenderer(
                body,
                main,
                new BookmarkTable(),
                numbering,
                Measure.UsableWidthTwips(setup),
                sourceDocumentPath,
                report,
                preview,
                diagrams,
                _logger);

            FrontMatter front = FrontMatter.Read(document);

            (string? headerId, string? footerId) =
                DocxFurniture.WriteHeaderAndFooter(main, front.Title ?? title, setup);

            // Three sections where there is content for three. Each break is the section's
            // properties riding in the paragraph mark that closes it; the last section's ride
            // on the body itself. A next-page break starts a new page on its own, so neither
            // of these needs the explicit page break it used to end with.
            if (setup.IncludeCoverPage)
            {
                DocxFurniture.WriteCoverPage(
                    body,
                    title,
                    front,
                    Measure.UsableHeightTwips(setup));

                body.AppendChild(DocxFurniture.SectionBreak(
                    SectionProperties(setup, headerId: null, footerId: null, pageNumbers: null)));
            }

            if (setup.IncludeTableOfContents)
            {
                DocxFurniture.WriteTableOfContents(
                    body,
                    Measure.UsableWidthTwips(setup),
                    headingNumbering);

                body.AppendChild(DocxFurniture.SectionBreak(SectionProperties(
                    setup,
                    headerId,
                    footerId,
                    NumberFormatValues.LowerRoman)));
            }

            renderer.CollectAnchors(document);
            renderer.WriteAll(document);

            // A body whose last element is a table, or which has no content at all, is not a
            // document Word is happy with: it wants a paragraph to put the cursor in.
            if (body.LastChild is null or Table)
            {
                body.AppendChild(new Paragraph());
            }

            body.AppendChild(SectionProperties(
                setup,
                headerId,
                footerId,
                NumberFormatValues.Decimal));

            // What Explorer, SharePoint and Word's own Info pane show about the file. The
            // front matter is the author telling us; the display name is the fallback.
            file.PackageProperties.Title = front.Title ?? title;
            file.PackageProperties.Creator = front.Author ?? "Marqora";
            file.PackageProperties.Subject = front.Subject;
            file.PackageProperties.Keywords = front.Keywords;
            file.PackageProperties.Created = DateTime.UtcNow;
            file.PackageProperties.Modified = DateTime.UtcNow;
        }

        // Nothing here awaits yet; the signature is async because embedding diagrams will
        // await the shell once the walker reaches them.
        await Task.CompletedTask.ConfigureAwait(false);

        return report.Messages;
    }

    /// <summary>
    /// The picture for every diagram in the document, asked for all at once.
    ///
    /// A diagram the shell cannot produce is recorded rather than passed over. The walker
    /// falls back to writing the definition as a code block, so nothing is lost from the page,
    /// but the caller should still be able to say that a picture did not make it.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, byte[]>> FetchDiagramsAsync(
        MarkdigDocument document,
        PreviewHarvest preview,
        Func<string, Task<byte[]?>>? diagramPng,
        ExportReport report)
    {
        if (diagramPng is null || preview.IsEmpty)
        {
            return new Dictionary<string, byte[]>();
        }

        var hashes = new HashSet<string>(StringComparer.Ordinal);

        foreach (FencedCodeBlock fence in document.Descendants<FencedCodeBlock>())
        {
            if (preview.TryDiagram(fence.Line, out string hash))
            {
                hashes.Add(hash);
            }
        }

        if (hashes.Count == 0)
        {
            return new Dictionary<string, byte[]>();
        }

        KeyValuePair<string, byte[]?>[] fetched = await Task.WhenAll(
            hashes.Select(async hash =>
                new KeyValuePair<string, byte[]?>(hash, await diagramPng(hash).ConfigureAwait(false))))
            .ConfigureAwait(false);

        var pictures = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach ((string hash, byte[]? png) in fetched)
        {
            if (png is { Length: > 0 })
            {
                pictures[hash] = png;
            }
            else
            {
                report.Note("A diagram could not be drawn (its source is in the document instead)");
            }
        }

        return pictures;
    }

    /// <summary>
    /// Page size, orientation, margins and page numbering for one section.
    ///
    /// A document that has a title page and a contents has three of these: the cover, which
    /// references no header or footer and numbers nothing; the contents, which number i, ii,
    /// iii; and the body, which begins again at 1. Pass null for the numbering to leave the
    /// section counting quietly on its own - which is what the cover wants, because the
    /// section after it restarts the count anyway and nothing on the cover prints.
    ///
    /// The child order is a schema sequence rather than a free-for-all - type, then pgSz,
    /// then pgMar, then pgNumType - and Word answers a wrong order with "unreadable content"
    /// rather than with anything that names the problem.
    /// </summary>
    private static SectionProperties SectionProperties(
        DocxExportSetup setup,
        string? headerId,
        string? footerId,
        NumberFormatValues? pageNumbers)
    {
        (int width, int height) = Measure.PageTwips(setup);

        var section = new SectionProperties();

        // The references come first: they are the one part of a section's properties that
        // sits before the page setup rather than among it.
        if (headerId is not null)
        {
            section.AppendChild(new HeaderReference
            {
                Type = HeaderFooterValues.Default,
                Id = headerId,
            });
        }

        if (footerId is not null)
        {
            section.AppendChild(new FooterReference
            {
                Type = HeaderFooterValues.Default,
                Id = footerId,
            });
        }

        foreach (OpenXmlElement element in PageSetup(setup, width, height))
        {
            section.AppendChild(element);
        }

        // Where the section numbers its own pages, it says so and starts again at one. There
        // is no titlePg here any more: that flag suppressed the header on the first page of a
        // single section, and was how the cover used to get out of carrying one. A cover with
        // a section to itself simply references no header and no footer.
        if (pageNumbers is { } format)
        {
            section.AppendChild(new PageNumberType { Format = format, Start = 1 });
        }

        return section;
    }

    private static OpenXmlElement[] PageSetup(DocxExportSetup setup, int width, int height)
    {
        // Word's presets are not all square: Moderate and Wide change the measure without
        // changing the page, so the sides and the top are asked for separately.
        int vertical = Measure.Twips(setup.VerticalMarginInches);
        int horizontal = Measure.Twips(setup.HorizontalMarginInches);

        return new OpenXmlElement[]
        {
            new SectionType { Val = SectionMarkValues.NextPage },
            new PageSize
            {
                Width = (uint)width,
                Height = (uint)height,
                Orient = setup.Orientation == PageOrientation.Landscape
                    ? PageOrientationValues.Landscape
                    : PageOrientationValues.Portrait,
            },
            new WordPageMargin
            {
                Top = vertical,
                Right = (uint)horizontal,
                Bottom = vertical,
                Left = (uint)horizontal,

                // Header and footer distance has to stay inside the margin or the header
                // prints on top of the body. With no margin at all there is nowhere to put
                // them, and the builder omits the references entirely.
                Header = (uint)Math.Min(Measure.Twips(0.5), Math.Max(0, vertical - 1)),
                Footer = (uint)Math.Min(Measure.Twips(0.5), Math.Max(0, vertical - 1)),
                Gutter = 0,
            },
        };
    }
}
