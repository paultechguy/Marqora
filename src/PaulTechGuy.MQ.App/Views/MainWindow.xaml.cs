// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PaulTechGuy.MQ.Abstractions;
using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.App.Services;
using PaulTechGuy.MQ.App.ViewModels;
using PaulTechGuy.MQ.Domain;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.System;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// The application window. Supplies native Windows 11 chrome, routes input to the view
/// model, and owns the WebView bridge for the lifetime of the window.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "A Window's lifetime is owned by the framework and it is never disposed by "
        + "a caller. The preview host is released in the AppWindow.Closing handler instead.")]
public sealed partial class MainWindow : Window
{
    private readonly IWebAssetProvider _assets;
    private readonly WindowContext _context;
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly IAppPaths _paths;
    private readonly ICheatsheetService _cheatsheet;
    private readonly IDiagramWindowService _diagramWindows;
    private readonly IFindAllWindowService _findAll;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MainWindow> _logger;

    /// <summary>Files handed over before the window was ready to open them.</summary>
    private readonly List<string> _pendingFiles = [];

    private readonly AltCharBeepFilter _beepFilter;

    private WebViewPreviewHost? _previewHost;

    /// <summary>
    /// The control the preview is currently drawing in, so the theme has something to
    /// colour. The host replaces it after a crash, which is why this is not the x:Name of
    /// something in the XAML - see CreatePreviewWebView.
    /// </summary>
    private WebView2? _previewWebView;

    private bool _isLoaded;
    private bool _isClosing;

    /// <summary>Guards against reacting to a selection change this class caused.</summary>
    private bool _isApplyingSelection;

    public MainWindow(
        MainViewModel viewModel,
        IWebAssetProvider assets,
        WindowContext context,
        ISettingsService settings,
        IDialogService dialogs,
        IAppPaths paths,
        ICheatsheetService cheatsheet,
        IDiagramWindowService diagramWindows,
        IFindAllWindowService findAll,
        ILoggerFactory loggerFactory,
        ILogger<MainWindow> logger)
    {
        ViewModel = viewModel;
        _assets = assets;
        _context = context;
        _settings = settings;
        _dialogs = dialogs;
        _paths = paths;
        _cheatsheet = cheatsheet;
        _diagramWindows = diagramWindows;
        _findAll = findAll;
        _loggerFactory = loggerFactory;
        _logger = logger;

        InitializeComponent();

        // Services that need an owner window resolve it through this holder.
        _context.Window = this;

        ConfigureChrome();
        RegisterAccelerators();

        // Alt accelerators fire on the key-down, but the message loop still translates the
        // press into a WM_SYSCHAR that DefWindowProc answers with a beep. The filter blanks
        // that message for this thread; without it every Alt+1/2/3 view switch makes noise.
        _beepFilter = AltCharBeepFilter.Install();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.RecentFiles.CollectionChanged += (_, _) => RebuildRecentMenu();

        // The toolbar's dropdowns are MenuFlyouts, so they can be filled as they open.
        DiagramMenu.Opening += (_, _) => FillSnippetMenu(DiagramMenu.Items, SnippetGroup.Diagram);

        // The bar's Insert dropdown is the one surface that carries the three block commands
        // above the catalogue. Its overflow twin lists them flat instead, and the Format menu
        // keeps them as top-level items, so neither asks for them here.
        SnippetMenu.Opening += (_, _) =>
            FillSnippetMenu(SnippetMenu.Items, SnippetGroup.General, withBlocks: true);
        TabListMenu.Opening += (_, _) => RebuildTabListMenu();

        // Picking a document from the list ends with the keyboard in it. On Closed rather
        // than on the item's Click: a MenuFlyout holds focus while it is open and hands it
        // back as it closes, which would undo a restore done any earlier. Dismissing the
        // menu without picking anything lands here too, and the document is still the right
        // place for the keyboard to be - it is where the click came from.
        TabListMenu.Closed += (_, _) => ViewModel.RestoreDocumentFocus();

        // The overflow copies of those two are submenus, which have no Opening of their own,
        // so they are filled when the overflow menu opens. Each surface gets its own items:
        // a MenuFlyoutItem cannot belong to two parents at once.
        OverflowMenu.Opening += (_, _) =>
        {
            FillSnippetMenu(OverflowDiagram.Items, SnippetGroup.Diagram);
            FillSnippetMenu(OverflowSnippet.Items, SnippetGroup.General);
        };

        // The Format menu's are MenuFlyoutSubItems, which have no Opening event, so they
        // are refreshed when the window is activated instead. That covers the way a
        // snippet actually gets added: switch to Explorer, drop a file in, come back.
        // Whether the clipboard holds text is refreshed on the same gesture, and for the
        // same reason: copying happens in the app you switched away to.
        Activated += OnWindowActivatedRefresh;

        // Copying inside this window never leaves it, so activation alone would miss it.
        // A static event with no matching unsubscribe, which is safe here and only here:
        // there is one MainWindow and it lives as long as the process does.
        Clipboard.ContentChanged += (_, _) => ViewModel.RefreshClipboardState();
        ViewModel.ExitRequested += (_, _) => Close();
        ViewModel.AboutRequested += (_, _) => _ = ShowAboutAsync();
        ViewModel.SupportRequested += (_, _) => _ = ShowSupportAsync();
        ViewModel.MenuRequested += (_, name) => OpenMenuByName(name);

        RootGrid.Loaded += OnLoaded;
        RootGrid.SizeChanged += OnRootSizeChanged;
        RootGrid.ActualThemeChanged += (_, _) =>
        {
            ApplyWebViewBackground();
            ApplyCaptionButtonTheme();
        };
        AppWindow.Changed += OnAppWindowChanged;
        AppWindow.Closing += OnAppWindowClosing;

        // Alt on its own focuses the menu bar. The shell watches for it too, for when the
        // editor holds the keyboard; this is the same gesture for the rest of the window.
        // Handled events count, because a bare modifier is marked handled on its way past.
        RootGrid.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnRootKeyDown), handledEventsToo: true);
        RootGrid.AddHandler(UIElement.KeyUpEvent, new KeyEventHandler(OnRootKeyUp), handledEventsToo: true);

        // Middle-click closes a tab. The handler asks for handled events too, because
        // TabViewItem marks PointerPressed handled while it decides selection.
        DocumentTabs.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(OnTabStripPointerPressed),
            handledEventsToo: true);

        // Clicking a tab hands the keyboard back to the document afterwards. Released
        // rather than pressed: a press is also the start of a drag, and taking focus in
        // the middle of one would fight the reorder. Handled events too, for the same
        // reason as above.
        DocumentTabs.AddHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler(OnTabStripPointerReleased),
            handledEventsToo: true);

        // Right-clicking a tab puts up its own menu - see MainWindow.TabContextMenu.cs.
        // ContextRequested rather than RightTapped, because the Menu key and a press-and-hold
        // raise it too. Handled events again: a TabViewItem is a ListViewItem, and marks
        // these on its way past.
        DocumentTabs.AddHandler(
            UIElement.ContextRequestedEvent,
            new Windows.Foundation.TypedEventHandler<UIElement, ContextRequestedEventArgs>(
                OnTabContextRequested),
            handledEventsToo: true);

        // Tab bounds move whenever a tab is opened, closed, renamed or reordered, and the
        // regions have to follow them. LayoutUpdated is the only signal that covers all of
        // those and fires after the containers have actually been measured.
        DocumentTabs.LayoutUpdated += (_, _) =>
        {
            // A closing window still lays out - emptying the tab strip is itself a layout
            // pass - but by then AppWindow may already be gone, and reading its title bar
            // insets threw. There is nothing worth positioning on a window on its way out.
            if (_isClosing)
            {
                return;
            }

            // Order matters: the titles decide how wide each tab wants to be, that decides
            // which tabs are shown, and only then is it known where the shown ones sit.
            EnsureTabStripDoesNotScroll();
            EnsureTabStripRuleAligned();
            UpdateTabTitles();
            UpdateVisibleTabs();
            PositionTabListButton();
            UpdateTabPassthroughRegions();
        };

        RebuildRecentMenu();
    }

    public MainViewModel ViewModel { get; }

    // x:Bind function bindings, used instead of converter resources. They must be instance
    // members: x:Bind resolves function bindings against the page instance, and a static
    // method is rejected at compile time.
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Required by x:Bind.")]
    public Visibility VisibleWhen(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Required by x:Bind.")]
    public Visibility CollapsedWhen(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// A tooltip, or none at all.
    ///
    /// ToolTipService shows an empty box for an empty string, which is a tooltip announcing
    /// that the element has nothing to say. Null is how you ask for no tooltip.
    /// </summary>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Required by x:Bind.")]
    public object? ToolTipOrNone(string value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>Opens files supplied on the command line, deferring until the window is ready.</summary>
    public void OpenAtStartup(IReadOnlyList<string> paths)
    {
        if (_isLoaded)
        {
            _ = ViewModel.OpenActivatedAsync(paths);
        }
        else
        {
            _pendingFiles.AddRange(paths);
        }
    }

    /// <summary>
    /// Opens files handed over by a second launch that redirected here, and brings the window
    /// forward: the user double-clicked a document and expects to be looking at it.
    ///
    /// Called from whichever thread the activation arrived on, which is not the UI thread.
    /// </summary>
    public void OpenFromActivation(IReadOnlyList<string> paths)
    {
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            BringToFront();
            OpenAtStartup(paths);
        }))
        {
            _logger.LogWarning("Could not hand {Count} redirected file(s) to the UI thread.", paths.Count);
        }
    }

    /// <summary>
    /// Restores the window if it was minimised and puts it in front.
    ///
    /// <see cref="Window.Activate"/> on its own does neither reliably when the request came
    /// from another process; the redirecting instance passes its foreground rights over so
    /// that the call below is allowed to succeed rather than merely flashing the taskbar.
    /// </summary>
    private void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();

        _ = SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    // ------------------------------------------------------------------- chrome

    private void ConfigureChrome()
    {
        Title = "Marqora";

        ApplyBranding();
        ApplyWebViewBackground();

        // Mica behind the chrome makes the window read as part of the desktop. The WebView
        // is opaque, so this shows through the tab strip, menu bar and status bar.
        SystemBackdrop = new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt };

        ExtendsContentIntoTitleBar = true;

        // The empty parts of the tab strip drag the window; the tabs themselves do not. This
        // also makes the strip non-client, which is why the tabs are carved back out of it -
        // see UpdateTabPassthroughRegions.
        SetTitleBar(DocumentTabs);

        // Windows still draws the three caption buttons over the extended content, and it
        // colours them from the app theme rather than the root's - so they are painted here.
        ApplyCaptionButtonTheme();

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 640;
            presenter.PreferredMinimumHeight = 420;
        }
    }

    /// <summary>
    /// What the theme has actually resolved to, read off the tree rather than out of the
    /// setting so System is already answered.
    ///
    /// The same question ApplyWebViewBackground asks, and asked in the same place, because
    /// the two answers have to agree: the control's background and the page drawn over it
    /// are the same colour by intent.
    /// </summary>
    private AppTheme CurrentEffectiveTheme =>
        RootGrid.ActualTheme == ElementTheme.Dark ? AppTheme.Dark : AppTheme.Light;

    /// <summary>
    /// Gives the WebView an opaque background matching the page behind it.
    ///
    /// It used to be Transparent, which bought nothing: the page paints its own background
    /// over every pixel it occupies. What transparency did cost was a slower composition
    /// path - an alpha-blended surface takes longer to settle after a resize, and switching
    /// view mode resizes the panes drastically. Matching the page's own colour also means
    /// the moment before the first paint is the right colour rather than a flash of white.
    /// </summary>
    private void ApplyWebViewBackground()
    {
        // ConfigureChrome runs before there is a control to colour. The one built after it
        // takes the colour on its way in, so there is nothing to do yet.
        if (_previewWebView is null)
        {
            return;
        }

        bool dark = RootGrid.ActualTheme == ElementTheme.Dark;

        // The same values as --mq-bg in app.css.
        _previewWebView.DefaultBackgroundColor = dark
            ? Windows.UI.Color.FromArgb(0xFF, 0x1F, 0x1F, 0x1F)
            : Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
    }

    /// <summary>
    /// Builds a WebView for the preview surface, ready for the host to attach to.
    ///
    /// Called once at startup and again for every replacement the host makes after a crash,
    /// which is the reason the theme colour is applied here rather than once and for all:
    /// a control created at three in the morning has to arrive the right colour too.
    /// </summary>
    private WebView2 CreatePreviewWebView()
    {
        _previewWebView = new WebView2();
        ApplyWebViewBackground();

        return _previewWebView;
    }

    /// <summary>
    /// Paints the minimise, maximise and close glyphs to match the theme the content is
    /// actually using.
    ///
    /// Windows draws those three buttons itself even when the content is extended into the
    /// title bar, and it picks their colour from the application's theme - not from the
    /// element tree. The theme here is applied as <see cref="FrameworkElement.RequestedTheme"/>
    /// on the root, so a dark window under a light Windows leaves the caption drawing
    /// near-black glyphs over dark chrome: invisible until hovered, when the hover plate
    /// finally gives them something to contrast against. Setting the colours explicitly, and
    /// again whenever the resolved theme changes, is the only way to keep the two in step.
    ///
    /// The backgrounds stay transparent so the Mica and the tab strip continue through the
    /// caption; only the hover and pressed plates paint, as a translucent wash in the
    /// direction that reads on the current theme.
    /// </summary>
    private void ApplyCaptionButtonTheme()
    {
        AppWindowTitleBar bar = AppWindow.TitleBar;

        bool dark = RootGrid.ActualTheme == ElementTheme.Dark;

        Windows.UI.Color glyph = dark ? Rgb(0xE6, 0xE6, 0xE6) : Rgb(0x1B, 0x1B, 0x1B);
        Windows.UI.Color muted = Rgb(0x8A, 0x8A, 0x8A);
        Windows.UI.Color hover = dark
            ? Windows.UI.Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)
            : Windows.UI.Color.FromArgb(0x14, 0x00, 0x00, 0x00);
        Windows.UI.Color pressed = dark
            ? Windows.UI.Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)
            : Windows.UI.Color.FromArgb(0x28, 0x00, 0x00, 0x00);

        bar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        bar.ButtonForegroundColor = glyph;
        bar.ButtonInactiveForegroundColor = muted;
        bar.ButtonHoverBackgroundColor = hover;
        bar.ButtonHoverForegroundColor = glyph;
        bar.ButtonPressedBackgroundColor = pressed;
        bar.ButtonPressedForegroundColor = glyph;
    }

    private static Windows.UI.Color Rgb(byte r, byte g, byte b) =>
        Windows.UI.Color.FromArgb(0xFF, r, g, b);

    /// <summary>
    /// Puts the logo on the window, the taskbar and the two places it appears in the content.
    ///
    /// The window icon is set from the .ico rather than the SVG because Windows wants a real
    /// icon resource with the small sizes baked in: it draws the caption at 16px and the
    /// taskbar at 32, and picking a purpose-made frame beats scaling one down. The in-content
    /// artwork uses the SVG, which is rendered at exactly the size it is drawn.
    /// </summary>
    private void ApplyBranding()
    {
        if (AppImages.HasIcon)
        {
            try
            {
                AppWindow.SetIcon(AppImages.IconPath);
            }
            catch (Exception ex)
            {
                // A missing or malformed icon is a cosmetic problem, never a fatal one.
                _logger.LogWarning(ex, "Could not apply the window icon.");
            }
        }

        HeaderLogo.Source = AppImages.Logo(36);
        DropSurfaceLogo.Source = AppImages.Logo(192);
    }

    /// <summary>
    /// The window's half of the keyboard, for when the chrome holds it: the tab strip, the
    /// toolbar, a dialog's owner, an open menu.
    ///
    /// It is only half. A XAML accelerator never fires while the WebView owns the keyboard,
    /// and the WebView owns it whenever either pane has focus - which is nearly always. The
    /// other half is HOST_SHORTCUTS in webshell/app.js, which answers the same keys from
    /// inside the page and posts them back through OnHostCommand. The two lists say the same
    /// thing and change together; KeyboardShortcuts.cs is what Help shows for them.
    /// </summary>
    private void RegisterAccelerators()
    {
        // The accelerators live on the root so they work wherever focus is in the chrome.
        // WinUI would otherwise advertise them with a floating key-tip anchored to the root
        // element, which appears as a stray label over the content.
        RootGrid.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;

        const VirtualKeyModifiers ctrl = VirtualKeyModifiers.Control;
        const VirtualKeyModifiers ctrlShift = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift;
        const VirtualKeyModifiers alt = VirtualKeyModifiers.Menu;
        const VirtualKeyModifiers ctrlAlt = VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu;

        // Files and tabs.
        Add(VirtualKey.N, ctrl, () => ViewModel.NewTabCommand.Execute(null));
        Add(VirtualKey.T, ctrl, () => ViewModel.NewTabCommand.Execute(null));
        Add(VirtualKey.O, ctrl, () => ViewModel.OpenCommand.Execute(null));
        Add(VirtualKey.O, ctrlShift, () => ViewModel.OpenFolderCommand.Execute(null));
        Add(VirtualKey.S, ctrl, () => ViewModel.SaveCommand.Execute(null));

        // Ctrl+Shift+S for Save All, as Visual Studio has it, which leaves Save As on the key
        // Save All used to hold. The two swapped rather than one of them losing a shortcut.
        Add(VirtualKey.S, ctrlShift, () => ViewModel.SaveAllCommand.Execute(null));
        Add(VirtualKey.S, ctrlAlt, () => ViewModel.SaveAsCommand.Execute(null));

        Add(VirtualKey.W, ctrl, () => ViewModel.CloseTabCommand.Execute(null));
        Add(VirtualKey.W, ctrlShift, () => ViewModel.CloseAllTabsCommand.Execute(null));

        // Ctrl+, for preferences, as Visual Studio Code and most of the editors people also
        // have open have it. VirtualKey has no name for the comma, so the code is given
        // directly; 188 is VK_OEM_COMMA, which is the comma on a US layout.
        Add((VirtualKey)188, ctrl, () => ViewModel.ShowPreferencesCommand.Execute(null));

        Add(VirtualKey.Tab, ctrl, () => ViewModel.NextTabCommand.Execute(null));
        Add(VirtualKey.Tab, ctrlShift, () => ViewModel.PreviousTabCommand.Execute(null));

        // Ctrl+1 to Ctrl+8 select that tab; Ctrl+9 jumps to the last, as in every browser.
        for (int i = 1; i <= 8; i++)
        {
            int position = i;
            Add((VirtualKey)((int)VirtualKey.Number0 + i), ctrl, () => ViewModel.ActivateTabByNumber(position));
        }

        Add(VirtualKey.Number9, ctrl, ViewModel.ActivateLastTab);

        // View modes moved to Alt so the Ctrl digits could go to tabs.
        Add(VirtualKey.Number1, alt, () => ViewModel.SetViewModeCommand.Execute("Source"));
        Add(VirtualKey.Number2, alt, () => ViewModel.SetViewModeCommand.Execute("SideBySide"));
        Add(VirtualKey.Number3, alt, () => ViewModel.SetViewModeCommand.Execute("Preview"));
        Add(VirtualKey.Z, alt, () => ViewModel.ToggleWordWrapCommand.Execute(null));

        // Zoom. Both the numeric keypad and the main-row keys, which report as OEM values.
        // Ctrl+0 is free for the reset because tab selection stops at Ctrl+9.
        Add(VirtualKey.Add, ctrl, () => ViewModel.ZoomInCommand.Execute(null));
        Add((VirtualKey)187, ctrl, () => ViewModel.ZoomInCommand.Execute(null));
        Add(VirtualKey.Subtract, ctrl, () => ViewModel.ZoomOutCommand.Execute(null));
        Add((VirtualKey)189, ctrl, () => ViewModel.ZoomOutCommand.Execute(null));
        Add(VirtualKey.NumberPad0, ctrl, () => ViewModel.ZoomResetCommand.Execute(null));
        Add(VirtualKey.Number0, ctrl, () => ViewModel.ZoomResetCommand.Execute(null));

        Add(VirtualKey.Add, ctrlShift, () => ViewModel.ZoomBothInCommand.Execute(null));
        Add((VirtualKey)187, ctrlShift, () => ViewModel.ZoomBothInCommand.Execute(null));
        Add(VirtualKey.Subtract, ctrlShift, () => ViewModel.ZoomBothOutCommand.Execute(null));
        Add((VirtualKey)189, ctrlShift, () => ViewModel.ZoomBothOutCommand.Execute(null));
        Add(VirtualKey.Number0, ctrlShift, () => ViewModel.ZoomBothResetCommand.Execute(null));
        Add(VirtualKey.NumberPad0, ctrlShift, () => ViewModel.ZoomBothResetCommand.Execute(null));

        // Edit commands, for when focus is on the chrome rather than in the editor. With
        // focus in the editor these keys never reach XAML and Monaco handles them itself.
        Add(VirtualKey.F, ctrl, () => RunEdit("find"));
        Add(VirtualKey.F, ctrlShift, () => RunEdit("findAll"));
        Add(VirtualKey.H, ctrl, () => RunEdit("replace"));
        Add(VirtualKey.F3, VirtualKeyModifiers.None, () => RunEdit("findNext"));
        Add(VirtualKey.F3, VirtualKeyModifiers.Shift, () => RunEdit("findPrevious"));
        Add(VirtualKey.G, ctrl, () => RunEdit("gotoLine"));
        Add(VirtualKey.Z, ctrl, () => RunEdit("undo"));
        Add(VirtualKey.Y, ctrl, () => RunEdit("redo"));
        Add(VirtualKey.A, ctrl, () => RunEdit("selectAll"));

        // Format Document, matching the shortcut editors have settled on.
        Add(VirtualKey.F, VirtualKeyModifiers.Shift | alt, () => ViewModel.FormatDocumentCommand.Execute(null));

        // The Format menu, again for when the chrome holds focus. Monaco registers the
        // same keys for when the editor does, and only one of the two ever sees a press.
        // Heading levels 1 to 6 are menu-only: Ctrl+digit is taken by tab selection, and
        // Ctrl+Alt+digit is AltGr+digit on European layouts, where it types a character.
        Add(VirtualKey.B, ctrl, () => RunMarkdown("Bold"));
        Add(VirtualKey.I, ctrl, () => RunMarkdown("Italic"));
        Add(VirtualKey.K, ctrl, () => RunMarkdown("Link"));
        Add((VirtualKey)192, ctrl, () => RunMarkdown("InlineCode"));
        Add(VirtualKey.X, ctrlShift, () => RunMarkdown("Strikethrough"));
        Add(VirtualKey.K, ctrlShift, () => RunMarkdown("CodeBlock"));
        Add((VirtualKey)190, ctrlShift, () => RunMarkdown("Blockquote"));
        Add(VirtualKey.Number8, ctrlShift, () => RunMarkdown("BulletList"));
        Add(VirtualKey.Number7, ctrlShift, () => RunMarkdown("NumberedList"));
        Add((VirtualKey)221, ctrlShift, () => RunMarkdown("HeadingIncrease"));
        Add((VirtualKey)219, ctrlShift, () => RunMarkdown("HeadingDecrease"));

        // Print. Ctrl+P reaches XAML rather than the WebView because the browser's own
        // accelerators are off, so this is the only thing bound to it.
        Add(VirtualKey.P, ctrl, () => ViewModel.PrintCommand.Execute(null));

        // Rich-text copy, alongside the plain Ctrl+C the editor already handles.
        Add(VirtualKey.C, ctrlShift, () => ViewModel.CopyAsRichTextCommand.Execute(null));

        // The cheatsheet. F1 alone is left for Windows' own help conventions.
        Add(VirtualKey.F1, ctrl, () => ViewModel.ToggleCheatsheetCommand.Execute(null));

        // Opening the menus from the keyboard. Format takes O because File has F, the way
        // Windows menus have always split those two.
        Add(VirtualKey.F, alt, () => OpenMenu(FileMenu));
        Add(VirtualKey.E, alt, () => OpenMenu(EditMenu));
        Add(VirtualKey.O, alt, () => OpenMenu(FormatMenu));
        Add(VirtualKey.V, alt, () => OpenMenu(ViewMenu));
        Add(VirtualKey.T, alt, () => OpenMenu(ToolsMenu));
        Add(VirtualKey.H, alt, () => OpenMenu(HelpMenu));

        void RunEdit(string command) => ViewModel.EditActionCommand.Execute(command);

        void RunMarkdown(string command) => ViewModel.ApplyMarkdownCommand.Execute(command);

        void Add(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };

            accelerator.Invoked += (_, args) =>
            {
                args.Handled = true;
                action();
            };

            RootGrid.KeyboardAccelerators.Add(accelerator);
        }
    }

    // ------------------------------------------------------------------ lifetime

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.InitializeAsync();
            RestoreWindowPlacement();

            if (!_assets.IsAvailable)
            {
                await _dialogs.ShowMessageAsync(
                    "Preview assets are missing",
                    "The bundled editor files were not found. Run build/Get-WebAssets.ps1 from the "
                    + "repository root and start Marqora again.\n\nMissing: "
                    + string.Join(", ", _assets.MissingAssets));

                _logger.LogError("Startup aborted: web assets unavailable.");
                return;
            }

            _previewHost = new WebViewPreviewHost(
                PreviewSurface,
                CreatePreviewWebView,
                _assets,
                _loggerFactory.CreateLogger<WebViewPreviewHost>());

            _previewHost.Ready += OnPreviewReady;
            _previewHost.RecoveryFailed += OnPreviewRecoveryFailed;
            _previewHost.StatsChanged += OnStatsChanged;
            _previewHost.CaretStateChanged += OnCaretStateChanged;
            _previewHost.HistoryStateChanged += OnHistoryStateChanged;
            _previewHost.PaneFocused += OnPaneFocused;
            _previewHost.ContextMenuRequested += OnContextMenuRequested;
            _previewHost.DiagramActivated += OnDiagramActivated;
            _previewHost.DiagramUpdated += OnDiagramUpdated;
            _previewHost.DiagramRemoved += OnDiagramRemoved;
            _previewHost.DiagramInvalid += OnDiagramInvalid;

            // The preview reports diagram changes only for the ones with a window open, so
            // it has to be retold whenever that set changes.
            _diagramWindows.WatchedChanged += OnWatchedDiagramsChanged;

            ViewModel.AttachPreviewHost(_previewHost);

            // The theme goes in before the page is navigated to, not after it reports ready.
            // Ready is the far side of a Monaco load, and the restored session is flushed
            // out of the host's queue just in front of it, so a shell that had to be told
            // its colour drew the boot screen and every reopened document in the wrong one
            // and then repainted. The window has resolved the theme by now - see
            // ApplyWebViewBackground, which reads it from the same place for the same reason.
            await _previewHost.InitializeAsync(CurrentEffectiveTheme);

            _isLoaded = true;

            // A file named on the command line replaces the restored session as the active
            // tab, but does not discard it.
            await ViewModel.RestoreSessionAsync();

            string[] pending = [.. _pendingFiles];
            _pendingFiles.Clear();

            // Whatever opens last is the tab in front, so the order here is the whole of who
            // wins the focus. On the first run of a new release the welcome document goes
            // after the restored session but before a file named on the command line, which
            // is what the user actually asked to see. A launch that held Shift asked for the
            // welcome document just as plainly, and then it goes last.
            if (ViewModel.WelcomeWasRequested)
            {
                await ViewModel.OpenActivatedAsync(pending);
                await ViewModel.ShowWelcomeAsync(takeFocus: true);
            }
            else
            {
                await ViewModel.ShowWelcomeAsync(takeFocus: pending.Length == 0);
                await ViewModel.OpenActivatedAsync(pending);
            }

            // Last, so a preference for a blank tab or the welcome document cannot take the
            // front tab from a file the user double-clicked to get here.
            await ViewModel.ApplyStartupBehaviorAsync(pending.Length > 0);

            _startupDocumentsOpen = true;
            PlaceStartupFocus();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Window initialization failed.");
            await _dialogs.ShowMessageAsync("Marqora could not start cleanly", ex.Message);
        }
    }

    /// <summary>
    /// The shell finished its handshake. The view model puts the tabs and the settings back
    /// from its side; this puts back the one piece of state it does not hold.
    ///
    /// Fires again after a crash, when a restarted preview knows nothing about the diagram
    /// windows still open over it. On the first ready there are none and the list is empty,
    /// which is exactly what the shell should be told then anyway.
    /// </summary>
    private void OnPreviewReady(object? sender, EventArgs e) =>
        _previewHost?.WatchDiagrams(_diagramWindows.Watched);

    /// <summary>
    /// The preview crashed and would not start again, so the panes are going to stay blank.
    ///
    /// Saying so is the whole of the job. The window is otherwise working and the workspace
    /// still holds every document's text, so Ctrl+S still writes what the user had - which
    /// is worth telling them before they restart and find out the hard way whether it did.
    /// </summary>
    private async void OnPreviewRecoveryFailed(object? sender, EventArgs e)
    {
        try
        {
            await _dialogs.ShowMessageAsync(
                "The preview stopped and could not be restarted",
                "The editor and preview panes have crashed and will stay blank until Marqora "
                + "is restarted.\n\nYour open documents are intact: saving still writes what "
                + "you had, so save anything unsaved before restarting.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not report the preview failure.");
        }
    }

    private void OnCaretStateChanged(object? sender, EditContext? context) =>
        ViewModel.UpdateMarkState(context);

    private void OnHistoryStateChanged(object? sender, HistoryState history) =>
        ViewModel.UpdateHistoryState(history.CanUndo, history.CanRedo);

    private void OnStatsChanged(object? sender, EditorStats stats) =>
        ViewModel.UpdateStats(stats.Line, stats.Column, stats.Words, stats.Characters);

    private void OnPaneFocused(object? sender, EditorPane pane) => ViewModel.SetActivePane(pane);

    /// <summary>
    /// The user double-clicked a diagram in the preview. Opening the window is asynchronous
    /// and nothing here waits on it: the message arrived from the WebView, and blocking its
    /// handler would stall the preview while a WebView starts up.
    /// </summary>
    private void OnDiagramActivated(object? sender, DiagramActivatedEventArgs e) =>
        ViewModel.ShowDiagramCommand.Execute(e);

    /// <summary>A watched diagram was edited and re-rendered; its window redraws in place.</summary>
    private void OnDiagramUpdated(object? sender, DiagramUpdatedEventArgs e) =>
        _diagramWindows.Update(e.DiagramId, e.Hash, e.Index, e.Svg);

    /// <summary>The diagram behind a window is gone; the window says so rather than lying.</summary>
    private void OnDiagramRemoved(object? sender, Guid e) =>
        _diagramWindows.MarkRemoved(e);

    /// <summary>The definition stopped parsing; the window flags what it shows as stale.</summary>
    private void OnDiagramInvalid(object? sender, DiagramInvalidEventArgs e) =>
        _diagramWindows.MarkInvalid(e.DiagramId, e.Message);

    private void OnWatchedDiagramsChanged(object? sender, EventArgs e) =>
        _previewHost?.WatchDiagrams(_diagramWindows.Watched);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.WindowTitle):
                Title = ViewModel.WindowTitle;
                break;

            case nameof(MainViewModel.ActiveTab):
                ApplySelectionFromViewModel();
                break;
        }
    }

    /// <summary>
    /// Intercepts the close so unsaved work can be rescued. The close is cancelled, the
    /// prompts are awaited, and the window is closed again only once every tab has been
    /// dealt with.
    /// </summary>
    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isClosing)
        {
            return;
        }

        args.Cancel = true;

        try
        {
            // Closing every tab would otherwise rewrite the session as it empties, so
            // persistence is frozen first and the tabs that were open stay recorded.
            ViewModel.BeginShutdown();

            if (!await ViewModel.CloseAllAsync())
            {
                ViewModel.CancelShutdown();
                return;
            }

            await _settings.FlushAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Close handling failed; closing anyway.");
        }

        _isClosing = true;

        // The cheatsheet is a second top-level window and is only ever hidden while the app
        // runs. WinUI keeps the process alive until every window is closed, so it has to go
        // before this one does. The same is true of any diagram pop-outs still open, which
        // is also why they are not worth restoring next time: closing the editor closes them.
        _cheatsheet.Shutdown();
        _diagramWindows.Shutdown();
        _findAll.Shutdown();

        _previewHost?.Dispose();
        _beepFilter.Dispose();
        Close();
    }

    // ---------------------------------------------------------------------- tabs

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingSelection)
        {
            return;
        }

        if (DocumentTabs.SelectedItem is DocumentTabViewModel tab)
        {
            ViewModel.OnTabSelectedByUser(tab);
        }
    }

    private void ApplySelectionFromViewModel()
    {
        _isApplyingSelection = true;
        DocumentTabs.SelectedItem = ViewModel.ActiveTab;
        _isApplyingSelection = false;

        // The tab just activated may be one of the hidden ones - picked from the document
        // list, or opened from Explorer with the strip full - and the visibility pass is
        // what puts it in the last visible slot. Run it now rather than trusting a
        // selection change alone to cause a layout pass: swapping which tab carries the
        // close button usually does, but a collapsed tab is not measured, so nothing
        // guarantees it. A tab so new it has no container yet is picked up by the
        // LayoutUpdated pass that creating the container always causes.
        UpdateTabTitles();
        UpdateVisibleTabs();
        UpdateTabPassthroughRegions();
    }

    /// <summary>
    /// Opens a menu named by the shell.
    ///
    /// Alt and a letter is registered twice: once on this window, for when the chrome has
    /// the keyboard, and once with Monaco, because a XAML accelerator never fires while the
    /// editor holds it — which is where the caret spends nearly all its time. Both ends
    /// arrive here.
    /// </summary>
    private void OpenMenuByName(string name)
    {
        // Alt on its own asks for the menu bar rather than a particular menu. Focusing the
        // first item is what a Windows menu bar has always done: the arrows then walk along
        // it and Enter opens one, so none of the letters have to be known in advance.
        if (name == "focus")
        {
            _ = FileMenu.Focus(FocusState.Keyboard);

            return;
        }

        MenuBarItem? menu = name switch
        {
            "file" => FileMenu,
            "edit" => EditMenu,
            "format" => FormatMenu,
            "view" => ViewMenu,
            "tools" => ToolsMenu,
            "help" => HelpMenu,
            _ => null,
        };

        if (menu is not null)
        {
            OpenMenu(menu);
        }
    }

    /// <summary>True while Alt is down and nothing else has been pressed with it.</summary>
    private bool _altAlone;

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e) =>
        _altAlone = e.Key == VirtualKey.Menu;

    private void OnRootKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Menu)
        {
            _altAlone = false;

            return;
        }

        if (_altAlone)
        {
            _altAlone = false;
            OpenMenuByName("focus");
        }
    }

    /// <summary>
    /// MenuBarItem exposes no way to open itself and does not act on AccessKey either, so
    /// the menu is expanded through its automation peer — the same route a screen reader
    /// takes to open it.
    /// </summary>
    private static void OpenMenu(MenuBarItem menu)
    {
        if (FrameworkElementAutomationPeer.CreatePeerForElement(menu) is { } peer
            && peer.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider expander)
        {
            expander.Expand();
        }
    }

    /*
      Room a tab spends on things that are not the title, taken from TabView's template
      metrics in the WinUI package's generic.xaml rather than guessed - a single guess
      generous enough for the close-button tab used to be booked for every tab, and with
      ten tabs open that left a quarter of the strip empty.

      A plain tab: header padding 8+8, border 1+1.
      The active tab: header padding 8+4, close margin 4, close button 32, border 1+1.

      Measured off the running strip since, which put an active tab at 47.7-48.5 and an
      inactive one at 17.8-18.5 across five tabs, and found the 1px border present in both
      states - so the accent edge on the active tab costs nothing, as its note in App.xaml
      says. The 50 stands: a pixel or two of slack inside a tab that is now pinned to this
      width is invisible, and coming in under would clip the close button.

      Booked for every tab, active or not. That is the point rather than an oversight: a tab
      that always books the close button's room cannot change width when the button arrives,
      and the 30 pixels of empty space that leaves on an inactive tab is the price of a strip
      that holds still. The plain figure is kept above for the record; nothing books it.
    */
    private const double ClosableTabChrome = 50;

    /// <summary>
    /// Slack between the cap and the widest a fitted title may be. Covers the pixel or two
    /// the fitter's off-tree ruler and the rendered TextBlock disagree by in a proportional
    /// font. No longer needed in the booking itself - a pinned tab is exactly as wide as it
    /// was booked for - but still wanted here, where a title measured a hair short would be
    /// a title clipped a hair short.
    /// </summary>
    private const double TabWidthSafety = 3;

    /// <summary>
    /// The gap between neighbouring tabs, matching the margin on the tab template. Booked
    /// as part of each tab's width so the fit pass and the layout cannot disagree about
    /// how much strip a tab takes.
    /// </summary>
    private const double TabSpacing = 6;

    /// <summary>
    /// Lists every open document, whether or not its tab is on screen.
    ///
    /// Alphabetical rather than in tab order: this is for finding a document by name, and
    /// tab order is already available on the strip itself. Names are shown in full — the
    /// shortening on the tabs exists because a tab is a fixed width, and a menu is not.
    /// </summary>
    private void RebuildTabListMenu()
    {
        TabListMenu.Items.Clear();

        IOrderedEnumerable<DocumentTabViewModel> ordered = ViewModel.Tabs
            .OrderBy(tab => tab.Title, StringComparer.CurrentCultureIgnoreCase);

        foreach (DocumentTabViewModel tab in ordered)
        {
            var entry = new MenuFlyoutItem { Text = tab.DisplayTitle, Tag = tab };

            // The open document is the one you are least likely to want, so it is marked
            // rather than hidden: it says where you are in a list that is not in tab order.
            if (ReferenceEquals(tab, ViewModel.ActiveTab))
            {
                entry.FontWeight = FontWeights.SemiBold;
            }

            ToolTipService.SetToolTip(entry, tab.Tooltip);
            entry.Click += OnTabListEntryClick;
            TabListMenu.Items.Add(entry);
        }

        if (TabListMenu.Items.Count == 0)
        {
            TabListMenu.Items.Add(new MenuFlyoutItem { Text = "No documents open", IsEnabled = false });
        }
    }

    private void OnTabListEntryClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DocumentTabViewModel tab })
        {
            ViewModel.OnTabSelectedByUser(tab);
        }
    }

    /// <summary>
    /// Shortens each tab's title from the middle so no tab grows past the cap, pins the tab
    /// to the width that title needs, and records it for the visibility pass.
    ///
    /// Fitted against the cap rather than the tab's current width, and that distinction is
    /// what makes it safe. These tabs size to their content, so measuring the tab and then
    /// shortening its title would shrink the tab, which would shorten the title again. The
    /// cap is a constant, so a name either already fits — and is left alone, keeping short
    /// tabs short — or is cut to the one width every long name settles at.
    ///
    /// Two things about a tab change when it becomes the active one, and both used to move
    /// its neighbours. The close button is the obvious 30 pixels. The other is the title:
    /// WinUI draws a selected tab's heavier, the same name measures about three per cent
    /// wider in that font, and a size-to-content tab passes that straight on to everything
    /// downstream of it. The two effects part company because the second is proportional to
    /// the name — the tab losing the selection and the tab gaining it shed and gain different
    /// amounts, and the difference lands on every tab after both. That was the pixel or two
    /// of drift, and near the fitting limit it also re-truncated the name mid-click.
    ///
    /// So both are answered, and both have to be: booking the close button's room on every
    /// tab and pinning the width settles the 30, and the fitter measuring every title at one
    /// fixed weight settles the rest. Pinning alone would not — what gets pinned is derived
    /// from a title width that was itself moving.
    ///
    /// The state marker is booked the same way and for the same reason. Its room is charged
    /// to every tab whether or not one is showing, and the widest marker at that, so a
    /// document going dirty — or a file going missing, or changing on disk — puts a glyph in
    /// front of its name without the tab moving to make space for it.
    /// </summary>
    private void UpdateTabTitles()
    {
        // Titles are fitted against the active tab's chrome even on tabs that are not
        // active, so a title never has to be re-shortened - and its tab never re-fitted -
        // just because the close button arrived.
        double room = TabMaximumWidth - ClosableTabChrome - TabWidthSafety;

        // Measured once per pass off the first tab, not once per tab: every title carries the
        // same family and size, so the answer is the same for all of them. Negative until
        // there is a tab to measure against.
        double marker = -1;

        // Rebuilt from scratch so a closed document's container is not kept alive - or
        // consulted - through the width table. A container fitted on an earlier pass and
        // skipped on this one (its title binding not evaluated yet) falls back to the cap
        // in WidthOf, which errs towards hiding and corrects on the next pass.
        _tabWidths.Clear();

        foreach (object item in DocumentTabs.TabItemsSource is IEnumerable<object> source ? source : [])
        {
            if (DocumentTabs.ContainerFromItem(item) is not TabViewItem container
                || container.Header is not TextBlock title
                || title.Tag is not string full)
            {
                continue;
            }

            if (marker < 0)
            {
                marker = TabTitleFitter.Reserve(title, DocumentTabViewModel.Markers);
            }

            // Taken apart again rather than fitted whole. Fitting the marker along with the
            // name made the name's room depend on which marker was in front of it, so a
            // document going dirty re-cut its own title and moved the tab by a pixel or two;
            // and a marker that arrived on a name already at the limit could be shortened
            // away by the very truncation it was meant to survive. Fitting the name alone
            // against room the marker is always charged for settles both.
            string state = DocumentTabViewModel.MarkerOf(full);
            string name = full[state.Length..];

            double pinned =
                TabTitleFitter.Fit(title, name, room - marker, state) + marker + ClosableTabChrome;

            // Min and max together, because Width is not ours to hold: TabView writes that
            // one itself while it manages tab sizing. Clamping both ends leaves the tab
            // measuring to exactly this whatever the template does inside it, which is what
            // takes the close button out of the strip's arithmetic.
            //
            // Guarded like the visibility writes below, and for the same reason: this runs
            // from a layout callback, and assigning a size unconditionally there would
            // invalidate the layout that called it and never settle.
            if (container.MinWidth != pinned)
            {
                container.MinWidth = pinned;
            }

            if (container.MaxWidth != pinned)
            {
                container.MaxWidth = pinned;
            }

            // Exact now rather than an estimate with slack on top: the tab is as wide as it
            // was just told to be, so there is nothing left for the safety margin to cover.
            _tabWidths[container] = pinned + TabSpacing;
        }
    }


    /// <summary>What each tab would occupy if shown, so a hidden one can still be reasoned about.</summary>
    private readonly Dictionary<TabViewItem, double> _tabWidths = [];

    /// <summary>
    /// Room the strip spends after the last tab: the add button (32 wide plus 3 of
    /// container padding, per the template) and the 9 pixels of header, footer and padding
    /// the strip's ItemsPresenter carries around the tab run.
    /// </summary>
    private const double AddButtonWidth = 44;

    /// <summary>
    /// Hides the tabs that will not fit, rather than letting the strip scroll to them.
    ///
    /// A scrolling strip clips whatever the viewport cuts through, so there is always a
    /// sliver of some tab at the edge and a pair of arrows to go with it. Collapsing the
    /// tabs that do not fit removes both at once: the content is never wider than the strip,
    /// so nothing is cut in half and the arrows have nothing to scroll to. Everything hidden
    /// is still one click away in the document list.
    ///
    /// Sound because it is a fixed point rather than a feedback loop: every input is
    /// independent of the pass's own output. The widths come from the title fitter, never
    /// from measuring a container - a collapsed tab measures as zero, so measuring would
    /// make the decision depend on itself. The room comes from the strip's width and the
    /// footer's reserved minimum, never from the footer's actual width - the footer sits
    /// in a star column, so its actual width IS whatever the tabs left over, and
    /// subtracting it told the pass that exactly the currently visible tabs fit, which is
    /// why widening the window never used to bring a hidden tab back. With stable inputs,
    /// re-running the pass writes nothing (the visibility writes are guarded) and the
    /// layout it is called from settles immediately.
    ///
    /// The visible tabs are the leading run of the tab order, plus the active tab when it
    /// did not make the cut on its own: its room is booked first, so it takes the last
    /// visible place. That is what makes picking a hidden document out of the list work,
    /// and what keeps a file opened from Explorer visible when the strip is already full.
    /// </summary>
    private void UpdateVisibleTabs()
    {
        // A reorder moves containers under the pointer; changing tab visibility from those
        // in-flight positions would fight the drag. The completed handler re-runs this.
        if (_isDraggingTab)
        {
            return;
        }

        List<TabViewItem> containers =
        [
            .. (DocumentTabs.TabItemsSource is IEnumerable<object> source ? source : [])
                .Select(DocumentTabs.ContainerFromItem)
                .OfType<TabViewItem>(),
        ];

        if (containers.Count == 0)
        {
            return;
        }

        // Before the first layout there is nothing sensible to decide; leave it alone rather
        // than hiding everything on the way up.
        if (DocumentTabs.ActualWidth <= 0)
        {
            return;
        }

        double available = DocumentTabs.ActualWidth
            - TabStripLeading.ActualWidth
            - AddButtonWidth
            - TabStripTrailing.MinWidth;

        List<TabViewItem> shown = [];
        double used = 0;

        // The active tab's room is booked before anything else's, so it is never the one
        // that misses out.
        TabViewItem? active = DocumentTabs.ContainerFromItem(ViewModel.ActiveTab) as TabViewItem;

        if (active is not null)
        {
            shown.Add(active);
            used = WidthOf(active);
        }

        bool full = false;

        foreach (TabViewItem container in containers)
        {
            if (ReferenceEquals(container, active))
            {
                continue;
            }

            double width = WidthOf(container);

            // Hiding continues to the end once one tab misses: the strip shows a leading
            // run of the tab order, not whichever assortment happens to fit.
            if (full || (used + width > available && shown.Count > 0))
            {
                full = true;
                continue;
            }

            used += width;
            shown.Add(container);
        }

        foreach (TabViewItem container in containers)
        {
            Visibility wanted = shown.Contains(container) ? Visibility.Visible : Visibility.Collapsed;

            // Assigning unconditionally would invalidate layout from inside a layout
            // callback, and this would never settle.
            if (container.Visibility != wanted)
            {
                container.Visibility = wanted;
            }
        }
    }

    /// <summary>
    /// What a tab occupies on the strip: its fitted title plus chrome and the gap to its
    /// neighbour, as recorded by <see cref="UpdateTabTitles"/>. Deliberately never the
    /// container's ActualWidth - a collapsed tab measures as zero, and feeding a
    /// measurement into the decision that produced it is the loop this strip used to be
    /// stuck in. The fallback for a tab not fitted yet is the cap, which errs towards
    /// hiding: the harmless direction, corrected on the next pass.
    /// </summary>
    private double WidthOf(TabViewItem container) =>
        _tabWidths.TryGetValue(container, out double width) ? width : TabMaximumWidth + TabSpacing;

    /// <summary>
    /// The cap tabs stop widening at, read from the same resource the control uses so the
    /// two cannot disagree about where a title has to stop.
    /// </summary>
    private static double TabMaximumWidth =>
        Application.Current.Resources["TabViewItemMaxWidth"] is double width ? width : 220;

    /// <summary>Set once the strip's ScrollViewer has been found and switched off.</summary>
    private bool _tabScrollingDisabled;

    /// <summary>
    /// Switches the tab strip's own scrolling off, once, as soon as the template part
    /// exists to switch off.
    ///
    /// The strip is a ListView whose template wraps the tabs in a ScrollViewer, and the
    /// half-drawn tabs the strip used to show were that ScrollViewer clipping: the control
    /// kept laying out the full tab run underneath whatever this class decided, and the
    /// viewport cut through whichever tab it ran out of room in. The visibility pass
    /// guarantees the visible tabs fit, so there is never anything legitimate to scroll
    /// to; disabling the scrolling makes the in-between states unreachable as well, and
    /// the scroll arrows - whose visibility follows the computed scrollbar visibility -
    /// can never appear at all.
    /// </summary>
    private void EnsureTabStripDoesNotScroll()
    {
        if (_tabScrollingDisabled || FindDescendant<ScrollViewer>(DocumentTabs) is not { } scroller)
        {
            return;
        }

        scroller.HorizontalScrollMode = ScrollMode.Disabled;
        scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
        _tabScrollingDisabled = true;
    }

    /// <summary>Set once the tab list has been stretched to the height of the strip.</summary>
    private bool _tabStripRuleAligned;

    /// <summary>
    /// Puts the tab column's share of the hairline under the strip back on the line.
    ///
    /// TabView draws that rule in pieces rather than as one border, because the selected
    /// tab has to break it: the header column gets one piece from the TabView template,
    /// everything from the add button rightwards gets another, and the stretch between
    /// them - the tab list - draws its own from inside the ListView, so that the tabs can
    /// interrupt it. The ListView is top aligned by its stock style and only as tall as
    /// its content, which is the strip's full height with a tab open and eight pixels of
    /// padding with none. So the moment the last tab closes, that middle piece jumps to
    /// within a few pixels of the top of the window and leaves a gap in the line at the
    /// foot of the strip - the strip itself does not shrink with it, because the leading
    /// header is given the strip's height outright.
    ///
    /// Stretching the list is what closes the gap, and only in that state: the height it
    /// takes with a tab open is already the height the strip is fixed at, so this changes
    /// nothing while any document is open.
    /// </summary>
    private void EnsureTabStripRuleAligned()
    {
        if (_tabStripRuleAligned || FindDescendant<ListView>(DocumentTabs) is not { } tabList)
        {
            return;
        }

        tabList.VerticalAlignment = VerticalAlignment.Stretch;
        _tabStripRuleAligned = true;
    }

    /// <summary>
    /// The first <typeparamref name="T"/> under <paramref name="root"/>, depth first. The
    /// strip has exactly one ScrollViewer and one ListView, both inside TabView's template,
    /// so first-found is the one meant in both cases.
    /// </summary>
    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);

        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);

            if (child is T found)
            {
                return found;
            }

            if (FindDescendant<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    /// <summary>
    /// Keeps the document list button at the far right of the strip, clear of the caption
    /// buttons. The inset Windows reserves for those buttons is in physical pixels and
    /// moves with display scale, so the margin is computed rather than declared in markup.
    /// The write is guarded because this runs from a layout callback.
    /// </summary>
    private void PositionTabListButton()
    {
        if (DocumentTabs.XamlRoot is not { } xamlRoot)
        {
            return;
        }

        double inset = AppWindow.TitleBar.RightInset / xamlRoot.RasterizationScale;
        var wanted = new Thickness(0, 0, inset + 6, 0);

        if (Math.Abs(TabListButton.Margin.Right - wanted.Right) > 0.5)
        {
            TabListButton.Margin = wanted;
        }
    }

    /// <summary>
    /// A new document opens ready to be typed into. The focus is the command's own doing
    /// now - see MainViewModel.NewTab - so the add button, the File menu and Ctrl+N all
    /// behave the same.
    /// </summary>
    private void OnAddTabButtonClick(TabView sender, object args) =>
        ViewModel.NewTabCommand.Execute(null);

    private void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Item is DocumentTabViewModel tab)
        {
            _ = CloseTabAndRestoreFocusAsync(tab);
        }
    }

    /// <summary>
    /// Closes a tab, then puts the keyboard in whatever document is left.
    ///
    /// The close is awaited rather than fired and forgotten, because a dirty document raises
    /// the save prompt first: restoring before that is answered would take the keyboard off
    /// the dialog. Cancelling the prompt lands here too, which is right - the tab is still
    /// open and it is still where the user was working.
    /// </summary>
    private async Task CloseTabAndRestoreFocusAsync(DocumentTabViewModel tab)
    {
        await ViewModel.CloseTabAsync(tab);

        ViewModel.RestoreDocumentFocus();
    }

    /*
      Middle-click closes a tab, the way every browser does.

      Why this took so many goes: the tab strip is handed to SetTitleBar, which makes it a
      non-client region. Windows decides what happens to input there before XAML sees any of
      it, and it forwards only the left button into the content - which is why clicking a tab
      selects it, and why every attempt to attach a PointerPressed handler somewhere different
      failed identically. The middle button was not being swallowed by the wrong handler; it
      was never becoming a XAML event at all.

      The fix is to stop the tabs being part of the caption in the first place.
      InputNonClientPointerSource takes a list of rectangles to carve back out of the
      non-client area - NonClientRegionKind.Passthrough - and input landing in one of those is
      delivered to the content as ordinary client input, every button of it. Registering each
      tab's own bounds leaves the rest of the strip dragging the window exactly as before, so
      nothing about the title bar changes except that the tabs are now really tabs.

      The same regions are what let a tab be dragged to a new position: a reorder is a drag,
      and a drag on the caption is Windows moving the window.
    */
    private readonly List<RectInt32> _tabRegions = [];

    /// <summary>
    /// Set while a tab is being dragged to a new position.
    ///
    /// A reorder moves tabs under the pointer, so it fires LayoutUpdated continuously, and
    /// re-registering the regions on every frame of a drag would rewrite the window's input
    /// map while that drag is relying on it. The bounds are stale for the length of the drag,
    /// which costs nothing - the pointer is already captured - and are brought up to date once
    /// it finishes.
    /// </summary>
    private bool _isDraggingTab;

    private void OnTabDragStarting(TabView sender, TabViewTabDragStartingEventArgs args) =>
        _isDraggingTab = true;

    private void OnTabDragCompleted(TabView sender, TabViewTabDragCompletedEventArgs args)
    {
        _isDraggingTab = false;

        // The visibility pass sat out the drag along with the regions, and the reorder may
        // have moved a tab across the fold in either direction.
        UpdateTabTitles();
        UpdateVisibleTabs();
        UpdateTabPassthroughRegions();

        // Its own call rather than leaving it to the released handler: the drag captured the
        // pointer, so the release goes to the drag and not to the strip.
        ViewModel.RestoreDocumentFocus();
    }

    /// <summary>
    /// Re-registers the tab rectangles as passthrough, if they have moved.
    ///
    /// Called from LayoutUpdated, so it runs often and does nothing almost every time. The
    /// comparison against the last set is what makes that cheap: the tree walk is a handful
    /// of nodes, and the interop call only happens when the answer has actually changed.
    /// </summary>
    private void UpdateTabPassthroughRegions()
    {
        if (_isClosing || _isDraggingTab || DocumentTabs.XamlRoot is not { } xamlRoot)
        {
            return;
        }

        double scale = xamlRoot.RasterizationScale;
        List<RectInt32> regions = [];

        /*
            Tabs report where they are, not where they can be seen. Once the strip overflows,
            an off-screen tab still transforms to a real position - and that position can be
            out beyond the right edge, underneath the caption buttons. A passthrough region
            there routes the click into the page instead of the button, so minimise, maximise
            and close all stop working while still lighting up on hover.

            The insets are what the framework reserves for those buttons, so nothing is
            allowed to claim ground at or past them. The left edge is held at the end of the
            branding, which a tab scrolled the other way would otherwise cover.
        */
        Windows.Foundation.Point brandingEnd = TabStripLeading
            .TransformToVisual(RootGrid)
            .TransformPoint(new Windows.Foundation.Point(TabStripLeading.ActualWidth, 0));

        int leftLimit = Math.Max(AppWindow.TitleBar.LeftInset, (int)Math.Round(brandingEnd.X * scale));
        int rightLimit = (int)Math.Round(RootGrid.ActualWidth * scale) - AppWindow.TitleBar.RightInset;

        foreach (TabViewItem item in TabItemsIn(DocumentTabs))
        {
            if (item.ActualWidth <= 0 || item.ActualHeight <= 0)
            {
                continue;
            }

            // Regions are physical pixels relative to the client area, and RootGrid fills the
            // client area because the content is extended into the title bar.
            Windows.Foundation.Point origin = item
                .TransformToVisual(RootGrid)
                .TransformPoint(new Windows.Foundation.Point(0, 0));

            int left = Math.Max(leftLimit, (int)Math.Round(origin.X * scale));
            int right = Math.Min(rightLimit, (int)Math.Round((origin.X + item.ActualWidth) * scale));

            // Entirely outside the usable strip: scrolled away, or not laid out yet.
            if (right <= left)
            {
                continue;
            }

            regions.Add(new RectInt32(
                left,
                (int)Math.Round(origin.Y * scale),
                right - left,
                (int)Math.Round(item.ActualHeight * scale)));
        }

        // The document list sits in the strip's footer, which is caption area: without a
        // region of its own the click is taken as a window drag and the menu never opens.
        if (TabListButton.ActualWidth > 0 && TabListButton.ActualHeight > 0)
        {
            Windows.Foundation.Point listOrigin = TabListButton
                .TransformToVisual(RootGrid)
                .TransformPoint(new Windows.Foundation.Point(0, 0));

            int listLeft = Math.Max(leftLimit, (int)Math.Round(listOrigin.X * scale));
            int listRight = Math.Min(
                rightLimit,
                (int)Math.Round((listOrigin.X + TabListButton.ActualWidth) * scale));

            if (listRight > listLeft)
            {
                regions.Add(new RectInt32(
                    listLeft,
                    (int)Math.Round(listOrigin.Y * scale),
                    listRight - listLeft,
                    (int)Math.Round(TabListButton.ActualHeight * scale)));
            }
        }

        if (regions.SequenceEqual(_tabRegions))
        {
            return;
        }

        _tabRegions.Clear();
        _tabRegions.AddRange(regions);

        try
        {
            var input = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);

            if (regions.Count == 0)
            {
                input.ClearRegionRects(NonClientRegionKind.Passthrough);
            }
            else
            {
                input.SetRegionRects(NonClientRegionKind.Passthrough, [.. regions]);
            }
        }
        catch (Exception ex)
        {
            // Losing the gesture is not worth taking the window down for.
            _logger.LogWarning(ex, "Could not update the tab strip input regions.");
        }
    }

    /// <summary>Every realized tab container inside the strip, in visual-tree order.</summary>
    private static IEnumerable<TabViewItem> TabItemsIn(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);

        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);

            if (child is TabViewItem item)
            {
                yield return item;
                continue;
            }

            foreach (TabViewItem nested in TabItemsIn(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>The ordinary route: a middle-button press delivered to the tab strip.</summary>
    private void OnTabStripPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType != PointerDeviceType.Mouse)
        {
            return;
        }

        PointerPointProperties properties = e.GetCurrentPoint(DocumentTabs).Properties;

        if (properties.PointerUpdateKind != PointerUpdateKind.MiddleButtonPressed)
        {
            return;
        }

        DocumentTabViewModel? tab =
            FindTabItem(e.OriginalSource as DependencyObject)?.DataContext as DocumentTabViewModel
            ?? TabAt(e.GetCurrentPoint(RootGrid).Position);

        _logger.LogInformation("Middle-click on the tab strip. Tab: {Tab}.", tab?.Title ?? "none");

        if (tab is null)
        {
            return;
        }

        e.Handled = true;
        _ = CloseTabAndRestoreFocusAsync(tab);
    }

    /// <summary>
    /// Hands the keyboard back to the document after a click on a tab.
    ///
    /// This is the whole of the fix for a click on the tab that is already selected. That
    /// click changes nothing - SelectionChanged does not fire, because the selection has not
    /// changed - so the only thing it did was move XAML focus to the tab, and the caret in
    /// the text stopped answering. Switching to a different tab goes through here as well,
    /// so the strip behaves the same either way.
    ///
    /// Only a release over a tab counts. The add button and the document list sit in the
    /// strip too, and both have somewhere better for the keyboard to be: a new document, and
    /// an open menu. The close button is left to the close path, which restores focus once
    /// the document has actually gone and the save prompt, if there was one, is answered.
    /// </summary>
    private void OnTabStripPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        // Touch and pen have no button to name, so only the mouse is filtered.
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse
            && e.GetCurrentPoint(DocumentTabs).Properties.PointerUpdateKind
                != PointerUpdateKind.LeftButtonReleased)
        {
            return;
        }

        // The tab menu is open, so the keyboard is meant to be on it. A right-click never
        // reaches here - the button filter above sees to that - but a press-and-hold does,
        // and that release is the end of the gesture that opened the menu, not a click on
        // the tab. The menu hands focus back itself when it closes.
        if (IsTabMenuOpen)
        {
            return;
        }

        DependencyObject? source = e.OriginalSource as DependencyObject;

        // Over a tab, and nothing else. Hit testing as well as walking up, for the reason
        // TabAt gives: several parts of a TabViewItem report a source outside that item's
        // own visual tree. The add button and the document list fail both tests, being in
        // the strip's own template rather than in any tab.
        if (FindTabItem(source) is null && TabAt(e.GetCurrentPoint(RootGrid).Position) is null)
        {
            return;
        }

        if (IsTabCloseButton(source))
        {
            return;
        }

        ViewModel.RestoreDocumentFocus();
    }

    /// <summary>
    /// True when a click already known to be on a tab landed on that tab's close button.
    ///
    /// Any button inside a TabViewItem is the close button: the template has exactly one.
    /// The walk stops at the tab so it cannot wander up into the strip, and the caller has
    /// established that the click was on a tab in the first place.
    /// </summary>
    private static bool IsTabCloseButton(DependencyObject? source)
    {
        while (source is not null and not TabViewItem)
        {
            if (source is Button)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    /// <summary>
    /// Works out which tab is at a point in the window's own coordinates.
    ///
    /// Hit testing rather than walking up from an event source: a TabViewItem is a templated
    /// control, and several of its parts report a source outside that item's visual tree.
    /// Testing the point catches those, which is the difference between the gesture working
    /// everywhere on a tab and working only in the middle of the label.
    /// </summary>
    private DocumentTabViewModel? TabAt(Windows.Foundation.Point position)
    {
        foreach (UIElement element in VisualTreeHelper.FindElementsInHostCoordinates(position, RootGrid))
        {
            if (FindTabItem(element)?.DataContext is DocumentTabViewModel found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Walks up from the clicked element to the tab that contains it.</summary>
    private static TabViewItem? FindTabItem(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is TabViewItem item)
            {
                return item;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    // -------------------------------------------------------------------- help

    private async void OnOpenLogFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            await Launcher.LaunchFolderPathAsync(_paths.LogDirectory);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not open the log folder.");
        }
    }

    /// <summary>
    /// Handled here rather than through a Command binding on the menu item.
    ///
    /// The item is a ToggleMenuFlyoutItem, and binding a Command to one of those proved
    /// unreliable: the tick would flip but the command frequently never ran, so the menu
    /// looked like it needed several clicks. Click always fires.
    /// </summary>
    private void OnCheatsheetItemClick(object sender, RoutedEventArgs e)
    {
        _logger.LogInformation("Cheatsheet menu item clicked.");
        ViewModel.ToggleCheatsheetCommand.Execute(null);
    }

    private async void OnShowShortcuts(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new ShortcutsDialog(_logger).AnchorTo(RootGrid);
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not show the keyboard shortcuts.");
        }
    }

    private async Task ShowAboutAsync()
    {
        try
        {
            var dialog = new AboutDialog(_paths, _logger).AnchorTo(RootGrid);
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not show the About dialog.");
        }
    }

    /// <summary>
    /// Help, Support the Project.
    ///
    /// The dialog only reports which button was pressed; the launching happens here, where
    /// the logger and the dialog service are. Both actions close the dialog, because you are
    /// leaving for the browser and there is nothing to come back to.
    ///
    /// Focus is left where the About path leaves it, rather than this one menu item
    /// behaving differently from the one below it.
    /// </summary>
    private async Task ShowSupportAsync()
    {
        try
        {
            var dialog = new SupportDialog().AnchorTo(RootGrid);

            string? url = await dialog.ShowAsync() switch
            {
                ContentDialogResult.Primary => ProjectLinks.SponsorsUrl,
                ContentDialogResult.Secondary => ProjectLinks.RepositoryUrl,
                _ => null,
            };

            if (url is null)
            {
                return;
            }

            // The URL is named rather than hidden behind a friendly word: someone whose
            // shell will not launch a browser can still read it off and type it in. What
            // went wrong is in the log, not in front of them.
            if (!await ExternalLink.OpenAsync(url, _logger))
            {
                await _dialogs.ShowMessageAsync(
                    "Could not open GitHub",
                    $"Marqora could not open your browser. You can visit {url} yourself.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not show the Support dialog.");
        }
    }

    /// <summary>
    /// Double-clicking the Split button evens up the divider, the same as double-clicking the
    /// divider itself. The first click of the pair has already switched to split view through
    /// the button's own command, which is the behaviour wanted anyway.
    /// </summary>
    private void OnSplitSegmentDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
        ViewModel.ResetSplitCommand.Execute(null);
    }

    /// <summary>
    /// Double-clicking the zoom readout takes both panes back to 100%, the same as
    /// Ctrl+Shift+0. As with the Split button above, the first click of the pair has already
    /// run the button's own command and reset the active pane, so the pair as a whole leaves
    /// source and preview both at 100% - which is the point of the gesture.
    /// </summary>
    private void OnZoomLabelDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
        ViewModel.ZoomBothResetCommand.Execute(null);
    }

    // ------------------------------------------------------------ chrome density

    /*
      The toolbar row carries three things side by side: the menu bar, the centred view
      switcher and the zoom cluster. Together they want roughly 630 effective pixels, which is
      more than the window's 640-pixel minimum leaves once padding is counted - so at narrow
      widths the switcher, sitting in the star column, was clipped mid-word.

      Rather than let it truncate, the row sheds density in two steps. Everything dropped here
      stays reachable another way: view modes from the View menu and Alt+1/2/3, scroll sync
      from the View menu, and the zoom readout is a reset button whose job Ctrl+0 also does.

      Driven from SizeChanged rather than an AdaptiveTrigger because the width that matters is
      the row's own, in effective pixels. AppWindow.Size is physical pixels and would need the
      display scale folded back in before it meant anything here.
    */
    private const double CompactChromeWidth = 900;
    private const double MinimalChromeWidth = 720;

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyChromeDensity(e.NewSize.Width);

    private void ApplyChromeDensity(double width)
    {
        bool minimal = width < MinimalChromeWidth;
        bool compact = width < CompactChromeWidth;

        ViewSwitcher.Visibility = minimal ? Visibility.Collapsed : Visibility.Visible;
        ZoomLabelButton.Visibility = minimal ? Visibility.Collapsed : Visibility.Visible;

        // Scroll sync goes first: it only applies in split view and is a toggle people set
        // once, unlike zoom, which is adjusted constantly.
        Visibility sync = compact ? Visibility.Collapsed : Visibility.Visible;
        ScrollSyncDivider.Visibility = sync;
        ScrollSyncToggle.Visibility = sync;

        // Tighter segments buy back about 36 pixels before anything has to disappear.
        var padding = compact ? new Thickness(8, 5, 8, 5) : new Thickness(14, 5, 14, 5);

        SourceSegment.Padding = padding;
        SplitSegment.Padding = padding;
        PreviewSegment.Padding = padding;

        ApplyFormatBarDensity(width);
        ApplyEmptyStateDensity(width);
    }

    /// <summary>
    /// Width below which the empty state's four action buttons fold onto two rows.
    ///
    /// The row of four draws to about 570 pixels, and it sits inside a card that is inset
    /// from the window and padded within that, so it runs out of room while the window
    /// still looks wide: the card's content stops widening at the empty state's own
    /// MaxWidth of 660, leaving roughly 610 inside the padding. The threshold is set where
    /// that stops being comfortable rather than where it starts clipping, so the row is
    /// never merely touching the card's sides - which is what it used to do.
    /// </summary>
    private const double EmptyStateStackWidth = 700;

    /// <summary>
    /// Folds the empty state's actions from one row of four to two rows of two.
    ///
    /// Nothing is dropped, unlike the toolbar above: this is the only route into the app
    /// for someone with no document open, so all four have to stay reachable. Grid
    /// attached properties rather than a second panel, so the buttons themselves - and
    /// their command bindings - exist once.
    /// </summary>
    private void ApplyEmptyStateDensity(double width)
    {
        bool folded = width < EmptyStateStackWidth;

        Grid.SetRow(NewDocumentButton, folded ? 1 : 0);
        Grid.SetColumn(NewDocumentButton, folded ? 0 : 2);

        Grid.SetRow(NewFromClipboardButton, folded ? 1 : 0);
        Grid.SetColumn(NewFromClipboardButton, folded ? 1 : 3);

        // The vertical gap exists only while there are two rows. It is set here rather
        // than as a margin on the buttons, which carry a horizontal margin only: a
        // vertical one would also space the single-row layout away from the text above it.
        EmptyStateActions.RowSpacing = folded ? 10 : 0;
    }

    /// <summary>
    /// Widths at which the formatting bar starts giving things up.
    ///
    /// Its own numbers rather than the chrome row's. That row holds a menu, a segmented
    /// switcher and a zoom cluster and needs about 630 effective pixels; this bar carries the
    /// file group and sixteen formatting controls and needs nearer 955, so shedding at the
    /// same widths would leave one row overcrowded while the other still had room.
    /// </summary>
    /// Measured rather than guessed. The full bar draws to about 953 pixels: the file group
    /// added 163, the code block and table gave back 83 on their way into the Insert
    /// dropdown, and blockquote kept its button. Below the compact width the two insert
    /// dropdowns go, leaving about 758; below the minimal width the file group, the heading
    /// and the lists go with them, leaving about 375. The minimal width covers that 758 with
    /// a little to spare - set below it there would be a band where the compact bar did not
    /// fit and nothing had shed yet, which is what 650 used to leave.
    private const double FormatBarCompactWidth = 990;
    private const double FormatBarMinimalWidth = 780;

    /// <summary>
    /// Moves whole groups into the overflow menu as the window narrows, worst-used first.
    ///
    /// The history and inline groups never go: undo is the most reached-for thing here, and
    /// bold, italic and link are the reason the bar exists. The two insert dropdowns go first
    /// — they are the widest and the least reached for — and the file group, the lists and
    /// heading follow only when there is genuinely no room.
    ///
    /// The file group is the one that sheds without a mirror. Open and Save have no business
    /// under a button labelled "More formatting", so they simply go; the File menu and
    /// Ctrl+O / Ctrl+S still have them.
    /// </summary>
    private void ApplyFormatBarDensity(double width)
    {
        bool minimal = width < FormatBarMinimalWidth;
        bool compact = width < FormatBarCompactWidth;

        // Code Block and Table live in the Insert dropdown now, so their overflow mirrors
        // travel with that group rather than with the block group that used to hold them.
        SetFormatGroup(
            !compact,
            FormatInsertGroup,
            InsertSeparator,
            OverflowDiagram,
            OverflowSnippet,
            OverflowCodeBlock,
            OverflowTable);

        SetFormatGroup(!minimal, FormatHeadingGroup, HeadingSeparator, OverflowHeading);
        SetFormatGroup(
            !minimal,
            FormatLineGroup,
            LineSeparator,
            OverflowBulletList,
            OverflowNumberedList,
            OverflowTaskList,
            OverflowBlockquote);

        Visibility files = minimal ? Visibility.Collapsed : Visibility.Visible;
        FileActionsGroup.Visibility = files;
        FileActionsSeparator.Visibility = files;

        // Nothing hidden, nothing to offer: the button goes rather than sitting there empty.
        FormatOverflowButton.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Shows a group on the bar or its mirror in the overflow menu, never both. The
    /// separator travels with the group so a hidden group does not leave a stray rule.
    /// </summary>
    private static void SetFormatGroup(
        bool onBar,
        FrameworkElement group,
        FrameworkElement separator,
        params MenuFlyoutItemBase[] mirror)
    {
        group.Visibility = onBar ? Visibility.Visible : Visibility.Collapsed;
        separator.Visibility = onBar ? Visibility.Visible : Visibility.Collapsed;

        foreach (MenuFlyoutItemBase item in mirror)
        {
            item.Visibility = onBar ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    // -------------------------------------------------------- window placement

    /// <summary>Smallest window worth restoring to; anything less is treated as corrupt state.</summary>
    private const int MinimumRestoreWidth = 640;
    private const int MinimumRestoreHeight = 420;

    private static bool IsPlausibleWindowSize(int width, int height) =>
        width >= MinimumRestoreWidth && height >= MinimumRestoreHeight;

    private void RestoreWindowPlacement()
    {
        WindowPlacement placement = _settings.Current.Window;

        // A settings file written by an older build, or by hand, can carry nonsense here.
        // Fall back to the defaults rather than opening something unusable.
        int width = Math.Max(placement.Width, MinimumRestoreWidth);
        int height = Math.Max(placement.Height, MinimumRestoreHeight);

        if (placement.HasPosition
            && IsPlausibleWindowSize(placement.Width, placement.Height)
            && FitsOnADisplay(placement))
        {
            AppWindow.MoveAndResize(new RectInt32(placement.X, placement.Y, width, height));
        }
        else
        {
            AppWindow.Resize(new SizeInt32(width, height));
        }

        if (placement.IsMaximized && AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }

        // Write back what was actually applied. Without this, a settings file carrying
        // unusable bounds keeps them until the user happens to move the window.
        CapturePlacement();
    }

    /// <summary>Records the window's current bounds, ignoring transient or nonsense states.</summary>
    private void CapturePlacement()
    {
        AppWindow window = AppWindow;

        if (window.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized }
            || !IsPlausibleWindowSize(window.Size.Width, window.Size.Height))
        {
            return;
        }

        bool maximized = window.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };

        _settings.Update(s => s with
        {
            Window = maximized
                // Keep the last restored bounds so un-maximizing returns somewhere sensible.
                ? s.Window with { IsMaximized = true }
                : new WindowPlacement
                {
                    X = window.Position.X,
                    Y = window.Position.Y,
                    Width = window.Size.Width,
                    Height = window.Size.Height,
                    IsMaximized = false,
                },
        });
    }

    /// <summary>
    /// Guards against restoring onto a monitor that is no longer attached, which would
    /// otherwise put the window somewhere the user cannot reach.
    /// </summary>
    private static bool FitsOnADisplay(WindowPlacement placement)
    {
        var area = DisplayArea.GetFromRect(
            new RectInt32(placement.X, placement.Y, placement.Width, placement.Height),
            DisplayAreaFallback.Nearest);

        RectInt32 bounds = area.WorkArea;

        return placement.X < bounds.X + bounds.Width
            && placement.Y < bounds.Y + bounds.Height
            && placement.X + placement.Width > bounds.X
            && placement.Y + placement.Height > bounds.Y;
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!_isLoaded || _isClosing || (!args.DidPositionChange && !args.DidSizeChange))
        {
            return;
        }

        CapturePlacement();
    }

    // ---------------------------------------------------------- drag and drop

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.Handled = true;

        if (e.DragUIOverride is { } overrides)
        {
            overrides.Caption = "Open in Marqora";
            overrides.IsCaptionVisible = true;
            overrides.IsGlyphVisible = true;
        }

        ViewModel.IsDragOver = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => ViewModel.IsDragOver = false;

    private async void OnDrop(object sender, DragEventArgs e)
    {
        ViewModel.IsDragOver = false;

        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        // The data view is only valid until this handler returns, so the deferral keeps it
        // alive across the await.
        DragOperationDeferral deferral = e.GetDeferral();

        try
        {
            IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();

            // Folders are kept as well as files: the view model expands a dropped folder
            // the same way File, Open Folder does.
            List<string> paths = [.. items.Select(item => item.Path).Where(p => !string.IsNullOrEmpty(p))];

            if (paths.Count > 0)
            {
                await ViewModel.OpenDroppedAsync(paths);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handling a dropped file failed.");
        }
        finally
        {
            deferral.Complete();
        }
    }

    // ------------------------------------------------------------ recent files

    /// <summary>
    /// Rebuilds File, Open Recent. Menu flyout items are not templated from a collection,
    /// so the submenu is repopulated whenever the recent list changes.
    /// </summary>
    // --------------------------------------------------------------- snippets

    /// <summary>
    /// Fills a snippet menu from the catalogue.
    ///
    /// Called every time one opens rather than kept in step by a watcher: the catalogue
    /// only lists filenames, so this is a directory read with nothing opened, and it means
    /// a file dropped into the folder a second ago is already there.
    ///
    /// Each menu gets its own items. The same snippet appears in the toolbar dropdown and
    /// in the Format menu, and a MenuFlyoutItem cannot belong to two parents.
    ///
    /// <paramref name="withBlocks"/> adds Code Block and Table to the catalogue, which is what
    /// makes the bar's dropdown Insert rather than Snippet. They are built here rather than
    /// declared in the markup because this clears the list on every open; declared items would
    /// survive exactly once.
    ///
    /// The result is one continuous run of things to insert, broken once where the user's own
    /// files start, and once more before the folder link - which is the only entry here that
    /// inserts nothing.
    /// </summary>
    private void FillSnippetMenu(IList<MenuFlyoutItemBase> items, SnippetGroup group, bool withBlocks = false)
    {
        items.Clear();

        IReadOnlyList<Snippet> snippets = ViewModel.ListSnippets(group);

        if (snippets.Count == 0)
        {
            items.Add(new MenuFlyoutItem { Text = "No snippets", IsEnabled = false });
        }

        // Everything that ships with the app, gathered into one run before any of it is shown:
        // the two block commands and the built-in snippets together, with no rule between them.
        // The boundary a rule would draw there is one the user cannot see - Code Block emits a
        // fence and the built-in "Maths Block" emits a $$ pair, and the only difference is that
        // one is a command and the other a row in a table, which is a fact about this code
        // rather than about what is being chosen between.
        List<MenuFlyoutItem> shipped = [];

        if (withBlocks)
        {
            shipped.Add(BlockItem("Code Block", "CodeBlock", "Ctrl+Shift+K"));
            shipped.Add(BlockItem("Table", "Table", null));
        }

        foreach (Snippet snippet in snippets)
        {
            if (snippet.IsBuiltIn)
            {
                shipped.Add(SnippetItem(snippet));
            }
        }

        // Name order for the general list, and the same comparison the catalogue sorts the
        // user's files with, so the two halves of the menu read alike. It is what puts the two
        // block commands in their place among the snippets rather than pinned above them:
        // pinning would have reinstated, quietly, exactly the distinction the rule was removed
        // for. The diagrams keep the order they are written in - see BuiltInSnippets.
        if (group == SnippetGroup.General)
        {
            shipped.Sort(static (a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Text, b.Text));
        }

        foreach (MenuFlyoutItem item in shipped)
        {
            items.Add(item);
        }

        // Then the user's own, already in name order from the catalogue, under the one break
        // this menu keeps.
        //
        // Named rather than merely ruled. This is the one boundary here the user can perceive
        // - everything above ships with the app, everything below is a file in a folder they
        // own and can edit - and it is the question a bare rule was failing to answer. A
        // disabled item is the stand-in for a heading, since WinUI menus have no first-class
        // one; directly under a rule it reads as a label rather than as something unavailable,
        // and keyboard navigation steps over it either way.
        bool headed = false;

        foreach (Snippet snippet in snippets)
        {
            if (snippet.IsBuiltIn)
            {
                continue;
            }

            if (!headed)
            {
                headed = true;

                if (items.Count > 0)
                {
                    items.Add(new MenuFlyoutSeparator());
                }

                items.Add(new MenuFlyoutItem { Text = "Your snippets", IsEnabled = false });
            }

            items.Add(SnippetItem(snippet));
        }

        // Only the general list is backed by a folder, so only it gets a way in.
        if (group != SnippetGroup.General)
        {
            return;
        }

        items.Add(new MenuFlyoutSeparator());

        var open = new MenuFlyoutItem { Text = "Open Snippets Folder..." };
        open.Click += OnOpenSnippetsFolder;
        items.Add(open);
    }

    /// <summary>
    /// One entry from the catalogue. The user's own carry their path as a tooltip, which is
    /// the answer to "which file is this?" for the only entries where that can be asked.
    /// </summary>
    private MenuFlyoutItem SnippetItem(Snippet snippet)
    {
        var item = new MenuFlyoutItem { Text = snippet.Name, Tag = snippet };

        if (!snippet.IsBuiltIn)
        {
            ToolTipService.SetToolTip(item, snippet.Path);
        }

        item.Click += OnSnippetClick;

        return item;
    }

    /// <summary>
    /// One of the three block commands at the head of the Insert dropdown.
    ///
    /// Bound to ApplyMarkdownCommand with the same parameter its Format-menu twin uses, so
    /// the two cannot come to mean different things. The accelerator text is display-only,
    /// as everywhere else: the real keys are registered once, with Monaco and on the root.
    /// </summary>
    private MenuFlyoutItem BlockItem(string text, string parameter, string? accelerator)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            Command = ViewModel.ApplyMarkdownCommand,
            CommandParameter = parameter,
        };

        if (accelerator is not null)
        {
            item.KeyboardAcceleratorTextOverride = accelerator;
        }

        return item;
    }

    private void OnWindowActivatedRefresh(object sender, WindowActivatedEventArgs e)
    {
        bool active = e.WindowActivationState != WindowActivationState.Deactivated;

        // Told either way, and before the early return. Nothing used to record that the window
        // had gone, because nothing needed to; a message held back until the user comes back
        // has to know they left.
        ViewModel.SetWindowActive(active);

        if (!active)
        {
            return;
        }

        FillSnippetMenu(FormatDiagramMenu.Items, SnippetGroup.Diagram);
        FillSnippetMenu(FormatSnippetMenu.Items, SnippetGroup.General);

        ViewModel.RefreshClipboardState();
        PlaceStartupFocus();
    }

    /// <summary>Set once the documents Marqora starts with are open.</summary>
    private bool _startupDocumentsOpen;

    /// <summary>Set once the keyboard has been put in one of them, which happens once.</summary>
    private bool _startupFocusPlaced;

    /// <summary>
    /// Puts the keyboard in the document Marqora opened with.
    ///
    /// Called from both the end of startup and the window being activated, and does its work
    /// on whichever happens last — either order is possible, and neither on its own is late
    /// enough. Asking during Loaded was not: XAML gives a newly activated window its own
    /// initial focus, and the first focusable thing here is the tab strip's add button. That
    /// is where the keyboard has always ended up, and why a fresh window answered none of
    /// the shortcuts — from the add button a keystroke reaches neither the window's
    /// accelerators nor the shell's own keybindings.
    ///
    /// Queued at low priority for the last part of the same problem. Low-priority work runs
    /// after the layout and render pass, and so after the focus XAML assigns during it;
    /// asking inside that pass is asking to be overruled a moment later.
    ///
    /// Once only. Every activation after the first is the user coming back to the window,
    /// and where they left the keyboard is theirs to decide, not ours to move.
    /// </summary>
    private void PlaceStartupFocus()
    {
        if (_startupFocusPlaced || !_startupDocumentsOpen)
        {
            return;
        }

        _startupFocusPlaced = true;

        // Qualified: this file uses Windows.System for VirtualKey, and that namespace has a
        // DispatcherQueuePriority of its own that is not the one this queue takes.
        if (!DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            ViewModel.RestoreDocumentFocus))
        {
            // Shutting down, or no queue to post to. Nothing is going to need the keyboard.
            _logger.LogWarning("Could not queue the startup focus.");
        }
    }

    private void OnSnippetClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Snippet snippet })
        {
            ViewModel.InsertSnippetCommand.Execute(snippet);
        }
    }

    private async void OnOpenSnippetsFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            // Created on the way out rather than only at startup, so the first click
            // cannot land on a folder that is not there yet.
            Directory.CreateDirectory(_paths.SnippetsDirectory);

            await Launcher.LaunchFolderPathAsync(_paths.SnippetsDirectory);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not open the snippets folder.");
        }
    }

    private void RebuildRecentMenu()
    {
        RecentMenu.Items.Clear();

        if (ViewModel.RecentFiles.Count == 0)
        {
            RecentMenu.Items.Add(new MenuFlyoutItem { Text = "No recent files", IsEnabled = false });
            return;
        }

        foreach (RecentFileViewModel file in ViewModel.RecentFiles)
        {
            var item = new MenuFlyoutItem
            {
                Text = file.FileName,
                Tag = file.Path,
                IsEnabled = file.Exists,
            };

            ToolTipService.SetToolTip(item, file.Tooltip);
            item.Click += OnRecentCardClick;

            if (file.IsPinned)
            {
                // Segoe Fluent Icons "Pin", written as a code point because the glyph
                // itself lives in a private-use range that does not survive plain-text edits.
                item.Icon = new FontIcon { Glyph = char.ConvertFromUtf32(0xE718), FontSize = 14 };
            }

            RecentMenu.Items.Add(item);
        }

        RecentMenu.Items.Add(new MenuFlyoutSeparator());

        // Same command as the start page, so the two entry points cannot drift apart. The
        // ellipsis is the menu's own convention for an item that prompts before acting.
        var clear = new MenuFlyoutItem { Text = "Clear Recent Files..." };
        clear.Click += (_, _) => ViewModel.ClearRecentCommand.Execute(null);
        RecentMenu.Items.Add(clear);
    }

    private void OnRecentCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
        {
            ViewModel.OpenRecentCommand.Execute(path);
        }
    }

    private void OnTogglePinClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
        {
            ViewModel.TogglePinCommand.Execute(path);
        }
    }

    private void OnRemoveRecentClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
        {
            ViewModel.RemoveRecentCommand.Execute(path);
        }
    }

    private void OnRevealRecentClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
        {
            ViewModel.RevealRecentCommand.Execute(path);
        }
    }

    // ----------------------------------------------------- dragging the window

    /*
      The strip between the add-tab button and the caption buttons - the TabView's
      TabStripFooter, about fifty pixels of it once the caption buttons have taken their
      share - drags the window.

      Two earlier attempts are worth recording, because both look correct and neither is.

      Declaring the strip as caption through InputNonClientPointerSource makes things worse.
      SetTitleBar is implemented on that same channel, so setting caption rectangles REPLACES
      the ones it installed rather than adding to them: the branding and the gaps between the
      tabs stop dragging the moment you do it. The region map is therefore left exactly as
      SetTitleBar arranged it, and this strip - which a TabView deliberately keeps out of the
      drag region, being where interactive content is meant to go - is handled as content.

      Handing the gesture to Windows with WM_NCLBUTTONDOWN and HTCAPTION does not work either,
      though it is the usual Win32 answer. It starts the move loop on the top-level window,
      but in WinUI 3 the mouse belongs to the XAML input window underneath, so the loop
      receives nothing until the button is released - and then moves the window to wherever
      the pointer finished. The window appears to jump on mouse-up rather than follow.

      So the drag is run here: capture the pointer, and move the window by the same amount the
      cursor has moved since the press. Cursor position comes from Win32 rather than from the
      pointer event because these are screen coordinates and must not move with the window;
      a window-relative position stays roughly still while the window chases it, which reads
      as the window drifting off on its own.
    */

    /// <summary>Set between a press on the drag strip and the release that ends it.</summary>
    private bool _isDraggingWindow;

    /// <summary>Where the cursor and the window were when the drag began, in screen pixels.</summary>
    private PointInt32 _dragStartCursor;
    private PointInt32 _dragStartWindow;

    /// <summary>
    /// A press has to travel this far before it counts as a drag, so that a click that
    /// happens to wobble does not nudge the window, and the second click of a double-click
    /// still arrives as one.
    /// </summary>
    private const int DragThreshold = 4;

    private void OnTitleBarDragPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType is not Microsoft.UI.Input.PointerDeviceType.Mouse
            || !e.GetCurrentPoint((UIElement)sender).Properties.IsLeftButtonPressed
            || !GetCursorPos(out NativePoint cursor))
        {
            return;
        }

        // A maximized window has nowhere to be moved to. Windows restores it and continues
        // the drag; matching that here would need the restored size before it exists, and a
        // double-click restores it anyway.
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized })
        {
            return;
        }

        _dragStartCursor = new PointInt32(cursor.X, cursor.Y);
        _dragStartWindow = AppWindow.Position;
        _isDraggingWindow = TabStripTrailing.CapturePointer(e.Pointer);

        // Deliberately not marked handled: DoubleTapped is built out of these events, and
        // swallowing the press would take maximize-on-double-click with it.
    }

    private void OnTitleBarDragPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingWindow || !GetCursorPos(out NativePoint cursor))
        {
            return;
        }

        int dx = cursor.X - _dragStartCursor.X;
        int dy = cursor.Y - _dragStartCursor.Y;

        if (Math.Abs(dx) < DragThreshold && Math.Abs(dy) < DragThreshold)
        {
            return;
        }

        AppWindow.Move(new PointInt32(_dragStartWindow.X + dx, _dragStartWindow.Y + dy));
        e.Handled = true;
    }

    private void OnTitleBarDragPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingWindow)
        {
            return;
        }

        _isDraggingWindow = false;
        TabStripTrailing.ReleasePointerCapture(e.Pointer);
    }

    /// <summary>
    /// Capture can be taken away - by another window activating, or the session locking -
    /// and the drag has to end with it or the next move would jump the window.
    /// </summary>
    private void OnTitleBarDragPointerCaptureLost(object sender, PointerRoutedEventArgs e) =>
        _isDraggingWindow = false;

    /// <summary>
    /// Double-click maximizes or restores, which is the other half of what a caption does.
    /// </summary>
    private void OnTitleBarDragDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter || !presenter.IsMaximizable)
        {
            return;
        }

        e.Handled = true;

        if (presenter.State == OverlappedPresenterState.Maximized)
        {
            presenter.Restore();
        }
        else
        {
            presenter.Maximize();
        }
    }

    // ------------------------------------------------------------------- interop

    /// <summary>
    /// DllImport rather than the source-generated LibraryImport: the generator emits unsafe
    /// marshalling code, which would mean enabling AllowUnsafeBlocks across the project for
    /// one call that passes nothing but a handle.
    /// </summary>
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// The cursor in screen pixels, which is the space AppWindow.Position is in. The pointer
    /// event reports a position relative to the window, and that one barely changes while the
    /// window is following the pointer.
    /// </summary>
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
