// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// Bridges the view model to the WebView-hosted editor and preview.
///
/// Both panes live in one page so scroll synchronization is a local calculation rather than
/// a round trip through the host. This class therefore does no layout work: it forwards
/// state in, translates messages out, and keeps the two sides from talking past each other
/// before the page is ready.
/// </summary>
public sealed class WebViewPreviewHost : IPreviewHost, IDisposable
{
    private const string DocumentVirtualHost = "marqora.document";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Panel _surface;
    private readonly Func<WebView2> _createWebView;
    private readonly IWebAssetProvider _assets;
    private readonly ILogger<WebViewPreviewHost> _logger;

    /// <summary>
    /// Messages raised before the page finished loading. Without this, opening a file from
    /// the command line races the shell and the document silently never appears.
    /// </summary>
    private readonly List<string> _pending = [];

    /// <summary>
    /// The control currently sitting in <see cref="_surface"/>.
    ///
    /// Not readonly, and this is the whole reason the host owns the control rather than
    /// being handed one: a browser process that exits takes its control down with it, and
    /// the way back is a different object in the same place.
    /// </summary>
    private WebView2 _webView;

    private string? _documentDirectory;

    /// <summary>
    /// The effective theme, as last known here.
    ///
    /// Kept so it can be written into the shell's URL before the page loads. The shell used
    /// to come up dark whatever the setting said and wait to be told otherwise, and being
    /// told is the far end of a Monaco load: under a light theme the boot screen, both panes
    /// and every restored document painted dark first and then repainted. See
    /// <see cref="AttachAsync"/> for the seed and shell.html for the end that reads it.
    ///
    /// Updated by <see cref="SetThemeAsync"/> as well as set at start-up, because a WebView
    /// rebuilt after a crash navigates again and has to come back the color the window is
    /// now rather than the color it was when the app started.
    /// </summary>
    private AppTheme _theme = AppTheme.Light;

    /// <summary>
    /// Where WebView2's own crash handler writes its minidumps, read while the environment
    /// is alive because by the time a process has failed there is nothing left to ask.
    ///
    /// Only ever logged. It is the one thread from a failure back to why it happened: the
    /// dump has the native stack, which is the only stack there is when the process that
    /// died was not this one.
    /// </summary>
    private string? _failureReportFolder;

    private bool _disposed;

    /// <param name="surface">
    /// The panel the WebView sits in. Its children belong to this class; nothing else
    /// should add to it.
    /// </param>
    /// <param name="createWebView">
    /// Supplies a control ready to be attached. The window makes them rather than the host,
    /// because a new one has to be given the background color of the current theme and the
    /// theme is the window's business.
    /// </param>
    public WebViewPreviewHost(
        Panel surface,
        Func<WebView2> createWebView,
        IWebAssetProvider assets,
        ILogger<WebViewPreviewHost> logger)
    {
        _surface = surface;
        _createWebView = createWebView;
        _assets = assets;
        _logger = logger;

        _webView = createWebView();
        _surface.Children.Add(_webView);
    }

    public bool IsReady { get; private set; }

    public string? DefaultSourceFont { get; private set; }

    public string? DefaultPreviewFont { get; private set; }

    public string? ResolvedSourceFont { get; private set; }

    public string? ResolvedPreviewFont { get; private set; }

    public event EventHandler? Ready;

    public event EventHandler? FontsResolved;

    public event EventHandler<EditorTextChangedEventArgs>? EditorTextChanged;

    public event EventHandler<Uri>? ExternalLinkActivated;

    public event EventHandler<ZoomChangedEventArgs>? ZoomChanged;

    public event EventHandler<double>? SplitterMoved;

    public event EventHandler<string>? CommandInvoked;

    public event EventHandler<string>? SelectionCopied;

    public event EventHandler<PaneContextMenuEventArgs>? ContextMenuRequested;

    public event EventHandler<DiagramActivatedEventArgs>? DiagramActivated;

    public event EventHandler<DiagramUpdatedEventArgs>? DiagramUpdated;

    public event EventHandler<Guid>? DiagramRemoved;

    public event EventHandler<DiagramInvalidEventArgs>? DiagramInvalid;

    /// <summary>
    /// What the caret is sitting in, for the formatting toolbar. Null when the selection
    /// was too large to report on.
    /// </summary>
    public event EventHandler<EditContext?>? CaretStateChanged;

    /// <summary>
    /// Whether undo and redo have anything to act on. Rides the same message as
    /// <see cref="CaretStateChanged"/> - the two go stale at exactly the same moments -
    /// but stays a separate event so the undo stack does not have to be smuggled into
    /// <see cref="EditContext"/>, which the editing rules require to describe nothing but
    /// text and a selection.
    /// </summary>
    public event EventHandler<HistoryState>? HistoryStateChanged;

    /// <summary>Editor statistics for the status bar: cursor position and counts.</summary>
    public event EventHandler<EditorStats>? StatsChanged;

    /// <summary>Which pane the user last interacted with, so zoom commands have a target.</summary>
    public event EventHandler<EditorPane>? PaneFocused;

    /// <summary>
    /// The zero-based source line now at the top of the preview, for the outline panel to
    /// highlight against.
    ///
    /// Silent unless <see cref="SetOutlineTrackingAsync"/> has asked for it. The source
    /// pane needs no equivalent: the caret answers the same question there and already
    /// travels with <see cref="StatsChanged"/>.
    /// </summary>
    public event EventHandler<int>? ViewportLineChanged;

    // ------------------------------------------------------------------ startup

    /// <param name="initialTheme">
    /// What the window has already resolved the theme to. Passed in rather than pushed as a
    /// message because a message cannot arrive before the page paints and this has to: see
    /// <see cref="_theme"/>.
    /// </param>
    public async Task InitializeAsync(AppTheme initialTheme)
    {
        _theme = initialTheme;

        if (!_assets.IsAvailable)
        {
            _logger.LogError(
                "Refusing to start the preview: missing web assets {Missing}.",
                string.Join(", ", _assets.MissingAssets));
            return;
        }

        await AttachAsync();
    }

    /// <summary>
    /// Brings the current control up: creates its core, applies the settings, subscribes to
    /// it and points it at the shell.
    ///
    /// Split out of <see cref="InitializeAsync"/> because it runs again every time a crashed
    /// WebView is replaced, while the missing-assets check above belongs to startup alone.
    /// </summary>
    private async Task AttachAsync()
    {
        await _webView.EnsureCoreWebView2Async();

        CoreWebView2 core = _webView.CoreWebView2;

        // Chromium's own context menu is off. It offered browser commands the document has
        // no use for - Back, Reload, Save as, Inspect - and it was drawn by Edge, so it
        // followed Edge's dark mode rather than Marqora's theme and came up dark in a light
        // window. Both panes now report the right-click across the bridge and the host puts
        // a WinUI flyout up instead, styled from Themes/Menus.xaml along with every other
        // menu in the app. Turning the menu off does not stop the page seeing the click:
        // the setting suppresses the menu, not the DOM event.
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsSwipeNavigationEnabled = false;
        // Zoom is driven through the bridge so both panes and the toolbar agree.
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;

#if DEBUG
        core.Settings.AreDevToolsEnabled = true;
#else
        core.Settings.AreDevToolsEnabled = false;
#endif

        // Note on drag and drop: WinUI's WebView2 in Windows App SDK 2.2 does not expose
        // AllowExternalDrop, so the browser keeps ownership of drops landing on the page.
        // That is handled rather than fought: Chromium responds to a dropped file by
        // navigating to its file:// URL, NavigationStarting below cancels that navigation
        // and hands the path to the host, which opens it as a document. Drops on the window
        // chrome take the ordinary WinUI path. Both routes end up opening the file.

        core.SetVirtualHostNameToFolderMapping(
            _assets.VirtualHostName,
            _assets.RootDirectory,
            CoreWebView2HostResourceAccessKind.Allow);

        // Relative images and media in a document are served from its folder; see
        // SetDocumentLocation for why this is a handler rather than a folder mapping.
        core.AddWebResourceRequestedFilter($"{DocumentBaseUrl}*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;

        core.WebMessageReceived += OnWebMessageReceived;
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.ProcessFailed += OnProcessFailed;

        try
        {
            _failureReportFolder = core.Environment.FailureReportFolderPath;
        }
        catch (Exception ex)
        {
            // Diagnostics about diagnostics. Not being able to name the dump folder is no
            // reason to hold up a preview that is otherwise coming up fine.
            _logger.LogDebug(ex, "Could not read the WebView failure report folder.");
        }

        Uri shell = ShellUriForTheme(_theme);

        _logger.LogInformation("Navigating the preview shell to {Uri}.", shell);
        _webView.Source = shell;
    }

    /// <summary>
    /// The shell's address with the theme written into its fragment.
    ///
    /// A fragment rather than a query string: it is not part of the request, so the virtual
    /// host still resolves shell.html as a plain file, and the guard in
    /// <see cref="OnNavigationStarting"/> matches on the part in front of it either way.
    ///
    /// The page reads it in the head, before the body is parsed, and is already the right
    /// color by its first paint. The setTheme message that follows once the shell reports
    /// ready then changes nothing, which is the point - it is still sent, because it also
    /// carries the match colors and because the theme can change later.
    /// </summary>
    private Uri ShellUriForTheme(AppTheme theme) =>
        new($"{_assets.ShellUri}#theme={(theme == AppTheme.Dark ? "dark" : "light")}");

    /// <summary>
    /// Records the open document's folder so relative images and links in the markdown
    /// resolve against it, the way they would in any other viewer.
    ///
    /// The folder is not mapped with SetVirtualHostNameToFolderMapping, although that is
    /// what the assets host uses. A folder mapping is handed to a page when it navigates and
    /// the shell page navigates once, at startup; a mapping added or changed after that is
    /// never seen by it, so every relative image came back ERR_NAME_NOT_RESOLVED until the
    /// page was reloaded. The folder changes on every tab switch, so instead the requests
    /// for marqora.document are answered here, in <see cref="OnWebResourceRequested"/>,
    /// against whatever folder is current at the time of each request.
    /// </summary>
    private void SetDocumentLocation(string? documentPath)
    {
        string? directory = string.IsNullOrWhiteSpace(documentPath)
            ? null
            : Path.GetDirectoryName(Path.GetFullPath(documentPath));

        if (directory is not null && !Directory.Exists(directory))
        {
            directory = null;
        }

        if (!string.Equals(directory, _documentDirectory, StringComparison.OrdinalIgnoreCase))
        {
            _documentDirectory = directory;
            _logger.LogDebug("Document folder for relative assets is now {Directory}.", directory ?? "(none)");
        }
    }

    /// <summary>
    /// Serves a file from the current document's folder in answer to a request for
    /// https://marqora.document/... - the address the shell rewrites relative sources to.
    ///
    /// Anything outside the folder, anything with no folder to resolve against and anything
    /// that is not a file gets a 404 rather than falling through, because falling through
    /// would leave the request to Chromium, and marqora.document is not a real host.
    /// </summary>
    private void OnWebResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        string? path = ResolveDocumentFile(e.Request.Uri);

        if (path is null)
        {
            e.Response = sender.Environment.CreateWebResourceResponse(null, 404, "Not Found", string.Empty);
            return;
        }

        try
        {
            FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            e.Response = sender.Environment.CreateWebResourceResponse(
                stream.AsRandomAccessStream(),
                200,
                "OK",
                $"Content-Type: {ContentTypeFor(path)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not read {Path} for the preview.", path);
            e.Response = sender.Environment.CreateWebResourceResponse(null, 404, "Not Found", string.Empty);
        }
    }

    /// <summary>
    /// The file a marqora.document URL names, or null when it names nothing that may be
    /// served: no document folder, a path that escapes it, or a path that is not a file.
    /// </summary>
    private string? ResolveDocumentFile(string url)
    {
        if (_documentDirectory is not { } directory
            || !url.StartsWith(DocumentBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string relative = url[DocumentBaseUrl.Length..];

        int cut = relative.IndexOfAny(['?', '#']);
        if (cut >= 0)
        {
            relative = relative[..cut];
        }

        string root = Path.GetFullPath(directory + Path.DirectorySeparatorChar);

        try
        {
            string path = Path.GetFullPath(Path.Combine(
                root,
                Uri.UnescapeDataString(relative).Replace('/', Path.DirectorySeparatorChar)));

            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(path)
                ? path
                : null;
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            _logger.LogDebug(ex, "Could not resolve {Url} to a file in the document folder.", url);
            return null;
        }
    }

    private static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".svg" => "image/svg+xml",
        ".webp" => "image/webp",
        ".avif" => "image/avif",
        ".bmp" => "image/bmp",
        ".ico" => "image/x-icon",
        ".mp4" or ".m4v" => "video/mp4",
        ".webm" => "video/webm",
        ".ogv" => "video/ogg",
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".ogg" or ".oga" => "audio/ogg",
        ".m4a" => "audio/mp4",
        ".flac" => "audio/flac",
        _ => "application/octet-stream",
    };

    public static string DocumentBaseUrl => $"https://{DocumentVirtualHost}/";

    // ------------------------------------------------------------------ outbound

    public Task OpenTabAsync(Guid documentId, string sourceText, RenderedMarkdown rendered) =>
        SendAsync("openTab", new { id = documentId, text = sourceText, html = rendered.Html });

    public Task ActivateTabAsync(Guid documentId, string? documentPath)
    {
        // The folder mapping has to move before the preview is drawn, or the incoming tab's
        // relative images would resolve against the outgoing tab's folder.
        SetDocumentLocation(documentPath);

        // The path is for the printed page header, which names the file the output came from.
        return SendAsync(
            "activateTab",
            new { id = documentId, documentBaseUrl = DocumentBaseUrl, documentPath = documentPath ?? string.Empty });
    }

    public Task CloseTabAsync(Guid documentId) => SendAsync("closeTab", new { id = documentId });

    public Task UpdatePreviewAsync(Guid documentId, RenderedMarkdown rendered) =>
        SendAsync("updatePreview", new { id = documentId, html = rendered.Html });

    public Task SetTabTextAsync(Guid documentId, string sourceText, RenderedMarkdown rendered) =>
        SendAsync("setTabText", new { id = documentId, text = sourceText, html = rendered.Html });

    public Task ClearAsync()
    {
        SetDocumentLocation(null);
        return SendAsync("clearSurface", new { });
    }

    public Task SetViewModeAsync(ViewMode mode) =>
        SendAsync("setViewMode", new { mode = mode.ToString() });

    /// <summary>
    /// The theme, and with it the two colors a match is drawn in.
    ///
    /// They ride along here rather than being written into app.css because the Find All
    /// window paints the same two colors with WinUI brushes, and a color written down
    /// twice is a color that will one day disagree with itself. <see cref="MatchColors"/>
    /// is where they are chosen; the shell puts them into --mq-selection and
    /// --mq-selection-text as it arrives, so Monaco and the stylesheet both read one value.
    /// </summary>
    public Task SetThemeAsync(AppTheme effectiveTheme)
    {
        // Remembered as well as sent, so a WebView rebuilt after a crash navigates to the
        // color the window is wearing now. See _theme.
        _theme = effectiveTheme;

        return SendAsync(
            "setTheme",
            new
            {
                theme = effectiveTheme.ToString(),
                selection = MatchColors.BackgroundHex,
                selectionText = MatchColors.ForegroundHex,
            });
    }

    public Task SetZoomAsync(EditorPane pane, ZoomLevel zoom) =>
        SendAsync("setZoom", new { pane = pane.ToString(), percent = zoom.Percent });

    public Task SetScrollSyncAsync(bool enabled) => SendAsync("setScrollSync", new { enabled });

    public Task SetOutlineTrackingAsync(bool enabled) =>
        SendAsync("setOutlineTracking", new { enabled });

    public Task SetWordWrapAsync(bool enabled) => SendAsync("setWordWrap", new { enabled });

    public Task SetLineNumbersAsync(bool enabled) => SendAsync("setLineNumbers", new { enabled });

    public Task SetShowWhitespaceAsync(bool enabled) => SendAsync("setShowWhitespace", new { enabled });

    public Task ApplyPreferencesAsync(PreviewPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        return SendAsync(
            "applyPreferences",
            new
            {
                // Null font families are sent as null rather than omitted: the web side
                // clears its own custom property on null, which is what restores the
                // stylesheet's stack.
                sourceFont = preferences.SourceFontFamily,
                sourceFontSize = preferences.SourceFontSize,
                previewFont = preferences.PreviewFontFamily,
                previewFontSize = preferences.PreviewFontSize,
                previewMaxWidth = preferences.PreviewMaxWidth,
                tabSize = preferences.TabSize,
                insertSpaces = preferences.InsertSpaces,
                minimap = preferences.ShowMinimap,
                highlightCurrentLine = preferences.HighlightCurrentLine,
                continueLists = preferences.ContinueLists,
                autoCloseBrackets = preferences.AutoCloseBrackets,
                // The heading level that counts 1, 2, 3, or zero for off. The enum's values
                // are those levels, so the cast is the mapping rather than a coincidence.
                headingNumbers = (int)preferences.HeadingNumbering,
            });
    }

    public Task SetDiagnosticsAsync(Guid documentId, IReadOnlyList<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        return SendAsync("setDiagnostics", new { id = documentId, markers = ToMarkers(diagnostics) });
    }

    public Task ClearDiagnosticsAsync() => SendAsync("clearDiagnostics", new { });

    public Task SetSpellingAsync(Guid documentId, IReadOnlyList<SpellingIssue> misspellings)
    {
        ArgumentNullException.ThrowIfNull(misspellings);

        // The domain type, near enough as it stands, rather than dressed up as a Monaco marker.
        // Misspellings are drawn as decorations - see the shell's setSpelling - so there is no
        // marker shape to fit and no severity to invent, and the zero-based positions stay
        // zero-based all the way across. The shell adds the one, as it does for edits.
        var issues = misspellings.Select(issue => new
        {
            line = issue.Line,
            start = issue.Start,
            length = issue.Length,
            repeated = issue.Kind == SpellingIssueKind.RepeatedWord,
        });

        return SendAsync("setSpelling", new { id = documentId, issues });
    }

    public Task ClearSpellingAsync() => SendAsync("clearSpelling", new { });

    /// <summary>
    /// Turns diagnostics into Monaco's own IMarkerData shape.
    ///
    /// The one place the app's zero-based positions meet Monaco's one-based ones. Unlike
    /// ApplyEditsAsync, where the JS side adds the one, the conversion happens here because what
    /// is being produced is Monaco's own structure rather than an instruction to be translated.
    /// </summary>
    private static object ToMarkers(IReadOnlyList<Diagnostic> diagnostics) =>
        diagnostics.Select(diagnostic => new
        {
            startLineNumber = diagnostic.Line + 1,
            startColumn = diagnostic.Column + 1,
            endLineNumber = diagnostic.Line + 1,
            endColumn = diagnostic.EndColumn + 1,

            // Monaco's own severity scale, which is not a simple ordinal: Hint 1, Info 2,
            // Warning 4, Error 8.
            severity = diagnostic.Severity switch
            {
                DiagnosticSeverity.Warning => 4,
                DiagnosticSeverity.Information => 2,
                _ => 1,
            },
            message = diagnostic.Message,
            source = diagnostic.Rule,
        });

    public Task SetWrapGlyphAsync(bool enabled) => SendAsync("setWrapGlyph", new { enabled });

    /// <summary>
    /// Runs one of the editor's own actions in the page: undo, redo, select all, find.
    ///
    /// Claims XAML focus on the way through, because every caller is a menu item or a
    /// toolbar button and the click that got here left focus on the chrome. The page
    /// cannot fix that from the inside - see <see cref="FocusWebView"/> - so an undo would
    /// land in the text while the next keystroke went to the button.
    ///
    /// Only the XAML half is claimed. Where focus lands inside the page belongs to the
    /// command: undo wants the text, and Find wants its own box.
    /// </summary>
    public Task RunEditorCommandAsync(string command)
    {
        FocusWebView();

        return SendAsync("editorCommand", new { command });
    }

    public Task RequestSelectionForClipboardAsync(bool cut) => SendAsync("requestSelection", new { cut });

    public Task RequestPreviewSelectionForClipboardAsync() => SendAsync("requestPreviewSelection", new { });

    public Task SelectAllInPreviewAsync() => SendAsync("selectAllInPreview", new { });

    // -------------------------------------------------------------------- export

    /// <summary>
    /// Outstanding rendered-HTML requests, keyed by request id.
    ///
    /// The bridge is otherwise one-way, so this is the only place a reply has to be matched
    /// to its request. Keying by id rather than assuming the next message back is the answer
    /// keeps an export correct even if other traffic arrives in between.
    /// </summary>
    private readonly Dictionary<Guid, TaskCompletionSource<string>> _htmlRequests = [];

    public async Task<string> GetRenderedHtmlAsync()
    {
        if (!IsReady)
        {
            return string.Empty;
        }

        var id = Guid.NewGuid();
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        _htmlRequests[id] = completion;

        try
        {
            await SendAsync("requestRenderedHtml", new { requestId = id }).ConfigureAwait(true);

            // A shell that has stopped responding must not hang the export for ever.
            Task finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(10)))
                .ConfigureAwait(true);

            if (finished != completion.Task)
            {
                _logger.LogWarning("The preview did not return its markup within ten seconds.");
                return string.Empty;
            }

            return await completion.Task.ConfigureAwait(true);
        }
        finally
        {
            _htmlRequests.Remove(id);
        }
    }

    /// <summary>Outstanding selection-range requests, keyed the same way as the HTML ones.</summary>
    private readonly Dictionary<Guid, TaskCompletionSource<LineRange?>> _selectionRequests = [];

    public async Task<LineRange?> GetSelectionRangeAsync()
    {
        if (!IsReady)
        {
            return null;
        }

        var id = Guid.NewGuid();
        var completion = new TaskCompletionSource<LineRange?>(TaskCreationOptions.RunContinuationsAsynchronously);

        _selectionRequests[id] = completion;

        try
        {
            await SendAsync("requestSelectionRange", new { requestId = id }).ConfigureAwait(true);

            Task finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(5)))
                .ConfigureAwait(true);

            if (finished != completion.Task)
            {
                _logger.LogWarning("The editor did not report its selection within five seconds.");
                return null;
            }

            return await completion.Task.ConfigureAwait(true);
        }
        finally
        {
            _selectionRequests.Remove(id);
        }
    }

    /// <summary>Outstanding preview-markup requests, keyed the same way as the others.</summary>
    private readonly Dictionary<Guid, TaskCompletionSource<PreviewSelection?>> _previewHtmlRequests = [];

    public async Task<PreviewSelection?> GetPreviewHtmlAsync()
    {
        if (!IsReady)
        {
            return null;
        }

        var id = Guid.NewGuid();
        var completion = new TaskCompletionSource<PreviewSelection?>(TaskCreationOptions.RunContinuationsAsynchronously);

        _previewHtmlRequests[id] = completion;

        try
        {
            await SendAsync("requestPreviewHtml", new { requestId = id }).ConfigureAwait(true);

            Task finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(10)))
                .ConfigureAwait(true);

            if (finished != completion.Task)
            {
                _logger.LogWarning("The preview did not report its markup within ten seconds.");

                return null;
            }

            return await completion.Task.ConfigureAwait(true);
        }
        finally
        {
            _previewHtmlRequests.Remove(id);
        }
    }

    /// <summary>Outstanding edit-context requests, keyed the same way as the others.</summary>
    private readonly Dictionary<Guid, TaskCompletionSource<EditContext?>> _editContextRequests = [];

    public async Task<EditContext?> GetEditContextAsync()
    {
        if (!IsReady)
        {
            return null;
        }

        var id = Guid.NewGuid();
        var completion = new TaskCompletionSource<EditContext?>(TaskCreationOptions.RunContinuationsAsynchronously);

        _editContextRequests[id] = completion;

        try
        {
            await SendAsync("requestEditContext", new { requestId = id }).ConfigureAwait(true);

            Task finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(5)))
                .ConfigureAwait(true);

            if (finished != completion.Task)
            {
                _logger.LogWarning("The editor did not report its selection within five seconds.");
                return null;
            }

            return await completion.Task.ConfigureAwait(true);
        }
        finally
        {
            _editContextRequests.Remove(id);
        }
    }

    public Task ApplyEditsAsync(EditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsEmpty)
        {
            return Task.CompletedTask;
        }

        // The page focuses the editor once the edits land, but only within the page. This
        // is the other half: without it the keyboard stays with the toolbar button that was
        // just clicked, and the arrow keys appear to stop working.
        FocusWebView();

        var edits = result.Edits.Select(edit => new
        {
            startLine = edit.Range.Start.Line,
            startColumn = edit.Range.Start.Column,
            endLine = edit.Range.End.Line,
            endColumn = edit.Range.End.Column,
            text = edit.Text,
        });

        return SendAsync("applyEdits", new
        {
            edits,
            selection = result.Selection is { } selection
                ? new
                {
                    startLine = selection.Start.Line,
                    startColumn = selection.Start.Column,
                    endLine = selection.End.Line,
                    endColumn = selection.End.Column,
                }
                : null,
        });
    }

    public Task ReplaceTextAsync(Guid documentId, string text, RenderedMarkdown rendered) =>
        SendAsync("replaceText", new { id = documentId, text, html = rendered.Html });

    /// <summary>
    /// Paints the canvas behind the page white until the returned handle is disposed.
    ///
    /// The print stylesheet already forces every color in the document to its light values,
    /// but the canvas the page is drawn onto is not part of the document: it is the
    /// WebView's own background, set to match the app's theme so the window does not flash
    /// white on a dark desktop. Chromium composites it underneath the printed page, so a
    /// PDF exported from a dark window came out with a #1F1F1F undercoat showing along
    /// every edge where the page's own white did not quite reach.
    ///
    /// Restored afterwards, so the window goes straight back to matching its theme.
    /// </summary>
    private CanvasColor ForceLightCanvas()
    {
        Windows.UI.Color previous = _webView.DefaultBackgroundColor;
        _webView.DefaultBackgroundColor = Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

        return new CanvasColor(_webView, previous);
    }

    private sealed class CanvasColor(WebView2 view, Windows.UI.Color previous) : IDisposable
    {
        public void Dispose() => view.DefaultBackgroundColor = previous;
    }

    public async Task ExportPdfAsync(string path, PdfPageSetup setup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(setup);

        if (_webView.CoreWebView2 is not { } core)
        {
            throw new InvalidOperationException("The preview is not ready to export.");
        }

        CoreWebView2PrintSettings settings = core.Environment.CreatePrintSettings();

        settings.Orientation = setup.Orientation == PageOrientation.Landscape
            ? CoreWebView2PrintOrientation.Landscape
            : CoreWebView2PrintOrientation.Portrait;

        settings.PageWidth = setup.WidthInches;
        settings.PageHeight = setup.HeightInches;

        settings.MarginTop = setup.MarginInches;
        settings.MarginBottom = setup.MarginInches;
        settings.MarginLeft = setup.MarginInches;
        settings.MarginRight = setup.MarginInches;

        settings.ShouldPrintBackgrounds = setup.IncludeBackgrounds;

        // Deliberately off: the built-in header and footer print the page title and the
        // source URL, and that URL would read https://marqora.assets/shell.html.
        settings.ShouldPrintHeaderAndFooter = false;

        settings.ScaleFactor = 1.0;

        _logger.LogInformation(
            "Printing to {Path} at {Width}x{Height}in, {Margin}in margins.",
            path,
            setup.WidthInches,
            setup.HeightInches,
            setup.MarginInches);

        using var _ = ForceLightCanvas();

        if (!await core.PrintToPdfAsync(path, settings))
        {
            throw new IOException($"The preview could not be written to {path}.");
        }
    }

    /// <summary>
    /// Prints the preview to the printer the user chose, with no dialog of the WebView's own.
    ///
    /// Neither dialog the WebView can raise is any use here. Its print preview is a browser
    /// window that prints a browser's band around the page - the date, the page title and the
    /// virtual-host URL - behind a "Headers and footers" checkbox no API can preset. The
    /// system dialog it claims to offer never appears from a WinUI window: the call returns,
    /// the renderer blocks, and there is nothing on screen. The caller therefore puts up the
    /// Windows print dialog itself and hands the answer here, where the settings the print
    /// goes out on are ours to set - and only on this route can that band be switched off.
    ///
    /// The same print stylesheet an exported PDF goes through applies, so the editor pane
    /// stays out of it and the window does not visibly change while the job runs.
    /// </summary>
    public async Task PrintAsync(PrintJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (_webView.CoreWebView2 is not { } core)
        {
            throw new InvalidOperationException("The preview is not ready to print.");
        }

        _logger.LogInformation(
            "Printing to {Printer}, {Copies} copies, {Width}x{Height}in.",
            job.PrinterName,
            job.Copies,
            job.WidthInches,
            job.HeightInches);

        using var _ = ForceLightCanvas();

        await WebViewPrinting.PrintAsync(core, job);
    }

    public Task InsertTextAsync(string text) => SendAsync("insertText", new { text });

    public Task SetSplitterPositionAsync(double position) =>
        SendAsync("setSplitterPosition", new { position });

    public Task ResetSplitterAsync() => SendAsync("resetSplitter", new { });

    public Task ScrollToEdgeAsync(EditorPane pane, bool toEnd, bool bothPanes) =>
        SendAsync("scrollToEdge", new { pane = pane.ToString(), edge = toEnd ? "end" : "start", both = bothPanes });

    public Task ScrollToLineAsync(int line) => SendAsync("scrollToLine", new { line });

    public Task SelectRangeAsync(Guid documentId, int line, int column, int length, bool focusEditor)
    {
        // Monaco's own focus() cannot do this half of it, for the reason FocusWebView spells
        // out: without XAML focus the page never sees a keystroke at all.
        if (focusEditor)
        {
            FocusWebView();
        }

        return SendAsync("selectRange", new { id = documentId, line, column, length, focus = focusEditor });
    }

    public Task FocusEditorAsync() => FocusPaneAsync(EditorPane.Source);

    public Task FocusPaneAsync(EditorPane pane)
    {
        // The result is worth knowing when focus does not stick: the page below will move
        // its own caret either way, so a refusal here is invisible on screen and looks like
        // the shell ignoring the request. XAML declines while the element is not yet
        // loaded, or while something else is holding focus down.
        if (!FocusWebView())
        {
            _logger.LogWarning("The WebView refused XAML focus; the keyboard stays on the chrome.");
        }

        return SendAsync("focusPane", new { pane = pane.ToString() });
    }

    /// <summary>
    /// Gives the WebView the keyboard back.
    ///
    /// This is the half the page cannot do for itself. Monaco's own focus() moves the caret
    /// inside the document, but that is focus within the page: if the WebView2 element does
    /// not hold XAML focus, Windows never routes a keystroke to the page at all and the keys
    /// go to whatever chrome was last clicked. After a toolbar button that is the button, so
    /// the arrow keys quietly do nothing until the user clicks back into the text.
    /// </summary>
    private bool FocusWebView() => _webView.Focus(FocusState.Programmatic);

    public void WatchDiagrams(IReadOnlyCollection<DiagramWatch> diagrams) =>
        _ = SendAsync("watchDiagrams", new
        {
            items = diagrams
                .Select(diagram => new { id = diagram.Id, documentId = diagram.DocumentId, hash = diagram.Hash })
                .ToArray(),
        });

    private Task SendAsync(string type, object payload)
    {
        string json = JsonSerializer.Serialize(new { type, payload }, JsonOptions);

        // Nothing is ever going to read this one. Queueing past the point where the preview
        // has been given up on would rebuild the very backlog this class now takes care not
        // to keep - a window that goes on filing commands behind a page that is not coming
        // back. See BeginRecovery.
        if (_abandoned)
        {
            return Task.CompletedTask;
        }

        if (!IsReady || _webView.CoreWebView2 is null)
        {
            _pending.Add(json);
            return Task.CompletedTask;
        }

        Post(json);
        return Task.CompletedTask;
    }

    private void Post(string json)
    {
        try
        {
            _webView.CoreWebView2.PostWebMessageAsJson(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not post a message to the preview shell.");
        }
    }

    // ------------------------------------------------------------------- inbound

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string raw;

        try
        {
            raw = e.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            // Not a string message; nothing the shell sends should reach here.
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            JsonElement root = document.RootElement;

            if (!root.TryGetProperty("type", out JsonElement typeElement))
            {
                return;
            }

            string type = typeElement.GetString() ?? string.Empty;
            JsonElement payload = root.TryGetProperty("payload", out JsonElement p) ? p : default;

            Dispatch(type, payload);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Malformed message from the preview shell.");
        }
    }

    private void Dispatch(string type, JsonElement payload)
    {
        switch (type)
        {
            case "ready":
                OnShellReady(payload);
                break;

            case "fontsResolved":
                ResolvedSourceFont = ReadString(payload, "sourceFont");
                ResolvedPreviewFont = ReadString(payload, "previewFont");
                FontsResolved?.Invoke(this, EventArgs.Empty);
                break;

            case "editorTextChanged":
                if (Guid.TryParse(ReadString(payload, "documentId"), out Guid editedId))
                {
                    EditorTextChanged?.Invoke(
                        this,
                        new EditorTextChangedEventArgs(editedId, ReadString(payload, "text")));
                }
                break;

            case "zoomChanged":
                if (Enum.TryParse(ReadString(payload, "pane"), out EditorPane pane))
                {
                    ZoomChanged?.Invoke(this, new ZoomChangedEventArgs(pane, ReadInt(payload, "percent", ZoomLevel.Default)));
                }
                break;

            case "splitterMoved":
                SplitterMoved?.Invoke(this, ReadDouble(payload, "position", 0.5));
                break;

            case "linkActivated":
                RaiseLinkActivated(ReadString(payload, "url"));
                break;

            case "command":
                CommandInvoked?.Invoke(this, ReadString(payload, "name"));
                break;

            case "selectionCopied":
                SelectionCopied?.Invoke(this, ReadString(payload, "text"));
                break;

            case "diagramActivated":
            {
                // A diagram that failed to render has no SVG to show, and the shell should
                // not have sent one; dropping it here keeps an empty window off the screen.
                if (Guid.TryParse(ReadString(payload, "documentId"), out Guid documentId)
                    && ReadString(payload, "hash") is { Length: > 0 } hash
                    && ReadString(payload, "svg") is { Length: > 0 } svg)
                {
                    DiagramActivated?.Invoke(
                        this,
                        new DiagramActivatedEventArgs(documentId, ReadInt(payload, "index", 0), hash, svg));
                }

                break;
            }

            case "diagramUpdated":
            {
                if (Guid.TryParse(ReadString(payload, "id"), out Guid updated)
                    && ReadString(payload, "svg") is { Length: > 0 } redrawn)
                {
                    DiagramUpdated?.Invoke(
                        this,
                        new DiagramUpdatedEventArgs(
                            updated,
                            ReadString(payload, "hash"),
                            ReadInt(payload, "index", 0),
                            redrawn));
                }

                break;
            }

            case "diagramRemoved":
            {
                if (Guid.TryParse(ReadString(payload, "id"), out Guid removed))
                {
                    DiagramRemoved?.Invoke(this, removed);
                }

                break;
            }

            case "diagramInvalid":
            {
                if (Guid.TryParse(ReadString(payload, "id"), out Guid invalid))
                {
                    DiagramInvalid?.Invoke(
                        this,
                        new DiagramInvalidEventArgs(invalid, ReadString(payload, "message")));
                }

                break;
            }

            case "renderedHtml":
                if (Guid.TryParse(ReadString(payload, "requestId"), out Guid requestId)
                    && _htmlRequests.TryGetValue(requestId, out TaskCompletionSource<string>? pending))
                {
                    pending.TrySetResult(ReadString(payload, "html"));
                }
                break;

            case "selectionRange":
                if (Guid.TryParse(ReadString(payload, "requestId"), out Guid selectionId)
                    && _selectionRequests.TryGetValue(selectionId, out TaskCompletionSource<LineRange?>? waiting))
                {
                    int start = ReadInt(payload, "startLine", -1);
                    int end = ReadInt(payload, "endLine", -1);

                    waiting.TrySetResult(start < 0 || end < start ? null : new LineRange(start, end));
                }
                break;

            case "caretState":
                // Read before the size check below, and deliberately: how much text is
                // selected has no bearing on whether there is anything to undo, so a
                // selection too large to describe still reports its history honestly.
                HistoryStateChanged?.Invoke(this, new HistoryState(
                    ReadBool(payload, "canUndo", true),
                    ReadBool(payload, "canRedo", true)));

                // Null when the selection was too large to be worth shipping. The toolbar
                // reads that as "nothing active" rather than guessing.
                CaretStateChanged?.Invoke(
                    this,
                    ReadBool(payload, "truncated", false) ? null : ReadEditContext(payload));
                break;

            case "previewHtml":
                if (Guid.TryParse(ReadString(payload, "requestId"), out Guid previewId)
                    && _previewHtmlRequests.TryGetValue(previewId, out TaskCompletionSource<PreviewSelection?>? preview))
                {
                    preview.TrySetResult(new PreviewSelection(
                        ReadString(payload, "html"), ReadString(payload, "text")));
                }
                break;

            case "editContext":
                if (Guid.TryParse(ReadString(payload, "requestId"), out Guid contextId)
                    && _editContextRequests.TryGetValue(contextId, out TaskCompletionSource<EditContext?>? context))
                {
                    context.TrySetResult(ReadEditContext(payload));
                }
                break;

            case "contextMenu":
                if (Enum.TryParse(ReadString(payload, "pane"), out EditorPane clicked))
                {
                    // An empty word means the pointer was not over a misspelling, which is how
                    // the menu decides whether to offer suggestions at all.
                    string misspelled = ReadString(payload, "word") ?? string.Empty;

                    SpellingHit? spelling = misspelled.Length == 0
                        ? null
                        : new SpellingHit(
                            misspelled,
                            ReadInt(payload, "wordLine", -1),
                            ReadInt(payload, "wordStart", -1),
                            ReadInt(payload, "wordEnd", -1),
                            ReadBool(payload, "wordRepeated", false));

                    ContextMenuRequested?.Invoke(this, new PaneContextMenuEventArgs(
                        clicked,
                        ReadDouble(payload, "x", 0),
                        ReadDouble(payload, "y", 0),
                        ReadBool(payload, "hasSelection", false),
                        Localize(ReadString(payload, "linkUrl")),
                        Localize(ReadString(payload, "imageUrl")),
                        spelling));
                }
                break;

            case "paneFocused":
                if (Enum.TryParse(ReadString(payload, "pane"), out EditorPane focused))
                {
                    PaneFocused?.Invoke(this, focused);
                }
                break;

            case "viewportLine":
                ViewportLineChanged?.Invoke(this, ReadInt(payload, "line", 0));
                break;

            case "stats":
                StatsChanged?.Invoke(this, new EditorStats(
                    ReadInt(payload, "line", 1),
                    ReadInt(payload, "column", 1),
                    ReadInt(payload, "lineCount", 0),
                    ReadInt(payload, "words", 0),
                    ReadInt(payload, "characters", 0)));
                break;

            case "log":
                LogFromShell(payload);
                break;

            default:
                _logger.LogDebug("Ignoring unknown shell message {Type}.", type);
                break;
        }
    }

    private void OnShellReady(JsonElement payload)
    {
        IsReady = true;

        // The stylesheet's own font stacks, so the preferences dialog can say what
        // "(default)" resolves to without a second copy of them living in C#.
        DefaultSourceFont = ReadString(payload, "sourceFont");
        DefaultPreviewFont = ReadString(payload, "previewFont");

        _logger.LogInformation("Preview shell reported ready.");

        foreach (string message in _pending)
        {
            Post(message);
        }

        _pending.Clear();

        Ready?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Surfaces script errors from the WebView, which are otherwise invisible.</summary>
    private void LogFromShell(JsonElement payload)
    {
        string level = ReadString(payload, "level");
        string message = ReadString(payload, "message");
        string detail = ReadString(payload, "detail");

        switch (level)
        {
            case "error":
                _logger.LogError("Preview shell: {Message} {Detail}", message, detail);
                break;
            case "warning":
                _logger.LogWarning("Preview shell: {Message} {Detail}", message, detail);
                break;
            default:
                _logger.LogInformation("Preview shell: {Message}", message);
                break;
        }
    }

    private void RaiseLinkActivated(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            ExternalLinkActivated?.Invoke(this, uri);
        }
        else
        {
            _logger.LogDebug("Ignoring link with an unusable target: {Url}", url);
        }
    }

    // ------------------------------------------------------------------ guards

    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs e)
    {
        // The shell is the only page this control ever shows. Anything else is either a
        // document link, which the host routes, or something unexpected.
        if (e.Uri.StartsWith(_assets.ShellUri.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _logger.LogInformation("Blocked in-place navigation to {Uri}.", e.Uri);
        e.Cancel = true;

        RaiseLinkActivated(e.Uri);
    }

    private void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        RaiseLinkActivated(e.Uri);
    }

    // ----------------------------------------------------------------- recovery

    /// <summary>
    /// How many times a dead WebView is rebuilt before the host stops trying.
    ///
    /// A crash that repeats straight away is not going to be cured by another attempt, and
    /// restarting for ever would hide a broken installation behind a flickering pane. Past
    /// this many, the user is told instead.
    /// </summary>
    private const int MaxRecoveryAttempts = 3;

    /// <summary>Wait before the first retry. Later ones wait a multiple of it.</summary>
    private static readonly TimeSpan RecoveryDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long a restarted WebView is given to finish its handshake.
    ///
    /// A control that comes back but never reports ready is, from where the user sits, the
    /// same blank pane as one that never came back, so the wait ends in another attempt
    /// rather than in silence.
    /// </summary>
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A crash this long after the previous one starts the count again. Marqora gets left
    /// open for days, so two unrelated crashes a week apart must not add up to a refusal to
    /// restart the second one.
    /// </summary>
    private static readonly TimeSpan UnrelatedAfter = TimeSpan.FromMinutes(10);

    private DateTimeOffset _lastFailure = DateTimeOffset.MinValue;
    private int _recoveryAttempts;
    private bool _recovering;
    private bool _abandoned;

    /// <summary>
    /// Raised when the preview could not be restarted and the pane is going to stay blank.
    ///
    /// The window turns this into something the user can read. It is not on
    /// <see cref="IPreviewHost"/> because there is nothing the view model could do with it:
    /// the workspace still holds every document's text, so saving still works, and what is
    /// left to do is say so.
    /// </summary>
    public event EventHandler? RecoveryFailed;

    private void OnProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs e)
    {
        // Repeats for as long as the page is busy rather than broken, so it gets a warning
        // and nothing else. Monaco working through a large paste looks exactly like this,
        // and reloading would throw away the keystrokes the workspace has not been told
        // about yet: a cure worse than a pane that catches up a second later.
        if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
        {
            _logger.LogWarning("The preview is not responding ({Reason}). Waiting for it.", e.Reason);
            return;
        }

        // Everything WebView2 is willing to say about it, because it will not be asked
        // twice. There is no managed exception behind any of this - the process that died
        // was not this one, so nothing was thrown here and there is no stack to catch.
        //
        // The exit code is the part worth reading, and it is read in hex: 0xC0000005 is an
        // access violation, 0xC0000409 a corrupted stack, and 0xFFFFFFFF or 0x1 is not a
        // crash at all but something outside the app ending the process on purpose - Task
        // Manager, a Stop-Process, a killed debug run. Reason does not separate those; it
        // comes back "Unexpected" for every one of them, which is how a real crash and a
        // deliberate kill used to write the same line in this log.
        //
        // The dump folder is the rest of the answer. WebView2 runs Crashpad and has already
        // written the native stack there by the time this line is logged.
        _logger.LogError(
            "WebView process failed: {Kind} ({Reason}), exit code 0x{ExitCode:X8}, process {Process}. "
            + "Crash dumps: {FailureReportFolder}",
            e.ProcessFailedKind,
            e.Reason,
            e.ExitCode,
            string.IsNullOrWhiteSpace(e.ProcessDescription) ? "browser" : e.ProcessDescription,
            _failureReportFolder ?? "(unknown)");

        switch (e.ProcessFailedKind)
        {
            // The whole WebView went with it, this control included. Every message sent
            // from here on lands nowhere, and only a new control brings the panes back.
            case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                BeginRecovery(rebuild: true);
                break;

            // The control outlived the page. Reloading it is enough.
            case CoreWebView2ProcessFailedKind.RenderProcessExited:
                BeginRecovery(rebuild: false);
                break;

            // GPU, utility, sandbox helper, plugin and iframe renderer processes. WebView2
            // restarts these itself and the panes carry on drawing, so IsReady is left
            // alone on purpose: clearing it would queue every message from that moment on
            // and leave the window ignoring the user over something already fixed.
            default:
                break;
        }
    }

    /// <summary>Starts one attempt at getting the preview back, unless one is already running.</summary>
    private void BeginRecovery(bool rebuild)
    {
        IsReady = false;

        // Whatever was queued for the dead page is stale. Coming back ends in the view
        // model pushing the whole of the app's state at the new page, so replaying the
        // backlog would only duplicate it - and keeping it would grow without limit if the
        // preview never came back at all. That unbounded queue is what a blank pane was
        // made of: the window went on accepting Select All and Find for hours and filed
        // every one of them behind a page that no longer existed.
        _pending.Clear();

        if (_disposed || _recovering || _abandoned)
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (now - _lastFailure > UnrelatedAfter)
        {
            _recoveryAttempts = 0;
        }

        _lastFailure = now;

        if (_recoveryAttempts >= MaxRecoveryAttempts)
        {
            _abandoned = true;

            _logger.LogError(
                "Giving up on the preview after {Attempts} attempts to restart it.",
                _recoveryAttempts);

            RecoveryFailed?.Invoke(this, EventArgs.Empty);
            return;
        }

        _recoveryAttempts++;
        _recovering = true;

        // Off the failure callback rather than inside it: closing a control from its own
        // event asks the WebView to tear itself down while it is still talking.
        if (!_surface.DispatcherQueue.TryEnqueue(() => _ = RecoverAsync(rebuild)))
        {
            _recovering = false;
            _logger.LogError("Could not schedule the preview restart; the window has gone.");
        }
    }

    private async Task RecoverAsync(bool rebuild)
    {
        bool failed = false;

        try
        {
            // A moment before trying, and longer each time. A browser process that has just
            // died is usually still busy taking its children down with it.
            await Task.Delay(RecoveryDelay * _recoveryAttempts).ConfigureAwait(true);

            if (_disposed)
            {
                return;
            }

            _logger.LogInformation(
                "Restarting the preview, attempt {Attempt} of {Max}.",
                _recoveryAttempts,
                MaxRecoveryAttempts);

            if (rebuild)
            {
                ReplaceWebView();
                await AttachAsync().ConfigureAwait(true);
            }
            else
            {
                _webView.Reload();
            }

            // Nothing is restored from here. The shell reports ready on its own, which
            // raises Ready, and the view model answers that by pushing the theme, the view
            // mode, the zoom and every open tab back into the new page - the same path a
            // cold start takes.
            await WaitForReadyAsync().ConfigureAwait(true);

            if (_disposed)
            {
                return;
            }

            if (IsReady)
            {
                _logger.LogInformation("The preview is back.");
            }
            else
            {
                _logger.LogError(
                    "The preview did not report ready within {Seconds} seconds.",
                    ReadyTimeout.TotalSeconds);

                failed = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not restart the preview.");
            failed = true;
        }
        finally
        {
            _recovering = false;
        }

        // A control that would not come up is worth replacing outright next time, whichever
        // kind of failure started this.
        if (failed && !_disposed)
        {
            BeginRecovery(rebuild: true);
        }
    }

    /// <summary>Waits for the shell's handshake, or for <see cref="ReadyTimeout"/>.</summary>
    private async Task WaitForReadyAsync()
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + ReadyTimeout;

        while (!IsReady && !_disposed && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(true);
        }
    }

    /// <summary>Throws the dead control away and puts a fresh one in the surface panel.</summary>
    private void ReplaceWebView()
    {
        WebView2 dead = _webView;

        DetachCore(dead);
        _surface.Children.Remove(dead);

        try
        {
            dead.Close();
        }
        catch (Exception ex)
        {
            // It is being discarded either way, and a complaint from a control whose process
            // has already gone is worth no more than a line in the log.
            _logger.LogDebug(ex, "Closing the failed WebView complained.");
        }

        // The document folder is kept: it is state of this class, not of the control that
        // died, and AttachAsync subscribes the resource handler on the replacement.
        _webView = _createWebView();
        _surface.Children.Add(_webView);
    }

    private void DetachCore(WebView2 view)
    {
        if (view.CoreWebView2 is { } core)
        {
            core.WebMessageReceived -= OnWebMessageReceived;
            core.NavigationStarting -= OnNavigationStarting;
            core.NewWindowRequested -= OnNewWindowRequested;
            core.ProcessFailed -= OnProcessFailed;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        DetachCore(_webView);
    }

    // --------------------------------------------------------------- json helpers

    private static string ReadString(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;

    private static int ReadInt(JsonElement payload, string name, int fallback) =>
        payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int result)
                ? result
                : fallback;

    private static bool ReadBool(JsonElement payload, string name, bool fallback) =>
        payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(name, out JsonElement value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : fallback;

    /// <summary>
    /// Rebuilds an editing context from the shell's reply. Null when the editor had no
    /// model to report on, which the caller treats as nothing to do.
    /// </summary>
    private static EditContext? ReadEditContext(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("lines", out JsonElement lines)
            || lines.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        List<string> text = [];
        foreach (JsonElement line in lines.EnumerateArray())
        {
            text.Add(line.GetString() ?? string.Empty);
        }

        var selection = new TextRange(
            new TextPosition(ReadInt(payload, "startLine", 0), ReadInt(payload, "startColumn", 0)),
            new TextPosition(ReadInt(payload, "endLine", 0), ReadInt(payload, "endColumn", 0)));

        return new EditContext(text, ReadInt(payload, "firstLine", 0), selection);
    }

    /// <summary>
    /// Turns a URL from the preview into something worth putting on the clipboard.
    ///
    /// Relative links and images in a document resolve against the marqora.document virtual
    /// host, so what the page reports for the picture next to a paragraph is
    /// https://marqora.document/diagrams/flow.png - an address that means nothing outside
    /// this WebView. Mapping it back to the file it actually is gives the user a path they
    /// can paste. Anything else, http and mailto included, is already what they asked for.
    /// </summary>
    private string? Localize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (_documentDirectory is null
            || !value.StartsWith(DocumentBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        string relative = value[DocumentBaseUrl.Length..];

        // A query or fragment on a local file is not part of its path.
        int cut = relative.IndexOfAny(['?', '#']);
        if (cut >= 0)
        {
            relative = relative[..cut];
        }

        try
        {
            return Path.GetFullPath(Path.Combine(
                _documentDirectory,
                Uri.UnescapeDataString(relative).Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            // Not a path this machine can express; the URL is still better than nothing.
            _logger.LogDebug(ex, "Could not map {Url} back to a file path.", value);
            return value;
        }
    }

    private static double ReadDouble(JsonElement payload, string name, double fallback) =>
        payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out double result)
                ? result
                : fallback;
}

/// <summary>Cursor position and document counts reported by the editor.</summary>
public readonly record struct EditorStats(int Line, int Column, int LineCount, int Words, int Characters);

/// <summary>Whether the active document has anything left to undo or to redo.</summary>
public readonly record struct HistoryState(bool CanUndo, bool CanRedo);
