// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.Web.WebView2.Core;
using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.App.Services;
using PaulTechGuy.MQ.Domain;
using Windows.Graphics;
using Windows.System;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// The floating markdown cheatsheet.
///
/// Built in code rather than XAML because there is nothing to lay out: the window is a
/// WebView showing one page. It is presented as a tool window, which is what a reference
/// palette should be — thin caption, no maximize, and out of the taskbar so it never looks
/// like a second document.
///
/// Closing it hides it instead. Rebuilding a WebView costs the better part of a second and
/// would throw away the scroll position, and the user is expected to dismiss and recall this
/// window often. <see cref="Shutdown"/> is the only path that truly closes it.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "A Window's lifetime belongs to the framework; the WebView is released in "
        + "Shutdown, which the application calls as it exits.")]
public sealed partial class CheatsheetWindow : Window
{
    /// <summary>Small enough to tuck beside an editor, large enough for a table to fit.</summary>
    private const int MinimumWidth = 360;
    private const int MinimumHeight = 320;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IWebAssetProvider _assets;
    private readonly IMarkdownRenderer _renderer;
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly ILogger<CheatsheetWindow> _logger;

    private readonly WebView2 _webView = new();

    /// <summary>The right-click menu, built the first time it is needed.</summary>
    private MenuFlyout? _menu;
    private MenuFlyoutItem? _copyItem;

    /// <summary>Messages raised before the page announced itself, replayed once it does.</summary>
    private readonly List<string> _pending = [];

    private readonly IntPtr _ownerHandle;

    private bool _isReady;
    private bool _isShuttingDown;
    private bool _hasRestoredPlacement;
    private bool _isOwned;

    public CheatsheetWindow(
        IWebAssetProvider assets,
        IMarkdownRenderer renderer,
        ISettingsService settings,
        IThemeService theme,
        IntPtr ownerHandle,
        ILogger<CheatsheetWindow> logger)
    {
        _assets = assets;
        _renderer = renderer;
        _settings = settings;
        _theme = theme;
        _ownerHandle = ownerHandle;
        _logger = logger;

        Title = "Markdown Cheatsheet";

        // A themed background behind the WebView, so the window does not flash white on a
        // dark desktop during the moment before the page paints.
        var root = new Grid
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"],
            RequestedTheme = theme.Effective == AppTheme.Dark ? ElementTheme.Dark : ElementTheme.Light,
        };

        root.Children.Add(_webView);
        Content = root;

        ConfigurePresenter();

        AppWindow.Changed += OnAppWindowChanged;
        AppWindow.Closing += OnClosing;

        _theme.EffectiveThemeChanged += OnEffectiveThemeChanged;
    }

    /// <summary>
    /// Makes the main window this window's owner, once, the first time it is shown.
    ///
    /// An owned window always floats above its owner, so the cheatsheet cannot end up buried
    /// behind the editor. That is worth more than it first appears: it is what lets the Tools
    /// menu item be a plain toggle. Without ownership, opening the menu activates the main
    /// window and raises it over the cheatsheet, so any rule asking "can the user see it right
    /// now" would be answering about a state the click itself had just changed.
    ///
    /// Ownership is not modality — the main window stays fully usable. It also means the
    /// cheatsheet minimises and restores along with the editor, which is what one expects of a
    /// palette belonging to a document window.
    ///
    /// It has to happen after the first show, not in the constructor. Setting the owner on a
    /// window WinUI has created but not yet displayed leaves it without WS_VISIBLE, and every
    /// later AppWindow.Show() silently does nothing.
    /// </summary>
    private void EnsureOwned()
    {
        if (_isOwned)
        {
            return;
        }

        _isOwned = true;

        if (_ownerHandle == IntPtr.Zero)
        {
            _logger.LogWarning("The cheatsheet has no owner window; it may fall behind the editor.");
            return;
        }

        _ = SetWindowLongPtr(Handle, GwlpHwndParent, _ownerHandle);
        _logger.LogDebug("The cheatsheet is now owned by the main window.");
    }

    /// <summary>Native handle, so the service can ask the shell what the user can see.</summary>
    public IntPtr Handle => WinRT.Interop.WindowNative.GetWindowHandle(this);

    /// <summary>
    /// Raised when the window is shown or hidden. Sourced from AppWindow rather than from
    /// the call sites, so dismissing with the close button reports itself the same way the
    /// menu command does.
    ///
    /// Named apart from Window.VisibilityChanged deliberately: that one carries WinRT event
    /// args and reports minimize and restore as well, and shadowing it would leave callers
    /// unsure which of the two they had subscribed to.
    /// </summary>
    public event EventHandler<bool>? ShownOrHidden;

    private void ConfigurePresenter()
    {
        // A reference palette rather than a second document: resizable, but with nothing to
        // maximize or minimize, and out of Alt+Tab and the taskbar. The Tools menu is how it
        // is recalled, so a taskbar button would only be a second, inconsistent way to
        // manage it.
        //
        // The flags are set individually rather than through CreateForToolWindow, whose
        // defaults do not survive being applied to a XAML Window's AppWindow.
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.PreferredMinimumWidth = MinimumWidth;
            presenter.PreferredMinimumHeight = MinimumHeight;
        }

        AppWindow.IsShownInSwitchers = false;

        if (AppImages.HasIcon)
        {
            try
            {
                AppWindow.SetIcon(AppImages.IconPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not apply the cheatsheet window icon.");
            }
        }

        ApplyTitleBarTheme(_theme.Effective);
    }

    /// <summary>
    /// Paints the caption to match the page below it.
    ///
    /// Left alone, Windows draws the caption in the user's accent colour, which on a window
    /// that is almost entirely one document reads as a stripe of unrelated colour. The main
    /// window sidesteps this by extending its content into the title bar; this one is too
    /// small to give up the caption, so the caption is coloured instead.
    /// </summary>
    private void ApplyTitleBarTheme(AppTheme theme)
    {
        AppWindowTitleBar bar = AppWindow.TitleBar;

        bool dark = theme == AppTheme.Dark;

        Windows.UI.Color surface = dark ? Rgb(0x27, 0x27, 0x27) : Rgb(0xF6, 0xF6, 0xF6);
        Windows.UI.Color text = dark ? Rgb(0xE6, 0xE6, 0xE6) : Rgb(0x1B, 0x1B, 0x1B);
        Windows.UI.Color muted = dark ? Rgb(0x8A, 0x8A, 0x8A) : Rgb(0x8A, 0x8A, 0x8A);
        Windows.UI.Color hover = dark ? Rgb(0x38, 0x38, 0x38) : Rgb(0xE6, 0xE6, 0xE6);
        Windows.UI.Color pressed = dark ? Rgb(0x4A, 0x4A, 0x4A) : Rgb(0xD0, 0xD0, 0xD0);

        bar.BackgroundColor = surface;
        bar.InactiveBackgroundColor = surface;
        bar.ForegroundColor = text;
        bar.InactiveForegroundColor = muted;

        bar.ButtonBackgroundColor = surface;
        bar.ButtonInactiveBackgroundColor = surface;
        bar.ButtonForegroundColor = text;
        bar.ButtonInactiveForegroundColor = muted;
        bar.ButtonHoverBackgroundColor = hover;
        bar.ButtonHoverForegroundColor = text;
        bar.ButtonPressedBackgroundColor = pressed;
        bar.ButtonPressedForegroundColor = text;
    }

    private static Windows.UI.Color Rgb(byte r, byte g, byte b) =>
        Windows.UI.Color.FromArgb(0xFF, r, g, b);

    // ------------------------------------------------------------------ startup

    /// <summary>
    /// Brings the WebView up and loads the cheatsheet. Safe to call more than once; the
    /// second and later calls do nothing, so showing the window is always cheap.
    /// </summary>
    public async Task InitializeAsync(RectInt32 nearby)
    {
        RestorePlacement(nearby);

        if (_webView.CoreWebView2 is not null)
        {
            return;
        }

        if (!_assets.IsAvailable)
        {
            _logger.LogError(
                "Refusing to open the cheatsheet: missing web assets {Missing}.",
                string.Join(", ", _assets.MissingAssets));
            return;
        }

        await _webView.EnsureCoreWebView2Async();

        if (_webView.CoreWebView2 is not { } core)
        {
            // EnsureCoreWebView2Async returning without a core means the runtime failed to
            // start. The main window reports that case already, so this stays quiet.
            _logger.LogError("The cheatsheet WebView did not initialize.");
            return;
        }

        // Off, and replaced by a WinUI flyout this window shows. Chromium's own was drawn
        // by Edge, so it followed Edge's dark mode rather than the app's theme, and it
        // offered browser commands a reference page has no use for. See ShowContextMenu.
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsSwipeNavigationEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;

        // The page is a reference document, so the browser's own find bar and reload are
        // welcome here in a way they are not over the editor.
        core.Settings.AreBrowserAcceleratorKeysEnabled = true;

#if DEBUG
        core.Settings.AreDevToolsEnabled = true;
#else
        core.Settings.AreDevToolsEnabled = false;
#endif

        core.SetVirtualHostNameToFolderMapping(
            _assets.VirtualHostName,
            _assets.RootDirectory,
            CoreWebView2HostResourceAccessKind.Allow);

        core.WebMessageReceived += OnWebMessageReceived;
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.ProcessFailed += OnProcessFailed;

        _logger.LogInformation("Opening the cheatsheet at {Uri}.", _assets.CheatsheetUri);
        _webView.Source = _assets.CheatsheetUri;
    }

    /// <summary>Reads the cheatsheet from disk and renders it through the usual pipeline.</summary>
    private async Task SendContentAsync()
    {
        string path = _assets.CheatsheetSourcePath;

        try
        {
            string markdown = await File.ReadAllTextAsync(path).ConfigureAwait(true);
            RenderedMarkdown rendered = _renderer.Render(markdown);

            Send("setContent", new { html = rendered.Html });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not read the cheatsheet at {Path}.", path);

            Send("setContent", new
            {
                html = "<div class=\"mq-render-error\" role=\"alert\"><strong>The cheatsheet could not be "
                    + "loaded.</strong><p>Run build/Get-WebAssets.ps1 and restart Marqora.</p></div>",
            });
        }
    }

    // ------------------------------------------------------------------- bridge

    private void Send(string type, object payload)
    {
        string json = JsonSerializer.Serialize(new { type, payload }, JsonOptions);

        if (!_isReady || _webView.CoreWebView2 is null)
        {
            _pending.Add(json);
            return;
        }

        Post(json);
    }

    private void Post(string json)
    {
        try
        {
            _webView.CoreWebView2.PostWebMessageAsJson(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not post a message to the cheatsheet page.");
        }
    }

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

            if (!root.TryGetProperty("type", out JsonElement typeElement))
            {
                return;
            }

            JsonElement payload = root.TryGetProperty("payload", out JsonElement p) ? p : default;

            switch (typeElement.GetString())
            {
                case "ready":
                    OnPageReady();
                    break;

                case "scrolled":
                    RememberScroll(ReadInt(payload, "top"));
                    break;

                case "linkActivated":
                    OpenExternally(ReadString(payload, "url"));
                    break;

                case "contextMenu":
                    ShowContextMenu(
                        ReadInt(payload, "x"),
                        ReadInt(payload, "y"),
                        payload.ValueKind == JsonValueKind.Object
                            && payload.TryGetProperty("hasSelection", out JsonElement selected)
                            && selected.ValueKind == JsonValueKind.True);
                    break;

                case "selectionCopied":
                    ClipboardText.Set(ReadString(payload, "text"), _logger);
                    break;

                case "log":
                    LogFromPage(payload);
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Malformed message from the cheatsheet page.");
        }
    }

    /// <summary>
    /// The page's right-click menu: reading and printing, which is all a reference page
    /// offers. A WinUI flyout rather than the browser's own, so it is the same menu as
    /// everywhere else in Marqora and takes its appearance from Themes/Menus.xaml.
    ///
    /// Built once and kept; only the Copy item changes, and only with the selection.
    /// </summary>
    private void ShowContextMenu(int x, int y, bool hasSelection)
    {
        if (_menu is null)
        {
            _menu = new MenuFlyout();

            _copyItem = new MenuFlyoutItem { Text = "Copy", KeyboardAcceleratorTextOverride = "Ctrl+C" };
            _copyItem.Click += (_, _) => Send("requestSelection", new { });

            var selectAll = new MenuFlyoutItem { Text = "Select All", KeyboardAcceleratorTextOverride = "Ctrl+A" };
            selectAll.Click += (_, _) => Send("selectAll", new { });

            var print = new MenuFlyoutItem { Text = "Print...", KeyboardAcceleratorTextOverride = "Ctrl+P" };
            print.Click += (_, _) => Print();

            _menu.Items.Add(_copyItem);
            _menu.Items.Add(selectAll);
            _menu.Items.Add(new MenuFlyoutSeparator());
            _menu.Items.Add(print);
        }

        if (_copyItem is not null)
        {
            _copyItem.IsEnabled = hasSelection;
        }

        _menu.ShowAt(_webView, new FlyoutShowOptions
        {
            Position = new Windows.Foundation.Point(x, y),
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
            ShowMode = FlyoutShowMode.Standard,
        });
    }

    /// <summary>
    /// Prints the cheatsheet: the Windows print dialog, then the pages.
    ///
    /// The same two steps the preview takes, and for the same reason - see
    /// <see cref="Services.Win32PrintDialog"/>. Neither dialog the WebView can raise will do.
    /// </summary>
    private async void Print()
    {
        try
        {
            if (_webView.CoreWebView2 is not { } core)
            {
                return;
            }

            PrintJob? job = Win32PrintDialog.Show(
                WinRT.Interop.WindowNative.GetWindowHandle(this),
                PdfPageSetup.Default);

            if (job is null)
            {
                return;
            }

            await WebViewPrinting.PrintAsync(core, job);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not print the cheatsheet.");
        }
    }

    private void OnPageReady()
    {
        _isReady = true;

        Send("setTheme", new { theme = _theme.Effective.ToString() });
        Send("restoreScroll", new { top = _settings.Current.CheatsheetScrollTop });

        foreach (string message in _pending)
        {
            Post(message);
        }

        _pending.Clear();

        _ = SendContentAsync();
    }

    private void OnEffectiveThemeChanged(object? sender, AppTheme theme)
    {
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = theme == AppTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
        }

        ApplyTitleBarTheme(theme);

        Send("setTheme", new { theme = theme.ToString() });
    }

    private void RememberScroll(int top) =>
        _settings.Update(s => s with { CheatsheetScrollTop = top });

    private void OpenExternally(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return;
        }

        if (uri.Scheme is not ("http" or "https" or "mailto"))
        {
            _logger.LogInformation("Ignoring a cheatsheet link with scheme {Scheme}.", uri.Scheme);
            return;
        }

        _ = Launcher.LaunchUriAsync(uri);
    }

    private void LogFromPage(JsonElement payload)
    {
        string message = ReadString(payload, "message");
        string detail = ReadString(payload, "detail");

        switch (ReadString(payload, "level"))
        {
            case "error":
                _logger.LogError("Cheatsheet page: {Message} {Detail}", message, detail);
                break;
            case "warning":
                _logger.LogWarning("Cheatsheet page: {Message} {Detail}", message, detail);
                break;
            default:
                _logger.LogInformation("Cheatsheet page: {Message}", message);
                break;
        }
    }

    // ------------------------------------------------------------------- guards

    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri.StartsWith(_assets.CheatsheetUri.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _logger.LogInformation("Blocked cheatsheet navigation to {Uri}.", e.Uri);
        e.Cancel = true;

        OpenExternally(e.Uri);
    }

    private void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        OpenExternally(e.Uri);
    }

    private void OnProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs e)
    {
        _isReady = false;
        _logger.LogError("The cheatsheet WebView failed: {Kind} ({Reason}).", e.ProcessFailedKind, e.Reason);
    }

    // ----------------------------------------------------------------- lifetime

    /// <summary>
    /// The close button hides the window rather than destroying it, so the page and its
    /// scroll position are still there the next time the user asks for the cheatsheet.
    /// </summary>
    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isShuttingDown)
        {
            return;
        }

        args.Cancel = true;
        CapturePlacement();
        AppWindow.Hide();
    }

    /// <summary>Closes the window for real, as the application exits.</summary>
    public void Shutdown()
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;

        CapturePlacement();

        _theme.EffectiveThemeChanged -= OnEffectiveThemeChanged;
        AppWindow.Changed -= OnAppWindowChanged;

        if (_webView.CoreWebView2 is { } core)
        {
            core.WebMessageReceived -= OnWebMessageReceived;
            core.NavigationStarting -= OnNavigationStarting;
            core.NewWindowRequested -= OnNewWindowRequested;
            core.ProcessFailed -= OnProcessFailed;
        }

        _webView.Close();
        Close();
    }

    // ---------------------------------------------------------------- placement

    /// <summary>
    /// Restores the remembered size and position, or places the window beside the main one
    /// the first time it is opened.
    /// </summary>
    private void RestorePlacement(RectInt32 nearby)
    {
        if (_hasRestoredPlacement)
        {
            return;
        }

        _hasRestoredPlacement = true;

        WindowPlacement placement = _settings.Current.CheatsheetPlacement;

        int width = Math.Max(placement.Width, MinimumWidth);
        int height = Math.Max(placement.Height, MinimumHeight);

        if (placement.HasPosition && FitsOnADisplay(placement.X, placement.Y, width, height))
        {
            AppWindow.MoveAndResize(new RectInt32(placement.X, placement.Y, width, height));
            return;
        }

        // No remembered position: sit just inside the main window's right edge, which reads
        // as belonging to it without covering the document.
        int x = nearby.X + Math.Max(0, nearby.Width - width - 48);
        int y = nearby.Y + 64;

        AppWindow.MoveAndResize(
            FitsOnADisplay(x, y, width, height)
                ? new RectInt32(x, y, width, height)
                : new RectInt32(nearby.X + 48, nearby.Y + 48, width, height));
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_isShuttingDown)
        {
            return;
        }

        if (args.DidVisibilityChange)
        {
            ShownOrHidden?.Invoke(this, sender.IsVisible);

            if (sender.IsVisible)
            {
                EnsureOwned();

                // The window is placed before it is shown, and CapturePlacement ignores an
                // invisible window because its bounds are not yet meaningful. Without this,
                // a cheatsheet the user never moved would have no remembered geometry.
                CapturePlacement();
            }
        }

        if (!args.DidPositionChange && !args.DidSizeChange)
        {
            return;
        }

        CapturePlacement();
    }

    private void CapturePlacement()
    {
        AppWindow window = AppWindow;

        // A hidden or minimized window reports bounds that must not be persisted; the main
        // window learned the same lesson.
        if (!window.IsVisible
            || window.Size.Width < MinimumWidth
            || window.Size.Height < MinimumHeight)
        {
            return;
        }

        _settings.Update(s => s with
        {
            CheatsheetWindow = new WindowPlacement
            {
                X = window.Position.X,
                Y = window.Position.Y,
                Width = window.Size.Width,
                Height = window.Size.Height,
            },
        });
    }

    /// <summary>Guards against restoring onto a monitor that is no longer attached.</summary>
    private static bool FitsOnADisplay(int x, int y, int width, int height)
    {
        var area = DisplayArea.GetFromRect(new RectInt32(x, y, width, height), DisplayAreaFallback.Nearest);
        RectInt32 bounds = area.WorkArea;

        return x < bounds.X + bounds.Width
            && y < bounds.Y + bounds.Height
            && x + width > bounds.X
            && y + height > bounds.Y;
    }

    // ------------------------------------------------------------------- interop

    /// <summary>Index of the owner-window slot in the extended window data.</summary>
    private const int GwlpHwndParent = -8;

    /// <summary>
    /// DllImport rather than the source-generated LibraryImport: the generator emits unsafe
    /// marshalling code, which would mean enabling AllowUnsafeBlocks across the project for
    /// one call that passes nothing but handles.
    /// </summary>
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    // -------------------------------------------------------------- json helpers

    private static string ReadString(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;

    private static int ReadInt(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int result)
                ? result
                : 0;
}
