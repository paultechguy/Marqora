// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using System.Diagnostics;
using PaulTechGuy.MQ.Abstractions;
using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Analysis;
using PaulTechGuy.MQ.Cli.Commands;
using PaulTechGuy.MQ.Cli.Inputs;
using PaulTechGuy.MQ.Cli.Outputs;
using PaulTechGuy.MQ.Cli.Rendering;
using PaulTechGuy.MQ.Cli.Reporting;
using PaulTechGuy.MQ.Cli.Settings;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Exporting;
using PaulTechGuy.MQ.Rendering;
using PaulTechGuy.MQ.Themes;

namespace PaulTechGuy.MQ.Cli.Verbs;

/// <summary>One document an export run is making a file of, with the request its renderer is given.</summary>
/// <param name="Name">The document's file name, which the app calls it by: the title a PDF falls back to.</param>
internal sealed record ExportInput(Document Document, RenderRequest Request, string Name);

/// <summary>What a run gives every format's job: its settings, the color themes, where the shell's files are, and where to say things.</summary>
internal sealed record ExportContext(SettingsSource Settings, ThemeCatalog Themes, IAppPaths Paths, Reporter Reporter);

/// <summary>
/// What one export verb adds to the run every export shares: its extension, its job, and how it
/// describes its page setup to --dry-run and --verbose.
/// </summary>
internal abstract class ExportFormat
{
    /// <summary>"pdf", as the verb is named after it.</summary>
    public abstract string Name { get; }

    /// <summary>".pdf", for the outputs --out-dir names.</summary>
    public abstract string Extension { get; }

    /// <summary>Whether -o - may send the output to stdout: a page can be piped, a PDF or a .docx cannot (CLI-OUT-09).</summary>
    public virtual bool WritesStandardOutput => false;

    /// <summary>The page setup in force, in a line each, or none for a format without pages.</summary>
    public abstract IReadOnlyList<string> Setup(SettingsSource settings);

    /// <summary>Runs the app's own job for this format, writing <paramref name="outputPath"/>.</summary>
    public abstract Task<ExportOutcome> RunAsync(IDocumentRenderer renderer, ExportInput input, string outputPath, ExportContext run, CancellationToken cancellationToken);
}

/// <summary>
/// What every export verb does around its job (WP-M2-08): the outputs checked before anything
/// starts, one browser for the run, a render that does not finish failing its file, the export
/// report turned into findings, and --dry-run.
///
/// In order:
/// <list type="number">
/// <item>The command line: -o for one document, --out-dir for any number, or neither, when each
/// output goes beside its document (amendment A16); never both, and a --timeout above zero.
/// -o - sends the one output to stdout, for a format that can be piped.</item>
/// <item>The settings and the theme, so a typo in either stops the run before any work.</item>
/// <item>Every output checked (CLI-OUT-07, CLI-OUT-08): a refused run starts no browser.</item>
/// <item>--dry-run prints the plan and what the text already shows is missing, and stops.</item>
/// <item>Each document rendered and written, to a file beside its name that is moved onto it only
/// once complete, so Ctrl+C or a failed render leaves no half-written output (CLI-RPT-08).</item>
/// </list>
/// </summary>
internal static class ExportVerb
{
    public sealed record Options(
        InputOptions Inputs,
        Option<string?> Output,
        Option<string?> OutDir,
        Option<string?> Theme,
        Option<int?> Timeout,
        Option<bool> DryRun,
        Option<bool> Overwrite);

    /// <summary>How long a document's render may take without --timeout (CLI-EX-05).</summary>
    public const int DefaultTimeoutSeconds = 60;

    /// <summary>-o -, as a planned output's path: stdout, by way of a file in the temp folder.</summary>
    private const string StandardOutput = "-";

    /// <summary>The checks before a run's dry run lists them: what is missing or does not resolve.</summary>
    private static readonly HashSet<string> PlanFindings = ["missing-image", "broken-link", "dead-anchor"];

    public static async Task<ExitCode> RunAsync(RunContext context, ParseResult parse, Options options, ExportFormat format)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(format);

        string? output = parse.GetValue(options.Output);
        string? outDir = parse.GetValue(options.OutDir);
        int seconds = parse.GetValue(options.Timeout) ?? DefaultTimeoutSeconds;
        bool dryRun = parse.GetValue(options.DryRun);

        if (output is not null && outDir is not null)
        {
            throw new UsageException("-o and --out-dir cannot be combined: -o names one output, --out-dir a folder for several.");
        }

        bool toStandardOutput = output == StandardOutput;

        if (toStandardOutput && !format.WritesStandardOutput)
        {
            throw new UsageException($"export {format.Name} writes a file, so -o - cannot be used: name the file.");
        }

        if (seconds <= 0)
        {
            throw new UsageException("--timeout takes a number of seconds, greater than 0.");
        }

        SettingsSource settings = await SettingsSource.LoadAsync(context).ConfigureAwait(false);
        ResolvedTheme theme = ThemeResolver.Resolve(parse.GetValue(options.Theme), settings, context.Environment.Themes());

        (IReadOnlyList<InputFile> files, ExitCode resolved) = Documents.Resolve(context, parse, options.Inputs);

        if (files.Count == 0)
        {
            return ExitCodes.Worst(resolved, ExitCode.Input);
        }

        if (output is not null && files.Count > 1)
        {
            throw new UsageException($"-o names one output, and {files.Count} documents were given. Use --out-dir for several.");
        }

        if (output is null && files.Any(f => f.IsStandardInput))
        {
            throw new UsageException("Standard input has no name to give its output; name the file with -o.");
        }

        // Without -o or --out-dir, each output goes beside its document, as File > Export offers.
        PlannedOutput[] planned = [.. files.Select(file => new PlannedOutput(
            file.Display,
            toStandardOutput ? StandardOutput
            : output is not null
                ? context.FullPath(output)
                : Path.Combine(outDir is not null ? context.FullPath(outDir) : Path.GetDirectoryName(file.FullPath!)!, Path.GetFileNameWithoutExtension(file.FullPath!) + format.Extension),
            OutputKind.File))];

        if (!dryRun && !toStandardOutput)
        {
            ExitCode checkedOut = OutputPlan.Check(context, files, planned, parse.GetValue(options.Overwrite));

            if (checkedOut != ExitCode.Success)
            {
                return ExitCodes.Worst(resolved, checkedOut);
            }
        }

        context.Reporter.Detail(null, $"Theme: {theme.Theme.Name}, from {theme.Describe(settings)}.");

        foreach (string line in format.Setup(settings))
        {
            context.Reporter.Detail(null, line);
        }

        if (dryRun)
        {
            return ExitCodes.Worst(resolved, await DryRunAsync(context, files, planned, format, settings, theme).ConfigureAwait(false));
        }

        var setup = new RenderSetup(
            context.Environment.AppPaths(),
            context.Environment.Themes(),
            theme.Theme.Id,
            PreviewPreferences.FromSettings(settings.Settings),
            TimeSpan.FromSeconds(seconds),
            context.Reporter);

        IRendererFactory renderers = context.Environment.CreateRenderers(setup);
        var pending = new List<string>();

        try
        {
            return ExitCodes.Worst(resolved, await ExportAsync(context, files, planned, format, settings, theme, renderers, setup.Timeout, pending).ConfigureAwait(false));
        }
        finally
        {
            // Ctrl+C included: the browser is ended and its folder removed before the run ends.
            await renderers.DisposeAsync().ConfigureAwait(false);

            // A print abandoned at Ctrl+C can finish writing after its file was cleared away. Its
            // browser is gone now, so nothing more can arrive.
            foreach (string file in pending)
            {
                Discard(context, file);
            }
        }
    }

    private static async Task<ExitCode> ExportAsync(
        RunContext context,
        IReadOnlyList<InputFile> files,
        PlannedOutput[] planned,
        ExportFormat format,
        SettingsSource settings,
        ResolvedTheme theme,
        IRendererFactory renderers,
        TimeSpan timeout,
        List<string> pending)
    {
        ExitCode worst = ExitCode.Success;

        for (int index = 0; index < files.Count; index++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            ExitCode code;

            try
            {
                code = await OneAsync(context, files[index], planned[index], format, settings, theme, renderers, timeout, pending).ConfigureAwait(false);
            }
            catch (RendererUnavailableException ex)
            {
                // Every file would meet the same: said once, and the run ends.
                context.Reporter.Error(null, ex.Message);

                foreach (InputFile file in files.Skip(index))
                {
                    context.Results.Json(new JsonLine { Path = file.Display, Verb = context.Verb, Outcome = Outcomes.Failed, Message = ex.Message });
                }

                return ExitCodes.Worst(worst, ExitCode.Render);
            }

            worst = ExitCodes.Worst(worst, code);

            if (context.FailFast && code != ExitCode.Success)
            {
                foreach (InputFile skipped in files.Skip(index + 1))
                {
                    context.Results.Json(new JsonLine { Path = skipped.Display, Verb = context.Verb, Outcome = Outcomes.Skipped });
                }

                break;
            }
        }

        return worst;
    }

    private static async Task<ExitCode> OneAsync(
        RunContext context,
        InputFile file,
        PlannedOutput planned,
        ExportFormat format,
        SettingsSource settings,
        ResolvedTheme theme,
        IRendererFactory renderers,
        TimeSpan timeout,
        List<string> pending)
    {
        if (await Documents.ReadAsync(context, file, baseDir: null).ConfigureAwait(false) is not { } document)
        {
            return ExitCode.Input;
        }

        ExportInput input = InputFor(document, settings, theme);
        var run = new ExportContext(settings, context.Environment.Themes(), context.Environment.AppPaths(), context.Reporter);
        bool toStandardOutput = planned.FullPath == StandardOutput;

        // Written beside its name and moved onto it; for stdout, written in the temp folder and copied out.
        string folder = toStandardOutput ? Path.GetTempPath() : Path.GetDirectoryName(planned.FullPath)!;
        string stem = toStandardOutput ? "marqora-cli-stdout" : Path.GetFileNameWithoutExtension(planned.FullPath);
        string writing = Path.Combine(folder, $".{stem}.{Guid.NewGuid():n}.tmp{format.Extension}");
        var timer = Stopwatch.StartNew();

        pending.Add(writing);

        try
        {
            Directory.CreateDirectory(folder);

            ExportOutcome outcome = await renderers
                .RunAsync(renderer => format.RunAsync(new StrictRenderer(renderer, timeout), input, writing, run, context.CancellationToken), context.CancellationToken)
                .ConfigureAwait(false);

            if (!outcome.Written || !File.Exists(writing))
            {
                throw new RenderFailedException("The document could not be rendered: the renderer handed back nothing to write.");
            }

            string? page = null;

            if (toStandardOutput)
            {
                // The page as it would be in the file, byte for byte: UTF-8 without a mark, as stdout is.
                page = await File.ReadAllTextAsync(writing, context.CancellationToken).ConfigureAwait(false);
                context.Results.Text(page);
            }
            else
            {
                File.Move(writing, planned.FullPath, overwrite: true);
            }

            DocumentFinding[] findings = [.. outcome.Issues.Select(Finding)];

            foreach (DocumentFinding finding in findings)
            {
                context.Reporter.Finding(document.Display, finding);
            }

            context.Reporter.Detail(document.Display, $"Exported in {timer.ElapsedMilliseconds} ms.");
            context.Reporter.Summary(document.Display, toStandardOutput ? "exported to standard output." : $"exported to {planned.FullPath}.");
            context.Results.Json(new JsonLine
            {
                Path = document.Display,
                Verb = context.Verb,
                Outcome = toStandardOutput ? Outcomes.Printed : Outcomes.Exported,
                Outputs = toStandardOutput ? [] : [planned.FullPath],
                Findings = findings,
                Theme = new JsonTheme(theme.Theme.Id, theme.SourceToken),
                Text = page,
            });

            return ExitCode.Success;
        }
        catch (RenderFailedException ex)
        {
            return Fail(context, document.Display, ExitCode.Render, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(context, document.Display, ExitCode.Output, $"Could not write {planned.FullPath}: {ex.Message}");
        }
        finally
        {
            Discard(context, writing);
        }
    }

    /// <summary>Removes a file an export did not finish, if it is there.</summary>
    private static void Discard(RunContext context, string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            context.Reporter.Warning(null, $"Could not remove {file}: {ex.Message}");
        }
    }

    /// <summary>
    /// The plan, on stdout, document by document (CLI-EX-04): where each output would go, the theme
    /// and its source, the page setup, and as findings what the text already shows is missing or
    /// does not resolve. Nothing is rendered, so nothing a render would find is listed.
    /// </summary>
    private static async Task<ExitCode> DryRunAsync(
        RunContext context,
        IReadOnlyList<InputFile> files,
        PlannedOutput[] planned,
        ExportFormat format,
        SettingsSource settings,
        ResolvedTheme theme)
    {
        var renderer = new MarkdigMarkdownRenderer(new ReporterLogger<MarkdigMarkdownRenderer>(context.Reporter));
        var analyzer = new MarkdownAnalyzer();
        ExitCode worst = ExitCode.Success;

        context.Results.Line($"Theme:      {theme.Theme.Name}, from {theme.Describe(settings)}");

        foreach (string line in format.Setup(settings))
        {
            context.Results.Line($"            {line}");
        }

        for (int index = 0; index < files.Count; index++)
        {
            if (await Documents.ReadAsync(context, files[index], baseDir: null).ConfigureAwait(false) is not { } document)
            {
                worst = ExitCodes.Worst(worst, ExitCode.Input);
                continue;
            }

            context.Results.Line($"Export:     {document.Display} -> {(planned[index].FullPath == StandardOutput ? "standard output" : planned[index].FullPath)}");

            DocumentFinding[] findings = [.. CheckVerb.Findings(document, renderer, analyzer, withStyle: false, externalIsError: false)
                .Where(f => PlanFindings.Contains(f.Code))
                .Select(f => f with { Severity = FindingSeverity.Warning })];

            foreach (DocumentFinding finding in findings)
            {
                context.Reporter.Finding(document.Display, finding);
            }

            context.Results.Json(new JsonLine
            {
                Path = document.Display,
                Verb = context.Verb,
                Outcome = Outcomes.Printed,
                Findings = findings,
                Theme = new JsonTheme(theme.Theme.Id, theme.SourceToken),
            });
        }

        return worst;
    }

    private static ExportInput InputFor(Document document, SettingsSource settings, ResolvedTheme theme) => new(
        document,
        new RenderRequest(document.File.Text, document.LocationPath, settings.Settings.HeadingNumbering, theme.Theme.Id),
        Path.GetFileName(document.LocationPath));

    /// <summary>
    /// A row of the export report as a finding (CLI-EX-03): its line, the report's own words, and
    /// the problem as its code. A warning for what the file lacks, information for an advisory;
    /// neither changes the exit code, since the file was made.
    /// </summary>
    internal static DocumentFinding Finding(ExportIssue issue) => new(
        issue.Line,
        0,
        Code(issue.Problem),
        issue.IsAdvisory ? FindingSeverity.Info : FindingSeverity.Warning,
        $"{issue.Problem}: {issue.Item}");

    /// <summary>"Not on this machine" as not-on-this-machine.</summary>
    private static string Code(string problem) =>
        string.Join('-', problem.ToLowerInvariant().Split([' ', '\'', ','], StringSplitOptions.RemoveEmptyEntries));

    private static ExitCode Fail(RunContext context, string path, ExitCode code, string message)
    {
        context.Reporter.Error(path, message);
        context.Results.Json(new JsonLine { Path = path, Verb = context.Verb, Outcome = Outcomes.Failed, Message = message });

        return code;
    }
}
