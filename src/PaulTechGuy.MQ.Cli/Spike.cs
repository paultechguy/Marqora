// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

#if MQ_SPIKE

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Analysis;
using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.Cli.Verbs;
using PaulTechGuy.MQ.Docx;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Exporting;
using PaulTechGuy.MQ.Rendering;
using PaulTechGuy.MQ.Repositories;
using PaulTechGuy.MQ.Themes;
using PaulTechGuy.MQ.WebRender;
using PaulTechGuy.MQ.WebRender.Win32;

namespace PaulTechGuy.MQ.Cli;

/// <summary>
/// The M0 spike (docs/CLI-Implementation-Plan.md, WP-M0-01 to WP-M0-03): markdown files in, a PDF
/// each out, through the shell in a hidden WebView2 and the app's own paged engine.
///
/// <code>marqora-cli __spike-pdf &lt;in.md…&gt; -o &lt;folder&gt; [--parent message-only|hidden|cloaked]
///     [--no-throttle] [--timeout &lt;s&gt;] [--cancel-after &lt;ms&gt;] [--verbose]
///     [--settings &lt;preferences.json&gt;] [--theme &lt;id&gt;]</code>
///
/// With <c>--settings</c>, a Word file each as well, through the app's own Word job and with those
/// settings, as the app makes one since WP-M2-07.
///
/// It answers the spike's questions and nothing else, and goes in WP-M2-10 when export pdf
/// replaces it. Timings go to stderr, measured from process start (CLI-SP-02).
/// </summary>
internal static class Spike
{
    private const string Usage =
        "usage: marqora-cli __spike-pdf <in.md...> -o <folder> [--parent message-only|hidden|cloaked] "
        + "[--no-throttle] [--timeout <seconds>] [--cancel-after <ms>] [--verbose] [--settings <preferences.json>] [--theme <id>]";

    /// <summary>
    /// Chromium's throttling of pages it believes are hidden or covered, switched off. Paged.js
    /// steps its layout on animation frames, which is what a hidden page stops producing.
    /// </summary>
    private const string NoThrottleArguments =
        "--disable-features=CalculateNativeWinOcclusion --disable-backgrounding-occluded-windows "
        + "--disable-renderer-backgrounding --disable-background-timer-throttling";

    private static readonly DateTime ProcessStart = Process.GetCurrentProcess().StartTime;

    public static async Task<int> RunAsync(string[] args)
    {
        if (!TryParse(args, out Options? options, out string? problem))
        {
            Console.Error.WriteLine($"marqora-cli: {problem}");
            Console.Error.WriteLine(Usage);
            return 2;
        }

        foreach (string input in options.Inputs)
        {
            if (!File.Exists(input))
            {
                Console.Error.WriteLine($"marqora-cli: {input}: no such file.");
                return 3;
            }
        }

        LogLevel level = options.Verbose ? LogLevel.Debug : LogLevel.Warning;
        var logger = new StderrLogger(level);

        using var cancel = new CancellationTokenSource();

        // Ctrl+C and Ctrl+Break both cancel. The handler says it has been dealt with, so the
        // runtime does not end the process before the browsers and the data folder are cleaned up.
        using PosixSignalRegistration interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Cancel);
        using PosixSignalRegistration quit = PosixSignalRegistration.Create(PosixSignal.SIGQUIT, Cancel);

        void Cancel(PosixSignalContext context)
        {
            context.Cancel = true;
            cancel.Cancel();
        }

        if (options.CancelAfter is { } after)
        {
            cancel.CancelAfter(after);
        }

        string run = PrepareRunFolder(logger, options.NoThrottle);

        Directory.CreateDirectory(options.OutputFolder);

        var assets = new WebAssetProvider(new AppPaths(), new StderrLogger<WebAssetProvider>(level));

        if (!assets.IsAvailable)
        {
            Console.Error.WriteLine($"marqora-cli: the shell is incomplete in {assets.RootDirectory}: {string.Join(", ", assets.MissingAssets)}.");
            return 5;
        }

        var renderer = new MarkdigMarkdownRenderer(new StderrLogger<MarkdigMarkdownRenderer>(level));
        ThemeCatalog themes = ThemeCatalog.Load();
        AppSettings? settings = null;

        if (options.SettingsFile is { } settingsFile)
        {
            PreferencesImportResult imported = await new PreferencesTransferService(VersionVerb.Current, new StderrLogger<PreferencesTransferService>(level))
                .ImportAsync(settingsFile, new AppSettings(), cancel.Token)
                .ConfigureAwait(true);

            if (!imported.Succeeded)
            {
                Console.Error.WriteLine($"marqora-cli: {settingsFile}: {imported.Failure}");
                return 2;
            }

            settings = imported.Settings!;
        }

        HeadingNumbering numbering = settings?.HeadingNumbering ?? HeadingNumbering.Off;
        var wordJob = new WordExportJob(new DocxExporter(new StderrLogger<DocxExporter>(level)), new StderrLogger<WordExportJob>(level));
        var pdfJob = new PdfExportJob(renderer, new MarkdownAnalyzer(), new StderrLogger<PdfExportJob>(level));

        Mark($"starting, parent window {options.Parent}{(options.NoThrottle ? ", throttling off" : string.Empty)}");

        using ParentWindow parent = ParentWindow.Create(options.Parent, 1280, 1600);

        HeadlessRenderHost? host = null;
        string? writing = null;
        int exit = 0;

        try
        {
            host = await HeadlessRenderHost.StartAsync(
                parent.Handle, assets, Path.Combine(run, "WebView2-Render"), themes, options.ThemeId, renderer, options.Timeout, logger, cancel.Token)
                .ConfigureAwait(true);

            Mark($"shell ready, browser process {host.BrowserProcessId}");

            if (settings is not null)
            {
                host.ApplyPreferences(PreviewPreferences.FromSettings(settings));
            }

            foreach (string input in options.Inputs)
            {
                string text = await File.ReadAllTextAsync(input, cancel.Token).ConfigureAwait(true);
                string name = Path.GetFileNameWithoutExtension(input);

                writing = Path.Combine(options.OutputFolder, name + ".pdf");

                // The app's own PDF job (WP-M2-06): rendered, printed, and the pictures it leaves out.
                Task<ExportOutcome> printing = pdfJob.RunAsync(
                    host,
                    new PdfExportRequest(
                        new RenderRequest(text, Path.GetFullPath(input), numbering, options.ThemeId),
                        writing,
                        name,
                        PdfPageSetup.Default,
                        new PaperFurniture(null, null, HeaderAndFooter: true)),
                    cancel.Token);

                ExportOutcome printed = await WithinAsync(printing, options.Timeout, $"{name}: the export did not finish.", cancel.Token)
                    .ConfigureAwait(true);

                writing = null;

                Mark($"{name}: printed by the {printed.Engine?.ToString().ToLowerInvariant()} engine, {new FileInfo(printed.Outputs[0]).Length:N0} bytes, {printed.Issues.Count} issues");

                foreach (ExportIssue issue in printed.Issues)
                {
                    Mark($"{name}:   {issue}");
                }

                if (settings is not null)
                {
                    ExportOutcome word = await wordJob.RunAsync(
                        host,
                        new WordExportRequest(
                            new RenderRequest(text, Path.GetFullPath(input), numbering, options.ThemeId),
                            Path.Combine(options.OutputFolder, name + ".docx"),
                            Path.GetFileName(input),
                            settings.DocxDefaults,
                            settings.LayoutDefaults,
                            DocumentImages.FromDocument(Path.GetFullPath(input)),
                            themes.Find(options.ThemeId)),
                        cancel.Token).ConfigureAwait(true);

                    Mark($"{name}: Word, {new FileInfo(word.Outputs[0]).Length:N0} bytes, {word.Issues.Count} issues");
                }
            }
        }
        catch (OperationCanceledException)
        {
            Mark("canceled");
            exit = 130;
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or COMException or IOException)
        {
            Console.Error.WriteLine($"marqora-cli: {ex.Message}");
            logger.LogDebug(ex, "The spike failed.");
            exit = 5;
        }
        finally
        {
            if (host is not null)
            {
                await host.DisposeAsync().ConfigureAwait(true);
            }

            PagedPrintHost.EndBrowser(logger);

            // A PDF the run did not finish is not left looking like one that it did.
            if (writing is not null && File.Exists(writing))
            {
                File.Delete(writing);
            }

            RemoveRunFolder(run, logger);

            Mark($"done, exit {exit}");
        }

        return exit;
    }

    /// <summary>
    /// The run's own data folder under %TEMP%\Marqora-cli, named for this process, with any left
    /// by a run that did not get to clean up removed first. Every WEBVIEW2_ variable inherited
    /// from outside is cleared, so the run does not depend on the shell it was started from.
    /// </summary>
    private static string PrepareRunFolder(ILogger logger, bool noThrottle)
    {
        foreach (string name in Environment.GetEnvironmentVariables().Keys.Cast<string>())
        {
            if (name.StartsWith("WEBVIEW2_", StringComparison.OrdinalIgnoreCase))
            {
                Environment.SetEnvironmentVariable(name, null);
            }
        }

        string root = Path.Combine(Path.GetTempPath(), "Marqora-cli");

        SweepStale(root, logger);

        string run = Path.Combine(
            root,
            string.Create(CultureInfo.InvariantCulture, $"{Environment.ProcessId}-{ProcessStart.Ticks}"));

        Directory.CreateDirectory(run);

        // The paged engine's folder is this one with -Print on the end, as the app's is (F3).
        // Nothing is created at this path itself.
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(run, "WebView2"));
        PagedPrintHost.Configure(PagedPrintHost.BesideUserDataFolderAsync);

        if (noThrottle)
        {
            Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", NoThrottleArguments);
        }

        return run;
    }

    /// <summary>Removes run folders whose process is no longer running.</summary>
    private static void SweepStale(string root, ILogger logger)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (string folder in Directory.EnumerateDirectories(root))
        {
            string name = Path.GetFileName(folder);
            int dash = name.IndexOf('-', StringComparison.Ordinal);

            if (dash <= 0
                || !int.TryParse(name.AsSpan(0, dash), NumberStyles.None, CultureInfo.InvariantCulture, out int id)
                || !long.TryParse(name.AsSpan(dash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out long ticks))
            {
                continue;
            }

            if (IsRunning(id, ticks))
            {
                continue;
            }

            logger.LogDebug("Removing {Folder}, left by a run that did not finish.", folder);
            RemoveRunFolder(folder, logger);
        }
    }

    /// <summary>The same process, not merely the same id: an id is reused once its process ends.</summary>
    private static bool IsRunning(int id, long startTicks)
    {
        try
        {
            using Process process = Process.GetProcessById(id);
            return process.StartTime.Ticks == startTicks;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes a run folder, retrying while a browser that has only just exited still holds a
    /// file in it.
    /// </summary>
    private static void RemoveRunFolder(string folder, ILogger logger)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }

                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(250);
            }
        }

        logger.LogWarning("Could not remove {Folder}; a later run removes it.", folder);
    }

    private static async Task<T> WithinAsync<T>(Task<T> task, TimeSpan timeout, string message, CancellationToken cancellationToken)
    {
        Task finished = await Task.WhenAny(task, Task.Delay(timeout, cancellationToken)).ConfigureAwait(true);

        cancellationToken.ThrowIfCancellationRequested();

        if (finished != task)
        {
            throw new TimeoutException(message);
        }

        return await task.ConfigureAwait(true);
    }

    private static void Mark(string what) =>
        Console.Error.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"marqora-cli: spike: {(DateTime.Now - ProcessStart).TotalMilliseconds,7:0} ms  {what}"));

    private static bool TryParse(string[] args, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Options? options, out string? problem)
    {
        options = null;
        problem = null;

        var inputs = new List<string>();
        string? output = null;
        ParentWindowKind parent = ParentWindowKind.MessageOnly;
        bool noThrottle = false;
        bool verbose = false;
        int timeoutSeconds = 60;
        string? settingsFile = null;
        string theme = "default";
        int? cancelAfter = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            string? Next() => i + 1 < args.Length ? args[++i] : null;

            switch (arg)
            {
                case "-o":
                    output = Next();
                    break;

                case "--parent":
                    switch (Next())
                    {
                        case "message-only":
                            parent = ParentWindowKind.MessageOnly;
                            break;
                        case "hidden":
                            parent = ParentWindowKind.HiddenPopup;
                            break;
                        case "cloaked":
                            parent = ParentWindowKind.CloakedPopup;
                            break;
                        default:
                            problem = "--parent takes message-only, hidden or cloaked.";
                            return false;
                    }

                    break;

                case "--no-throttle":
                    noThrottle = true;
                    break;

                case "--settings":
                    settingsFile = Next();
                    break;

                case "--theme":
                    theme = Next() ?? theme;
                    break;

                case "--verbose":
                    verbose = true;
                    break;

                case "--timeout":
                    if (!int.TryParse(Next(), NumberStyles.None, CultureInfo.InvariantCulture, out timeoutSeconds) || timeoutSeconds <= 0)
                    {
                        problem = "--timeout takes a number of seconds.";
                        return false;
                    }

                    break;

                case "--cancel-after":
                    if (!int.TryParse(Next(), NumberStyles.None, CultureInfo.InvariantCulture, out int ms))
                    {
                        problem = "--cancel-after takes a number of milliseconds.";
                        return false;
                    }

                    cancelAfter = ms;
                    break;

                default:
                    if (arg.StartsWith('-'))
                    {
                        problem = $"unknown option {arg}.";
                        return false;
                    }

                    inputs.Add(arg);
                    break;
            }
        }

        if (inputs.Count == 0 || output is null)
        {
            problem = "give at least one markdown file and -o <folder>.";
            return false;
        }

        options = new Options(inputs, output, parent, noThrottle, verbose, TimeSpan.FromSeconds(timeoutSeconds), cancelAfter, settingsFile, theme);
        return true;
    }

    private sealed record Options(
        IReadOnlyList<string> Inputs,
        string OutputFolder,
        ParentWindowKind Parent,
        bool NoThrottle,
        bool Verbose,
        TimeSpan Timeout,
        int? CancelAfter,
        string? SettingsFile,
        string ThemeId);
}

#endif
