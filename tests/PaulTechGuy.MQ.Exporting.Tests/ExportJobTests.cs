// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Abstractions.Analysis;
using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Themes;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Exporting.Tests;

/// <summary>
/// WP-M2-06: the export jobs both front ends run, over a renderer that records what it is asked
/// instead of a browser. What each job asks the renderer for, in what order, what it does when
/// the renderer does not answer, and what comes back in its <see cref="ExportOutcome"/>.
///
/// The files themselves are the exporters' and are held by their own tests and the baseline;
/// here the exporters are stand-ins too, so a job is seen doing only its own part.
/// </summary>
public sealed class ExportJobTests
{
    private static readonly RenderRequest Guide = new("# Guide\n", @"C:\docs\guide.md", HeadingNumbering.Off, "forest");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_HTML_export_the_renderer_did_not_answer_writes_nothing_and_says_why()
    {
        var renderer = new RecordingRenderer { Rendering = BridgeOutcome.TimedOut<RenderedDocument>() };
        var exporter = new RecordingHtmlExporter();

        ExportOutcome outcome = await new HtmlExportJob(exporter)
            .RunAsync(renderer, new HtmlExportRequest(Guide, "out.html", "Guide", DocumentImages.None, 0), Ct);

        outcome.Written.ShouldBeFalse();
        outcome.Rendering.ShouldBe(BridgeResult.TimedOut);
        exporter.Markup.ShouldBeNull();
    }

    [Fact]
    public async Task An_HTML_export_packages_the_rendered_markup_and_reports_each_picture_left_linked()
    {
        var renderer = new RecordingRenderer();
        var exporter = new RecordingHtmlExporter { Skipped = ["huge.png"] };

        ExportOutcome outcome = await new HtmlExportJob(exporter)
            .RunAsync(renderer, new HtmlExportRequest(Guide, "out.html", "Guide", DocumentImages.None, 820), Ct);

        renderer.Asked.ShouldBe(["render forest"]);
        exporter.Markup.ShouldBe("<h1>Guide</h1>");
        exporter.Measure.ShouldBe(820);
        outcome.Outputs.ShouldBe(["out.html"]);

        ExportIssue row = outcome.Issues.ShouldHaveSingleItem();
        row.Problem.ShouldBe(HtmlExportJob.TooLargeToEmbed);
        row.Item.ShouldBe("huge.png");
    }

    [Fact]
    public async Task A_PDF_is_rendered_then_printed_and_its_rows_come_back_in_line_order()
    {
        var renderer = new RecordingRenderer
        {
            Printed = new PdfExportOutcome(PrintEngine.Paged, [new ExportIssue { Line = 9, Problem = "Diagram did not render", Item = "flowchart" }]),
        };
        var analyzer = new FixedAnalyzer(new LinkFinding { Line = 2, Start = 0, Length = 9, Url = "gone.png", Kind = LinkFindingKind.MissingImage, Message = "Image not found" });

        ExportOutcome outcome = await new PdfExportJob(new FixedMarkdown(), analyzer, NullLogger<PdfExportJob>.Instance)
            .RunAsync(renderer, new PdfExportRequest(Guide, "out.pdf", "Guide", PdfPageSetup.Default, new PaperFurniture(null, null, true)), Ct);

        renderer.Asked.ShouldBe(["render forest", "print out.pdf Guide"]);
        outcome.Engine.ShouldBe(PrintEngine.Paged);
        outcome.Outputs.ShouldBe(["out.pdf"]);
        outcome.Issues.Select(i => (i.Line, i.Problem)).ShouldBe([(3, "Not found"), (9, "Diagram did not render")]);
    }

    [Fact]
    public async Task A_PDF_whose_render_did_not_answer_is_still_printed_as_the_app_always_has()
    {
        var renderer = new RecordingRenderer { Rendering = BridgeOutcome.TimedOut<RenderedDocument>() };

        ExportOutcome outcome = await new PdfExportJob(new FixedMarkdown(), new FixedAnalyzer(), NullLogger<PdfExportJob>.Instance)
            .RunAsync(renderer, new PdfExportRequest(Guide, "out.pdf", "Guide", PdfPageSetup.Default, new PaperFurniture(null, null, false)), Ct);

        renderer.Asked.ShouldBe(["render forest", "print out.pdf Guide"]);
        outcome.Written.ShouldBeTrue();
    }

    [Fact]
    public async Task A_Word_export_asks_for_pictures_from_the_context_it_was_started_on()
    {
        var context = new MarkedContext();
        var renderer = new RecordingRenderer { Context = context };
        var exporter = new RecordingDocxExporter();

        SynchronizationContext? before = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);

        try
        {
            ExportOutcome outcome = await new WordExportJob(exporter, NullLogger<WordExportJob>.Instance)
                .RunAsync(renderer, WordRequest(), Ct);

            outcome.Outputs.ShouldBe(["out.docx"]);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(before);
        }

        // The exporter runs on a worker; every call it made came back to the starting context.
        exporter.RenderedHtml.ShouldBe("<h1>Guide</h1>");
        exporter.Png.ShouldBe([1, 2, 3]);
        exporter.Svg.ShouldBe("<svg/>");
        renderer.OnContext.ShouldBe([true, true, true, true]);
    }

    [Fact]
    public async Task A_Word_export_is_written_without_what_the_renderer_could_not_give()
    {
        var renderer = new RecordingRenderer
        {
            Rendering = BridgeOutcome.Failed<RenderedDocument>(new InvalidOperationException("no preview")),
            PictureFails = true,
        };
        var exporter = new RecordingDocxExporter();

        ExportOutcome outcome = await new WordExportJob(exporter, NullLogger<WordExportJob>.Instance)
            .RunAsync(renderer, WordRequest(), Ct);

        outcome.Written.ShouldBeTrue();
        exporter.RenderedHtml.ShouldBe(string.Empty);
        exporter.Png.ShouldBeNull();
        exporter.Svg.ShouldBeNull();
        exporter.Math.ShouldBeNull();
    }

    private static WordExportRequest WordRequest() =>
        new(Guide, "out.docx", "Guide", DocxExportSetup.Default, ExportLayout.Default, DocumentImages.None, null);

    /// <summary>A context that marks the threads its posts run on, so a call can tell it came through it.</summary>
    private sealed class MarkedContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) =>
            ThreadPool.QueueUserWorkItem(_ =>
            {
                SetSynchronizationContext(this);
                d(state);
            });
    }

    private sealed class RecordingRenderer : IDocumentRenderer
    {
        public BridgeOutcome<RenderedDocument> Rendering { get; init; } = BridgeOutcome.Rendered(new RenderedDocument("<h1>Guide</h1>"));

        public PdfExportOutcome Printed { get; init; } = new(PrintEngine.Paged, []);

        public bool PictureFails { get; init; }

        public SynchronizationContext? Context { get; init; }

        public List<string> Asked { get; } = [];

        public List<bool> OnContext { get; } = [];

        public Task<BridgeOutcome<RenderedDocument>> RenderAsync(RenderRequest request, CancellationToken cancellationToken = default)
        {
            Note($"render {request.ColorThemeId}");
            return Task.FromResult(Rendering);
        }

        public Task<byte[]?> DiagramPngAsync(string hash, CancellationToken cancellationToken = default) =>
            Picture<byte[]>("png", [1, 2, 3]);

        public Task<string?> DiagramSvgAsync(string hash, CancellationToken cancellationToken = default) =>
            Picture("svg", "<svg/>");

        public Task<MathPicture?> MathPngAsync(int line, int ordinal, CancellationToken cancellationToken = default) =>
            Picture("math", new MathPicture([4], 2));

        public Task<BridgeOutcome<string>> RenderDetachedAsync(string html, CancellationToken cancellationToken = default) =>
            Task.FromResult(BridgeOutcome.Rendered(html));

        public Task<PdfExportOutcome> PrintPdfAsync(string path, PdfPageSetup setup, string title, PaperFurniture furniture, CancellationToken cancellationToken = default)
        {
            Note($"print {path} {title}");
            return Task.FromResult(Printed);
        }

        private Task<T?> Picture<T>(string what, T value)
            where T : class
        {
            Note(what);

            return PictureFails
                ? throw new InvalidOperationException($"no {what}")
                : Task.FromResult<T?>(value);
        }

        private void Note(string what)
        {
            lock (Asked)
            {
                Asked.Add(what);

                if (Context is not null)
                {
                    OnContext.Add(ReferenceEquals(SynchronizationContext.Current, Context));
                }
            }
        }
    }

    private sealed class RecordingHtmlExporter : IHtmlExporter
    {
        public IReadOnlyList<string> Skipped { get; init; } = [];

        public string? Markup { get; private set; }

        public int Measure { get; private set; }

        public Task<IReadOnlyList<string>> WriteAsync(
            string outputPath, string title, string renderedHtml, DocumentImages images, int measurePixels = 0, CancellationToken cancellationToken = default)
        {
            Markup = renderedHtml;
            Measure = measurePixels;
            return Task.FromResult(Skipped);
        }
    }

    /// <summary>Asks for one of each picture, the way the real exporter does for a diagram and an equation.</summary>
    private sealed class RecordingDocxExporter : IDocxExporter
    {
        public string? RenderedHtml { get; private set; }

        public byte[]? Png { get; private set; }

        public string? Svg { get; private set; }

        public MathPicture? Math { get; private set; }

        public async Task<IReadOnlyList<ExportIssue>> WriteAsync(
            string outputPath,
            string title,
            string markdown,
            DocxExportSetup setup,
            ExportLayout layout,
            HeadingNumbering headingNumbering,
            string? sourceDocumentPath,
            string? renderedPreviewHtml,
            Func<string, Task<byte[]?>>? diagramPng = null,
            DocumentImages? images = null,
            ColorTheme? colorTheme = null,
            Func<string, Task<string?>>? diagramSvg = null,
            Func<int, int, Task<MathPicture?>>? mathPng = null,
            CancellationToken cancellationToken = default)
        {
            RenderedHtml = renderedPreviewHtml;
            Png = await diagramPng!("abc");
            Svg = await diagramSvg!("abc");
            Math = await mathPng!(4, 1);
            return [];
        }
    }

    private sealed class FixedMarkdown : IMarkdownRenderer
    {
        public RenderedMarkdown Render(string markdown) => RenderedMarkdown.Empty;

        public RenderedMarkdown Render(string markdown, HeadingNumbering headingNumbering) => RenderedMarkdown.Empty;
    }

    private sealed class FixedAnalyzer(params LinkFinding[] findings) : IMarkdownAnalyzer
    {
        public AnalysisResult Analyze(AnalysisRequest request) => new() { LinkFindings = findings };
    }
}
