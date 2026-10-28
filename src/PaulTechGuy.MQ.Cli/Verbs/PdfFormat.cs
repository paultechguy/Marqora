// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Analysis;
using PaulTechGuy.MQ.Cli.Reporting;
using PaulTechGuy.MQ.Cli.Settings;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Exporting;
using PaulTechGuy.MQ.Rendering;

namespace PaulTechGuy.MQ.Cli.Verbs;

/// <summary>
/// export pdf: the app's PDF job, with the page setup and the layout of the settings source and the
/// document's own title and furniture, read the way the app reads them.
/// </summary>
internal sealed class PdfFormat : ExportFormat
{
    public override string Name => "pdf";

    public override string Extension => ".pdf";

    public override IReadOnlyList<string> Setup(SettingsSource settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        PdfPageSetup page = settings.Settings.PdfDefaults;
        ExportLayout layout = settings.Settings.LayoutDefaults;

        return
        [
            $"Page:       {page.Paper}, {page.Orientation.ToString().ToLowerInvariant()}, {page.Margin.ToString().ToLowerInvariant()} margins, backgrounds {(page.IncludeBackgrounds ? "printed" : "left out")}",
            $"Layout:     cover page {OnOff(layout.IncludeCoverPage)}, contents {OnOff(layout.IncludeTableOfContents)}, header and footer {OnOff(layout.IncludeHeaderAndFooter)}",
        ];
    }

    public override Task<ExportOutcome> RunAsync(IDocumentRenderer renderer, ExportInput input, string outputPath, ExportContext run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(run);

        SettingsSource settings = run.Settings;
        Reporter reporter = run.Reporter;
        string text = input.Request.Markdown;
        var job = new PdfExportJob(
            new MarkdigMarkdownRenderer(new ReporterLogger<MarkdigMarkdownRenderer>(reporter)),
            new MarkdownAnalyzer(),
            new ReporterLogger<PdfExportJob>(reporter));

        return job.RunAsync(
            renderer,
            new PdfExportRequest(
                input.Request,
                outputPath,
                PaperFurnitureFor.Title(text, input.Name),
                settings.Settings.PdfDefaults,
                PaperFurnitureFor.Document(text, input.Name, settings.Settings.LayoutDefaults, input.Request.Numbering)),
            cancellationToken);
    }

    private static string OnOff(bool on) => on ? "on" : "off";
}
