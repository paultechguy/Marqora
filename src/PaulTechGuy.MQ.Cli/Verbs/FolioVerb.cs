// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using PaulTechGuy.MQ.Cli.Commands;
using PaulTechGuy.MQ.Cli.Inputs;
using PaulTechGuy.MQ.Cli.Outputs;
using PaulTechGuy.MQ.Cli.Rendering;
using PaulTechGuy.MQ.Cli.Reporting;
using PaulTechGuy.MQ.Cli.Settings;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Exporting;
using PaulTechGuy.MQ.Folio;
using PaulTechGuy.MQ.Imaging;
using PaulTechGuy.MQ.Rendering;
using PaulTechGuy.MQ.Services;

namespace PaulTechGuy.MQ.Cli.Verbs;

/// <summary>
/// folio: File > Share as Folio, without the window (CLI-FO-01 to CLI-FO-06, WP-M1-16).
///
/// The app's own sequence, read from MainViewModel and followed: plan; fetch the web pictures only
/// under --fetch-web-images, and plan again with what was fetched; shrink; write. The folder and zip
/// forms are written here; the single page is rendered by the run's own browser (WP-M2-12).
///
/// Everything that could stop the run is checked before any of it starts - the form, the output,
/// every input - so a refused run has planned, fetched and written nothing. What is written goes to
/// a sibling first and is moved into place at the end, so a run that fails or is stopped part way
/// leaves no half-made Folio under the name asked for.
///
/// The Folio report becomes findings, one document at a time: a warning for what the shared copy
/// lacks, information for what is worth knowing. Neither fails the run - the Folio was made - which
/// is the export rule too (requirements §5, question 4).
/// </summary>
internal static class FolioVerb
{
    public sealed record Options(
        InputOptions Inputs,
        Option<string> Output,
        Option<string?> Form,
        Option<string?> Theme,
        Option<int?> ShrinkImages,
        Option<int?> Timeout,
        Option<bool> FetchWebImages,
        Option<bool> DryRun,
        Option<bool> Overwrite);

    public static async Task<ExitCode> RunAsync(RunContext context, ParseResult parse, Options options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(options);

        string given = parse.GetValue(options.Output)!;
        string output = context.FullPath(given);
        FolioForm form = FormOf(given, output, parse.GetValue(options.Form));

        int? shrink = parse.GetValue(options.ShrinkImages);
        int seconds = parse.GetValue(options.Timeout) ?? ExportVerb.DefaultTimeoutSeconds;

        if (shrink is <= 0)
        {
            throw new UsageException("--shrink-images takes a width in pixels, greater than 0.");
        }

        if (seconds <= 0)
        {
            throw new UsageException("--timeout takes a number of seconds, greater than 0.");
        }

        int width = shrink ?? 0;
        bool fetch = parse.GetValue(options.FetchWebImages);
        bool dryRun = parse.GetValue(options.DryRun);

        SettingsSource settings = await SettingsSource.LoadAsync(context).ConfigureAwait(false);
        ResolvedTheme theme = ThemeResolver.Resolve(parse.GetValue(options.Theme), settings, context.Environment.Themes());

        (IReadOnlyList<InputFile> files, ExitCode resolved) = Documents.Resolve(context, parse, options.Inputs, InputOrder.AsNamed);

        if (files.Any(f => f.IsStandardInput))
        {
            throw new UsageException("folio shares files, and standard input is not one: its pictures have no folder to be found in. Name the documents.");
        }

        // A Folio short of a document that was named is not the Folio that was asked for.
        if (resolved != ExitCode.Success || files.Count == 0)
        {
            return ExitCodes.Worst(resolved, ExitCode.Input);
        }

        if (!dryRun)
        {
            PlannedOutput planned = new(given, output, form == FolioForm.Folder ? OutputKind.Folder : OutputKind.File);
            ExitCode checkedOut = OutputPlan.Check(context, files, [planned], parse.GetValue(options.Overwrite));

            if (checkedOut != ExitCode.Success)
            {
                return checkedOut;
            }
        }

        List<Document> documents = [];

        foreach (InputFile file in files)
        {
            if (await Documents.ReadAsync(context, file, baseDir: null).ConfigureAwait(false) is { } document)
            {
                documents.Add(document);
            }
        }

        if (documents.Count < files.Count)
        {
            return ExitCode.Input;
        }

        var renderer = new MarkdigMarkdownRenderer(new ReporterLogger<MarkdigMarkdownRenderer>(context.Reporter));
        FolioSource[] sources = [.. documents.Select(d => new FolioSource(d.Input.FullPath!, d.File.Text, renderer.Render(d.File.Text).Links))];

        // Explicitly without a fetched map, as the app's preflight plans: the sites are named
        // before anything goes to them.
        FolioPlan plan = FolioPlanner.Plan(sources, width);

        if (dryRun)
        {
            Preflight(context, given, form, theme, settings, plan, width, fetch);
            Report(context, documents, plan, outputs: [], Outcomes.Printed);

            return ExitCode.Success;
        }

        bool overwrite = parse.GetValue(options.Overwrite);

        // The documents were checked with the output; the pictures are known only now. A folder
        // holding one the Folio collects is never emptied to make room for the Folio.
        if (form == FolioForm.Folder && overwrite && Directory.Exists(output)
            && FolderReplacement.Refusal(output, plan.Assets.Select(a => a.SourcePath), context.Environment.CurrentDirectory) is { } refusal)
        {
            context.Reporter.Error(given, refusal);
            context.Results.Json(new JsonLine { Path = given, Verb = context.Verb, Outcome = Outcomes.Failed, Message = refusal });

            return ExitCode.Output;
        }

        context.Reporter.Detail(given, $"Theme: {theme.Theme.Name}, from {theme.Describe(settings)}.");

        using FolioScratchFolder scratch = FolioScratch.Create(new ReporterLogger(context.Reporter));

        if (fetch && plan.RemoteImages.Count > 0)
        {
            context.Reporter.Detail(given, $"Fetching {FolioPreflight.Count(plan.RemoteImages.Count, "picture")} from {string.Join(", ", plan.RemoteHosts)}.");

            FolioFetchResult fetched = await new FolioFetcher(new ReporterLogger<FolioFetcher>(context.Reporter), VersionVerb.Current)
                .FetchAsync(plan.RemoteImages, scratch.Path, progress: null, context.CancellationToken)
                .ConfigureAwait(false);

            plan = FolioPlanner.Plan(sources, width, fetched.Files);

            if (fetched.Failures.Count > 0)
            {
                plan = plan with { Warnings = [.. plan.Warnings, .. fetched.Failures] };
            }
        }

        if (width > 0)
        {
            var shrinker = new FolioShrinker(
                new ReporterLogger<FolioShrinker>(context.Reporter),
                new WicImageResizer(new ReporterLogger<WicImageResizer>(context.Reporter)));

            plan = await shrinker.ShrinkAsync(plan, width, scratch.Path, context.CancellationToken).ConfigureAwait(false);
        }

        // Weighed with what was fetched and shrunk, since that is what the page would carry, and
        // refused before a browser starts or anything is written (CLI-FO-02). Never switched to the
        // zip on the run's own say: the author named the form.
        if (form == FolioForm.SingleFile && FolioPreflight.Weight(plan, singleFile: true) is var weight and > FolioPreflight.HeavyPageBytes)
        {
            string tooLarge = $"The single page would be about {FolioPreflight.Size(weight)}, past the {FolioPreflight.Size(FolioPreflight.HeavyPageBytes)} the app warns at. "
                + $"Share it as a zip (--form zip, about {FolioPreflight.Size(plan.TotalBytes)}), or make its pictures smaller with --shrink-images.";

            context.Reporter.Error(given, tooLarge);
            context.Results.Json(new JsonLine { Path = given, Verb = context.Verb, Outcome = Outcomes.Failed, Message = tooLarge, Theme = Theme(theme) });

            return ExitCode.Output;
        }

        FolioManifest manifest = FolioManifest.For(plan, VersionVerb.Current, System.Environment.MachineName);
        var writer = new FolioWriter(new ReporterLogger<FolioWriter>(context.Reporter));

        try
        {
            if (form == FolioForm.SingleFile)
            {
                ExitCode page = await WritePageAsync(context, given, output, plan, settings, theme, TimeSpan.FromSeconds(seconds)).ConfigureAwait(false);

                if (page != ExitCode.Success)
                {
                    return page;
                }
            }
            else if (form == FolioForm.Zip)
            {
                await WriteZipAsync(writer, plan, manifest, output, context.CancellationToken).ConfigureAwait(false);
            }
            else
            {
                string? left = await FolderReplacement.ReplaceAsync(output, overwrite, pending => writer.WriteFolderAsync(plan, manifest, pending, context.CancellationToken)).ConfigureAwait(false);

                if (left is not null)
                {
                    context.Reporter.Warning(given, $"The Folio is in place, but what it replaced could not all be deleted; it was left in {left}.");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            string message = $"Could not write the Folio to {output}: {ex.Message}";

            context.Reporter.Error(given, message);
            context.Results.Json(new JsonLine { Path = given, Verb = context.Verb, Outcome = Outcomes.Failed, Message = message, Theme = Theme(theme) });

            return ExitCode.Output;
        }

        Report(context, documents, plan, outputs: [output], Outcomes.Exported);

        context.Reporter.Summary(given, $"shared {FolioPreflight.Count(plan.Documents.Count, "document")} and {FolioPreflight.Count(plan.Assets.Count, "picture")} as {Describe(form)}.");
        context.Results.Json(new JsonLine
        {
            Path = given,
            Verb = context.Verb,
            Outcome = Outcomes.Exported,
            Outputs = [output],
            Theme = Theme(theme),
        });

        return ExitCode.Success;
    }

    /// <summary>
    /// The form, from the output path and --form (CLI-FO-01). A trailing separator or a folder that
    /// exists means the folder form, .html the single page, .zip the zip. --form decides a path
    /// that says nothing, and one that disagrees with the path is a usage error rather than a guess.
    /// </summary>
    internal static FolioForm FormOf(string given, string full, string? named)
    {
        FolioForm? fromPath =
            given.EndsWith('/') || given.EndsWith('\\') || Directory.Exists(full) ? FolioForm.Folder
            : Path.GetExtension(full).ToLowerInvariant() switch
            {
                ".html" or ".htm" => FolioForm.SingleFile,
                ".zip" => FolioForm.Zip,
                _ => null,
            };

        FolioForm? fromOption = named switch
        {
            "single" => FolioForm.SingleFile,
            "folder" => FolioForm.Folder,
            "zip" => FolioForm.Zip,
            _ => null,
        };

        return (fromPath, fromOption) switch
        {
            ({ } path, { } option) when path != option =>
                throw new UsageException($"-o {given} names {Describe(path)}, but --form says {named}."),
            ({ } path, _) => path,
            (null, { } option) => option,
            _ => throw new UsageException($"Cannot tell the form from -o {given}: end it in .zip, .html or a separator, or pass --form."),
        };
    }

    private static string Describe(FolioForm form) => form switch
    {
        FolioForm.SingleFile => "a single page",
        FolioForm.Zip => "a zip",
        _ => "a folder",
    };

    private static JsonTheme Theme(ResolvedTheme theme) => new(theme.Theme.Id, theme.SourceToken);

    /// <summary>
    /// The preflight, as the window shows it before the author decides (CLI-FO-05): one line each on
    /// stdout, and the same figures in the JSON. The problems behind the counts are findings on the
    /// documents' own lines, so each is named where it is.
    /// </summary>
    private static void Preflight(RunContext context, string given, FolioForm form, ResolvedTheme theme, SettingsSource settings, FolioPlan plan, int width, bool fetch)
    {
        bool single = form == FolioForm.SingleFile;
        long estimate = FolioPreflight.Weight(plan, single);
        FolioAdvice? advice = FolioPreflight.Advice(plan, single, width);

        int missing = plan.Warnings.Count(w => w.Kind == FolioWarningKind.MissingImage);
        int outside = plan.Warnings.Count(w => w.Kind == FolioWarningKind.OutsideLink);
        int web = plan.RemoteImages.Count;

        string[] names = [.. plan.Documents.Select(d => d.EntryName)];

        context.Results.Line($"Folio:      {given}, {Describe(form)}");
        context.Results.Line($"Theme:      {theme.Theme.Name}, from {theme.Describe(settings)}");
        context.Results.Line($"Documents:  {plan.Documents.Count}");

        foreach (string name in names)
        {
            context.Results.Line($"  {name}");
        }

        context.Results.Line(plan.RelocatedCount > 0
            ? $"Pictures:   {plan.Assets.Count}, {plan.RelocatedCount} collected from elsewhere"
            : $"Pictures:   {plan.Assets.Count}");

        context.Results.Line($"Missing:    {FolioPreflight.Count(missing, "picture")} not on this machine");
        context.Results.Line($"Outside:    {FolioPreflight.Count(outside, "link")} pointing outside the Folio");

        if (fetch && web > 0)
        {
            // Named, never contacted: this is the list the author agrees to before anything goes out.
            context.Results.Line($"Web:        {FolioPreflight.Count(web, "picture")} to collect from {string.Join(", ", plan.RemoteHosts)}; none contacted by --dry-run");
        }
        else
        {
            context.Results.Line($"Web:        {FolioPreflight.Count(web, "picture")} left on the web{(web > 0 ? "; --fetch-web-images collects them" : string.Empty)}");
        }

        if (width > 0)
        {
            context.Results.Line($"Shrink:     pictures wider than {width} px");
        }

        context.Results.Line($"Estimate:   about {FolioPreflight.Size(estimate)}");

        if (advice is not null)
        {
            context.Results.Line($"Advice:     {advice.Title}. {advice.Message}");
        }

        context.Results.Json(new JsonLine
        {
            Path = given,
            Verb = context.Verb,
            Outcome = Outcomes.Printed,
            Theme = Theme(theme),
            Preflight = new JsonPreflight(
                form switch
                {
                    FolioForm.SingleFile => "single",
                    FolioForm.Zip => "zip",
                    _ => "folder",
                },
                names,
                plan.Assets.Count,
                plan.RelocatedCount,
                missing,
                outside,
                web,
                estimate,
                advice is null ? null : $"{advice.Title}. {advice.Message}",
                fetch ? plan.RemoteHosts : null),
        });
    }

    /// <summary>
    /// What the app's Folio report lists, as findings on each document (CLI-FO-06), in the report's
    /// own words. Every document gets its JSON line, findings or not, so a script can see the set.
    /// </summary>
    private static void Report(RunContext context, IReadOnlyList<Document> documents, FolioPlan plan, IReadOnlyList<string> outputs, string outcome)
    {
        foreach (Document document in documents)
        {
            DocumentFinding[] findings = [.. plan.Warnings
                .Where(w => string.Equals(w.DocumentPath, document.Input.FullPath, StringComparison.OrdinalIgnoreCase))
                .OrderBy(w => w.Line)
                .Select(Finding)];

            foreach (DocumentFinding finding in findings)
            {
                context.Reporter.Finding(document.Display, finding);
            }

            context.Results.Json(new JsonLine
            {
                Path = document.Display,
                Verb = context.Verb,
                Outcome = outcome,
                Outputs = outputs,
                Findings = findings,
            });
        }
    }

    /// <summary>A Folio warning as a finding: its line made one-based, the report's wording, and the kind as its code.</summary>
    internal static DocumentFinding Finding(FolioWarning warning) => new(
        warning.Line + 1,
        0,
        Code(warning.Kind),
        warning.IsAdvisory ? FindingSeverity.Info : FindingSeverity.Warning,
        $"{FolioReportWording.Problem(warning.Kind)}: {FolioReportWording.Item(warning)}");

    /// <summary>The kind, in kebab case: MissingImage is missing-image.</summary>
    private static string Code(FolioWarningKind kind) =>
        string.Concat(kind.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? "-" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    /// <summary>
    /// The single page (WP-M2-12): the app's page job, each document finished by the run's own
    /// browser with RenderDetachedAsync, in the resolved theme's light palette. Written beside its
    /// name and moved onto it, as the zip is. A document whose render does not finish fails the
    /// Folio with exit 5 (CLI-EX-05): the app puts that document in unfinished, and a script has
    /// nobody watching to notice.
    /// </summary>
    private static async Task<ExitCode> WritePageAsync(RunContext context, string given, string output, FolioPlan plan, SettingsSource settings, ResolvedTheme theme, TimeSpan timeout)
    {
        // The packager writes the export theme's palette into the page, and reads it from the
        // settings: the theme this run resolved is put there, as export html does.
        AppSettings fixedSettings = settings.Settings with { ExportColorTheme = theme.Theme.Id };

        var packager = new RenderedHtmlPackager(context.Environment.AppPaths(), context.Environment.Themes(), new FixedSettings(fixedSettings), new ReporterLogger<RenderedHtmlPackager>(context.Reporter));
        var job = new FolioPageJob(
            new MarkdigMarkdownRenderer(new ReporterLogger<MarkdigMarkdownRenderer>(context.Reporter)),
            new FolioHtmlWriter(packager, new ReporterLogger<FolioHtmlWriter>(context.Reporter)),
            new ReporterLogger<FolioPageJob>(context.Reporter));

        var request = new FolioPageRequest(
            plan,
            string.Empty,
            FolioManifest.SuggestedName(plan, DateTimeOffset.Now),
            new Dictionary<string, HeadingNumbering>(),
            fixedSettings.HeadingNumbering,
            fixedSettings.PreviewMaxWidth,
            VersionVerb.Current,
            System.Environment.MachineName);

        var setup = new RenderSetup(
            context.Environment.AppPaths(),
            context.Environment.Themes(),
            theme.Theme.Id,
            PreviewPreferences.FromSettings(fixedSettings),
            timeout,
            context.Reporter);

        string folder = Path.GetDirectoryName(output)!;
        string pending = Path.Combine(folder, $".{Path.GetFileNameWithoutExtension(output)}.{Guid.NewGuid():n}.tmp.html");
        IRendererFactory renderers = context.Environment.CreateRenderers(setup);
        var progress = new DetailLine(line => context.Reporter.Detail(given, line));

        try
        {
            Directory.CreateDirectory(folder);

            ExportOutcome outcome = await renderers
                .RunAsync(renderer => job.RunAsync(new StrictRenderer(renderer, timeout), request with { OutputPath = pending }, progress, context.CancellationToken), context.CancellationToken)
                .ConfigureAwait(false);

            if (!outcome.Written || !File.Exists(pending))
            {
                throw new RenderFailedException("The Folio page could not be built: the renderer handed back nothing to write.");
            }

            File.Move(pending, output, overwrite: true);

            return ExitCode.Success;
        }
        catch (Exception ex) when (ex is RenderFailedException or RendererUnavailableException)
        {
            context.Reporter.Error(given, ex.Message);
            context.Results.Json(new JsonLine { Path = given, Verb = context.Verb, Outcome = Outcomes.Failed, Message = ex.Message, Theme = Theme(theme) });

            return ExitCode.Render;
        }
        finally
        {
            // Ctrl+C included: the browser is ended and its folder removed, and then nothing more
            // can arrive in the page it was writing.
            await renderers.DisposeAsync().ConfigureAwait(false);
            File.Delete(pending);
        }
    }

    /// <summary>The zip, written beside its name and moved onto it, replacing what is there only because --overwrite was given.</summary>
    private static async Task WriteZipAsync(FolioWriter writer, FolioPlan plan, FolioManifest manifest, string output, CancellationToken cancellationToken)
    {
        string folder = Path.GetDirectoryName(output)!;
        string pending = Path.Combine(folder, $".{Path.GetFileName(output)}.{Guid.NewGuid():n}.tmp");

        Directory.CreateDirectory(folder);

        try
        {
            await writer.WriteZipAsync(plan, manifest, pending, cancellationToken).ConfigureAwait(false);
            File.Move(pending, output, overwrite: true);
        }
        finally
        {
            File.Delete(pending);
        }
    }

    /// <summary>
    /// The page job's progress, said at once under --verbose. Not <see cref="Progress{T}"/>, which
    /// posts each report for later, and could put "Rendering..." after the line saying it is done.
    /// </summary>
    private sealed class DetailLine(Action<string> show) : IProgress<string>
    {
        public void Report(string value) => show(value);
    }
}
