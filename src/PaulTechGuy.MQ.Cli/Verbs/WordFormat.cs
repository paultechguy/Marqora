// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Cli.Reporting;
using PaulTechGuy.MQ.Cli.Settings;
using PaulTechGuy.MQ.Docx;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Exporting;

namespace PaulTechGuy.MQ.Cli.Verbs;

/// <summary>
/// export word (WP-M2-11): the app's Word job and exporter, so the file is the one File > Export >
/// Word writes. The diagrams, the math and the code's colors come from the run's own render, and
/// the page and layout from the settings source alone (CLI-EX-WORD-01): Word takes no page flags.
/// </summary>
internal sealed class WordFormat : ExportFormat
{
    public override string Name => "word";

    public override string Extension => ".docx";

    public override IReadOnlyList<string> Setup(SettingsSource settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        DocxExportSetup page = settings.Settings.DocxDefaults;
        ExportLayout layout = settings.Settings.LayoutDefaults;

        return
        [
            $"Page:       {page.Paper}, {page.Orientation.ToString().ToLowerInvariant()}, {page.Margin.ToString().ToLowerInvariant()} margins",
            $"Layout:     cover page {OnOff(layout.IncludeCoverPage)}, contents {OnOff(layout.IncludeTableOfContents)}, header and footer {OnOff(layout.IncludeHeaderAndFooter)}",
        ];
    }

    public override Task<ExportOutcome> RunAsync(IDocumentRenderer renderer, ExportInput input, string outputPath, ExportContext run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(run);

        AppSettings settings = run.Settings.Settings;
        var job = new WordExportJob(new DocxExporter(new ReporterLogger<DocxExporter>(run.Reporter)), new ReporterLogger<WordExportJob>(run.Reporter));

        return job.RunAsync(
            renderer,
            new WordExportRequest(
                input.Request,
                outputPath,
                input.Name,
                settings.DocxDefaults,
                settings.LayoutDefaults,
                DocumentImages.FromDocument(input.Request.DocumentPath),
                run.Themes.Find(input.Request.ColorThemeId)),
            cancellationToken);
    }

    private static string OnOff(bool on) => on ? "on" : "off";
}
