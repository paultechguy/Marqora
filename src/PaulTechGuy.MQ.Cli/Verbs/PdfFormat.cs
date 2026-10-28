// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Analysis;
using PaulTechGuy.MQ.Cli.Reporting;
using PaulTechGuy.MQ.Cli.Settings;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Exporting;
using PaulTechGuy.MQ.Rendering;

namespace PaulTechGuy.MQ.Cli.Verbs;

/// <summary>
/// export pdf (WP-M2-10): the app's PDF job, with the document's own title and furniture read the
/// way the app reads them. The page is the settings source's PDF setup, with --paper,
/// --orientation, --margin and --[no-]backgrounds laid over it (CLI-EX-PDF-01); the cover, the
/// contents and the header and footer come from the settings alone, as the requirement keeps them.
/// </summary>
internal sealed class PdfFormat : ExportFormat
{
    /// <summary>What the report says when the classic engine printed the file.</summary>
    public const string ClassicEngine = "Printed by the classic engine";

    private readonly Option<PaperSize?> _paper = new("--paper")
    {
        Description = "The paper: letter, a4 or legal. Without it, the settings' paper, or Letter.",
        HelpName = "letter|a4|legal",
        CustomParser = result => Parse<PaperSize>(result, "--paper"),
    };

    private readonly Option<PageOrientation?> _orientation = new("--orientation")
    {
        Description = "portrait or landscape. Without it, the settings' orientation, or portrait.",
        HelpName = "portrait|landscape",
        CustomParser = result => Parse<PageOrientation>(result, "--orientation"),
    };

    private readonly Option<PageMargin?> _margin = new("--margin")
    {
        Description = $"The margins, as Page Setup names them: {string.Join("; ", PageMargins.Labels)}. Without it, the settings' margins, or normal.",
        HelpName = "normal|narrow|moderate|wide|none",
        CustomParser = result => Parse<PageMargin>(result, "--margin"),
    };

    private readonly Option<bool> _backgrounds = new("--backgrounds")
    {
        Description = "Print background colors and shading, such as code blocks and table headers. The default, unless the settings turn it off.",
    };

    private readonly Option<bool> _noBackgrounds = new("--no-backgrounds")
    {
        Description = "Leave background colors and shading out, for a printer that would spend ink on them.",
    };

    public override string Name => "pdf";

    public override string Extension => ".pdf";

    public override void AddOptions(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);

        command.Options.Add(_paper);
        command.Options.Add(_orientation);
        command.Options.Add(_margin);
        command.Options.Add(_backgrounds);
        command.Options.Add(_noBackgrounds);

        command.Validators.Add(result =>
        {
            if (result.GetValue(_backgrounds) && result.GetValue(_noBackgrounds))
            {
                result.AddError("--backgrounds and --no-backgrounds cannot be combined; choose one.");
            }
        });
    }

    public override SettingsSource Apply(ParseResult parse, SettingsSource settings)
    {
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(settings);

        PdfPageSetup given = settings.Settings.PdfDefaults;

        PdfPageSetup page = given with
        {
            Paper = parse.GetValue(_paper) ?? given.Paper,
            Orientation = parse.GetValue(_orientation) ?? given.Orientation,
            Margin = parse.GetValue(_margin) ?? given.Margin,
            IncludeBackgrounds = parse.GetValue(_noBackgrounds) ? false : parse.GetValue(_backgrounds) || given.IncludeBackgrounds,
        };

        return page == given ? settings : settings with { Settings = settings.Settings with { PdfSetup = page } };
    }

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

    public override async Task<ExportOutcome> RunAsync(IDocumentRenderer renderer, ExportInput input, string outputPath, ExportContext run, CancellationToken cancellationToken)
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

        ExportOutcome outcome = await job.RunAsync(
            renderer,
            new PdfExportRequest(
                input.Request,
                outputPath,
                PaperFurnitureFor.Title(text, input.Name),
                settings.Settings.PdfDefaults,
                PaperFurnitureFor.Document(text, input.Name, settings.Settings.LayoutDefaults, input.Request.Numbering)),
            cancellationToken).ConfigureAwait(true);

        // The app says this on its status line, because the file has none of what the paged
        // engine adds: page numbers, a contents page, a cover. A run says it as advice on the file.
        if (outcome.Engine == PrintEngine.Classic)
        {
            ExportIssue classic = new()
            {
                IsAdvisory = true,
                Problem = ClassicEngine,
                Item = "the paged engine could not lay this document out, so the PDF has no page numbers, contents page or cover",
            };

            outcome = outcome with { Issues = [classic, .. outcome.Issues] };
        }

        return outcome;
    }

    /// <summary>An enum value by its name, ignoring case, or the parser's own error naming the choices.</summary>
    private static TEnum? Parse<TEnum>(System.CommandLine.Parsing.ArgumentResult result, string option)
        where TEnum : struct, Enum
    {
        string token = result.Tokens.Count > 0 ? result.Tokens[0].Value : string.Empty;

        if (Enum.TryParse(token, ignoreCase: true, out TEnum value) && Enum.IsDefined(value) && !int.TryParse(token, out _))
        {
            return value;
        }

        string[] names = [.. Enum.GetNames<TEnum>().Select(n => n.ToLowerInvariant())];

        result.AddError($"{option} takes {string.Join(", ", names[..^1])} or {names[^1]}; '{token}' is not one of them.");

        return null;
    }

    private static string OnOff(bool on) => on ? "on" : "off";
}
