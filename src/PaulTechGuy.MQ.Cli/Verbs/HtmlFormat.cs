// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Cli.Reporting;
using PaulTechGuy.MQ.Cli.Settings;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Exporting;

namespace PaulTechGuy.MQ.Cli.Verbs;

/// <summary>
/// export html (WP-M2-09): the app's HTML job and packager, so the page is the one File > Export >
/// HTML writes. The page width is the settings source's PreviewMaxWidth, as the app's is the
/// reader's own, and the page can go to stdout (CLI-OUT-09).
/// </summary>
internal sealed class HtmlFormat : ExportFormat
{
    public override string Name => "html";

    public override string Extension => ".html";

    public override bool WritesStandardOutput => true;

    public override IReadOnlyList<string> Setup(SettingsSource settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        int width = settings.Settings.PreviewMaxWidth;

        return [width > 0 ? $"Width:      {width} px at most" : "Width:      none; the page is as wide as the window it is opened in"];
    }

    public override Task<ExportOutcome> RunAsync(IDocumentRenderer renderer, ExportInput input, string outputPath, ExportContext run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(run);

        // The packager writes the export theme's palette into the page, and reads it from the
        // settings: the theme this run resolved is put there, so --theme wins as it does for the render.
        AppSettings settings = run.Settings.Settings with { ExportColorTheme = input.Request.ColorThemeId };

        var packager = new RenderedHtmlPackager(run.Paths, run.Themes, new FixedSettings(settings), new ReporterLogger<RenderedHtmlPackager>(run.Reporter));
        var job = new HtmlExportJob(new HtmlExporter(packager, new ReporterLogger<HtmlExporter>(run.Reporter)));

        return job.RunAsync(
            renderer,
            new HtmlExportRequest(
                input.Request,
                outputPath,
                input.Name,
                DocumentImages.FromDocument(input.Request.DocumentPath),
                settings.PreviewMaxWidth),
            cancellationToken);
    }
}
