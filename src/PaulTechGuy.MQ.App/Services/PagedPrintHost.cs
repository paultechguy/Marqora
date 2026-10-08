// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Domain;
using Windows.Foundation;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// The paged engine laid the document out, then failed while writing the PDF or sending the
/// pages to the printer.
///
/// Separate from a failure during layout because the two call for different answers. Before
/// anything is sent, the classic engine can take the job over. Once pages may have reached a
/// printer's spooler it cannot: falling back would print the document twice.
/// </summary>
internal sealed class PagedOutputException(string output, Exception inner)
    : Exception($"The paged engine could not finish the {output}: {inner.Message}", inner);

/// <summary>How the print page's controller is kept out of sight. Spike S1 tried both; only OffScreen lays out.</summary>
internal enum PagedHostMode
{
    /// <summary>
    /// <c>IsVisible = false</c>. Tested on 2026-10-06 and it does not work: Chromium treats a
    /// controller that is not visible as a hidden page and stops producing the animation
    /// frames Paged.js steps its layout on, so the fixture never finished - no PDF, no error,
    /// until the layout timeout. Kept so the result stays reproducible; nothing offers it.
    /// </summary>
    Hidden,

    /// <summary>
    /// Visible, with its bounds outside the window's client area, so nothing is drawn on
    /// screen but the page is not hidden as far as the browser knows. The mode that works:
    /// the fixture laid out in about a second (61 pages) and printed in under three.
    /// </summary>
    OffScreen,
}

/// <summary>
/// The page box Paged.js lays out against, in inches: the paper after orientation, and the
/// margins inside it. The same four figures whether the pages go to a file or a printer.
/// </summary>
internal sealed record PagedPage(
    double WidthInches,
    double HeightInches,
    double VerticalMarginInches,
    double HorizontalMarginInches)
{
    public static PagedPage Of(PdfPageSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);

        return new(setup.WidthInches, setup.HeightInches, setup.VerticalMarginInches, setup.HorizontalMarginInches);
    }

    public static PagedPage Of(PrintJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        return new(job.WidthInches, job.HeightInches, job.VerticalMarginInches, job.HorizontalMarginInches);
    }

    /// <summary>
    /// The same page with an edge taken off every side and out of every margin, so what is
    /// inside the margins does not move. A margin smaller than the edge is kept at zero rather
    /// than going negative; that page's text then sits the difference further in.
    /// </summary>
    public PagedPage Inset(double edge) => new(
        WidthInches - 2 * edge,
        HeightInches - 2 * edge,
        Math.Max(0, VerticalMarginInches - edge),
        Math.Max(0, HorizontalMarginInches - edge));
}

/// <summary>What one paged job measured, for the log.</summary>
internal sealed record PagedPrintResult(
    int Pages,
    TimeSpan Layout,
    TimeSpan Output,
    long Bytes,
    bool Tagged,
    bool Outline,
    string Visibility,
    IReadOnlyDictionary<string, bool> Faces,
    int NotesMoved,
    int NotesPlaced,
    int ContentsEntries);

/// <summary>
/// Lays a document out into pages with Paged.js in a WebView2 that is never shown, then prints
/// the pages: to a PDF through the DevTools protocol, or to a printer.
///
/// The engine the alignment plan chose (docs/Export-Alignment-Plan.md, D2, §6.2), behind every
/// PDF and printout Marqora makes - the document's, the cheatsheet's and a diagram pop-out's -
/// through <see cref="PagedJobs"/>, which falls back to the classic engine when this one cannot
/// finish. The week-one spike (§4) answered whether a hidden controller lays out at all, how
/// fast, at what scale and with which faces, and what a real printer makes of the pages.
///
/// One controller per job, created and closed here, one job at a time, in an environment of
/// its own (see <see cref="EndBrowser"/>). Paged.js cannot run twice in one page, so a fresh
/// navigation per job is the design either way.
/// </summary>
internal static class PagedPrintHost
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);

    /// <summary>One paged job at a time, whichever window asked for it.</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>
    /// The paged engine's own WebView2 environment, made on first use. Null until then, and
    /// again after <see cref="EndBrowser"/>, so the next job starts a fresh browser.
    ///
    /// Its own, in a data folder beside the preview's rather than the preview's own, because a
    /// print can hang inside Chromium where no API reaches it, and a hung print outlives the
    /// app. While it shared the preview's folder, the orphan it left held that folder, and the
    /// next launch's preview could not start: every view came up empty. In a folder of its own
    /// a stuck print blocks only prints, and its browser can be ended without the preview's.
    /// </summary>
    private static CoreWebView2Environment? _environment;

    /// <summary>The paged engine's browser process, once a job has started one; 0 before.</summary>
    private static int _browserProcessId;

    /// <summary>The variable Program.cs names the preview's WebView2 data folder with.</summary>
    private const string UserDataVariable = "WEBVIEW2_USER_DATA_FOLDER";

    /// <summary>
    /// The paged engine's environment, made the first time a job needs it, in the folder
    /// beside the preview's (Program.cs sets that one through WEBVIEW2_USER_DATA_FOLDER).
    /// </summary>
    private static async Task<CoreWebView2Environment> EnvironmentAsync()
    {
        if (_environment is { } existing)
        {
            return existing;
        }

        string preview = Environment.GetEnvironmentVariable(UserDataVariable)
            ?? throw new InvalidOperationException("The WebView2 data folder is not set.");

        string print = preview.TrimEnd(Path.DirectorySeparatorChar) + "-Print";

        // The variable outranks the folder passed here - WebView2 reads it first - so passed
        // alone, the folder was ignored: the "own" environment was the preview's folder under
        // different options, which WebView2 refuses (0x8007139F, not in the correct state), and
        // every paged job fell back to the classic engine. So the variable names the print
        // folder for the one call that reads it, and is put back before anything else runs:
        // this is the UI thread and nothing is awaited in between, so no other WebView can be
        // created while it is changed.
        IAsyncOperation<CoreWebView2Environment> creating;

        Environment.SetEnvironmentVariable(UserDataVariable, print);

        try
        {
            creating = CoreWebView2Environment.CreateWithOptionsAsync(
                browserExecutableFolder: null,
                userDataFolder: print,
                options: new CoreWebView2EnvironmentOptions());
        }
        finally
        {
            Environment.SetEnvironmentVariable(UserDataVariable, preview);
        }

        _environment = await creating;

        return _environment;
    }

    /// <summary>
    /// Ends the paged engine's browser process and everything under it, and forgets the
    /// environment so the next job makes another.
    ///
    /// Called when a job times out - a print stuck inside Chromium does not stop because the
    /// app stopped waiting for it - and at shutdown, so nothing outlives the app. The preview
    /// is untouched: it runs in another environment, in another folder. A job a printer's
    /// spooler already holds stays there; the queue is the printer's to clear.
    /// </summary>
    public static void EndBrowser(ILogger? logger = null)
    {
        int id = _browserProcessId;

        _environment = null;
        _browserProcessId = 0;

        if (id == 0)
        {
            return;
        }

        try
        {
            using Process browser = Process.GetProcessById(id);

            browser.Kill(entireProcessTree: true);
            logger?.LogWarning("Ended the paged engine's browser, process {Id}.", id);
        }
        catch (ArgumentException)
        {
            // Already gone, which is the usual case at shutdown.
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger?.LogWarning(ex, "Could not end the paged engine's browser, process {Id}.", id);
        }
    }
    private static readonly TimeSpan LayoutTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PrintTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The margin a printer job asks the printer for, in inches.
    ///
    /// Not zero, though Paged.js draws the page's margins itself. A job of zero margins asks a
    /// printer for edge-to-edge, which a laser cannot do: sent to an HP LaserJet, both pages
    /// reached the spooler and Chromium never closed the job, which spooled until its process
    /// was killed. Microsoft Print to PDF took the same pages without complaint. So the pages
    /// are laid out this much smaller on every side, with margins this much smaller, and the
    /// printer is asked for exactly this much - the text, header and footer land where they
    /// would have, and nothing asks the printer for its unprintable edge.
    /// </summary>
    private const double PrinterEdgeInches = 0.25;

    /// <param name="printMarkup">
    /// The shell's <c>requestPrintHtml</c> answer: JSON with the markup, the theme stylesheet
    /// and the root style.
    /// </param>
    public static Task<PagedPrintResult> ExportAsync(
        IntPtr parentWindow,
        IWebAssetProvider assets,
        string printMarkup,
        PdfPageSetup setup,
        string title,
        PaperFurniture furniture,
        string path,
        PagedHostMode mode,
        Windows.Foundation.TypedEventHandler<CoreWebView2, CoreWebView2WebResourceRequestedEventArgs>? documentAssets,
        ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return RunAsync(
            parentWindow,
            assets,
            printMarkup,
            PagedPage.Of(setup),
            title,
            furniture,
            mode,
            documentAssets,
            "PDF",
            null,
            logger,
            async core =>
            {
                byte[] pdf = await PrintToPdfAsync(core, setup.IncludeBackgrounds, logger).ConfigureAwait(true);

                await File.WriteAllBytesAsync(path, pdf).ConfigureAwait(true);

                // Read from the file itself rather than trusted from the request: a runtime
                // that does not know a flag may ignore it without saying so.
                return (pdf.LongLength, Contains(pdf, "/StructTreeRoot"), Contains(pdf, "/Outlines"));
            });
    }

    /// <summary>
    /// The same pages, to the printer the user chose (spike S5).
    ///
    /// The pages already carry their margins: Paged.js draws them inside its page boxes. The
    /// box is the paper less PrinterEdgeInches on every side, the job asks for exactly that edge
    /// as its margins and no scaling, so the text lands where the PDF puts it. Zero margins hung
    /// an HP LaserJet; see PrinterEdgeInches. A page range is applied by the print page, never
    /// by the printer: see SendToPrinterAsync.
    /// </summary>
    public static Task<PagedPrintResult> PrintAsync(
        IntPtr parentWindow,
        IWebAssetProvider assets,
        string printMarkup,
        PrintJob job,
        string title,
        PaperFurniture furniture,
        PagedHostMode mode,
        Windows.Foundation.TypedEventHandler<CoreWebView2, CoreWebView2WebResourceRequestedEventArgs>? documentAssets,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(job);

        return RunAsync(
            parentWindow,
            assets,
            printMarkup,
            PagedPage.Of(job).Inset(PrinterEdgeInches),
            title,
            furniture,
            mode,
            documentAssets,
            "print to " + job.PrinterName,
            job.PageRanges,
            logger,
            async core =>
            {
                await SendToPrinterAsync(core, job, logger).ConfigureAwait(true);

                return (0L, false, false);
            });
    }

    private static async Task<PagedPrintResult> RunAsync(
        IntPtr parentWindow,
        IWebAssetProvider assets,
        string printMarkup,
        PagedPage page,
        string title,
        PaperFurniture furniture,
        PagedHostMode mode,
        Windows.Foundation.TypedEventHandler<CoreWebView2, CoreWebView2WebResourceRequestedEventArgs>? documentAssets,
        string output,
        string? pageRanges,
        ILogger logger,
        Func<CoreWebView2, Task<(long Bytes, bool Tagged, bool Outline)>> emit)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentException.ThrowIfNullOrWhiteSpace(printMarkup);

        // One job at a time. A PDF asked for while a print is still laying out waits its turn
        // rather than sharing the engine.
        await Gate.WaitAsync().ConfigureAwait(true);

        CoreWebView2Controller controller;

        try
        {
            CoreWebView2Environment environment = await EnvironmentAsync().ConfigureAwait(true);

            controller = await environment.CreateCoreWebView2ControllerAsync(
                CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)parentWindow));

            _browserProcessId = (int)controller.CoreWebView2.BrowserProcessId;
        }
        catch
        {
            // Forgotten, so the next job makes another rather than failing on this one again.
            _environment = null;
            Gate.Release();
            throw;
        }

        try
        {
            CoreWebView2 core = Prepare(controller, assets, page, mode);

            // Pictures the document names by a relative path are answered as the preview
            // answers them - from the active document's folder, by the preview's own handler -
            // so a PDF shows the pictures the screen shows, and nothing is fetched from anywhere
            // else (docs/Export-Alignment-Plan.md, §6.1).
            if (documentAssets is not null)
            {
                core.AddWebResourceRequestedFilter("https://marqora.document/*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += documentAssets;
            }

            var clock = Stopwatch.StartNew();

            JsonElement measured = await LayOutAsync(core, assets, printMarkup, page, title, furniture, pageRanges).ConfigureAwait(true);

            // Step by step, because a print that stalls says nothing else: the first printer run
            // logged the dialog and then silence, which could have been either step.
            logger.LogInformation(
                "Paged {Output}: laid out in {Layout} ms, {Kept} of {Pages} pages kept; sending.",
                output,
                (long)clock.Elapsed.TotalMilliseconds,
                measured.TryGetProperty("kept", out JsonElement kept) ? kept.GetInt32() : 0,
                measured.TryGetProperty("pages", out JsonElement total) ? total.GetInt32() : 0);

            TimeSpan layout = clock.Elapsed;

            CheckPaper(measured, output, logger);

            clock.Restart();

            (long bytes, bool tagged, bool outline) emittedResult;

            try
            {
                emittedResult = await emit(core).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                throw new PagedOutputException(output, ex);
            }

            (long bytes, bool tagged, bool outline) = emittedResult;

            TimeSpan emitted = clock.Elapsed;

            var faces = new Dictionary<string, bool>(StringComparer.Ordinal);

            if (measured.TryGetProperty("faces", out JsonElement faceList))
            {
                foreach (JsonProperty face in faceList.EnumerateObject())
                {
                    faces[face.Name] = face.Value.ValueKind == JsonValueKind.True;
                }
            }

            var result = new PagedPrintResult(
                measured.TryGetProperty("pages", out JsonElement pages) ? pages.GetInt32() : 0,
                layout,
                emitted,
                bytes,
                tagged,
                outline,
                measured.TryGetProperty("visibility", out JsonElement v) ? v.GetString() ?? "?" : "?",
                faces,
                Count(measured, "notesMoved"),
                Count(measured, "notesPlaced"),
                Count(measured, "contentsEntries"));

            logger.LogInformation(
                "Paged {Output} ({Mode}): {Pages} pages, layout {Layout} ms, output {Emitted} ms, {Bytes} bytes, tagged {Tagged}, outline {Outline}, page {Visibility}, faces {Faces}, footnotes {Placed} of {Moved} at the page foot, contents {Entries} entries, cover {Cover}.",
                output,
                mode,
                result.Pages,
                (long)layout.TotalMilliseconds,
                (long)emitted.TotalMilliseconds,
                bytes,
                tagged,
                outline,
                result.Visibility,
                string.Join(", ", faces.Select(f => f.Key + "=" + (f.Value ? "yes" : "no"))),
                result.NotesPlaced,
                result.NotesMoved,
                result.ContentsEntries,
                furniture.Cover is not null);

            logger.LogInformation(
                "Paged {Output}: footnote markup found - {Found}.",
                output,
                measured.TryGetProperty("notesFound", out JsonElement found) ? found.GetString() : "not reported");

            return result;
        }
        catch (Exception ex) when (ex is TimeoutException || ex.InnerException is TimeoutException)
        {
            // Giving up the wait does not stop the job: a print stuck inside Chromium carries
            // on, and outlived the app before this. End it here, where it can be.
            EndBrowser(logger);
            throw;
        }
        finally
        {
            try
            {
                controller.Close();
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                // The browser was ended above; there is nothing left to close.
            }

            Gate.Release();
        }
    }

    /// <summary>
    /// The paper spec against what the print page drew: one element of each kind, measured by
    /// print.js (measureStyles) and held to <see cref="PaperSpec"/> - size, weight, slant and
    /// line height (docs/Export-Alignment-Plan.md, §8, the third check).
    ///
    /// Logged rather than thrown: a heading half a point off is a bug to fix, not a reason to
    /// refuse somebody their PDF. One line saying it matched, or a warning per difference, so
    /// the phase gate is read off any paged export's log.
    /// </summary>
    private static void CheckPaper(JsonElement measured, string output, ILogger logger)
    {
        if (!measured.TryGetProperty("computed", out JsonElement computed) || computed.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var drift = new List<string>();
        int checkedCount = 0;

        foreach (JsonProperty found in computed.EnumerateObject())
        {
            if (PaperSpec.All.FirstOrDefault(e => e.Name == found.Name) is not { } spec)
            {
                continue;
            }

            checkedCount++;

            // CSS pixels are 96 to the inch and points 72, so a point is four thirds of a pixel.
            double points = found.Value.GetProperty("size").GetDouble() * 0.75;
            int weight = found.Value.GetProperty("weight").GetInt32();
            bool italic = found.Value.GetProperty("italic").GetBoolean();
            double line = found.Value.GetProperty("line").GetDouble();
            double specLine = spec.SizePoints / 0.75 * spec.LineHeight;

            if (Math.Abs(points - spec.SizePoints) > 0.1)
            {
                drift.Add(Invariant($"{spec.Name} size {points:0.##}pt, spec {spec.SizePoints}pt"));
            }

            if (weight != spec.CssWeight)
            {
                drift.Add(Invariant($"{spec.Name} weight {weight}, spec {spec.CssWeight}"));
            }

            if (italic != spec.Italic)
            {
                drift.Add(Invariant($"{spec.Name} {(italic ? "italic" : "upright")}, spec {(spec.Italic ? "italic" : "upright")}"));
            }

            // A line height of "normal" reads as zero and says nothing either way.
            if (line > 0 && Math.Abs(line - specLine) > 0.6)
            {
                drift.Add(Invariant($"{spec.Name} line {line:0.#}px, spec {specLine:0.#}px"));
            }
        }

        if (drift.Count == 0)
        {
            logger.LogInformation("Paged {Output}: paper check, {Count} kinds of element match the spec.", output, checkedCount);
            return;
        }

        foreach (string difference in drift)
        {
            logger.LogWarning("Paged {Output}: paper check, {Difference}.", output, difference);
        }
    }

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);

    /// <summary>The controller sized to one page at 96 dpi, kept out of sight as asked, settings applied.</summary>
    private static CoreWebView2 Prepare(
        CoreWebView2Controller controller,
        IWebAssetProvider assets,
        PagedPage page,
        PagedHostMode mode)
    {
        int width = (int)Math.Ceiling(page.WidthInches * 96);
        int height = (int)Math.Ceiling(page.HeightInches * 96);

        controller.DefaultBackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);

        if (mode == PagedHostMode.Hidden)
        {
            controller.IsVisible = false;
            controller.Bounds = new Windows.Foundation.Rect(0, 0, width, height);
        }
        else
        {
            controller.IsVisible = true;
            controller.Bounds = new Windows.Foundation.Rect(-width - 10_000, -height - 10_000, width, height);
        }

        CoreWebView2 core = controller.CoreWebView2;

        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
#if DEBUG
        core.Settings.AreDevToolsEnabled = true;
#else
        core.Settings.AreDevToolsEnabled = false;
#endif

        core.SetVirtualHostNameToFolderMapping(
            assets.VirtualHostName,
            assets.RootDirectory,
            CoreWebView2HostResourceAccessKind.Allow);

        return core;
    }

    /// <summary>Loads the print page, hands it the document, and waits for its pages.</summary>
    private static async Task<JsonElement> LayOutAsync(
        CoreWebView2 core,
        IWebAssetProvider assets,
        string printMarkup,
        PagedPage page,
        string title,
        PaperFurniture furniture,
        string? pageRanges)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rendered = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        core.WebMessageReceived += (_, e) =>
        {
            using JsonDocument message = JsonDocument.Parse(e.WebMessageAsJson);
            JsonElement root = message.RootElement;
            string type = root.TryGetProperty("type", out JsonElement t) ? t.GetString() ?? string.Empty : string.Empty;

            switch (type)
            {
                case "ready":
                    ready.TrySetResult();
                    break;
                case "rendered":
                    rendered.TrySetResult(root.Clone());
                    break;
                case "failed":
                    rendered.TrySetException(new InvalidOperationException(
                        "The print page could not lay the document out: "
                        + (root.TryGetProperty("message", out JsonElement m) ? m.GetString() : "no reason given")));
                    break;
            }
        };

        core.Navigate($"https://{assets.VirtualHostName}/print.html");

        await WithTimeout(ready.Task, ReadyTimeout, "The print page did not load.").ConfigureAwait(true);

        using (JsonDocument markup = JsonDocument.Parse(printMarkup))
        {
            JsonElement m = markup.RootElement;

            core.PostWebMessageAsJson(JsonSerializer.Serialize(new
            {
                type = "paginate",
                html = m.GetProperty("html").GetString(),
                themeCss = m.TryGetProperty("themeCss", out JsonElement theme) ? theme.GetString() : string.Empty,
                // The paper spec the page carries, or the spec itself for a page that has none of
                // its own to send - the cheatsheet and a diagram pop-out.
                paperCss = m.TryGetProperty("paperCss", out JsonElement paper) && paper.GetString() is { Length: > 0 } carried
                    ? carried
                    : PaperSpec.Css(),
                rootStyle = m.TryGetProperty("rootStyle", out JsonElement root) ? root.GetString() : string.Empty,

                // What kind of page the markup came from - none for a document or a diagram,
                // "cheatsheet" for the cheatsheet - which picks the extra sheet print.js loads.
                kind = m.TryGetProperty("kind", out JsonElement kind) ? kind.GetString() : null,
                title,
                pages = pageRanges ?? string.Empty,
                page = new
                {
                    widthInches = page.WidthInches,
                    heightInches = page.HeightInches,
                    verticalMarginInches = page.VerticalMarginInches,
                    horizontalMarginInches = page.HorizontalMarginInches,
                },
                furniture = new
                {
                    headerAndFooter = furniture.HeaderAndFooter,
                    cover = furniture.Cover is { } cover
                        ? new
                        {
                            title = cover.Title,
                            subtitle = cover.Subtitle,
                            date = cover.Date,
                            version = cover.Version,
                            author = cover.Author,
                        }
                        : null,
                    contents = furniture.Contents is { } contents
                        ? new { title = contents.Title, first = contents.First, last = contents.Last }
                        : null,
                },
            }));
        }

        return await WithTimeout(
            rendered.Task, LayoutTimeout, "Paged.js did not finish laying the document out.").ConfigureAwait(true);
    }

    /// <summary>
    /// Page.printToPDF with the outline and tags asked for, and without them if this runtime
    /// refuses the flags - they are marked experimental, and an untagged PDF is not a failure.
    ///
    /// Zero margins and the CSS page size: Paged.js has already drawn the margins inside its
    /// page boxes, and the protocol's own default of 0.4 in would be added on top.
    /// </summary>
    private static async Task<byte[]> PrintToPdfAsync(CoreWebView2 core, bool shade, ILogger logger)
    {
        string Parameters(bool tagged) => JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["printBackground"] = shade,
            ["preferCSSPageSize"] = true,
            ["marginTop"] = 0,
            ["marginBottom"] = 0,
            ["marginLeft"] = 0,
            ["marginRight"] = 0,
            ["displayHeaderFooter"] = false,
            ["generateTaggedPDF"] = tagged,
            ["generateDocumentOutline"] = tagged,
        });

        string answer;

        try
        {
            answer = await core.CallDevToolsProtocolMethodAsync("Page.printToPDF", Parameters(true));
        }
        catch (Exception ex) when (ex is ArgumentException or COMException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Page.printToPDF refused the outline and tag flags; printing without them.");
            answer = await core.CallDevToolsProtocolMethodAsync("Page.printToPDF", Parameters(false));
        }

        using JsonDocument document = JsonDocument.Parse(answer);

        return Convert.FromBase64String(document.RootElement.GetProperty("data").GetString() ?? string.Empty);
    }

    /// <summary>
    /// The pages to a printer, on the settings WebViewPrinting.PrintAsync uses for the live
    /// page - printer, copies, collation, color, sides - except the page itself: the paper
    /// Paged.js laid out against, margins of PrinterEdgeInches, no scaling, no browser band.
    /// </summary>
    private static async Task SendToPrinterAsync(CoreWebView2 core, PrintJob job, ILogger logger)
    {
        CoreWebView2PrintSettings settings = core.Environment.CreatePrintSettings();

        settings.PrinterName = job.PrinterName;
        settings.Copies = job.Copies;
        settings.Collation = job.Collate
            ? CoreWebView2PrintCollation.Collated
            : CoreWebView2PrintCollation.Uncollated;

        settings.Orientation = job.Orientation == PageOrientation.Landscape
            ? CoreWebView2PrintOrientation.Landscape
            : CoreWebView2PrintOrientation.Portrait;

        settings.MediaSize = CoreWebView2PrintMediaSize.Custom;
        settings.PageWidth = job.WidthInches;
        settings.PageHeight = job.HeightInches;

        // The printer's own margins are the edge the page was inset by (PrinterEdgeInches), so
        // the text lands exactly where the PDF puts it; see that constant for why not zero.
        settings.MarginTop = PrinterEdgeInches;
        settings.MarginBottom = PrinterEdgeInches;
        settings.MarginLeft = PrinterEdgeInches;
        settings.MarginRight = PrinterEdgeInches;

        settings.ShouldPrintBackgrounds = job.IncludeBackgrounds;
        settings.ShouldPrintHeaderAndFooter = false;
        settings.ScaleFactor = 1.0;

        settings.ColorMode = job.ColorMode switch
        {
            PrintColorMode.Color => CoreWebView2PrintColorMode.Color,
            PrintColorMode.Grayscale => CoreWebView2PrintColorMode.Grayscale,
            _ => CoreWebView2PrintColorMode.Default,
        };

        settings.Duplex = job.Duplex switch
        {
            PrintDuplex.OneSided => CoreWebView2PrintDuplex.OneSided,
            PrintDuplex.LongEdge => CoreWebView2PrintDuplex.TwoSidedLongEdge,
            PrintDuplex.ShortEdge => CoreWebView2PrintDuplex.TwoSidedShortEdge,
            _ => CoreWebView2PrintDuplex.Default,
        };

        // No PageRanges, deliberately. The print page has already set aside every page outside
        // the range (print.js, keepPages), so what is left is the whole job. Handed to Chromium,
        // a range over the paged document hung: any print that did not start at page 1 - page 5
        // alone, pages 5-6 - spooled for ever, on an HP LaserJet and on Microsoft Print to PDF
        // alike, while the same pages from page 1 and the whole document printed at once.

        var clock = Stopwatch.StartNew();

        logger.LogInformation(
            "Paged print: handing {Printer} {Width}x{Height} in, margins {Edge} in, pages {Ranges} (set aside in the page, not by the printer).",
            job.PrinterName,
            job.WidthInches,
            job.HeightInches,
            PrinterEdgeInches,
            string.IsNullOrWhiteSpace(job.PageRanges) ? "all" : job.PageRanges);

        // Bounded. The first run to a real printer never came back: the spooler showed the job
        // spooling with nothing reaching the paper, and the app waited on this call for ever.
        CoreWebView2PrintStatus status = await WithTimeout(
            core.PrintAsync(settings).AsTask(),
            PrintTimeout,
            $"{job.PrinterName} did not accept the pages").ConfigureAwait(true);

        logger.LogInformation(
            "Paged print: {Printer} answered {Status} after {Elapsed} ms.",
            job.PrinterName,
            status,
            (long)clock.Elapsed.TotalMilliseconds);

        if (status != CoreWebView2PrintStatus.Succeeded)
        {
            throw new IOException(status == CoreWebView2PrintStatus.PrinterUnavailable
                ? $"{job.PrinterName} is not available."
                : $"The pages could not be sent to {job.PrinterName}.");
        }
    }

    /// <summary>A number the print page reported, or zero when it did not report one.</summary>
    private static int Count(JsonElement measured, string name) =>
        measured.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;

    /// <summary>Whether an ASCII marker occurs in the bytes - a PDF's dictionary keys are ASCII.</summary>
    private static bool Contains(byte[] bytes, string marker) =>
        bytes.AsSpan().IndexOf(System.Text.Encoding.ASCII.GetBytes(marker)) >= 0;

    private static async Task WithTimeout(Task task, TimeSpan timeout, string message)
    {
        if (await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(true) != task)
        {
            throw new TimeoutException(message + " (" + timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture) + " s)");
        }

        await task.ConfigureAwait(true);
    }

    private static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout, string message)
    {
        await WithTimeout((Task)task, timeout, message).ConfigureAwait(true);

        return await task.ConfigureAwait(true);
    }
}
