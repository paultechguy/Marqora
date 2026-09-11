// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.App.Services;
using PaulTechGuy.MQ.Domain;
using Windows.Graphics;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// One mermaid diagram, in a window of its own.
///
/// Built in code rather than XAML for the same reason as the cheatsheet: there is nothing to
/// lay out, only a WebView showing one page.
///
/// Unlike the cheatsheet this is an ordinary top-level window - resizable, maximizable, in
/// the taskbar and in Alt+Tab, and deliberately not owned by the main window. An owned window
/// can never fall behind its owner, and the point of these is that the user arranges them
/// around the editor however they like, including behind it.
///
/// It is also genuinely closed rather than hidden. The cheatsheet is recalled often and its
/// scroll position is worth a second of startup; a diagram window is opened for as long as it
/// is wanted and then dismissed, and holding a WebView per diagram would be a poor trade
/// against reopening in under a second.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "A Window's lifetime belongs to the framework; the WebView is released "
        + "when the window closes.")]
public sealed partial class DiagramWindow : Window
{
    /// <summary>Small enough to tuck beside the editor, large enough for a diagram to read.</summary>
    private const int MinimumWidth = 320;
    private const int MinimumHeight = 240;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IWebAssetProvider _assets;
    private readonly IThemeService _theme;

    /// <summary>Only for the page setup a print starts from; a diagram window stores nothing.</summary>
    private readonly ISettingsService _settings;

    private readonly ILogger<DiagramWindow> _logger;

    private readonly WebView2 _webView = new();

    /// <summary>The right-click menu, built the first time it is needed.</summary>
    private MenuFlyout? _menu;

    /// <summary>Messages raised before the page announced itself, replayed once it does.</summary>
    private readonly List<string> _pending = [];

    private readonly string _documentName;

    /// <summary>Full path of the document, shown on the printed page header.</summary>
    private readonly string _documentPath;

    private string _title;

    /// <summary>Where the diagram sits in its document, for the name an export suggests.</summary>
    private int _index;

    private string _svg;
    private bool _isReady;
    private bool _isRemoved;
    private bool _isInvalid;

    public DiagramWindow(
        IWebAssetProvider assets,
        IThemeService theme,
        ISettingsService settings,
        Guid id,
        Guid documentId,
        string hash,
        int index,
        string title,
        string documentName,
        string documentPath,
        string svg,
        ILogger<DiagramWindow> logger)
    {
        _assets = assets;
        _theme = theme;
        _settings = settings;
        _logger = logger;
        _svg = svg;
        _index = index;
        _title = title;
        _documentName = documentName;
        _documentPath = documentPath;

        Id = id;
        DocumentId = documentId;
        Hash = hash;
        Title = title;

        // A themed background behind the WebView, so the window does not flash white on a
        // dark desktop during the moment before the page paints.
        var root = new Grid
        {
            Background = (Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"],
            RequestedTheme = theme.Effective == AppTheme.Dark ? ElementTheme.Dark : ElementTheme.Light,
        };

        root.Children.Add(_webView);
        Content = root;

        ConfigurePresenter();

        AppWindow.Closing += OnClosing;
        _theme.EffectiveThemeChanged += OnEffectiveThemeChanged;
    }

    /// <summary>This window, for as long as it is open. Never changes.</summary>
    public Guid Id { get; }

    /// <summary>The document the diagram came from.</summary>
    public Guid DocumentId { get; }

    /// <summary>The definition the preview is tracking on this window's behalf.</summary>
    public string Hash { get; private set; }

    /// <summary>Raised once the user has closed the window, so the service can forget it.</summary>
    public event EventHandler? Dismissed;

    /// <summary>
    /// Sizes the window and brings its page up.
    ///
    /// <paramref name="maximize"/> is applied here, before the WebView exists, and that
    /// ordering is the whole point of doing it here rather than after the window is shown.
    /// The page fits the first diagram it is given to whatever its surface measures; taking
    /// the window to its full size first means that measurement is already the right one,
    /// and no refit is needed. <see cref="MaximizeAndFit"/> is the other case - a window
    /// that is already open and has already fitted.
    /// </summary>
    public async Task InitializeAsync(RectInt32 placement, bool maximize)
    {
        AppWindow.MoveAndResize(placement);

        if (maximize)
        {
            MaximizeWindow();
        }

        if (_webView.CoreWebView2 is not null)
        {
            return;
        }

        if (!_assets.IsAvailable)
        {
            _logger.LogError(
                "Refusing to open a diagram window: missing web assets {Missing}.",
                string.Join(", ", _assets.MissingAssets));
            return;
        }

        await _webView.EnsureCoreWebView2Async();

        if (_webView.CoreWebView2 is not { } core)
        {
            _logger.LogError("The diagram WebView did not initialize.");
            return;
        }

        // Off, and replaced by a WinUI flyout the host shows. Chromium's menu offered
        // browser commands a diagram has no use for and was drawn by Edge, which meant it
        // followed Edge's dark mode rather than the app's theme. See ShowContextMenu below.
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsSwipeNavigationEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;

        // The page owns Ctrl with the wheel for zooming, so the browser's own zoom would
        // fight it for the same gesture and scale the toolbar along with the diagram.
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsZoomControlEnabled = false;

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

        _webView.Source = _assets.DiagramUri;
    }

    /// <summary>
    /// Replaces what the window is showing, as the diagram is edited.
    ///
    /// The page keeps the zoom the user chose across this: the diagram is swapped, not the
    /// view of it, so a window left at 300% stays at 300% while its diagram grows a branch.
    ///
    /// An update also means the diagram is back, so undoing a deletion clears the notice
    /// rather than leaving the window claiming to be stale while showing something current.
    /// </summary>
    public void Update(string hash, int index, string svg)
    {
        MarkPresent();

        Hash = hash;
        Retitle(index);

        if (string.Equals(_svg, svg, StringComparison.Ordinal))
        {
            return;
        }

        _svg = svg;
        Send("setDiagram", new { svg });
    }

    /// <summary>
    /// Keeps the number in the title on the diagram as it moves through the document. Only
    /// when it has actually moved: rewriting the title on every keystroke would set the
    /// taskbar flickering for no reason.
    /// </summary>
    private void Retitle(int index)
    {
        _index = index;

        string next = string.IsNullOrWhiteSpace(_documentName)
            ? $"Diagram {index + 1}"
            : $"{_documentName} - Diagram {index + 1}";

        if (string.Equals(_title, next, StringComparison.Ordinal))
        {
            return;
        }

        _title = next;
        Title = _isRemoved ? $"{_title} (source removed)" : _title;
    }

    /// <summary>
    /// Says that the diagram is no longer in its document, because the fenced block was
    /// deleted or the document was closed.
    ///
    /// Said twice over: in the title, which is what the taskbar and Alt+Tab show while the
    /// window is minimised, and as a chip in the toolbar, which is where the eye already is
    /// when the window is in front. The diagram itself is left alone - it is the last good
    /// render and still the thing the window is for.
    /// </summary>
    public void MarkRemoved()
    {
        if (_isRemoved)
        {
            return;
        }

        _isRemoved = true;
        Title = $"{_title} (source removed)";
        Send("setRemoved", new { removed = true });
    }

    private void MarkPresent()
    {
        if (_isRemoved)
        {
            _isRemoved = false;
            Title = _title;
            Send("setRemoved", new { removed = false });
        }

        // A render arriving at all means the definition parses again.
        if (_isInvalid)
        {
            _isInvalid = false;
            Send("setInvalid", new { message = (string?)null });
        }
    }

    /// <summary>
    /// Says that the definition no longer parses, and passes mermaid's own complaint along.
    ///
    /// Only a chip, and the diagram is left as it was: this is a state you type through, and
    /// the last good render is the thing being compared against while the mistake is found.
    /// The title is deliberately not touched either - a removal outlives the moment, a typo
    /// does not, and rewriting the title on every keystroke would set the taskbar flickering.
    /// </summary>
    public void MarkInvalid(string message)
    {
        if (_isInvalid)
        {
            return;
        }

        _isInvalid = true;
        Send("setInvalid", new { message });
    }

    /// <summary>
    /// Takes an already-open window to full size and refits its diagram to it.
    ///
    /// The refit has to be asked of the page rather than done from here, and it cannot be
    /// the plain "fit to window" the menu sends. Maximizing resizes the WebView on this
    /// thread, but the page's own layout has not caught up by the time a message posted now
    /// arrives, so a fit measured then would fit to the size the window has just stopped
    /// being. The page's "refit" fits immediately and again on the resize when it lands,
    /// which covers both a window that grew and one that was already this size.
    /// </summary>
    public void MaximizeAndFit()
    {
        MaximizeWindow();
        Send("command", new { name = "refit" });
    }

    /// <summary>
    /// Full size, unless it is there already.
    ///
    /// Maximize un-minimizes on its way, so a window down in the taskbar needs no restore
    /// first - which is also why <see cref="Raise"/> can run after this and find nothing to
    /// do about the minimized state.
    /// </summary>
    private void MaximizeWindow()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter
            && presenter.State != OverlappedPresenterState.Maximized)
        {
            presenter.Maximize();
        }
    }

    /// <summary>Brings the window forward, restoring it first if it was minimized.</summary>
    public void Raise()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter
            && presenter.State == OverlappedPresenterState.Minimized)
        {
            presenter.Restore();
        }

        AppWindow.Show();
        Activate();
    }

    /// <summary>Closes the window without raising <see cref="Dismissed"/>, as the app exits.</summary>
    public void Shutdown()
    {
        AppWindow.Closing -= OnClosing;
        _theme.EffectiveThemeChanged -= OnEffectiveThemeChanged;

        ReleaseWebView();
        Close();
    }

    private void ConfigurePresenter()
    {
        // An ordinary document-like window: everything the cheatsheet turns off stays on.
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
            presenter.PreferredMinimumWidth = MinimumWidth;
            presenter.PreferredMinimumHeight = MinimumHeight;
        }

        if (AppImages.HasIcon)
        {
            try
            {
                AppWindow.SetIcon(AppImages.IconPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not apply the diagram window icon.");
            }
        }
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        AppWindow.Closing -= OnClosing;
        _theme.EffectiveThemeChanged -= OnEffectiveThemeChanged;

        ReleaseWebView();

        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    private void ReleaseWebView()
    {
        if (_webView.CoreWebView2 is { } core)
        {
            core.WebMessageReceived -= OnWebMessageReceived;
            core.NavigationStarting -= OnNavigationStarting;
            core.NewWindowRequested -= OnNewWindowRequested;
        }

        _webView.Close();
    }

    private void OnEffectiveThemeChanged(object? sender, AppTheme theme)
    {
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = theme == AppTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
        }

        Send("setTheme", new { theme = theme.ToString() });
    }

    /// <summary>
    /// Two messages arrive from the page: it announces itself when it is ready, and it
    /// reports a right-click so this window can put a menu up.
    /// </summary>
    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        string body = args.TryGetWebMessageAsString();

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;

            string type = root.TryGetProperty("type", out JsonElement typeElement)
                ? typeElement.GetString() ?? string.Empty
                : string.Empty;

            JsonElement payload = root.TryGetProperty("payload", out JsonElement p) ? p : default;

            switch (type)
            {
                case "ready":
                    OnPageReady();
                    break;

                case "contextMenu":
                    ShowContextMenu(ReadDouble(payload, "x"), ReadDouble(payload, "y"));
                    break;

                case "diagramPng":
                    _ = CopyPngAsync(ReadString(payload, "data"));
                    break;

                case "diagramPngError":
                    _logger.LogWarning(
                        "The diagram page could not rasterize the diagram: {Message}",
                        ReadString(payload, "message") ?? "(no message)");
                    break;

                default:
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Malformed message from the diagram page.");
        }

        static double ReadDouble(JsonElement payload, string name) =>
            payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty(name, out JsonElement value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetDouble(out double result)
                    ? result
                    : 0;

        static string? ReadString(JsonElement payload, string name) =>
            payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty(name, out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
    }

    /// <summary>The page's rasterization of the diagram, base64 over the message bridge.</summary>
    private async Task CopyPngAsync(string? base64)
    {
        if (string.IsNullOrEmpty(base64))
        {
            _logger.LogWarning("The diagram page's PNG message carried no data.");
            return;
        }

        try
        {
            await ClipboardImage.SetAsync(Convert.FromBase64String(base64), _logger);
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "Malformed PNG data from the diagram page.");
        }
    }

    /// <summary>The diagram was queued from the constructor, so it lands on this first flush.</summary>
    private void OnPageReady()
    {
        _isReady = true;

        Send("setTheme", new { theme = _theme.Effective.ToString() });
        Send("setSource", new { path = _documentPath });
        Send("setDiagram", new { svg = _svg });

        foreach (string queued in _pending)
        {
            _webView.CoreWebView2?.PostWebMessageAsString(queued);
        }

        _pending.Clear();
    }

    /// <summary>
    /// The window's own commands, as a menu. A WinUI flyout rather than the browser's menu,
    /// so it is the same menu as everywhere else in Marqora and takes its font, spacing and
    /// colors from Themes/Menus.xaml.
    ///
    /// Built once and kept: a context menu is opened often and nothing in it changes.
    /// </summary>
    private void ShowContextMenu(double x, double y)
    {
        _menu ??= BuildMenu();

        _menu.ShowAt(_webView, new FlyoutShowOptions
        {
            Position = new Windows.Foundation.Point(x, y),
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
            ShowMode = FlyoutShowMode.Standard,
        });
    }

    private MenuFlyout BuildMenu()
    {
        var menu = new MenuFlyout();

        menu.Items.Add(Command("Zoom In", "zoomIn", "Ctrl++"));
        menu.Items.Add(Command("Zoom Out", "zoomOut", "Ctrl+-"));
        menu.Items.Add(Command("Actual Size", "zoomReset", "Ctrl+0"));
        menu.Items.Add(Command("Fit to Window", "zoomFit", null));
        menu.Items.Add(Command("Center", "center", null));
        menu.Items.Add(new MenuFlyoutSeparator());

        // Rasterized in the page rather than here: the SVG only exists as markup on this
        // side, and the page already has it laid out at its natural size. The page answers
        // with a "diagramPng" message, handled in OnWebMessageReceived.
        var copyPng = new MenuFlyoutItem { Text = "Copy as PNG" };
        copyPng.Click += (_, _) => Send("command", new { name = "copyPng" });
        menu.Items.Add(copyPng);

        // The SVG this window is showing, as markup. It is what the preview rendered, so a
        // paste lands the diagram exactly as it appears here rather than as mermaid source
        // somebody else would have to render.
        var copySvg = new MenuFlyoutItem { Text = "Copy as SVG" };
        copySvg.Click += (_, _) => ClipboardText.Set(_svg, _logger);
        menu.Items.Add(copySvg);

        menu.Items.Add(new MenuFlyoutSeparator());

        var pdf = new MenuFlyoutItem { Text = "Export to PDF..." };
        pdf.Click += (_, _) => ExportPdf();
        menu.Items.Add(pdf);

        var html = new MenuFlyoutItem { Text = "Export to HTML..." };
        html.Click += (_, _) => ExportHtml();
        menu.Items.Add(html);

        var print = new MenuFlyoutItem { Text = "Print...", KeyboardAcceleratorTextOverride = "Ctrl+P" };
        print.Click += (_, _) => Print();
        menu.Items.Add(print);

        return menu;

        MenuFlyoutItem Command(string text, string name, string? accelerator)
        {
            var item = new MenuFlyoutItem { Text = text };

            if (accelerator is not null)
            {
                item.KeyboardAcceleratorTextOverride = accelerator;
            }

            item.Click += (_, _) => Send("command", new { name });
            return item;
        }
    }

    /// <summary>
    /// Writes the diagram to a PDF: page setup, then where to put it, then the pages.
    ///
    /// The setup dialog is anchored to this window rather than the main one, for the same
    /// reason the print dialog is - it belongs over the diagram it is about. It opens on the
    /// setup held in preferences and saves the answer back, so a diagram and a document come
    /// off the same paper unless one of them is told otherwise.
    ///
    /// What reaches the page is the print stylesheet's view of this window: the zoom strip
    /// gone and the whole diagram at its natural size, rather than the corner a window left
    /// at 500% happens to be showing.
    /// </summary>
    private async void ExportPdf()
    {
        try
        {
            if (_webView.CoreWebView2 is not { } core)
            {
                return;
            }

            var dialog = new PdfExportDialog(_title, _settings.Current.PdfDefaults)
                .AnchorTo(Content as FrameworkElement);

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            PdfPageSetup setup = dialog.Setup;

            // Saved on accepting the dialog rather than on a successful write, the same rule
            // the document export follows: the answer is what the user chose, and a failed
            // write does not make it the wrong choice.
            _settings.Update(s => s with { PdfSetup = setup });

            string? path = Win32Dialogs.SaveFile(
                WinRT.Interop.WindowNative.GetWindowHandle(this),
                "Export as PDF document",
                SuggestedFileName(".pdf"),
                [".pdf"],
                "PDF document");

            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            await WebViewPrinting.ExportPdfAsync(core, path, setup);

            _logger.LogInformation("Exported a diagram to {Path}.", path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not export the diagram to PDF.");
        }
    }

    /// <summary>
    /// Writes the diagram to a standalone HTML file.
    ///
    /// Nothing to ask beyond where to put it: an SVG needs no paper size, and what lands in
    /// the file fetches nothing when it is opened.
    /// </summary>
    private async void ExportHtml()
    {
        try
        {
            string? path = Win32Dialogs.SaveFile(
                WinRT.Interop.WindowNative.GetWindowHandle(this),
                "Export as HTML document",
                SuggestedFileName(".html"),
                [".html", ".htm"],
                "HTML document");

            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            await File.WriteAllTextAsync(path, DiagramHtmlDocument.Build(_svg, _title));

            _logger.LogInformation("Exported a diagram to {Path}.", path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not export the diagram to HTML.");
        }
    }

    /// <summary>
    /// What an export offers to call the file: the window's own name, minus the markdown
    /// extension that would otherwise read as "Notes.md - Diagram 2.pdf".
    /// </summary>
    private string SuggestedFileName(string extension) =>
        (string.IsNullOrWhiteSpace(_documentName)
            ? $"Diagram {_index + 1}"
            : $"{Path.GetFileNameWithoutExtension(_documentName)} - Diagram {_index + 1}")
        + extension;

    /// <summary>
    /// Prints the diagram: the print dialog, then the pages.
    ///
    /// The same two steps the preview takes, through the same dialog - see
    /// <see cref="PrintDialog"/>. Neither dialog the WebView can raise will do.
    ///
    /// Anchored to this window's own content rather than the main window's, so it opens over
    /// the diagram and in the theme the diagram is wearing. The page setup is the one held in
    /// preferences, so a diagram prints on the paper everything else does.
    /// </summary>
    private async void Print()
    {
        try
        {
            if (_webView.CoreWebView2 is not { } core)
            {
                return;
            }

            PrintJob? job = await PrintDialog.ShowAsync(
                Content as FrameworkElement,
                _settings.Current.PdfDefaults);

            if (job is null)
            {
                return;
            }

            await WebViewPrinting.PrintAsync(core, job);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not print the diagram.");
        }
    }

    /// <summary>The page is local and static; nothing should ever navigate it elsewhere.</summary>
    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (!args.Uri.StartsWith($"https://{_assets.VirtualHostName}/", StringComparison.OrdinalIgnoreCase))
        {
            args.Cancel = true;
        }
    }

    private void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args) =>
        args.Handled = true;

    private void Send(string type, object payload)
    {
        string message = JsonSerializer.Serialize(new { type, payload }, JsonOptions);

        if (!_isReady || _webView.CoreWebView2 is null)
        {
            _pending.Add(message);
            return;
        }

        _webView.CoreWebView2.PostWebMessageAsString(message);
    }
}
