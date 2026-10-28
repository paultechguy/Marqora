// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Exporting;
using PaulTechGuy.MQ.Themes;

namespace PaulTechGuy.MQ.WebRender;

/// <summary>
/// The preview shell, loaded in a WebView2 nobody sees, with one document open at a time.
///
/// A preview with one tab and no screen: it loads the same shell.html, is told the same color
/// themes and paper spec on ready, and opens a document with the same openTab and activateTab
/// the live preview gets. What it hands back is therefore what the app would export for the same
/// document, by construction rather than by two renders kept in step
/// (docs/CLI-Implementation-Plan.md, F4).
///
/// <see cref="RenderAsync"/> opens a document as the one tab and leaves it open, so the diagrams,
/// equations and PDF asked for afterwards are that document's. The next render replaces it.
///
/// Where the preview degrades, this does not: a render that does not settle comes back as
/// <see cref="BridgeResult.TimedOut"/>, and marqora-cli fails that file (CLI-EX-05).
///
/// Everything here runs on the <see cref="Win32.StaPump"/> thread that created it.
/// </summary>
public sealed class HeadlessRenderHost : IDocumentRenderer, IAsyncDisposable
{
    /// <summary>The variable WebView2 reads a data folder from ahead of any folder it is passed.</summary>
    private const string UserDataVariable = "WEBVIEW2_USER_DATA_FOLDER";

    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);

    /// <summary>For one diagram's or one equation's picture, as the preview allows.</summary>
    private static readonly TimeSpan PictureTimeout = TimeSpan.FromSeconds(20);

    private readonly CoreWebView2Environment _environment;

    private readonly CoreWebView2Controller _controller;

    private readonly nint _parentWindow;

    private readonly IWebAssetProvider _assets;

    private readonly ThemeCatalog _themes;

    private readonly IMarkdownRenderer _markdown;

    private readonly TimeSpan _renderTimeout;

    private readonly ILogger _logger;

    /// <summary>The requests this host makes of the shell, made the same way the live preview makes them.</summary>
    private readonly ShellBridge _bridge;

    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource _browserExited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The color theme the shell was last told, for screen and exports alike.</summary>
    private string _themeId;

    /// <summary>The tab of the document last rendered, or null before the first.</summary>
    private Guid? _current;

    /// <summary>The folder its relative pictures are served from.</summary>
    private string? _documentDirectory;

    private HeadlessRenderHost(
        CoreWebView2Environment environment,
        CoreWebView2Controller controller,
        nint parentWindow,
        IWebAssetProvider assets,
        ThemeCatalog themes,
        string themeId,
        IMarkdownRenderer markdown,
        TimeSpan renderTimeout,
        ILogger logger)
    {
        _environment = environment;
        _controller = controller;
        _parentWindow = parentWindow;
        _assets = assets;
        _themes = themes;
        _themeId = themeId;
        _markdown = markdown;
        _renderTimeout = renderTimeout;
        _logger = logger;
        _bridge = new ShellBridge(new CoreWebView2Transport(controller.CoreWebView2));

        BrowserProcessId = (int)controller.CoreWebView2.BrowserProcessId;

        _environment.BrowserProcessExited += (_, _) => _browserExited.TrySetResult();
        _controller.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
    }

    /// <summary>The browser process this host started, for counting what a run leaves behind.</summary>
    public int BrowserProcessId { get; }

    /// <summary>
    /// Starts a browser in <paramref name="userDataFolder"/>, loads the shell, and waits until it
    /// reports ready and has its themes and paper spec.
    /// </summary>
    /// <param name="markdown">Turns a request's text into the markup the shell finishes.</param>
    /// <param name="renderTimeout">How long a document's render, its print markup or a Folio document's may take.</param>
    public static async Task<HeadlessRenderHost> StartAsync(
        nint parentWindow,
        IWebAssetProvider assets,
        string userDataFolder,
        ThemeCatalog themes,
        string themeId,
        IMarkdownRenderer markdown,
        TimeSpan renderTimeout,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(themes);
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(logger);

        CoreWebView2Environment environment = await CreateEnvironmentAsync(userDataFolder).ConfigureAwait(true);

        CoreWebView2Controller? controller = null;

        try
        {
            controller = await environment.CreateCoreWebView2ControllerAsync(
                CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)parentWindow));

            var host = new HeadlessRenderHost(
                environment, controller, parentWindow, assets, themes, themes.Find(themeId).Id, markdown, renderTimeout, logger);

            await host.OpenShellAsync(cancellationToken).ConfigureAwait(true);

            return host;
        }
        catch
        {
            // The environment has started a browser whether or not a controller came of it, and
            // nothing else will end it: there is no host to dispose. Seen over SSH, where there
            // is no interactive desktop and the controller is refused (0x80070578) - the browser
            // lingered past the run's cleanup and held its data folder.
            Abandon(environment, controller, logger);
            throw;
        }
    }

    private async Task OpenShellAsync(CancellationToken cancellationToken)
    {
        // Visible as far as the browser knows, but placed outside the parent's client area so
        // nothing is drawn: the mode PagedPrintHost found to be the one that lays out. A width
        // the preview pane would have on a laptop, so tables wrap where they would on screen.
        const int Width = 1280;
        const int Height = 1600;

        _controller.DefaultBackgroundColor = global::Windows.UI.Color.FromArgb(255, 255, 255, 255);
        _controller.IsVisible = true;
        _controller.Bounds = new global::Windows.Foundation.Rect(-Width - 10_000, -Height - 10_000, Width, Height);

        CoreWebView2 core = _controller.CoreWebView2;

        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDevToolsEnabled = false;

        core.SetVirtualHostNameToFolderMapping(
            _assets.VirtualHostName,
            _assets.RootDirectory,
            CoreWebView2HostResourceAccessKind.Allow);

        // A document's own pictures, from its folder, as the preview serves them.
        core.AddWebResourceRequestedFilter($"{DocumentFiles.BaseUrl}*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;

        // Light, always: everything that leaves the app is light (CLAUDE.md, Screen and output).
        // The accent rides in the fragment for the reason WebViewPreviewHost.ShellUriForTheme gives.
        string accent = DocumentAccent.LightHex.TrimStart('#');

        core.Navigate($"{_assets.ShellUri}#theme=light&accent={accent}&accentPrint={accent}");

        await WaitAsync(_ready.Task, ReadyTimeout, "The shell did not report ready.", cancellationToken)
            .ConfigureAwait(true);

        // What WebViewPreviewHost.OnShellReady sends ahead of everything else: the themes, then
        // the paper spec. One theme for screen and exports alike - there is no screen.
        Post("setColorThemes", ColorThemePayloads.Catalog(_themes, _themeId, _themeId));
        Post("setPaper", new { css = PaperSpec.Css() });
    }

    // ------------------------------------------------------------------ IDocumentRenderer

    /// <summary>
    /// Opens the document as the shell's one tab, in place of the last, waits for its render to
    /// settle, and answers with its finished markup. The tab stays open for what is asked next.
    /// </summary>
    public async Task<BridgeOutcome<RenderedDocument>> RenderAsync(RenderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        RenderedMarkdown markdown = _markdown.Render(request.Markdown, request.Numbering);

        var id = Guid.NewGuid();

        try
        {
            UseTheme(request.ColorThemeId);

            if (_current is { } previous)
            {
                Post("closeTab", new { id = previous });
                _current = null;
            }

            _documentDirectory = DocumentFiles.FolderOf(request.DocumentPath);

            Post("openTab", new { id, text = request.Markdown, html = markdown.Html });
            Post("activateTab", new { id, documentBaseUrl = DocumentFiles.BaseUrl, documentPath = request.DocumentPath ?? string.Empty });

            _current = id;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return BridgeOutcome.Failed<RenderedDocument>(ex);
        }

        BridgeOutcome<string> settled = await _bridge.RenderSettledAsync(_renderTimeout, cancellationToken).ConfigureAwait(true);

        if (!settled.IsRendered)
        {
            Unanswered(settled, "The document's render did not settle.");

            return settled.Map(_ => new RenderedDocument(string.Empty));
        }

        BridgeOutcome<string> rendered = await _bridge.RenderedHtmlAsync(_renderTimeout, cancellationToken).ConfigureAwait(true);

        if (!rendered.IsRendered)
        {
            Unanswered(rendered, "The shell did not hand back the document's markup.");
        }

        return rendered.Map(html => new RenderedDocument(html ?? string.Empty));
    }

    public async Task<byte[]?> DiagramPngAsync(string hash, CancellationToken cancellationToken = default) =>
        await AskAsync("a diagram's picture", _bridge.DiagramPngAsync(hash, forCopy: false, PictureTimeout, cancellationToken))
                .ConfigureAwait(true) is { } data
            ? ShellAnswers.DiagramPng(data, _logger)
            : null;

    public Task<string?> DiagramSvgAsync(string hash, CancellationToken cancellationToken = default) =>
        AskAsync("a diagram's drawing", _bridge.DiagramSvgAsync(hash, PictureTimeout, cancellationToken));

    public async Task<MathPicture?> MathPngAsync(int line, int ordinal, CancellationToken cancellationToken = default) =>
        await AskAsync("an equation's picture", _bridge.MathPngAsync(line, ordinal, PictureTimeout, cancellationToken))
                .ConfigureAwait(true) is { } data
            ? ShellAnswers.MathPng(data, _logger)
            : null;

    public async Task<BridgeOutcome<string>> RenderDetachedAsync(string html, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(html);

        BridgeOutcome<string> outcome = await _bridge.ExportHtmlAsync(html, _renderTimeout, cancellationToken).ConfigureAwait(true);

        if (!outcome.IsRendered)
        {
            Unanswered(outcome, "The shell did not finish a document for export.");
        }

        return outcome;
    }

    /// <summary>
    /// The rendered document to a PDF: through the paged engine as the app's PDF goes, or the
    /// classic engine, which prints this host's own page through its print stylesheet.
    /// </summary>
    public async Task<PdfExportOutcome> PrintPdfAsync(
        string path,
        PdfPageSetup setup,
        string title,
        PaperFurniture furniture,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(furniture);

        if (_current is null)
        {
            throw new InvalidOperationException("Nothing has been rendered to print.");
        }

        // Kept for the report: the markup also lists the diagrams that would not draw.
        string? markup = null;

        PdfJob job = await PagedJobs.ExportPdfAsync(
            _parentWindow,
            _assets,
            async () => markup = await AskAsync("the print markup", _bridge.PrintHtmlAsync(_renderTimeout, cancellationToken)).ConfigureAwait(true),
            path,
            setup,
            title,
            furniture,
            OnWebResourceRequested,
            _logger,
            () => ExportClassicPdfAsync(path, setup, title, cancellationToken)).ConfigureAwait(true);

        return new PdfExportOutcome(job.Engine, ShellAnswers.PdfIssues(markup, job, _logger));
    }

    /// <summary>
    /// The classic engine: this host's page printed as it stands, through the print stylesheet,
    /// once the page has its shape and title and every diagram its light drawing. Its canvas is
    /// already white.
    /// </summary>
    private async Task ExportClassicPdfAsync(string path, PdfPageSetup setup, string title, CancellationToken cancellationToken)
    {
        double ratio = PrintArea.Ratio(setup.WidthInches, setup.HeightInches, setup.HorizontalMarginInches, setup.VerticalMarginInches);

        await AskAsync("print readiness", _bridge.PrepareForPrintAsync(ratio, title, PictureTimeout, cancellationToken))
            .ConfigureAwait(true);

        await WebViewPrinting.ExportPdfAsync(_controller.CoreWebView2, path, setup).ConfigureAwait(true);
    }

    /// <summary>
    /// The shell's answer to one request: null when the answer was empty, which is the shell
    /// saying it had none to give, or did not come in time. <paramref name="what"/> names it in
    /// the log.
    /// </summary>
    private async Task<string?> AskAsync(string what, Task<BridgeOutcome<string>> asking)
    {
        BridgeOutcome<string> outcome = await asking.ConfigureAwait(true);

        if (!outcome.IsRendered)
        {
            Unanswered(outcome, $"The shell did not answer the request for {what}.");
            return null;
        }

        return string.IsNullOrEmpty(outcome.Value) ? null : outcome.Value;
    }

    /// <summary>Tells the shell the theme a document leaves in, when it is not the one it has.</summary>
    private void UseTheme(string themeId)
    {
        string id = _themes.Find(themeId).Id;

        if (id == _themeId)
        {
            return;
        }

        // Both, as there is no screen: what the page shows is what leaves.
        Post("setColorTheme", new { id, preview = false });
        Post("setExportColorTheme", new { id });

        _themeId = id;
    }

    private void OnWebResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs e) =>
        DocumentFiles.Serve(sender, e, _documentDirectory, _logger);

    /// <summary>
    /// Closes the browser and waits for its process to end, so the data folder can be removed.
    /// Ends the process tree itself if the browser does not go within a few seconds.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _controller.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        _controller.CoreWebView2.WebResourceRequested -= OnWebResourceRequested;
        _controller.Close();

        Task finished = await Task.WhenAny(_browserExited.Task, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(true);

        if (finished != _browserExited.Task)
        {
            _logger.LogWarning("The render browser, process {Id}, did not exit after closing; ending it.", BrowserProcessId);
            EndProcessTree(BrowserProcessId, _logger);
        }
    }

    /// <summary>
    /// Closes what a failed start had made and ends its browser. The browser is found through the
    /// controller when there is one, and otherwise by asking the environment for its processes.
    /// </summary>
    private static void Abandon(CoreWebView2Environment environment, CoreWebView2Controller? controller, ILogger logger)
    {
        int browser = 0;

        try
        {
            if (controller is not null)
            {
                browser = (int)controller.CoreWebView2.BrowserProcessId;
                controller.Close();
            }
            else
            {
                browser = environment.GetProcessInfos()
                    .Where(process => process.Kind == CoreWebView2ProcessKind.Browser)
                    .Select(process => process.ProcessId)
                    .FirstOrDefault();
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            logger.LogDebug(ex, "The failed browser could not be asked about itself.");
        }

        EndProcessTree(browser, logger);
    }

    /// <summary>Ends a browser process and everything under it, if it is still there.</summary>
    public static void EndProcessTree(int processId, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (processId == 0)
        {
            return;
        }

        try
        {
            using Process browser = Process.GetProcessById(processId);

            browser.Kill(entireProcessTree: true);
            browser.WaitForExit(5000);
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning(ex, "Could not end browser process {Id}.", processId);
        }
    }

    /// <summary>
    /// An environment in a data folder of this host's own. The variable WebView2 reads first is
    /// pointed at the folder for the one call that reads it and put back straight after, as
    /// PagedPrintHost does; nothing is awaited in between, so no other environment can be made
    /// while it is changed.
    /// </summary>
    private static async Task<CoreWebView2Environment> CreateEnvironmentAsync(string userDataFolder)
    {
        string? previous = Environment.GetEnvironmentVariable(UserDataVariable);

        global::Windows.Foundation.IAsyncOperation<CoreWebView2Environment> creating;

        Environment.SetEnvironmentVariable(UserDataVariable, userDataFolder);

        try
        {
            creating = CoreWebView2Environment.CreateWithOptionsAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder,
                options: new CoreWebView2EnvironmentOptions());
        }
        finally
        {
            Environment.SetEnvironmentVariable(UserDataVariable, previous);
        }

        return await creating;
    }

    /// <summary>Says why a request to the shell came back without an answer.</summary>
    private void Unanswered(BridgeOutcome<string> outcome, string timedOut)
    {
        if (outcome.Result == BridgeResult.Failed)
        {
            _logger.LogWarning(outcome.Error, "Could not post a message to the render shell.");
        }
        else
        {
            _logger.LogWarning("{Message}", timedOut);
        }
    }

    private async Task WaitAsync(Task task, TimeSpan timeout, string message, CancellationToken cancellationToken)
    {
        Task finished = await Task.WhenAny(task, Task.Delay(timeout, cancellationToken)).ConfigureAwait(true);

        cancellationToken.ThrowIfCancellationRequested();

        if (finished != task)
        {
            _logger.LogWarning("{Message}", message);
            throw new TimeoutException(message);
        }

        await task.ConfigureAwait(true);
    }

    private void Post(string type, object payload) =>
        _controller.CoreWebView2.PostWebMessageAsJson(ShellBridge.Serialize(type, payload));

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string raw;

        try
        {
            raw = e.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            JsonElement root = document.RootElement;

            string type = root.TryGetProperty("type", out JsonElement t) ? t.GetString() ?? string.Empty : string.Empty;
            JsonElement payload = root.TryGetProperty("payload", out JsonElement p) ? p : default;

            if (_bridge.Accept(type, payload))
            {
                return;
            }

            switch (type)
            {
                case "ready":
                    _ready.TrySetResult();
                    break;

                case "log":
                case "error":
                case "warning":
                    _logger.LogDebug("Shell {Type}: {Message} {Detail}", type, Read(payload, "message"), Read(payload, "detail"));
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Malformed message from the shell.");
        }
    }

    private static string Read(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
