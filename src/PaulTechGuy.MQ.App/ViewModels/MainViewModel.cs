// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Analysis;
using PaulTechGuy.MQ.Abstractions.Editing;
using PaulTechGuy.MQ.Abstractions.Formatting;
using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.App.Services;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Finding;
using Windows.ApplicationModel.DataTransfer;

namespace PaulTechGuy.MQ.App.ViewModels;

/// <summary>
/// Drives the main window.
///
/// It owns no file or rendering logic of its own: it coordinates the workspace, settings and
/// recent-file services, renders through <see cref="IMarkdownRenderer"/>, and pushes results
/// at whatever <see cref="IPreviewHost"/> is attached.
///
/// <see cref="Tabs"/> mirrors the workspace's document list. TabView both reads and writes
/// that collection, because drag-reordering moves items in the bound source directly.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IWorkspaceService _workspace;
    private readonly ISettingsService _settings;
    private readonly IRecentFilesService _recent;
    private readonly IMarkdownRenderer _renderer;
    private readonly IFileDialogService _fileDialogs;
    private readonly IDialogService _dialogs;
    private readonly IThemeService _themeService;
    private readonly IUiDispatcher _ui;
    private readonly IHtmlExporter _exporter;
    private readonly RenderedHtmlPackager _packager;
    private readonly IExportDialogService _exportDialogs;
    private readonly IPrintDialogService _printDialogs;
    private readonly IMarkdownFormatter _formatter;
    private readonly IMarkdownEditor _editor;
    private readonly IMarkdownAnalyzer _analyzer;
    private readonly ISnippetCatalog _snippets;
    private readonly IFormatDialogService _formatDialogs;
    private readonly IPreferencesDialogService _preferencesDialogs;
    private readonly ICheatsheetService _cheatsheet;
    private readonly IDiagramWindowService _diagramWindows;
    private readonly IFindAllWindowService _findAll;
    private readonly IWelcomeDocumentService _welcome;
    private readonly ILogger<MainViewModel> _logger;

    private IPreviewHost? _host;

    /// <summary>
    /// The welcome document waiting to be opened, or null when this version has already shown
    /// it. Decided during <see cref="InitializeAsync"/> - before the session is restored, so
    /// that a restored copy of it holds this release's text - and acted on afterwards by
    /// <see cref="ShowWelcomeAsync"/>.
    /// </summary>
    private string? _welcomePath;

    /// <summary>Set while this class is reordering Tabs, so the change is not echoed back.</summary>
    private bool _isSyncingTabs;

    // Plain fields behind CanUndo and CanRedo rather than observable properties, because
    // neither is the whole answer on its own: what the shell reports is true only while a
    // document is open, and a closed tab takes its undo stack with it without the shell
    // having any caret left to say so from.
    private bool _shellCanUndo;
    private bool _shellCanRedo;

    // Partial properties rather than annotated fields: the generated WinRT marshalling is
    // correct for XAML binding, and it is the form the toolkit now expects. Partial
    // properties cannot carry initializers, so defaults are set in the constructor.

    // Save All is deliberately absent from this list: whether any tab is dirty is
    // HasDirtyTabs's question, and it is recomputed beside HasDocument on every change.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveAsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReloadFromDiskCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseTabCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseOtherTabsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseAllTabsCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevealInFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyPathCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportHtmlCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyAsRichTextCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportPdfCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand))]
    [NotifyCanExecuteChangedFor(nameof(FormatDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(FormatAllDocumentsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyMarkdownCommand))]
    [NotifyPropertyChangedFor(nameof(CanFormat))]
    [NotifyPropertyChangedFor(nameof(CanUndo))]
    [NotifyPropertyChangedFor(nameof(CanRedo))]
    public partial bool HasDocument { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReloadFromDiskCommand))]
    public partial bool IsDirty { get; set; }

    /// <summary>
    /// Whether any open tab holds unsaved work, which is the whole of Save All's answer: it
    /// writes every dirty document, so which tab is being looked at has nothing to do with it.
    ///
    /// A flag recomputed on each workspace change rather than a walk of the document list on
    /// demand, because CanExecute is asked far more often than the answer moves.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveAllCommand))]
    public partial bool HasDirtyTabs { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    public partial string DocumentName { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReloadFromDiskCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevealInFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyPathCommand))]
    public partial string DocumentPath { get; set; }

    /// <summary>
    /// Where the active document stands with the file behind it.
    ///
    /// Held here so Reload from Disk can tell its three cases apart: nothing to reload
    /// (in sync), something worth reloading (changed), and nothing left to reload from
    /// (missing). The tab strip reads the same state from its own document snapshot.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReloadFromDiskCommand))]
    public partial ExternalState ActiveExternalState { get; set; }

    /// <summary>
    /// Whether the clipboard holds text right now, which is all New from Clipboard can act on.
    ///
    /// Pushed in by the shell rather than read on demand: CanExecute is asked on every menu
    /// open and every command notification, and opening the clipboard that often is both slow
    /// and rude to whatever else is using it. See <see cref="RefreshClipboardState"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NewFromClipboardCommand))]
    public partial bool ClipboardHasText { get; set; }

    /// <summary>The selected tab. TabView binds this two-way.</summary>
    [ObservableProperty]
    public partial DocumentTabViewModel? ActiveTab { get; set; }

    /// <summary>
    /// Only the active tab shows a close button, so the flag has to move with the selection.
    /// Generated by the toolkit and called on every change to <see cref="ActiveTab"/>.
    /// </summary>
    partial void OnActiveTabChanged(DocumentTabViewModel? oldValue, DocumentTabViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsActive = false;
        }

        if (newValue is not null)
        {
            newValue.IsActive = true;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSourceView))]
    [NotifyPropertyChangedFor(nameof(IsPreviewView))]
    [NotifyPropertyChangedFor(nameof(IsSplitView))]
    [NotifyPropertyChangedFor(nameof(CanFormat))]
    [NotifyCanExecuteChangedFor(nameof(ApplyMarkdownCommand))]
    public partial ViewMode ViewMode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSystemTheme))]
    [NotifyPropertyChangedFor(nameof(IsLightTheme))]
    [NotifyPropertyChangedFor(nameof(IsDarkTheme))]
    public partial AppTheme Theme { get; set; }

    [ObservableProperty]
    public partial bool ScrollSyncEnabled { get; set; }

    [ObservableProperty]
    public partial bool WordWrapEnabled { get; set; }

    [ObservableProperty]
    public partial bool LineNumbersEnabled { get; set; }

    [ObservableProperty]
    public partial bool ShowWhitespaceEnabled { get; set; }

    [ObservableProperty]
    public partial bool ShowWrapGlyphEnabled { get; set; }

    [ObservableProperty]
    public partial bool DiagnosticsEnabled { get; set; }

    // What the formatting toolbar shows. Each of these says what its button would *do*
    // rather than what the text *is* -- see MarkdownMarkState for why the distinction
    // matters. They are pushed from the shell as the caret moves.

    [ObservableProperty]
    public partial bool IsBoldActive { get; set; }

    [ObservableProperty]
    public partial bool IsItalicActive { get; set; }

    [ObservableProperty]
    public partial bool IsStrikethroughActive { get; set; }

    [ObservableProperty]
    public partial bool IsInlineCodeActive { get; set; }

    [ObservableProperty]
    public partial bool IsBulletListActive { get; set; }

    [ObservableProperty]
    public partial bool IsNumberedListActive { get; set; }

    [ObservableProperty]
    public partial bool IsTaskListActive { get; set; }

    [ObservableProperty]
    public partial bool IsBlockquoteActive { get; set; }

    /// <summary>
    /// The heading dropdown's caption: the level the selection already is, or the plain
    /// word when it is not a heading or the lines disagree.
    /// </summary>
    [ObservableProperty]
    public partial string HeadingLabel { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomLabel))]
    public partial int ActiveZoomPercent { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomLabel))]
    public partial EditorPane ActivePane { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; }

    /// <summary>
    /// The long form of <see cref="StatusText"/>, shown as a tooltip on it, or empty.
    ///
    /// Deliberately general rather than tied to any one message: the status bar has room for
    /// a sentence and no more, and this is where a message puts what would not fit. Cleared
    /// automatically whenever <see cref="StatusText"/> is replaced, so a stale detail cannot
    /// end up explaining a message it has nothing to do with.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusDetail))]
    public partial string StatusDetail { get; set; }

    public bool HasStatusDetail => !string.IsNullOrEmpty(StatusDetail);

    /// <summary>
    /// The glyph in front of the status message, or empty for the ordinary messages that have
    /// none.
    ///
    /// It outlives the highlight on purpose. If the icon went at the same moment the pill did,
    /// the text would slide left by the width of the icon while the user was reading it, for
    /// no reason they could see - a worse interruption than the one the highlight was for. It
    /// goes when the message itself is replaced, and by then the text has changed anyway, so
    /// nothing appears to move.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusIcon))]
    public partial string StatusIconGlyph { get; set; }

    public bool HasStatusIcon => !string.IsNullOrEmpty(StatusIconGlyph);

    /// <summary>
    /// Whether the status message is currently wearing its highlight.
    ///
    /// For the messages that report something the user did not ask for and gets told about
    /// only once. Plain caption text in the corner of the window is not enough for those -
    /// it has to survive being walked away from and still be noticed on the way back.
    /// </summary>
    [ObservableProperty]
    public partial bool IsStatusHighlighted { get; set; }

    [ObservableProperty]
    public partial int WordCount { get; set; }

    [ObservableProperty]
    public partial int CharacterCount { get; set; }

    [ObservableProperty]
    public partial int CursorLine { get; set; }

    [ObservableProperty]
    public partial int CursorColumn { get; set; }

    /// <summary>True while a file is being dragged over the window, so the view can react.</summary>
    [ObservableProperty]
    public partial bool IsDragOver { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Reload a document automatically when its file changes and it holds no edits.</summary>
    [ObservableProperty]
    public partial bool ReloadOnExternalChangeEnabled { get; set; }

    /// <summary>
    /// What the change banner is showing.
    ///
    /// Only ever describes the active tab. Documents waiting behind it keep their tab markers
    /// and are counted in the status bar; each gets the banner as it is opened, which is what
    /// stops a branch switch from producing a queue of prompts.
    ///
    /// Never null - it holds <see cref="ExternalChangeNotice.None"/> while the banner is shut,
    /// so no binding in the markup walks through a null.
    /// </summary>
    [ObservableProperty]
    public partial ExternalChangeNotice ExternalNotice { get; set; }

    /// <summary>Whether the change banner is open. Separate from the notice it is showing.</summary>
    [ObservableProperty]
    public partial bool HasExternalNotice { get; set; }

    /// <summary>
    /// "3 other files changed on disk", or empty. Stands in the status bar for the tabs whose
    /// files changed while the user was looking at a different one.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExternalPending))]
    public partial string ExternalPendingSummary { get; set; }

    public bool HasExternalPending => !string.IsNullOrEmpty(ExternalPendingSummary);

    /// <summary>
    /// Documents whose files changed underneath them, in the order the changes arrived.
    ///
    /// A list rather than a set: the banner's "1 of 4" counts a position in it, and that
    /// position should not jump around as tabs are visited.
    /// </summary>
    private readonly List<Guid> _pendingExternal = [];

    /// <summary>
    /// Tabs the user has waved away with the banner's close button.
    ///
    /// Kept apart from <see cref="_pendingExternal"/> so that dismissing is not the same as
    /// resolving: the tab marker stays, and the banner comes back the next time the tab is
    /// activated. Nothing is lost by clicking ✕.
    /// </summary>
    private readonly HashSet<Guid> _dismissedExternal = [];

    /// <summary>
    /// Documents whose files were reloaded without asking, and who have not been told yet.
    ///
    /// A clean buffer whose file changes is replaced in silence - which is the right thing to
    /// do, and the reason it needs saying afterwards. The user finds out when they arrive at
    /// the document: either it is the one already in front of them, or they switch to it. An
    /// id leaves this set the moment it is announced, so one reload is announced exactly once
    /// however they get there.
    ///
    /// Session state, not document state. Restarting reads every file fresh, so a reload
    /// remembered from last Tuesday would describe nothing.
    /// </summary>
    private readonly HashSet<Guid> _unannouncedReloads = [];

    /// <summary>How long the status highlight stays up once the user is there to see it.</summary>
    private static readonly TimeSpan StatusHighlightDuration = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Whether the window has the user's attention, as reported by the window itself.
    ///
    /// Assumed true until told otherwise: announcing a message nobody was there for is a much
    /// smaller failure than holding one forever because the first notification never arrived.
    /// </summary>
    private bool _windowIsActive = true;

    private CancellationTokenSource? _highlightExpiry;
    private DateTimeOffset _highlightResumedUtc;
    private TimeSpan _highlightRemaining;

    public MainViewModel(
        IWorkspaceService workspace,
        ISettingsService settings,
        IRecentFilesService recent,
        IMarkdownRenderer renderer,
        IFileDialogService fileDialogs,
        IDialogService dialogs,
        IThemeService theme,
        IUiDispatcher ui,
        IHtmlExporter exporter,
        RenderedHtmlPackager packager,
        IExportDialogService exportDialogs,
        IPrintDialogService printDialogs,
        IMarkdownFormatter formatter,
        IMarkdownEditor editor,
        IMarkdownAnalyzer analyzer,
        ISnippetCatalog snippets,
        IFormatDialogService formatDialogs,
        IPreferencesDialogService preferencesDialogs,
        ICheatsheetService cheatsheet,
        IDiagramWindowService diagramWindows,
        IFindAllWindowService findAll,
        IWelcomeDocumentService welcome,
        ILogger<MainViewModel> logger)
    {
        _workspace = workspace;
        _settings = settings;
        _recent = recent;
        _renderer = renderer;
        _fileDialogs = fileDialogs;
        _dialogs = dialogs;
        _themeService = theme;
        _ui = ui;
        _exporter = exporter;
        _packager = packager;
        _exportDialogs = exportDialogs;
        _printDialogs = printDialogs;
        _formatter = formatter;
        _editor = editor;
        _analyzer = analyzer;
        _snippets = snippets;
        _formatDialogs = formatDialogs;
        _preferencesDialogs = preferencesDialogs;
        _cheatsheet = cheatsheet;
        _diagramWindows = diagramWindows;
        _findAll = findAll;
        _welcome = welcome;
        _logger = logger;

        DocumentName = string.Empty;
        DocumentPath = string.Empty;

        // Starts true, and the shell corrects it the moment the window is first activated.
        // The optimistic default is the safe one: an offered command that reports an empty
        // clipboard is a smaller failure than a greyed-out one that would have worked.
        ClipboardHasText = true;

        ExternalNotice = ExternalChangeNotice.None;
        ExternalPendingSummary = string.Empty;
        StatusText = "Ready";
        StatusDetail = string.Empty;
        StatusIconGlyph = string.Empty;
        HeadingLabel = "Heading";
        ViewMode = ViewMode.SideBySide;
        Theme = AppTheme.System;
        ScrollSyncEnabled = true;
        WordWrapEnabled = true;
        LineNumbersEnabled = true;
        ActiveZoomPercent = ZoomLevel.Default;

        // Where the keyboard goes until the user has been somewhere themselves. Every focus
        // restore asks this - the startup one included - and the source pane is the only
        // answer that puts a caret on screen: the preview is a focusable article with no
        // caret in it, so starting there looks exactly like focus having gone nowhere, and
        // stays that way, because nothing moves this until the editor is clicked in.
        //
        // Only decides anything side by side. With one pane showing, the shell overrides it
        // with the pane that is actually there - see focusPane in app.js - so the welcome
        // document still opens in the preview it asks for.
        ActivePane = EditorPane.Source;
        CursorLine = 1;
        CursorColumn = 1;

        _workspace.Changed += OnWorkspaceChanged;
        _recent.Changed += OnRecentFilesChanged;
        _themeService.EffectiveThemeChanged += OnEffectiveThemeChanged;
        _cheatsheet.VisibilityChanged += (_, visible) => IsCheatsheetVisible = visible;
        _diagramWindows.OpenCountChanged += (_, count) => OpenDiagramWindowCount = count;
        _findAll.MatchActivated += OnFindMatchActivated;

        Tabs.CollectionChanged += OnTabsCollectionChanged;
    }

    public ObservableCollection<DocumentTabViewModel> Tabs { get; } = [];

    public ObservableCollection<RecentFileViewModel> RecentFiles { get; } = [];

    public bool HasRecentFiles => RecentFiles.Count > 0;

    public string WindowTitle =>
        HasDocument ? $"{(IsDirty ? "* " : string.Empty)}{DocumentName} - Marqora" : "Marqora";

    public bool IsSourceView => ViewMode == ViewMode.Source;

    public bool IsPreviewView => ViewMode == ViewMode.Preview;

    public bool IsSplitView => ViewMode == ViewMode.SideBySide;

    /// <summary>
    /// Whether the markdown commands apply right now: a document is open and the source
    /// pane is on screen to apply them to.
    ///
    /// Split view counts. What matters is that the pane is visible, not that it holds
    /// focus — clicking into the preview to scroll must not disable the toolbar, or it
    /// would flicker every time the user moved between panes.
    /// </summary>
    public bool CanFormat => HasDocument && ViewMode is not ViewMode.Preview;

    /// <summary>
    /// Whether Undo has anything to take back, for the toolbar button and its Edit-menu
    /// twin. Reported by the shell as the document changes, and gated on a document being
    /// open at all: closing the last tab disposes the model and its history with it, which
    /// leaves the shell with no caret to report from and would otherwise strand this true.
    /// </summary>
    public bool CanUndo => _shellCanUndo && HasDocument;

    /// <summary>The same, for Redo. See <see cref="CanUndo"/>.</summary>
    public bool CanRedo => _shellCanRedo && HasDocument;

    public bool IsSystemTheme => Theme == AppTheme.System;

    public bool IsLightTheme => Theme == AppTheme.Light;

    public bool IsDarkTheme => Theme == AppTheme.Dark;

    public string ZoomLabel => $"{ActiveZoomPercent}%";

    /// <summary>Raised when the user chooses File, Exit.</summary>
    public event EventHandler? ExitRequested;

    /// <summary>Raised when the user chooses Help, About.</summary>
    public event EventHandler? AboutRequested;

    /// <summary>Raised when the user chooses Help, Support the Project.</summary>
    public event EventHandler? SupportRequested;

    /// <summary>
    /// A menu should be opened from the keyboard. Raised only for the shell's own
    /// forwarding, because MenuBarItem belongs to the window rather than to this class.
    /// </summary>
    public event EventHandler<string>? MenuRequested;

    // ------------------------------------------------------------------ startup

    /// <summary>Loads persisted state. Called once, before the preview host is attached.</summary>
    public async Task InitializeAsync()
    {
        await _settings.InitializeAsync().ConfigureAwait(true);
        await _recent.InitializeAsync().ConfigureAwait(true);
        await _recent.PruneMissingAsync().ConfigureAwait(true);

        AppSettings current = _settings.Current;

        ViewMode = current.ViewMode;
        Theme = current.Theme;
        ScrollSyncEnabled = current.ScrollSyncEnabled;
        WordWrapEnabled = current.WordWrapEnabled;
        LineNumbersEnabled = current.ShowLineNumbers;
        ShowWhitespaceEnabled = current.ShowWhitespace;
        DiagnosticsEnabled = current.ShowDiagnostics;
        ShowWrapGlyphEnabled = current.ShowWrapGlyph;
        ReloadOnExternalChangeEnabled = current.ReloadOnExternalChange;
        ActiveZoomPercent = current.PreviewZoomPercent;

        _themeService.Apply(current.Theme);

        RefreshRecentFiles();

        // Before the session is restored rather than after it. The welcome document may
        // already be among the tabs to reopen, and refreshing the file underneath a tab that
        // had just loaded the previous release's copy would either flash a reload past the
        // user or, with unsaved edits in it, stop to ask about a document they did not write.
        _welcomePath = await _welcome.PrepareAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Connects the WebView bridge. Everything the shell needs is pushed once it reports
    /// ready, so state set before the page loaded is not lost.
    /// </summary>
    public void AttachPreviewHost(IPreviewHost host)
    {
        _host = host;

        host.Ready += OnHostReady;
        host.FontsResolved += (_, _) => FontsResolved?.Invoke(this, EventArgs.Empty);
        host.EditorTextChanged += OnEditorTextChanged;
        host.ZoomChanged += OnHostZoomChanged;
        host.SplitterMoved += OnSplitterMoved;
        host.CommandInvoked += OnHostCommand;
        host.ExternalLinkActivated += OnExternalLinkActivated;
        host.SelectionCopied += OnSelectionCopied;
    }

    private async void OnHostReady(object? sender, EventArgs e)
    {
        try
        {
            await PushAllStateAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize the preview shell.");
        }
    }

    private async Task PushAllStateAsync()
    {
        if (_host is null)
        {
            return;
        }

        AppSettings current = _settings.Current;

        await _host.SetThemeAsync(_themeService.Effective).ConfigureAwait(true);
        await _host.SetViewModeAsync(ViewMode).ConfigureAwait(true);
        await _host.SetScrollSyncAsync(ScrollSyncEnabled).ConfigureAwait(true);
        await _host.SetWordWrapAsync(WordWrapEnabled).ConfigureAwait(true);
        await _host.SetLineNumbersAsync(LineNumbersEnabled).ConfigureAwait(true);
        await _host.SetShowWhitespaceAsync(ShowWhitespaceEnabled).ConfigureAwait(true);
        await _host.SetWrapGlyphAsync(ShowWrapGlyphEnabled).ConfigureAwait(true);
        await _host.ApplyPreferencesAsync(PreviewPreferences.FromSettings(current)).ConfigureAwait(true);
        await _host.SetSplitterPositionAsync(current.SplitterPosition).ConfigureAwait(true);
        await _host.SetZoomAsync(EditorPane.Source, new ZoomLevel(current.SourceZoomPercent)).ConfigureAwait(true);
        await _host.SetZoomAsync(EditorPane.Preview, new ZoomLevel(current.PreviewZoomPercent)).ConfigureAwait(true);

        // Documents opened before the shell was ready still need their tabs created.
        foreach (MarkdownDocument document in _workspace.Documents)
        {
            RenderedMarkdown rendered = await RenderAsync(document.Text).ConfigureAwait(true);
            await _host.OpenTabAsync(document.Id, document.Text, rendered).ConfigureAwait(true);
            await PublishDiagnosticsAsync(document.Id, document.Text, document.Path, rendered).ConfigureAwait(true);
        }

        if (_workspace.Active is { } active)
        {
            await _host.ActivateTabAsync(active.Id, active.Path).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Reopens the previous session. Called once, after the host is attached.
    ///
    /// Skipped entirely when the user has asked for something else at startup. The document
    /// list is still written on the way out either way, so switching the preference back
    /// picks the session up again rather than starting from an empty one.
    /// </summary>
    public async Task RestoreSessionAsync()
    {
        AppSettings current = _settings.Current;

        if (current.Startup != StartupBehavior.RestoreSession)
        {
            return;
        }

        IReadOnlyList<string> paths = current.DocumentsToRestore;

        if (paths.Count == 0)
        {
            return;
        }

        await _workspace.RestoreAsync(paths, current.ActiveDocumentIndex).ConfigureAwait(true);
    }

    /// <summary>
    /// Whatever the startup preference asks for beyond the restored session, once the files
    /// named on the command line have been opened.
    ///
    /// Runs last so it cannot take the front tab from a file the user actually double-clicked,
    /// and does nothing at all on the default setting.
    /// </summary>
    public async Task ApplyStartupBehaviorAsync(bool openedFromCommandLine)
    {
        switch (_settings.Current.Startup)
        {
            case StartupBehavior.EmptyTab when !HasDocument:
                // Only when nothing else got there first. A command-line file is what the
                // user asked for, and a blank tab beside it would be clutter.
                _workspace.CreateUntitled();
                break;

            case StartupBehavior.WelcomeDocument when !openedFromCommandLine:
                await OpenWelcomeAtStartupAsync().ConfigureAwait(true);
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Opens the welcome document as an ordinary file, for the startup preference.
    ///
    /// Deliberately not ShowWelcomeAsync: that refreshes the copy from the shipped master as
    /// it opens it, which is right once per release and wrong for a document somebody has
    /// chosen to see every day - it would throw away their edits on each launch.
    /// </summary>
    private async Task OpenWelcomeAtStartupAsync()
    {
        string path = _welcome.DocumentPath;

        // Already in front, because this release's introduction has just been shown or the
        // restored session had it open. Opening it twice would only make a duplicate tab.
        if (_workspace.Documents.Any(d =>
            string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            await _workspace.OpenAsync(path).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A startup preference that cannot be honoured is not worth a dialog in front of
            // an app that has otherwise started perfectly well.
            _logger.LogWarning(ex, "Could not open the welcome document at startup.");
        }
    }

    /// <summary>
    /// True when this launch held Shift and is asking for the welcome document outright.
    /// The shell reads it to decide what opens last, which is what decides the active tab.
    /// </summary>
    public bool WelcomeWasRequested => _welcome.WasRequested;

    /// <summary>
    /// Opens the welcome document, on the first launch of each new release. Does nothing on
    /// every other launch. Called after the previous session has been restored, so it lands
    /// as the last tab and the one in front.
    /// </summary>
    /// <param name="takeFocus">
    /// Whether this document is what the user should be looking at. False when files were
    /// named on the command line: the document they double-clicked is what they asked for, so
    /// it opens afterwards and keeps both the focus and the view mode. The welcome document is
    /// still opened, in a tab beside it.
    ///
    /// A launch that held Shift overrides this. That is someone asking for the document in as
    /// many words, and an explicit request outranks a guess about which tab matters.
    /// </param>
    public async Task ShowWelcomeAsync(bool takeFocus)
    {
        if (_welcomePath is not { } path)
        {
            return;
        }

        takeFocus |= _welcome.WasRequested;

        // Once per install of a version, whether or not the tab could be opened. Retrying on
        // the next launch would mean a document that could not be read reappearing forever.
        _welcomePath = null;

        await OpenPathAsync(path).ConfigureAwait(true);

        if (takeFocus && _workspace.Active?.Path is { } opened
            && string.Equals(opened, path, StringComparison.OrdinalIgnoreCase))
        {
            // Preview only, because this document is written to be read rather than edited.
            // Deliberately not persisted: the view mode is the user's, and a choice the app
            // made on their behalf should not survive into their next session.
            await ApplyViewModeAsync(ViewMode.Preview, persist: false, takeFocus: false).ConfigureAwait(true);
        }
    }

    // ------------------------------------------------------------------ opening

    [RelayCommand]
    private async Task OpenAsync()
    {
        string? path = await _fileDialogs.PickOpenFileAsync().ConfigureAwait(true);

        if (!string.IsNullOrWhiteSpace(path))
        {
            await OpenPathAsync(path).ConfigureAwait(true);
        }

        // On a cancelled picker too: nothing opened, but the menu that was walked to get
        // here still has the keyboard and will not give it back on its own.
        RestoreDocumentFocusAfterChrome();
    }

    [RelayCommand]
    private Task OpenRecentAsync(string? path) =>
        string.IsNullOrWhiteSpace(path) ? Task.CompletedTask : OpenPathAsync(path);

    /// <summary>
    /// Above this many files, opening a folder asks first. Each tab carries an editor model,
    /// so a large folder is a real cost rather than a cosmetic one.
    /// </summary>
    private const int ManyFilesThreshold = 25;

    /// <summary>Opens every markdown file directly inside a chosen folder, each in its own tab.</summary>
    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        await OpenChosenFolderAsync().ConfigureAwait(true);

        // Every way out of the folder route ends here, including the several that open
        // nothing at all - a cancelled picker, an unreadable folder, a folder with no
        // markdown in it, a declined "open all of them".
        RestoreDocumentFocusAfterChrome();
    }

    private async Task OpenChosenFolderAsync()
    {
        string? folder = await _fileDialogs.PickFolderAsync().ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        IReadOnlyList<string> files;

        try
        {
            files = MarkdownFileTypes.EnumerateInFolder(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not read {Folder}.", folder);
            await _dialogs.ShowMessageAsync("Could not read folder", ex.Message).ConfigureAwait(true);
            return;
        }

        if (files.Count == 0)
        {
            await _dialogs.ShowMessageAsync(
                "No markdown files",
                $"{Path.GetFileName(folder)} contains no markdown files.\n\nMarqora looks for "
                + string.Join(", ", MarkdownFileTypes.FolderExtensions)
                + " directly inside the folder, not in subfolders.")
                .ConfigureAwait(true);
            return;
        }

        if (files.Count > ManyFilesThreshold)
        {
            ConfirmResult confirm = await _dialogs.ConfirmAsync(
                "Open every file?",
                $"{Path.GetFileName(folder)} contains {files.Count} markdown files. "
                + "Opening them all will create that many tabs.",
                primaryText: $"Open all {files.Count}").ConfigureAwait(true);

            if (confirm != ConfirmResult.Primary)
            {
                return;
            }
        }

        await OpenManyAsync(files, $"Opened {files.Count} files from {Path.GetFileName(folder)}")
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Opens a batch of files, keeping the first one active. Without that the last file
    /// opened would win, which is not what a folder full of documents should land on.
    /// </summary>
    private async Task OpenManyAsync(IReadOnlyList<string> paths, string status)
    {
        Guid firstId = Guid.Empty;

        try
        {
            IsBusy = true;

            foreach (string path in paths)
            {
                try
                {
                    MarkdownDocument document = await _workspace.OpenAsync(path).ConfigureAwait(true);

                    if (firstId == Guid.Empty)
                    {
                        firstId = document.Id;
                    }

                    await _recent.AddAsync(path).ConfigureAwait(true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // One unreadable file should not abandon the rest of the batch.
                    _logger.LogWarning(ex, "Skipped {Path} while opening a batch.", path);
                }
            }
        }
        finally
        {
            IsBusy = false;
        }

        if (firstId != Guid.Empty)
        {
            _workspace.Activate(firstId);
        }

        StatusText = status;

        // Once for the batch, not once per file: the restore chains behind the activation
        // above, and the whole point of that activation is that the first file is the one
        // the keyboard should land in. See OpenPathAsync for why this is needed at all.
        RestoreDocumentFocusAfterChrome();
    }

    /// <summary>
    /// Adds an empty in-memory document as a new tab, ready to be typed into.
    ///
    /// The restore is queued rather than awaited: creating the document raises the workspace
    /// changes that open and activate it, and RestoreDocumentFocus chains itself behind them,
    /// so the shell is told to focus the tab only once it has been given it. It belongs here
    /// rather than on the add button, because the same command is on the File menu and on
    /// Ctrl+N, and only one of those three surfaces used to hand the keyboard back.
    /// </summary>
    [RelayCommand]
    private void NewTab()
    {
        _workspace.CreateUntitled();
        StatusText = "New document";

        RestoreDocumentFocusAfterChrome();
    }

    /// <summary>
    /// Opens the clipboard as a new document and puts the keyboard in it - whether or not
    /// there turned out to be anything to open. A command that decided to do nothing has
    /// still taken focus from the menu it was picked from, so it owes it back.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanNewFromClipboard))]
    private async Task NewFromClipboardAsync()
    {
        await CreateFromClipboardAsync().ConfigureAwait(true);

        RestoreDocumentFocusAfterChrome();
    }

    /// <summary>
    /// Opens whatever text is on the clipboard as a new untitled document.
    ///
    /// The text is taken as it stands. Copying from a rendered page therefore gives its
    /// visible text rather than markdown; turning HTML back into markdown is a different
    /// feature and a heavier one.
    ///
    /// Nothing is created when there is nothing to paste. A blank tab would be indis-
    /// tinguishable from New document, which is not what was asked for, so the reason is
    /// reported instead - the command is gated on <see cref="ClipboardHasText"/>, but that
    /// is a snapshot, and the clipboard can empty between the menu opening and the click.
    /// </summary>
    private async Task CreateFromClipboardAsync()
    {
        string text;

        try
        {
            DataPackageView view = Clipboard.GetContent();

            if (!view.Contains(StandardDataFormats.Text))
            {
                StatusText = "The clipboard has no text to open";
                return;
            }

            text = await view.GetTextAsync();
        }
        catch (Exception ex)
        {
            // The clipboard is shared and can be held open by another process.
            _logger.LogWarning(ex, "Could not read the clipboard.");
            StatusText = "The clipboard could not be read";
            return;
        }

        if (string.IsNullOrEmpty(text))
        {
            StatusText = "The clipboard has no text to open";
            return;
        }

        _workspace.CreateUntitled(text);

        _logger.LogInformation("Created a document from {Length} characters of clipboard text.", text.Length);
        StatusText = "New document from clipboard";
    }

    private bool CanNewFromClipboard() => ClipboardHasText;

    /// <summary>
    /// Re-reads whether the clipboard holds text, for <see cref="ClipboardHasText"/>.
    ///
    /// Called when the window is activated and whenever Windows reports the clipboard has
    /// changed, which between them cover both ways it moves: copied elsewhere and switched
    /// back to, or copied from a pane of this window.
    ///
    /// Only the format list is asked for, never the text itself. Contains is a peek at the
    /// formats on offer; GetTextAsync would make whatever is holding the clipboard render its
    /// content, which is far too much work to do on every alt-tab.
    /// </summary>
    public void RefreshClipboardState() => _ui.Post(() =>
    {
        try
        {
            ClipboardHasText = Clipboard.GetContent().Contains(StandardDataFormats.Text);
        }
        catch (Exception ex)
        {
            // Shared resource: another process can have it open, and a clipboard that cannot
            // be read is not a clipboard that is empty. Left as it was rather than guessed at.
            _logger.LogWarning(ex, "Could not check the clipboard for text.");
        }
    });

    /// <summary>
    /// Opens a file, reporting failures to the user rather than throwing. Called from the
    /// menu, the recent list, drag and drop, and the command line.
    /// </summary>
    public async Task OpenPathAsync(string path)
    {
        if (!File.Exists(path))
        {
            await _dialogs.ShowMessageAsync(
                "File not found",
                $"{path} is no longer there. It has been removed from the recent list.")
                .ConfigureAwait(true);

            await _recent.RemoveAsync(path).ConfigureAwait(true);

            // Nothing opened, but the gesture is over and the keyboard is still on whatever
            // made it. See the finally below.
            RestoreDocumentFocusAfterChrome();
            return;
        }

        try
        {
            IsBusy = true;

            MarkdownDocument document = await _workspace.OpenAsync(path).ConfigureAwait(true);
            await _recent.AddAsync(path).ConfigureAwait(true);

            StatusText = $"Opened {document.DisplayName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not open {Path}.", path);
            await _dialogs.ShowMessageAsync("Could not open file", ex.Message).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;

            // Opening a document puts the keyboard in it, whichever surface asked. Nothing
            // else does it for them: a MenuBarItem takes focus back from its own flyout as
            // that flyout closes, so File, Open Recent used to leave the arrow keys walking
            // the menu bar rather than the text. A start page card and a drop keep focus in
            // the same way. Queued behind the workspace chain rather than sent now, so it
            // lands after the shell has been given the tab it is being asked to focus.
            RestoreDocumentFocusAfterChrome();
        }
    }

    /// <summary>
    /// Opens files named on the command line, whether they arrived with this launch or were
    /// redirected here by a later one.
    ///
    /// A single file takes the same route as the menu, so a file that has since been deleted
    /// still says so; a batch - which is what selecting several files in Explorer produces -
    /// is opened the way a drop is, skipping quietly over anything unreadable.
    /// </summary>
    public Task OpenActivatedAsync(IReadOnlyList<string> paths) => paths.Count switch
    {
        0 => Task.CompletedTask,
        1 => OpenPathAsync(paths[0]),
        _ => OpenManyAsync(paths, $"Opened {paths.Count} files"),
    };

    /// <summary>
    /// Opens everything supported in a drop, each in its own tab. A dropped folder is
    /// expanded the same way File, Open Folder expands one, so both gestures agree.
    /// </summary>
    public async Task OpenDroppedAsync(IReadOnlyList<string> paths)
    {
        IsDragOver = false;

        List<string> supported = [];

        foreach (string path in paths)
        {
            if (Directory.Exists(path))
            {
                try
                {
                    supported.AddRange(MarkdownFileTypes.EnumerateInFolder(path));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Could not read the dropped folder {Folder}.", path);
                }
            }
            else if (MarkdownFileTypes.IsSupported(path))
            {
                supported.Add(path);
            }
        }

        if (supported.Count == 0)
        {
            StatusText = "Nothing there to open";
            await _dialogs.ShowMessageAsync(
                "Nothing to open",
                "Marqora opens markdown files: " + string.Join(", ", MarkdownFileTypes.Extensions)
                + "\n\nA dropped folder is searched for "
                + string.Join(", ", MarkdownFileTypes.FolderExtensions) + " files.")
                .ConfigureAwait(true);
            return;
        }

        if (supported.Count > ManyFilesThreshold)
        {
            ConfirmResult confirm = await _dialogs.ConfirmAsync(
                "Open every file?",
                $"That drop contains {supported.Count} markdown files. "
                + "Opening them all will create that many tabs.",
                primaryText: $"Open all {supported.Count}").ConfigureAwait(true);

            if (confirm != ConfirmResult.Primary)
            {
                return;
            }
        }

        string status = supported.Count == 1
            ? $"Opened {Path.GetFileName(supported[0])}"
            : $"Opened {supported.Count} files";

        await OpenManyAsync(supported, status).ConfigureAwait(true);
    }

    // ------------------------------------------------------------------- saving

    /// <summary>
    /// Throws away the buffer and takes what is on disk.
    ///
    /// The workspace already reloads by itself when a file changes underneath an unmodified
    /// document; this is the deliberate version, and the only route to it once the document
    /// is dirty - which is exactly when that automatic path stands back.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanReloadFromDisk))]
    private async Task ReloadFromDiskAsync()
    {
        await ReloadActiveFromDiskAsync().ConfigureAwait(true);

        RestoreDocumentFocusAfterChrome();
    }

    private async Task ReloadActiveFromDiskAsync()
    {
        if (_workspace.Active is not { } document || document.Path is null)
        {
            return;
        }

        // Only worth asking when there is something to lose. Cancel is the default, so a
        // stray Enter on this dialog cannot discard an afternoon's work.
        if (document.IsDirty)
        {
            ConfirmResult answer = await _dialogs.ConfirmAsync(
                "Reload from disk",
                $"\"{document.DisplayName}\" has unsaved changes. Reloading replaces them with "
                    + "the version on disk, and they cannot be recovered.",
                "Discard changes and reload").ConfigureAwait(true);

            if (answer != ConfirmResult.Primary)
            {
                return;
            }
        }

        try
        {
            await _workspace.ReloadAsync(document.Id).ConfigureAwait(true);
            StatusText = $"Reloaded {document.DisplayName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not reload {Path} from disk.", document.Path);

            await _dialogs
                .ShowMessageAsync("Reload failed", $"{document.DisplayName} could not be read. {ex.Message}")
                .ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Whether there is anything to reload, and anywhere to reload it from.
    ///
    /// An untitled document has nothing on disk to go back to, and a missing one has nothing
    /// left there either - its buffer is all that survives, so offering to replace it with the
    /// file could only ever fail. What remains is the case the command exists for: a buffer
    /// that differs from the file, either because it was edited here or because the file was
    /// rewritten elsewhere. A clean document that is in sync would re-read identical text.
    /// </summary>
    private bool CanReloadFromDisk() =>
        HasDocument
        && !string.IsNullOrWhiteSpace(DocumentPath)
        && ActiveExternalState != ExternalState.Missing
        && (IsDirty || ActiveExternalState == ExternalState.Changed);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (_workspace.Active is { } document)
        {
            await SaveDocumentAsync(document.Id).ConfigureAwait(true);
        }

        RestoreDocumentFocusAfterChrome();
    }

    /// <summary>
    /// Writes one document, whether or not it is the one on screen.
    ///
    /// Addressed by id rather than read off the active document, because Save All comes
    /// through here too: switching tabs to write each one would be a lot of flicker for
    /// something asked for precisely so it could happen in one go.
    /// </summary>
    private async Task SaveDocumentAsync(Guid id)
    {
        if (_workspace.Find(id) is not { } document)
        {
            return;
        }

        // A document that has never been saved needs a location first.
        if (document.IsUntitled)
        {
            await SaveDocumentAsAsync(id).ConfigureAwait(true);
            return;
        }

        await FormatBeforeSaveAsync(id).ConfigureAwait(true);

        // Re-read: formatting replaced the document's text.
        if (_workspace.Find(id) is not { } current)
        {
            return;
        }

        document = current;

        try
        {
            await _workspace.SaveAsync(id).ConfigureAwait(true);
            StatusText = $"Saved {document.DisplayName}";
        }
        catch (DirectoryNotFoundException ex)
        {
            // The usual way here is a document whose file was deleted along with its folder,
            // where saving is the offered way back. Reporting a failure the user cannot act on
            // would leave them holding text with nowhere to put it, so ask for somewhere else.
            _logger.LogWarning(ex, "The folder holding {Path} is gone; falling back to Save As.", document.DisplayPath);

            await _dialogs.ShowMessageAsync(
                "That folder no longer exists",
                $"{document.DisplayName} cannot be written back because the folder it came from is gone. "
                    + "Choose somewhere else to save it.").ConfigureAwait(true);

            await SaveDocumentAsAsync(id).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not save {Path}.", document.DisplayPath);
            await _dialogs.ShowMessageAsync("Could not save", ex.Message).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Runs the formatter before a save, when the user has asked for that.
    ///
    /// Always the whole document, never just a selection: the file being written is the whole
    /// file, and formatting only part of it on the way out would be a strange thing to do.
    /// </summary>
    private async Task FormatBeforeSaveAsync(Guid documentId)
    {
        if (!_settings.Current.Formatting.FormatOnSave)
        {
            return;
        }

        if (_workspace.Find(documentId) is not { } document)
        {
            return;
        }

        await ApplyFormatAsync(documentId, document.Text, null, announce: false).ConfigureAwait(true);
    }

    /// <summary>
    /// Whether Save has anything to write.
    ///
    /// A document that matches what is on disk does not, and writing it again would touch the
    /// file's timestamp for no gain - which the external-change watcher would then have to
    /// explain away. Save As stays open for the other reason to write a clean document, which
    /// is wanting a second copy of it somewhere else.
    ///
    /// A missing file counts as dirty, so Save stays within reach for the one case where
    /// rewriting an unedited buffer is exactly the point: putting back a file that has been
    /// deleted underneath it.
    /// </summary>
    private bool CanSave() => HasDocument && IsDirty;

    /// <summary>
    /// Writes every open document that has unsaved changes.
    ///
    /// Each one goes through the same path a single save does, so format-on-save applies, an
    /// untitled tab is asked where it should go, and a folder that has since been deleted is
    /// offered somewhere else. Anything that goes wrong has already said so on its own, so
    /// the count at the end is the only report needed.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSaveAll))]
    private async Task SaveAllAsync()
    {
        await SaveEveryDirtyAsync().ConfigureAwait(true);

        RestoreDocumentFocusAfterChrome();
    }

    private async Task SaveEveryDirtyAsync()
    {
        // Ids rather than documents, and a copy of them: every save replaces the record it
        // wrote, and a Save As can reorder the list under the loop.
        List<Guid> dirty = [.. _workspace.Documents.Where(d => d.IsDirty).Select(d => d.Id)];

        if (dirty.Count == 0)
        {
            StatusText = "Nothing to save";
            return;
        }

        // Save As makes its document active so its dialog names something the user can see.
        // Put the tab they were on back afterwards.
        Guid? returnTo = _workspace.Active?.Id;
        int saved = 0;

        foreach (Guid id in dirty)
        {
            await SaveDocumentAsync(id).ConfigureAwait(true);

            // Still dirty means a cancelled Save As or a write that failed. Closed means the
            // document went away mid-run, which is nobody's idea of a save.
            if (_workspace.Find(id) is { IsDirty: false })
            {
                saved++;
            }
        }

        if (returnTo is { } previous && _workspace.Find(previous) is not null)
        {
            _workspace.Activate(previous);
        }

        StatusText = saved == dirty.Count
            ? $"Saved {Documents(saved)}"
            : $"Saved {saved} of {Documents(dirty.Count)}";
    }

    /// <summary>
    /// Whether Save All has anything to write, anywhere in the workspace. Not gated on a
    /// document being active as well: with tabs open there is always one, and the flag is
    /// false whenever there are none.
    /// </summary>
    private bool CanSaveAll() => HasDirtyTabs;

    /// <summary>"1 document" or "4 documents", for status text that reads as a sentence.</summary>
    private static string Documents(int count) => count == 1 ? "1 document" : $"{count} documents";

    [RelayCommand(CanExecute = nameof(CanActOnDocument))]
    private async Task SaveAsAsync()
    {
        if (_workspace.Active is { } document)
        {
            await SaveDocumentAsAsync(document.Id).ConfigureAwait(true);
        }

        RestoreDocumentFocusAfterChrome();
    }

    private async Task SaveDocumentAsAsync(Guid id)
    {
        if (_workspace.Find(id) is not { } document)
        {
            return;
        }

        // Make the document visible first. Reached through Save All this can be an untitled
        // tab the user is not looking at, and a picker naming a document they cannot see is
        // a question they have no way to answer.
        _workspace.Activate(id);

        string? path = await _fileDialogs.PickSaveFileAsync(document.DisplayName).ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            await _workspace.SaveAsAsync(id, path).ConfigureAwait(true);
            await _recent.AddAsync(path).ConfigureAwait(true);

            StatusText = $"Saved as {Path.GetFileName(path)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not save as {Path}.", path);
            await _dialogs.ShowMessageAsync("Could not save", ex.Message).ConfigureAwait(true);
        }
    }

    private bool CanActOnDocument() => HasDocument;

    /// <summary>
    /// Whether the active document has a file behind it, for the commands that can only talk
    /// about one: showing it in Explorer, and copying its path. Both used to be offered on an
    /// untitled document, where one silently did nothing and the other explained itself in the
    /// status bar - a greyed item says the same thing before the click rather than after it.
    /// </summary>
    private bool CanActOnFile() => HasDocument && !string.IsNullOrWhiteSpace(DocumentPath);

    /// <summary>
    /// True when there is a document and it holds something other than whitespace.
    ///
    /// Gates the commands that can only produce a meaningless result on an empty file:
    /// exporting a blank page, formatting nothing, searching for text that cannot be there.
    /// Kept separate from <see cref="CanActOnDocument"/>, which only asks whether a document
    /// exists - saving and closing an empty document are perfectly reasonable.
    /// </summary>
    private bool CanActOnContent() => HasContent;

    /// <summary>Whether the active document holds anything but whitespace.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportHtmlCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyAsRichTextCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportPdfCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand))]
    [NotifyCanExecuteChangedFor(nameof(FormatDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(FormatAllDocumentsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ScrollToTopCommand))]
    [NotifyCanExecuteChangedFor(nameof(ScrollToBottomCommand))]
    public partial bool HasContent { get; set; }

    // ------------------------------------------------------------------ closing

    [RelayCommand(CanExecute = nameof(CanActOnDocument))]
    private async Task CloseTabAsync()
    {
        if (ActiveTab is { } tab)
        {
            await CloseTabAsync(tab).ConfigureAwait(true);
        }

        RestoreDocumentFocusAfterChrome();
    }

    /// <summary>Closes one tab, offering to save first. Used by the close button and menu.</summary>
    public async Task CloseTabAsync(DocumentTabViewModel tab)
    {
        if (!await ConfirmDiscardAsync(tab).ConfigureAwait(true))
        {
            return;
        }

        _workspace.Close(tab.Id);
    }

    [RelayCommand(CanExecute = nameof(CanCloseOthers))]
    private async Task CloseOtherTabsAsync()
    {
        await CloseTabsOtherThanActiveAsync().ConfigureAwait(true);

        RestoreDocumentFocusAfterChrome();
    }

    private async Task CloseTabsOtherThanActiveAsync()
    {
        if (ActiveTab is not { } keep)
        {
            return;
        }

        foreach (DocumentTabViewModel tab in Tabs.Where(t => t.Id != keep.Id).ToList())
        {
            if (!await ConfirmDiscardAsync(tab).ConfigureAwait(true))
            {
                return;
            }

            _workspace.Close(tab.Id);
        }
    }

    private bool CanCloseOthers() => Tabs.Count > 1;

    [RelayCommand(CanExecute = nameof(CanActOnDocument))]
    private async Task CloseAllTabsAsync()
    {
        await CloseAllAsync().ConfigureAwait(true);

        // Not in CloseAllAsync itself: the window's closing handler calls that one, and a
        // window on its way out has no document left to put the keyboard in.
        RestoreDocumentFocusAfterChrome();
    }

    /// <summary>
    /// Closes every tab, prompting for each dirty one. Returns false if the user cancelled,
    /// which the window's closing handler uses to abort shutdown.
    /// </summary>
    public async Task<bool> CloseAllAsync()
    {
        foreach (DocumentTabViewModel tab in Tabs.ToList())
        {
            if (!await ConfirmDiscardAsync(tab).ConfigureAwait(true))
            {
                return false;
            }

            _workspace.Close(tab.Id);
        }

        return true;
    }

    /// <summary>
    /// Offers to save a tab before it is closed. Returns false when the user cancels, in
    /// which case the caller must abandon what it was doing.
    /// </summary>
    private async Task<bool> ConfirmDiscardAsync(DocumentTabViewModel tab)
    {
        if (!tab.IsDirty)
        {
            return true;
        }

        // Make the tab in question visible, or the prompt names a document the user cannot see.
        _workspace.Activate(tab.Id);

        ConfirmResult result = await _dialogs.ConfirmAsync(
            "Save changes?",
            $"{tab.Title} has unsaved changes.",
            primaryText: "Save",
            secondaryText: "Discard").ConfigureAwait(true);

        switch (result)
        {
            case ConfirmResult.Primary:
                await SaveAsync().ConfigureAwait(true);

                // A cancelled Save As leaves the document dirty; do not close it.
                return _workspace.Find(tab.Id) is not { IsDirty: true };

            case ConfirmResult.Secondary:
                return true;

            default:
                return false;
        }
    }

    // --------------------------------------------------------- external changes

    /// <summary>
    /// Brings the banner into line with the pending set and whichever tab is in front.
    ///
    /// Called after anything that could change either. Cheap enough to run unconditionally,
    /// which is worth more than working out when it is needed and being wrong once.
    /// </summary>
    private void RefreshExternalNotice()
    {
        // A document that resolved itself - reloaded, saved, or the file came back - is no
        // longer pending, whatever put it in the list.
        _pendingExternal.RemoveAll(id => _workspace.Find(id) is not { HasExternalChange: true });
        _dismissedExternal.RemoveWhere(id => !_pendingExternal.Contains(id));

        // Closing, saving and reloading by hand all end a document's claim to be announced,
        // and all three clear AutoReloadedUtc or take the document away entirely - so one
        // sweep against it covers them without a case each. Without this, closing a tab
        // before its message fires leaves a ghost in the next "N more were reloaded" count,
        // and saving one has the user told later about a file they have since written.
        _unannouncedReloads.RemoveWhere(
            id => _workspace.Find(id) is not { AutoReloadedUtc: not null });

        UpdateExternalStatus();

        if (_workspace.Active is not { HasExternalChange: true } active
            || _dismissedExternal.Contains(active.Id))
        {
            HasExternalNotice = false;
            ExternalNotice = ExternalChangeNotice.None;
            return;
        }

        int position = _pendingExternal.IndexOf(active.Id) + 1;

        // A document already reloaded once is not worth offering to reload again, so the
        // sweep counts only what it would actually act on.
        int dirty = _pendingExternal.Count(id =>
            _workspace.Find(id) is { } d && !string.Equals(d.Text, d.SavedText, StringComparison.Ordinal));

        ExternalNotice = ExternalChangeNotice.For(
            active,
            Math.Max(position, 1),
            _pendingExternal.Count,
            dirty);

        HasExternalNotice = true;
    }

    /// <summary>
    /// Counts the tabs waiting behind this one.
    ///
    /// Without it, a file that changed under a tab the user never goes back to is announced
    /// by a marker on a tab they are not looking at, and nothing else.
    ///
    /// A standing indicator of its own rather than a line of <see cref="StatusText"/>: it
    /// describes a condition that lasts until the tabs are visited, and writing it there put
    /// it in a fight with the messages that report what the user just did - "Kept your version
    /// of notes.md" was replaced by the count in the same breath.
    /// </summary>
    private void UpdateExternalStatus()
    {
        int others = _pendingExternal.Count(id => id != _workspace.Active?.Id);

        ExternalPendingSummary = others switch
        {
            0 => string.Empty,
            1 => "1 other file changed on disk",
            _ => $"{others} other files changed on disk",
        };
    }

    // ------------------------------------------- announcing a silent reload

    /// <summary>
    /// Says that a document was replaced from disk without being asked, if that has not been
    /// said already.
    ///
    /// Called from the three places the user can find out: the reload landing on the document
    /// they are already looking at, them switching to a document it landed on earlier, and
    /// them coming back to the window. Announcing takes the id out of
    /// <see cref="_unannouncedReloads"/>, so whichever of the three happens first is the only
    /// one that speaks.
    /// </summary>
    /// <returns>Whether there was anything to say.</returns>
    private bool AnnounceReloadIfPending(MarkdownDocument? document)
    {
        if (document is null || !_unannouncedReloads.Remove(document.Id))
        {
            return false;
        }

        // Whatever is left is waiting on a tab the user has not been to yet. Naming those in
        // the tooltip costs nothing and saves opening each one to find out which changed.
        List<string> waiting =
        [
            .. _unannouncedReloads
                .Select(id => _workspace.Find(id)?.DisplayName)
                .OfType<string>()
                .Order(StringComparer.CurrentCultureIgnoreCase),
        ];

        // "more were reloaded", not "other files changed on disk". The centre of this same bar
        // says the latter for tabs waiting on a decision, and the two must not read as the
        // same sentence: one is work the user still has to do, this one is already done.
        string text = waiting.Count switch
        {
            0 => $"Reloaded {document.DisplayName} from disk",
            1 => $"Reloaded {document.DisplayName} from disk — 1 more was reloaded",
            _ => $"Reloaded {document.DisplayName} from disk — {waiting.Count} more were reloaded",
        };

        string detail = waiting.Count == 0
            ? string.Empty
            : "Reloaded from disk:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, waiting.Prepend(document.DisplayName));

        ShowHighlightedStatus(text, ReloadedGlyph, detail);

        return true;
    }

    /// <summary>
    /// Segoe Fluent's refresh arrow, in front of a reload message.
    ///
    /// A glyph from the icon font rather than a character like ⟳: several Windows font stacks
    /// render symbol characters as colour emoji, at the wrong size and weight and immune to
    /// being recoloured, which is the same reason the tab strip's missing-file marker is a
    /// plain exclamation mark.
    /// </summary>
    private const string ReloadedGlyph = "\uE72C";

    /// <summary>
    /// Told by the window whether it has the user's attention.
    ///
    /// Two things hang off it. A reload that lands while Marqora is behind another window is
    /// held rather than announced, because there is nobody there to read it - that is the
    /// whole "stepped away for lunch" case. And a highlight already showing stops counting
    /// down, so it is still lit when they come back rather than having expired to an empty
    /// desk.
    /// </summary>
    public void SetWindowActive(bool isActive)
    {
        if (_windowIsActive == isActive)
        {
            return;
        }

        _windowIsActive = isActive;

        if (!isActive)
        {
            PauseStatusHighlight();

            if (_settings.Current.AutoSave == AutoSaveMode.OnFocusLoss)
            {
                _ = AutoSaveAsync();
            }

            return;
        }

        // A fresh announcement arms its own countdown, so only pick the old one back up when
        // there was nothing waiting to be said.
        if (!AnnounceReloadIfPending(_workspace.Active))
        {
            ResumeStatusHighlight();
        }
    }

    /// <summary>
    /// Puts a message in the status bar wearing the highlight, for the things the user did not
    /// ask for and only gets told about once.
    /// </summary>
    private void ShowHighlightedStatus(string text, string glyph, string detail = "")
    {
        // Text first, deliberately. Assigning it runs OnStatusTextChanged, which takes down
        // any highlight already showing along with its glyph, its detail and its timer;
        // everything below then applies to the message that has just replaced it.
        StatusText = text;
        StatusIconGlyph = glyph;
        StatusDetail = detail;
        _highlightRemaining = StatusHighlightDuration;
        IsStatusHighlighted = true;

        // Unconditional, and it has to be. Two reloads of the same document produce the same
        // string, which raises no change notification at all - but the second one still
        // deserves its full eight seconds.
        ResumeStatusHighlight();
    }

    /// <summary>
    /// Any other message replacing this one takes the highlight down with it.
    ///
    /// Without this the pill would stay lit around whatever came next - "Saved notes.md"
    /// wearing the colour that belonged to a reload notice two seconds earlier, which reads
    /// as the save having gone strangely rather than as a leftover.
    /// </summary>
    partial void OnStatusTextChanged(string value)
    {
        PauseStatusHighlight();

        IsStatusHighlighted = false;
        StatusIconGlyph = string.Empty;
        StatusDetail = string.Empty;
        _highlightRemaining = TimeSpan.Zero;
    }

    /// <summary>
    /// Starts, or picks back up, the countdown that takes the highlight off the message.
    ///
    /// The first timer in the app, so it is worth saying what it is not: there is no clock
    /// running unless a highlight is up and the window has focus. The countdown is armed
    /// afresh each time rather than left running and consulted, which is the same
    /// cancel-then-replace idiom as the file watcher's debounce and the shell's zoom badge.
    /// </summary>
    private void ResumeStatusHighlight()
    {
        PauseStatusHighlight();

        if (!IsStatusHighlighted || !_windowIsActive || _highlightRemaining <= TimeSpan.Zero)
        {
            return;
        }

        var expiry = new CancellationTokenSource();
        TimeSpan wait = _highlightRemaining;

        _highlightExpiry = expiry;
        _highlightResumedUtc = DateTimeOffset.UtcNow;

        _ = ExpireAsync();

        async Task ExpireAsync()
        {
            try
            {
                await Task.Delay(wait, expiry.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // How a countdown normally ends early - another message arrived, or the
                // window lost focus. Not a failure, and nothing to log.
                return;
            }

            _ui.Post(() =>
            {
                // Only if this is still the countdown in charge. One that was replaced while
                // its delay was in flight must not take down the highlight that replaced it.
                if (ReferenceEquals(_highlightExpiry, expiry))
                {
                    IsStatusHighlighted = false;
                    _highlightRemaining = TimeSpan.Zero;
                }
            });
        }
    }

    /// <summary>Stops the countdown, keeping what is left of it for when the window returns.</summary>
    private void PauseStatusHighlight()
    {
        if (_highlightExpiry is not { } expiry)
        {
            return;
        }

        _highlightExpiry = null;
        expiry.Cancel();
        expiry.Dispose();

        TimeSpan elapsed = DateTimeOffset.UtcNow - _highlightResumedUtc;

        _highlightRemaining = _highlightRemaining > elapsed
            ? _highlightRemaining - elapsed
            : TimeSpan.Zero;
    }

    /// <summary>Takes what is on disk, discarding whatever the buffer holds.</summary>
    [RelayCommand]
    private async Task ReloadExternalAsync()
    {
        if (ExternalNotice.DocumentId == Guid.Empty)
        {
            return;
        }

        await ReloadOneAsync(ExternalNotice.DocumentId).ConfigureAwait(true);
        RefreshExternalNotice();
    }

    /// <summary>
    /// Keeps the buffer and clears the marker: the user has looked and decided.
    ///
    /// Different from dismissing the banner, which only defers. This resolves, so the tab
    /// stops carrying a question that has been answered.
    /// </summary>
    [RelayCommand]
    private void KeepMine()
    {
        if (ExternalNotice.DocumentId == Guid.Empty)
        {
            return;
        }

        Guid id = ExternalNotice.DocumentId;
        string name = _workspace.Find(id)?.DisplayName ?? string.Empty;

        _workspace.ResolveExternalChange(id);

        StatusText = $"Kept your version of {name}";
        RefreshExternalNotice();
    }

    /// <summary>
    /// Hides the banner without deciding anything. The tab keeps its marker and the banner
    /// returns the next time this tab is opened.
    /// </summary>
    [RelayCommand]
    private void DismissExternalNotice()
    {
        // Reads the notice rather than HasExternalNotice, which the InfoBar has usually
        // already cleared through its two-way binding by the time this runs. Missing the
        // record of the dismissal would let the banner reopen on the very next change.
        if (ExternalNotice.DocumentId != Guid.Empty)
        {
            _dismissedExternal.Add(ExternalNotice.DocumentId);
        }

        HasExternalNotice = false;
        ExternalNotice = ExternalChangeNotice.None;
    }

    /// <summary>
    /// Writes the buffer beside the original as "name.local.md" and opens it.
    ///
    /// Marqora has no diff view, and building one is a feature rather than a button. Putting
    /// both versions on screen as ordinary tabs lets them be compared in whatever tool the
    /// user already has, and costs one method.
    /// </summary>
    [RelayCommand]
    private async Task SaveMineAsAsync()
    {
        if (_workspace.Find(ExternalNotice.DocumentId) is not { Path: { } path } document)
        {
            return;
        }

        string folder = Path.GetDirectoryName(path) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);

        string candidate = Path.Combine(folder, $"{stem}.local{extension}");

        // Never overwrite: a second conflict on the same file must not quietly replace the
        // copy taken during the first one.
        for (int n = 2; File.Exists(candidate); n++)
        {
            candidate = Path.Combine(folder, $"{stem}.local{n}{extension}");
        }

        try
        {
            await File.WriteAllTextAsync(candidate, document.Text).ConfigureAwait(true);
            await _workspace.OpenAsync(candidate).ConfigureAwait(true);

            StatusText = $"Saved your version as {Path.GetFileName(candidate)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not write a local copy beside {Path}.", path);

            await _dialogs
                .ShowMessageAsync("Could not save a copy", ex.Message)
                .ConfigureAwait(true);
        }
    }

    /// <summary>Reloads every pending document that holds no edits, and says what it left alone.</summary>
    [RelayCommand]
    private Task ReloadAllUnmodifiedAsync() => ReloadPendingAsync(includeDirty: false);

    /// <summary>Reloads every pending document, edits and all.</summary>
    [RelayCommand]
    private Task ReloadAllDiscardingAsync() => ReloadPendingAsync(includeDirty: true);

    private async Task ReloadPendingAsync(bool includeDirty)
    {
        // Copied first: reloading mutates the pending list as each document resolves.
        List<Guid> targets = [.. _pendingExternal];

        int reloaded = 0;
        int skipped = 0;

        foreach (Guid id in targets)
        {
            if (_workspace.Find(id) is not { } document)
            {
                continue;
            }

            // Nothing to reload from; a missing file can only be written back.
            if (document.External == ExternalState.Missing)
            {
                skipped++;
                continue;
            }

            if (!includeDirty && !string.Equals(document.Text, document.SavedText, StringComparison.Ordinal))
            {
                skipped++;
                continue;
            }

            if (await ReloadOneAsync(id).ConfigureAwait(true))
            {
                reloaded++;
            }
        }

        RefreshExternalNotice();

        StatusText = skipped == 0
            ? $"Reloaded {reloaded} {(reloaded == 1 ? "file" : "files")}"
            : $"Reloaded {reloaded} {(reloaded == 1 ? "file" : "files")}; left {skipped} alone";
    }

    /// <summary>Reloads one document, reporting a failure rather than letting it pass silently.</summary>
    private async Task<bool> ReloadOneAsync(Guid id)
    {
        if (_workspace.Find(id) is not { } document)
        {
            return false;
        }

        try
        {
            await _workspace.ReloadAsync(id).ConfigureAwait(true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not reload {Path}.", document.DisplayPath);

            await _dialogs
                .ShowMessageAsync("Reload failed", $"{document.DisplayName} could not be read. {ex.Message}")
                .ConfigureAwait(true);

            return false;
        }
    }

    // ------------------------------------------------------------- tab movement

    [RelayCommand(CanExecute = nameof(CanCycleTabs))]
    private void NextTab() => CycleTab(1);

    [RelayCommand(CanExecute = nameof(CanCycleTabs))]
    private void PreviousTab() => CycleTab(-1);

    private bool CanCycleTabs() => Tabs.Count > 1;

    private void CycleTab(int direction)
    {
        if (Tabs.Count == 0 || ActiveTab is null)
        {
            return;
        }

        int index = Tabs.IndexOf(ActiveTab);
        int next = ((index + direction) % Tabs.Count + Tabs.Count) % Tabs.Count;

        _workspace.Activate(Tabs[next].Id);

        // Moving to another document takes the keyboard with it. Ctrl+Tab from the editor
        // already has it there and this costs nothing; the View menu route is the one that
        // would otherwise leave it on the menu bar.
        RestoreDocumentFocusAfterChrome();
    }

    /// <summary>Selects a tab by its 1-based position, for Ctrl+1 to Ctrl+8.</summary>
    public void ActivateTabByNumber(int oneBasedIndex)
    {
        if (oneBasedIndex >= 1 && oneBasedIndex <= Tabs.Count)
        {
            _workspace.Activate(Tabs[oneBasedIndex - 1].Id);
        }
    }

    /// <summary>Selects the last tab, for Ctrl+9.</summary>
    public void ActivateLastTab()
    {
        if (Tabs.Count > 0)
        {
            _workspace.Activate(Tabs[^1].Id);
        }
    }

    /// <summary>
    /// Mirrors a drag-reorder back into the workspace. TabView moves the item in the bound
    /// collection itself, so this reacts rather than drives.
    /// </summary>
    private void OnTabsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_isSyncingTabs || e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Move)
        {
            return;
        }

        if (e.NewItems?.Count > 0 && e.NewItems[0] is DocumentTabViewModel moved)
        {
            _workspace.Move(moved.Id, e.NewStartingIndex);
            PersistSession();
        }
    }

    // ------------------------------------------------------------- workspace sync

    /// <summary>
    /// The tail of the workspace-change queue. Changes are applied strictly one after
    /// another; see <see cref="OnWorkspaceChanged"/>.
    /// </summary>
    private Task _workspaceChain = Task.CompletedTask;

    /// <summary>
    /// Queues a workspace change behind the previous one.
    ///
    /// The ordering matters more than it looks. Opening a document raises Opened and then
    /// Activated back to back, and handling Opened has to await a render before it can tell
    /// the shell about the new tab. Posting each change independently let the Activated
    /// continuation overtake that await: the shell was asked to activate a tab it had not
    /// been given yet, ignored the request as it must, and the document then sat in a tab
    /// that was never made current - a blank window with no error anywhere.
    ///
    /// Chaining is enough to fix it because every change is applied on the UI thread, so the
    /// assignment below cannot race with itself.
    /// </summary>
    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs e) =>
        _ui.Post(() => _workspaceChain = ApplyAfterAsync(_workspaceChain, e));

    private async Task ApplyAfterAsync(Task previous, WorkspaceChangedEventArgs e)
    {
        // ApplyWorkspaceChangeAsync swallows its own failures, so the chain never faults and
        // one bad change cannot stall every change after it.
        await previous.ConfigureAwait(true);
        await ApplyWorkspaceChangeAsync(e).ConfigureAwait(true);
    }

    private async Task ApplyWorkspaceChangeAsync(WorkspaceChangedEventArgs e)
    {
        try
        {
            switch (e.Change)
            {
                case WorkspaceChange.Opened when e.Document is { } opened:
                    await AddTabAsync(opened).ConfigureAwait(true);
                    break;

                case WorkspaceChange.Closed:
                    await RemoveTabAsync(e.DocumentId).ConfigureAwait(true);
                    RefreshExternalNotice();
                    break;

                case WorkspaceChange.Activated when e.Document is { } activated:
                    await SelectTabAsync(activated).ConfigureAwait(true);

                    // Opening a tab is asking to see it, which includes anything waiting on
                    // it. A banner dismissed earlier comes back here rather than staying gone.
                    _dismissedExternal.Remove(activated.Id);
                    RefreshExternalNotice();

                    // Arriving at a document is the moment to mention that it was replaced
                    // from disk while the user was elsewhere. Activate is the one chokepoint
                    // every route runs through - the tab strip, the document list, Ctrl+Tab,
                    // Ctrl+1..9, a Find All jump - so this catches all of them at once.
                    if (_windowIsActive)
                    {
                        AnnounceReloadIfPending(activated);
                    }

                    break;

                case WorkspaceChange.Edited when e.Document is { } edited:
                    FindTab(edited.Id)?.Update(edited);
                    UpdateActiveDocumentState();
                    break;

                case WorkspaceChange.Saved when e.Document is { } saved:
                    FindTab(saved.Id)?.Update(saved);
                    UpdateActiveDocumentState();
                    RefreshExternalNotice();
                    PersistSession();
                    break;

                case WorkspaceChange.ExternalStateChanged when e.Document is { } stale:
                    FindTab(stale.Id)?.Update(stale);
                    UpdateActiveDocumentState();

                    if (stale.HasExternalChange && !_pendingExternal.Contains(stale.Id))
                    {
                        _pendingExternal.Add(stale.Id);
                    }

                    RefreshExternalNotice();
                    break;

                case WorkspaceChange.ReloadedFromDisk when e.Document is { } reloaded:

                    // Listed before the shell round-trip below, not after it. Those awaits are
                    // a cross-process call that can take a while, and the window coming back to
                    // the front runs on the UI thread outside this chain: if the document were
                    // not on the list by then, the user's return would find nothing to say and
                    // the announcement would turn up later, at a moment they did not cause.
                    //
                    // A reload the user asked for clears AutoReloadedUtc, so the null check is
                    // all it takes to leave those alone. They already know.
                    if (reloaded.AutoReloadedUtc is not null)
                    {
                        _unannouncedReloads.Add(reloaded.Id);
                    }

                    FindTab(reloaded.Id)?.Update(reloaded);
                    UpdateActiveDocumentState();

                    if (_host is not null)
                    {
                        RenderedMarkdown rendered = await RenderAsync(reloaded.Text).ConfigureAwait(true);

                        // ReplaceTextAsync rather than SetTabTextAsync, which resets the Monaco
                        // model and takes the undo history and the caret with it. As a single
                        // edit instead, the caret and scroll position survive a reload - and
                        // Ctrl+Z takes one back, which is what makes reloading without asking
                        // something the user can recover from.
                        await _host.ReplaceTextAsync(reloaded.Id, reloaded.Text, rendered).ConfigureAwait(true);
                        await PublishDiagnosticsAsync(reloaded.Id, reloaded.Text, reloaded.Path, rendered)
                            .ConfigureAwait(true);
                    }

                    RefreshExternalNotice();

                    // Said now only when the user is here to read it. Otherwise it waits on the
                    // list for them to arrive at the tab, or for the window to come back to the
                    // front. Announcing regardless, as this used to, wrote a line about a
                    // document they were not looking at and let the next keystroke wipe it.
                    //
                    // The membership check is not redundant: coming back to the window while
                    // the awaits above were in flight will already have said this.
                    if (_windowIsActive
                        && _workspace.Active?.Id == reloaded.Id
                        && _unannouncedReloads.Contains(reloaded.Id))
                    {
                        AnnounceReloadIfPending(reloaded);
                    }

                    break;

                case WorkspaceChange.Reordered:
                    PersistSession();
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply a {Change} change.", e.Change);
        }
    }

    private async Task AddTabAsync(MarkdownDocument document)
    {
        if (FindTab(document.Id) is not null)
        {
            return;
        }

        _isSyncingTabs = true;
        Tabs.Add(new DocumentTabViewModel(document));
        _isSyncingTabs = false;

        NotifyTabCountChanged();

        if (_host is not null)
        {
            RenderedMarkdown rendered = await RenderAsync(document.Text).ConfigureAwait(true);
            await _host.OpenTabAsync(document.Id, document.Text, rendered).ConfigureAwait(true);
            await PublishDiagnosticsAsync(document.Id, document.Text, document.Path, rendered).ConfigureAwait(true);
        }

        PersistSession();
    }

    private async Task RemoveTabAsync(Guid id)
    {
        if (FindTab(id) is { } tab)
        {
            _isSyncingTabs = true;
            Tabs.Remove(tab);
            _isSyncingTabs = false;

            NotifyTabCountChanged();
        }

        if (_host is not null)
        {
            await _host.CloseTabAsync(id).ConfigureAwait(true);

            if (Tabs.Count == 0)
            {
                await _host.ClearAsync().ConfigureAwait(true);
            }
        }

        UpdateActiveDocumentState();
        PersistSession();
    }

    private async Task SelectTabAsync(MarkdownDocument document)
    {
        if (FindTab(document.Id) is { } tab)
        {
            ActiveTab = tab;
        }

        UpdateActiveDocumentState();

        if (_host is not null)
        {
            await _host.ActivateTabAsync(document.Id, document.Path).ConfigureAwait(true);
        }

        PersistSession();
    }

    /// <summary>Called by the view when the user picks a different tab in the strip.</summary>
    public void OnTabSelectedByUser(DocumentTabViewModel? tab)
    {
        if (tab is not null)
        {
            _workspace.Activate(tab.Id);
        }
    }

    /// <summary>
    /// Puts the keyboard back in the document after the chrome has taken it.
    ///
    /// Clicking a tab gives XAML focus to the tab, which is the control doing what it has
    /// always done — but the keyboard was in the text, and the tab strip is not where the
    /// next keystroke belongs. Clicking the tab that is already selected changes nothing
    /// else at all, so without this it is a gesture whose only effect is to stop the caret
    /// working. The pane is whichever one last had the keyboard, so returning to a document
    /// being read in the preview does not silently move to the source.
    ///
    /// Queued behind the workspace chain rather than sent straight away, for the reason
    /// <see cref="OnWorkspaceChanged"/> gives: a new tab is opened and activated by two
    /// changes that each await the shell, and focus asked for ahead of them would land on a
    /// document the shell has not been given yet. Nothing is queued when the click did not
    /// open anything, because the chain is already complete by then.
    /// </summary>
    public void RestoreDocumentFocus() =>
        _ui.Post(() => _workspaceChain = RestoreFocusAfterAsync(_workspaceChain));

    /// <summary>
    /// The same restore, one turn later. What a command picked from a menu or a toolbar
    /// button must use.
    ///
    /// A MenuFlyout holds focus while it is open and hands it back to whatever opened it as
    /// it closes, so a restore that runs inside the click is undone a moment afterwards.
    /// That is not hypothetical for a command: several finish without ever awaiting -
    /// closing a tab with nothing unsaved in it, copying a path - and their restore would
    /// land while the menu was still up. Deferring past the render pass puts it after the
    /// flyout has finished with the keyboard, the same way the startup focus is placed.
    ///
    /// The commands that do await are covered by the same call. They finish once their
    /// dialog has been answered, so by then there is nothing left to take the keyboard
    /// from - which is why this is safe to use on all of them rather than only some.
    /// </summary>
    public void RestoreDocumentFocusAfterChrome() => _ui.PostAfterRender(RestoreDocumentFocus);

    private async Task RestoreFocusAfterAsync(Task previous)
    {
        await previous.ConfigureAwait(true);

        // Nothing to focus once the last tab has gone: the WebView is collapsed and the
        // empty state is what is on screen.
        if (_host is null || !HasDocument)
        {
            return;
        }

        try
        {
            await _host.FocusPaneAsync(ActivePane).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // Swallowed like every other link in the chain: a lost focus is not worth
            // faulting the queue that every later workspace change waits on.
            _logger.LogWarning(ex, "Could not put focus back in the document.");
        }
    }

    private DocumentTabViewModel? FindTab(Guid id) => Tabs.FirstOrDefault(t => t.Id == id);

    private void UpdateActiveDocumentState()
    {
        MarkdownDocument? document = _workspace.Active;

        HasDocument = document is not null;

        // Whitespace-only counts as empty: there is nothing to export, format or search for
        // in a file of blank lines, and offering those commands only invites a no-op.
        HasContent = !string.IsNullOrWhiteSpace(document?.Text);

        DocumentName = document?.DisplayName ?? string.Empty;
        DocumentPath = document?.Path ?? string.Empty;
        IsDirty = document?.IsDirty ?? false;
        ActiveExternalState = document?.External ?? ExternalState.InSync;

        // Every tab, not just this one: Save All writes the whole workspace, and a document
        // can go dirty - or come back clean - while another tab is the one on screen. This
        // runs on each workspace change, which is the only thing that can move the answer.
        HasDirtyTabs = _workspace.Documents.Any(d => d.IsDirty);

        if (document is not null)
        {
            FindTab(document.Id)?.Update(document);
        }
    }

    private void NotifyTabCountChanged()
    {
        CloseOtherTabsCommand.NotifyCanExecuteChanged();
        NextTabCommand.NotifyCanExecuteChanged();
        PreviousTabCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Freezes session persistence for shutdown.
    ///
    /// Closing the window closes every tab, and each close would otherwise rewrite the
    /// session with the shrinking list, leaving an empty one behind. The session recorded
    /// while the app was running is the one worth keeping.
    /// </summary>
    public void BeginShutdown() => _isShuttingDown = true;

    /// <summary>Resumes session persistence after a cancelled close.</summary>
    public void CancelShutdown() => _isShuttingDown = false;

    private bool _isShuttingDown;

    /// <summary>Records the open documents so the next launch can restore them.</summary>
    private void PersistSession()
    {
        if (_isShuttingDown)
        {
            return;
        }

        // Untitled documents have no path to reopen from, so they are not recorded.
        List<string> paths = [.. Tabs.Where(t => t.Path is not null).Select(t => t.Path!)];

        int activeIndex = ActiveTab?.Path is { } activePath
            ? Math.Max(0, paths.FindIndex(p => string.Equals(p, activePath, StringComparison.OrdinalIgnoreCase)))
            : 0;

        _settings.Update(s => s with { OpenDocuments = paths, ActiveDocumentIndex = activeIndex });
    }

    // -------------------------------------------------------------- view state

    /// <summary>
    /// Takes the mode as a string because XAML CommandParameter values arrive untyped, and
    /// a typed command would reject the literal written in the markup.
    /// </summary>
    [RelayCommand]
    private async Task SetViewModeAsync(string? modeName)
    {
        if (!Enum.TryParse(modeName, ignoreCase: true, out ViewMode mode))
        {
            _logger.LogWarning("Ignoring unknown view mode {Mode}.", modeName);
            return;
        }

        await ApplyViewModeAsync(mode, persist: true, takeFocus: true).ConfigureAwait(true);
    }

    /// <summary>
    /// Puts the shell into a view mode and tells everything bound to it.
    /// </summary>
    /// <param name="persist">
    /// Whether this becomes the mode the app starts in next time. True for the user's own
    /// choice; false for one the app made for them, which is the welcome document opening in
    /// preview - overwriting a saved preference on their behalf would be the app forgetting
    /// something they set.
    /// </param>
    /// <param name="takeFocus">
    /// Whether to put the keyboard in the pane the new mode implies. True for the user's own
    /// click on the segments or the View menu, and false when the app switches on their
    /// behalf: Find All drops into split view to show a match, and stepping the results list
    /// has to leave the keyboard in that list or the arrow keys stop walking it.
    /// </param>
    private async Task ApplyViewModeAsync(ViewMode mode, bool persist, bool takeFocus)
    {
        ViewMode = mode;

        // Re-announced even when unchanged so a click on the already-active segment
        // restores its checked state rather than leaving it visually toggled off.
        OnPropertyChanged(nameof(ViewMode));
        OnPropertyChanged(nameof(IsSourceView));
        OnPropertyChanged(nameof(IsPreviewView));
        OnPropertyChanged(nameof(IsSplitView));
        OnPropertyChanged(nameof(CanFormat));

        if (persist)
        {
            _settings.Update(s => s with { ViewMode = mode });
        }

        if (_host is not null)
        {
            await _host.SetViewModeAsync(mode).ConfigureAwait(true);
        }

        if (!takeFocus)
        {
            return;
        }

        // Which pane depends only on the mode being switched to, never on where the keyboard
        // was. Source and split both mean the editor: split is the editing view, and a
        // focused preview shows no caret, so landing there in split view is indistinguishable
        // from focus having gone nowhere. Preview has no editor to land in.
        //
        // ActivePane is set as well as focused, so everything that asks it later - the next
        // file opened from a menu, the zoom label, a scroll-to-edge - agrees with where the
        // keyboard actually is.
        ActivePane = mode == ViewMode.Preview ? EditorPane.Preview : EditorPane.Source;

        // Switching modes hides a pane, and a pane that goes display:none takes the DOM
        // focus that was in it down with it - so this is a restore in the full sense even
        // when the mode did not change at all.
        RestoreDocumentFocusAfterChrome();
    }

    /// <summary>
    /// Opens the preferences dialog.
    ///
    /// Nothing is applied here afterwards: preferences take effect as they are changed, so
    /// by the time the dialog closes every one of them is already in force. The focus
    /// restore is the same one every menu command owes.
    /// </summary>
    [RelayCommand]
    private async Task ShowPreferencesAsync()
    {
        await _preferencesDialogs.ShowPreferencesAsync().ConfigureAwait(true);

        RestoreDocumentFocusAfterChrome();
    }

    [RelayCommand]
    private void SetTheme(string? themeName)
    {
        if (!Enum.TryParse(themeName, ignoreCase: true, out AppTheme theme))
        {
            _logger.LogWarning("Ignoring unknown theme {Theme}.", themeName);
            return;
        }

        ApplyTheme(theme);

        RestoreDocumentFocusAfterChrome();
    }

    /// <summary>
    /// Switches theme without touching focus, for the preferences dialog. See the note above
    /// the View-menu toggles for why the focus restore has to stay out of it.
    /// </summary>
    public void ApplyTheme(AppTheme theme)
    {
        Theme = theme;

        OnPropertyChanged(nameof(IsSystemTheme));
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsDarkTheme));

        _settings.Update(s => s with { Theme = theme });
        _themeService.Apply(theme);
    }

    /// <summary>
    /// Shows both panes for as long as the preferences dialog is open.
    ///
    /// Most preferences show their effect in one pane only - a source font in the editor, a
    /// heading number in the preview - so someone working in a single pane would change a
    /// setting and watch nothing happen. Both panes up means every setting can be seen
    /// landing, which is the whole argument for applying them live.
    ///
    /// Not persisted, and that is the important half. The view mode is also a preference -
    /// the one the app starts in - so writing this to the settings record would mean pressing
    /// OK saved a layout the user never chose. It stays a display state for the life of the
    /// dialog and nothing more.
    ///
    /// Focus is left alone for the reason the View-menu toggles are: the WebView is behind a
    /// modal dialog, and putting the keyboard into it would swallow the user's typing.
    ///
    /// Nothing to show without a document, so nothing is done.
    /// </summary>
    public Task ShowBothPanesAsync() =>
        HasDocument
            ? ApplyViewModeAsync(ViewMode.SideBySide, persist: false, takeFocus: false)
            : Task.CompletedTask;

    /// <summary>
    /// Puts the panes back to the mode the settings name. Called however the preferences
    /// dialog closes.
    ///
    /// Read from the settings rather than from a mode captured on the way in, so that
    /// Restore Defaults is honoured. That button resets the view mode along with everything
    /// else, and putting back what the user had beforehand would quietly undo part of what
    /// they had just asked for. When nothing touched the view mode the two are the same.
    /// </summary>
    public Task RestoreViewModeAsync() =>
        ApplyViewModeAsync(_settings.Current.ViewMode, persist: false, takeFocus: false);

    /// <summary>The source pane's font when no preference names one, as app.css declares it.</summary>
    public string? DefaultSourceFont => _host?.DefaultSourceFont;

    /// <summary>The preview's font when no preference names one, as app.css declares it.</summary>
    public string? DefaultPreviewFont => _host?.DefaultPreviewFont;

    /// <summary>The font the source pane is actually drawn in, whatever was asked for.</summary>
    public string? ResolvedSourceFont => _host?.ResolvedSourceFont;

    /// <summary>The font the preview is actually drawn in, whatever was asked for.</summary>
    public string? ResolvedPreviewFont => _host?.ResolvedPreviewFont;

    /// <summary>
    /// Raised when the shell has re-measured which fonts are in use. Forwarded from the host
    /// so the preferences dialog, which has no business knowing about the host, can listen.
    /// </summary>
    public event EventHandler? FontsResolved;

    /// <summary>
    /// Pushes the preferences the web surface owns down to it.
    ///
    /// Called by the preferences dialog after every change, and once at startup so a saved
    /// font or tab size is in force before the first document is shown. Cheap enough to call
    /// on each keystroke of a spin box: it is one posted message, and the shell applies it
    /// in a single pass.
    /// </summary>
    public async Task ApplyPreviewPreferencesAsync()
    {
        if (_host is null)
        {
            return;
        }

        try
        {
            await _host.ApplyPreferencesAsync(PreviewPreferences.FromSettings(_settings.Current))
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // A preference that does not reach the shell is a cosmetic failure. It must not
            // take the dialog down with it, and the value is saved either way.
            _logger.LogWarning(ex, "Could not apply preferences to the preview.");
        }
    }

    [RelayCommand]
    private Task ZoomInAsync() => ApplyZoomAsync(new ZoomLevel(ActiveZoomPercent).In());

    [RelayCommand]
    private Task ZoomOutAsync() => ApplyZoomAsync(new ZoomLevel(ActiveZoomPercent).Out());

    [RelayCommand]
    private Task ZoomResetAsync() => ApplyZoomAsync(ZoomLevel.Normal);

    private async Task ApplyZoomAsync(ZoomLevel zoom)
    {
        ActiveZoomPercent = zoom.Percent;
        PersistZoom(ActivePane, zoom.Percent);

        if (_host is not null)
        {
            await _host.SetZoomAsync(ActivePane, zoom).ConfigureAwait(true);
        }

        RestoreDocumentFocusAfterChrome();
    }

    /// <summary>Zooms both panes together, keeping a side-by-side reading balanced.</summary>
    [RelayCommand]
    private Task ZoomBothInAsync() => ApplyZoomBothAsync(1);

    [RelayCommand]
    private Task ZoomBothOutAsync() => ApplyZoomBothAsync(-1);

    [RelayCommand]
    private Task ZoomBothResetAsync() => ApplyZoomBothAsync(0);

    private async Task ApplyZoomBothAsync(int direction)
    {
        AppSettings current = _settings.Current;

        ZoomLevel source = Step(new ZoomLevel(current.SourceZoomPercent), direction);
        ZoomLevel preview = Step(new ZoomLevel(current.PreviewZoomPercent), direction);

        _settings.Update(s => s with
        {
            SourceZoomPercent = source.Percent,
            PreviewZoomPercent = preview.Percent,
        });

        ActiveZoomPercent = ActivePane == EditorPane.Source ? source.Percent : preview.Percent;

        if (_host is not null)
        {
            await _host.SetZoomAsync(EditorPane.Source, source).ConfigureAwait(true);
            await _host.SetZoomAsync(EditorPane.Preview, preview).ConfigureAwait(true);
        }

        RestoreDocumentFocusAfterChrome();

        static ZoomLevel Step(ZoomLevel from, int direction) => direction switch
        {
            > 0 => from.In(),
            < 0 => from.Out(),
            _ => ZoomLevel.Normal,
        };
    }

    /*
        Each of the View menu's toggles is split in two: a command, which flips the value and
        then puts the keyboard back in the document, and a setter, which only does the work.

        The preferences dialog drives the setters. It has to: the focus restore exists for a
        menu item, which hands the keyboard back as it closes, and running it while a modal
        dialog is open would post focus into the WebView behind that dialog - so the next key
        the user pressed would be typed into the document rather than into the preferences
        they were still editing.

        The setters are also idempotent, which the commands are not. A checkbox reports its
        state on every change, including the ones the dialog itself made when it was
        populated, and a toggle would invert those back.
    */

    [RelayCommand]
    private async Task ToggleScrollSyncAsync()
    {
        await SetScrollSyncAsync(!ScrollSyncEnabled).ConfigureAwait(true);

        RestoreDocumentFocusAfterChrome();
    }

    public async Task SetScrollSyncAsync(bool enabled)
    {
        if (ScrollSyncEnabled == enabled)
        {
            return;
        }

        ScrollSyncEnabled = enabled;
        _settings.Update(s => s with { ScrollSyncEnabled = enabled });

        if (_host is not null)
        {
            await _host.SetScrollSyncAsync(enabled).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ToggleWordWrapAsync()
    {
        await SetWordWrapAsync(!WordWrapEnabled).ConfigureAwait(true);

        RestoreDocumentFocusAfterChrome();
    }

    public async Task SetWordWrapAsync(bool enabled)
    {
        if (WordWrapEnabled == enabled)
        {
            return;
        }

        WordWrapEnabled = enabled;
        _settings.Update(s => s with { WordWrapEnabled = enabled });

        if (_host is not null)
        {
            await _host.SetWordWrapAsync(enabled).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ToggleLineNumbersAsync()
    {
        await SetLineNumbersAsync(!LineNumbersEnabled).ConfigureAwait(true);

        RestoreDocumentFocusAfterChrome();
    }

    public async Task SetLineNumbersAsync(bool enabled)
    {
        if (LineNumbersEnabled == enabled)
        {
            return;
        }

        LineNumbersEnabled = enabled;
        _settings.Update(s => s with { ShowLineNumbers = enabled });

        if (_host is not null)
        {
            await _host.SetLineNumbersAsync(enabled).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ToggleShowWhitespaceAsync()
    {
        await SetShowWhitespaceAsync(!ShowWhitespaceEnabled).ConfigureAwait(true);

        RestoreDocumentFocusAfterChrome();
    }

    public async Task SetShowWhitespaceAsync(bool enabled)
    {
        if (ShowWhitespaceEnabled == enabled)
        {
            return;
        }

        ShowWhitespaceEnabled = enabled;
        _settings.Update(s => s with { ShowWhitespace = enabled });

        if (_host is not null)
        {
            await _host.SetShowWhitespaceAsync(enabled).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Whether a file that changes on disk under an unmodified document is taken silently.
    ///
    /// Off means every external change waits to be looked at, clean buffer or not. The setting
    /// has existed since before the change banner did, defaulted on, and appeared in no menu -
    /// a setting nobody could reach.
    /// </summary>
    [RelayCommand]
    private void ToggleReloadOnExternalChange()
    {
        SetReloadOnExternalChange(!ReloadOnExternalChangeEnabled);

        RestoreDocumentFocusAfterChrome();
    }

    public void SetReloadOnExternalChange(bool enabled)
    {
        if (ReloadOnExternalChangeEnabled == enabled)
        {
            return;
        }

        ReloadOnExternalChangeEnabled = enabled;
        _settings.Update(s => s with { ReloadOnExternalChange = enabled });
    }

    /// <summary>
    /// Turns the squiggles on and off. Switching them off clears every tab at once, rather
    /// than leaving stale marks on the ones not currently in front.
    /// </summary>
    [RelayCommand]
    private async Task ToggleDiagnosticsAsync()
    {
        await ApplyDiagnosticsToggleAsync().ConfigureAwait(true);

        RestoreDocumentFocusAfterChrome();
    }

    private Task ApplyDiagnosticsToggleAsync() => SetDiagnosticsAsync(!DiagnosticsEnabled);

    public async Task SetDiagnosticsAsync(bool enabled)
    {
        if (DiagnosticsEnabled == enabled)
        {
            return;
        }

        DiagnosticsEnabled = enabled;
        _settings.Update(s => s with { ShowDiagnostics = enabled });

        if (_host is null)
        {
            return;
        }

        if (!DiagnosticsEnabled)
        {
            await _host.ClearDiagnosticsAsync().ConfigureAwait(true);

            return;
        }

        // Back on: the active document has not changed, so nothing would re-publish it.
        if (_workspace.Active is { } document)
        {
            RenderedMarkdown rendered = await RenderAsync(document.Text).ConfigureAwait(true);

            await PublishDiagnosticsAsync(document.Id, document.Text, document.Path, rendered).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Checks a document and sends the result to the editor.
    ///
    /// Called wherever a fresh render exists, so the analysis rides along with work already
    /// being done and inherits the same debounce that keeps re-rendering off every keystroke.
    /// </summary>
    private async Task PublishDiagnosticsAsync(
        Guid documentId,
        string text,
        string? path,
        RenderedMarkdown rendered)
    {
        if (_host is null || !DiagnosticsEnabled)
        {
            return;
        }

        try
        {
            IReadOnlyList<Diagnostic> found = await Task.Run(() => _analyzer.Analyze(new AnalysisRequest
            {
                Text = text,
                DocumentPath = path,
                Links = rendered.Links,
                Outline = rendered.Outline,
                Anchors = rendered.Anchors,
            })).ConfigureAwait(true);

            await _host.SetDiagnosticsAsync(documentId, found).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // Checking a document is a convenience. Failing at it must not disturb editing.
            _logger.LogError(ex, "Could not analyze document {DocumentId}.", documentId);
        }
    }

    [RelayCommand]
    private async Task ToggleWrapGlyphAsync()
    {
        await SetWrapGlyphAsync(!ShowWrapGlyphEnabled).ConfigureAwait(true);

        RestoreDocumentFocusAfterChrome();
    }

    public async Task SetWrapGlyphAsync(bool enabled)
    {
        if (ShowWrapGlyphEnabled == enabled)
        {
            return;
        }

        ShowWrapGlyphEnabled = enabled;
        _settings.Update(s => s with { ShowWrapGlyph = enabled });

        if (_host is not null)
        {
            await _host.SetWrapGlyphAsync(enabled).ConfigureAwait(true);
        }
    }

    // -------------------------------------------------------------------- edit

    /// <summary>Runs an Edit-menu command against the source pane.</summary>
    [RelayCommand]
    private async Task EditActionAsync(string? command)
    {
        if (_host is null || string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        // Clipboard commands are handled here rather than in the editor. A browser permits
        // copy and paste only during a trusted user gesture, and a click on a native menu
        // is not one, so the editor's own clipboard actions do nothing when invoked this
        // way. The host has no such restriction.
        switch (command)
        {
            case "copy":
                await _host.RequestSelectionForClipboardAsync(cut: false).ConfigureAwait(true);
                return;

            case "cut":
                await _host.RequestSelectionForClipboardAsync(cut: true).ConfigureAwait(true);
                return;

            case "paste":
                await PasteFromClipboardAsync().ConfigureAwait(true);
                return;

            // Find All is the app's own window rather than one of the editor's actions, so it
            // never reaches the shell.
            case "findAll":
                await FindAllCommand.ExecuteAsync(null).ConfigureAwait(true);
                return;

            default:
                await _host.RunEditorCommandAsync(command).ConfigureAwait(true);
                return;
        }
    }

    /// <summary>Opens Find All, seeded with whatever is selected in the editor.</summary>
    [RelayCommand]
    private async Task FindAllAsync() => _findAll.Show(await SelectedTermAsync().ConfigureAwait(true));

    /// <summary>
    /// The editor's selection, when it is worth searching for.
    ///
    /// Read from the editor rather than from this app's copy of the document, which trails by
    /// a debounce interval: selecting a word just typed and pressing Ctrl+Shift+F would
    /// otherwise seed the box with what the line used to say. A selection spanning lines is
    /// not a search term, so it seeds nothing and the box keeps whatever it already held.
    /// </summary>
    private async Task<string?> SelectedTermAsync()
    {
        if (_host is null)
        {
            return null;
        }

        if (await _host.GetEditContextAsync().ConfigureAwait(true) is not { } context)
        {
            return null;
        }

        TextRange selection = context.Selection.Ordered;

        if (selection.IsEmpty
            || !selection.IsSingleLine
            || context.LineAt(selection.Start.Line) is not { } line)
        {
            return null;
        }

        int start = Math.Clamp(selection.Start.Column, 0, line.Length);
        int end = Math.Clamp(selection.End.Column, start, line.Length);

        return end > start ? line[start..end] : null;
    }

    /// <summary>
    /// Takes the editor to a result picked in the Find All window.
    ///
    /// The reveal is queued behind whatever the activation queued rather than sent straight
    /// after it. Workspace changes are applied on a chain, so asking for another tab does not
    /// switch to it by the time this returns, and the shell drops a selection aimed at a tab
    /// it has not been given yet - which would make the click do nothing at all.
    /// </summary>
    private void OnFindMatchActivated(object? sender, FindMatchActivatedEventArgs e)
    {
        if (_workspace.Find(e.DocumentId) is null)
        {
            StatusText = "That document is no longer open";
            return;
        }

        if (_workspace.Active?.Id != e.DocumentId)
        {
            _workspace.Activate(e.DocumentId);
        }

        _ui.Post(() => _workspaceChain = RevealAfterAsync(_workspaceChain, e));
    }

    /// <summary>Swallows its own failures, so the workspace chain can never fault.</summary>
    private async Task RevealAfterAsync(Task previous, FindMatchActivatedEventArgs e)
    {
        await previous.ConfigureAwait(true);

        if (_host is null)
        {
            return;
        }

        if (_workspace.Find(e.DocumentId) is not { } document)
        {
            StatusText = "That document is no longer open";
            return;
        }

        if (Locate(document.Text, e.Match) is not { } match)
        {
            StatusText = $"That match is no longer in {document.DisplayName}";
            return;
        }

        try
        {
            // A match cannot be shown in a pane that is not on screen. Split rather than
            // source only, so the preview the user was reading stays beside it.
            //
            // Switched here rather than by the shell when the selection arrives: the two
            // messages are delivered in order, so this way the editor has been laid out by
            // the time it is asked to reveal a line. Doing it the other way round reveals
            // against a pane that is still display:none, and the match ends up selected
            // somewhere off screen.
            if (ViewMode == ViewMode.Preview)
            {
                await ApplyViewModeAsync(ViewMode.SideBySide, persist: true, takeFocus: false).ConfigureAwait(true);
            }

            await _host
                .SelectRangeAsync(e.DocumentId, match.Line, match.Column, match.Length, e.FocusEditor)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not reveal a Find All match.");
        }
    }

    /// <summary>
    /// Where a match is now, or null when the text it found has gone.
    ///
    /// Results are a snapshot, so by the time one is picked the document may have been edited
    /// above it and every line number below moved. Selecting the recorded position blindly
    /// would highlight whatever happens to be there instead, which is how a results list
    /// quietly starts lying about itself.
    ///
    /// The recorded text is checked first, which is the case almost every time and costs one
    /// line. Only when it has moved is the document searched for that text again, taking the
    /// occurrence nearest the original line — the one the user was looking at.
    /// </summary>
    private static FindMatch? Locate(string text, FindMatch match)
    {
        string found = match.Text;

        if (DocumentFinder.LineAt(text, match.Line) is { } line
            && match.Column + found.Length <= line.Length
            && string.CompareOrdinal(line, match.Column, found, 0, found.Length) == 0)
        {
            return match;
        }

        FindResults again = DocumentFinder.Find(
            new FindQuery { Term = found, MatchCase = true },
            [new FindDocument(Guid.Empty, string.Empty, string.Empty, text)]);

        if (again.Documents.Count == 0)
        {
            return null;
        }

        FindMatch? nearest = null;
        int distance = int.MaxValue;

        foreach (FindMatch candidate in again.Documents[0].Matches)
        {
            int gap = Math.Abs(candidate.Line - match.Line);

            if (gap >= distance)
            {
                continue;
            }

            distance = gap;
            nearest = candidate;
        }

        return nearest;
    }

    private async Task PasteFromClipboardAsync()
    {
        try
        {
            DataPackageView view = Clipboard.GetContent();

            if (!view.Contains(StandardDataFormats.Text))
            {
                return;
            }

            string text = await view.GetTextAsync();

            if (!string.IsNullOrEmpty(text) && _host is not null)
            {
                await _host.InsertTextAsync(text).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            // The clipboard is shared and can be held open by another process.
            _logger.LogWarning(ex, "Could not read the clipboard.");
        }
    }

    /// <summary>
    /// Copies whatever is selected in the preview pane, which holds a selection of its own
    /// quite separate from the editor's.
    /// </summary>
    [RelayCommand]
    private async Task CopyPreviewSelectionAsync()
    {
        if (_host is not null)
        {
            await _host.RequestPreviewSelectionForClipboardAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Selects the whole rendered document, for a copy of all of it.</summary>
    [RelayCommand]
    private async Task SelectAllInPreviewAsync()
    {
        if (_host is not null)
        {
            await _host.SelectAllInPreviewAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Places text either pane reported onto the Windows clipboard.</summary>
    private void OnSelectionCopied(object? sender, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            StatusText = "Nothing selected";
            return;
        }

        ClipboardText.Set(text, _logger);
    }

    // ----------------------------------------------------------- recent files

    [RelayCommand]
    private Task TogglePinAsync(string? path) =>
        string.IsNullOrWhiteSpace(path) ? Task.CompletedTask : _recent.TogglePinAsync(path);

    [RelayCommand]
    private Task RemoveRecentAsync(string? path) =>
        string.IsNullOrWhiteSpace(path) ? Task.CompletedTask : _recent.RemoveAsync(path);

    /// <summary>
    /// Opens the folder behind a recent entry. The row already shows where the file lives, so
    /// the path itself is the control: reading it and going there are the same gesture.
    /// </summary>
    [RelayCommand]
    private void RevealRecent(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            RevealInExplorer(path);
        }
    }

    /// <summary>
    /// Empties the recent list, asking first: there is no undo, and the entries are the only
    /// record of where those files were.
    ///
    /// Pinning is how a user marks an entry as worth keeping, so when any are pinned the
    /// prompt offers to spare them rather than deciding on their behalf. With nothing pinned
    /// that second option would be noise, and the prompt is a plain confirm.
    /// </summary>
    [RelayCommand]
    private async Task ClearRecentAsync()
    {
        int total = _recent.Items.Count;

        if (total == 0)
        {
            return;
        }

        int pinned = _recent.Items.Count(item => item.IsPinned);
        string entries = total == 1 ? "1 entry" : $"{total} entries";

        ConfirmResult answer = pinned == 0
            ? await _dialogs.ConfirmAsync(
                "Clear recent files?",
                $"{entries} will be removed from the list. The files themselves are left alone.",
                primaryText: "Clear all").ConfigureAwait(true)
            : await _dialogs.ConfirmAsync(
                "Clear recent files?",
                $"{entries} will be removed from the list, {pinned} of them pinned. "
                + "The files themselves are left alone.",
                primaryText: "Clear everything",
                secondaryText: "Keep pinned").ConfigureAwait(true);

        if (answer == ConfirmResult.Cancel)
        {
            return;
        }

        RecentClearScope scope = answer == ConfirmResult.Primary
            ? RecentClearScope.Everything
            : RecentClearScope.Unpinned;

        await _recent.ClearAsync(scope).ConfigureAwait(true);

        StatusText = scope == RecentClearScope.Everything
            ? "Recent files cleared"
            : "Cleared all but the pinned files";
    }

    [RelayCommand(CanExecute = nameof(CanActOnFile))]
    private void RevealInFolder()
    {
        if (!string.IsNullOrEmpty(DocumentPath))
        {
            RevealInExplorer(DocumentPath);
        }

        // Explorer takes the foreground, so this decides where the keyboard is when the
        // window is come back to rather than where it is now.
        RestoreDocumentFocusAfterChrome();
    }

    /// <summary>
    /// Shows a path in Explorer: the file selected inside its folder while it is still there,
    /// otherwise the folder on its own. A recent entry outlives the file it points at, and
    /// /select on something missing leaves Explorer to pick a landing spot of its own.
    /// </summary>
    private void RevealInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true,
                });

                return;
            }

            string? folder = Path.GetDirectoryName(path);

            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                StatusText = "That folder is no longer there";
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folder}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not open Explorer for {Path}.", path);
        }
    }

    [RelayCommand(CanExecute = nameof(CanActOnFile))]
    private void CopyPath()
    {
        StatusText = string.IsNullOrEmpty(DocumentPath)
            ? "This document has not been saved yet"
            : ClipboardText.Set(DocumentPath, _logger)
                ? "Path copied"
                : "The clipboard is in use";

        RestoreDocumentFocusAfterChrome();
    }

    // ------------------------------------------------------------------- export

    /// <summary>
    /// Puts the rendered document on the clipboard as rich text, for pasting into Word,
    /// Outlook or anything else that keeps formatting.
    ///
    /// Takes the preview's selection when there is one, and the whole document otherwise,
    /// which is how copying behaves everywhere else. The markdown source goes on as the
    /// plain-text flavour, so pasting into an editor still gives markdown.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanActOnContent))]
    private async Task CopyAsRichTextAsync()
    {
        if (_workspace.Active is not { } document || _host is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = "Copying...";

            if (await _host.GetPreviewHtmlAsync().ConfigureAwait(true) is not { } selection
                || string.IsNullOrWhiteSpace(selection.Html))
            {
                await _dialogs.ShowMessageAsync(
                    "Nothing to copy",
                    "The preview has not finished rendering yet. Try again in a moment.").ConfigureAwait(true);

                return;
            }

            // Embedding images means reading and encoding them, which is not something to
            // do on the UI thread for a document full of screenshots.
            string fragment = await Task.Run(() => _packager.BuildFragment(selection.Html, document.Path))
                .ConfigureAwait(true);

            bool wholeDocument = selection.Text.Length == 0;

            StatusText = ClipboardHtml.Set(fragment, wholeDocument ? document.Text : selection.Text, _logger)
                ? wholeDocument ? "Copied as rich text" : "Selection copied as rich text"
                : "The clipboard is in use";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not copy as rich text.");

            await _dialogs.ShowMessageAsync("Could not copy", ex.Message).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Exports the active document as a self-contained HTML file.
    ///
    /// The markup comes from the live preview rather than a fresh render, so diagrams,
    /// maths and highlighting are exported exactly as they appear on screen.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanActOnContent))]
    private async Task ExportHtmlAsync()
    {
        await WriteHtmlExportAsync().ConfigureAwait(true);

        RestoreDocumentFocusAfterChrome();
    }

    private async Task WriteHtmlExportAsync()
    {
        if (_workspace.Active is not { } document || _host is null || _exporter is null)
        {
            return;
        }

        string? path = await _fileDialogs
            .PickExportFileAsync(SuggestedExportName(document, ".html"), "HTML document", [".html", ".htm"])
            .ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = "Exporting HTML...";

            string rendered = await _host.GetRenderedHtmlAsync().ConfigureAwait(true);

            if (string.IsNullOrWhiteSpace(rendered))
            {
                await _dialogs.ShowMessageAsync(
                    "Nothing to export",
                    "The preview has not finished rendering yet. Try again in a moment.")
                    .ConfigureAwait(true);
                return;
            }

            await _exporter
                .WriteAsync(path, document.DisplayName, rendered, document.Path)
                .ConfigureAwait(true);

            await AnnounceExportAsync(path).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not export HTML to {Path}.", path);
            await _dialogs.ShowMessageAsync("Could not export", ex.Message).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Exports the active document as a PDF, after asking for page setup.</summary>
    [RelayCommand(CanExecute = nameof(CanActOnContent))]
    private async Task ExportPdfAsync()
    {
        await WritePdfExportAsync().ConfigureAwait(true);

        RestoreDocumentFocusAfterChrome();
    }

    private async Task WritePdfExportAsync()
    {
        if (_workspace.Active is not { } document || _host is null)
        {
            return;
        }

        PdfPageSetup? setup = await _exportDialogs
            .RequestPdfSetupAsync(document.DisplayName, _settings.Current.PdfDefaults)
            .ConfigureAwait(true);

        if (setup is null)
        {
            return;
        }

        // Saved on accepting the dialog rather than on a successful write: the answer is
        // what the user chose, and a failed export does not make it the wrong choice.
        _settings.Update(s => s with { PdfSetup = setup });

        string? path = await _fileDialogs
            .PickExportFileAsync(SuggestedExportName(document, ".pdf"), "PDF document", [".pdf"])
            .ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = "Exporting PDF...";

            await _host.ExportPdfAsync(path, setup).ConfigureAwait(true);
            await AnnounceExportAsync(path).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogError(ex, "Could not export PDF to {Path}.", path);
            await _dialogs.ShowMessageAsync("Could not export", ex.Message).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Prints the preview, as opposed to exporting it to a file.
    ///
    /// Two steps, and the app owns both: the Windows print dialog asks which printer, then
    /// the preview is printed to it. Neither dialog the WebView can raise is any use - one
    /// is a browser window that prints a browser's header and footer, the other never
    /// appears at all - and printing this way is what allows that band to be switched off.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanActOnContent))]
    private async Task PrintAsync()
    {
        await SendToPrinterAsync().ConfigureAwait(true);

        RestoreDocumentFocusAfterChrome();
    }

    private async Task SendToPrinterAsync()
    {
        if (_host is null)
        {
            return;
        }

        // Paper and orientation come from the dialog; margins and backgrounds have no field
        // in it, so they come from the same page setup a PDF export starts on - which is now
        // the one held in preferences, so paper chosen once applies to both.
        PrintJob? job = await _printDialogs
            .PickPrinterAsync(_settings.Current.PdfDefaults)
            .ConfigureAwait(true);

        if (job is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = $"Printing to {job.PrinterName}...";

            await _host.PrintAsync(job).ConfigureAwait(true);

            StatusText = $"Sent to {job.PrinterName}";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            _logger.LogError(ex, "Could not print to {Printer}.", job.PrinterName);
            await _dialogs.ShowMessageAsync("Could not print", ex.Message).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// The document's own name with the export extension, so exporting notes.md offers
    /// notes.pdf. An unsaved document has no name to borrow, so it falls back to Untitled.
    /// </summary>
    private static string SuggestedExportName(MarkdownDocument document, string extension) =>
        document.Path is { } path
            ? Path.GetFileNameWithoutExtension(path) + extension
            : "Untitled" + extension;

    private async Task AnnounceExportAsync(string path)
    {
        StatusText = $"Exported {Path.GetFileName(path)}";

        try
        {
            // Opening the result is the usual next step, and it doubles as confirmation
            // that the file is readable by whatever handles that type.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            // No handler registered for the type, or the shell refused. The file is still
            // written, so this is worth a line in the log and nothing more.
            _logger.LogInformation(ex, "Exported {Path} but could not open it.", path);
        }

        await Task.CompletedTask.ConfigureAwait(true);
    }

    // ------------------------------------------------------------------ scrolling

    [RelayCommand(CanExecute = nameof(CanActOnContent))]
    private Task ScrollToTopAsync() => ScrollToEdgeAsync(toEnd: false);

    [RelayCommand(CanExecute = nameof(CanActOnContent))]
    private Task ScrollToBottomAsync() => ScrollToEdgeAsync(toEnd: true);

    /// <summary>
    /// Moves the pane the user was last in. With scroll sync on in split view both panes go,
    /// because leaving them deliberately misaligned would undo the point of the sync.
    /// </summary>
    private Task ScrollToEdgeAsync(bool toEnd)
    {
        if (_host is null)
        {
            return Task.CompletedTask;
        }

        bool both = ViewMode == ViewMode.SideBySide && ScrollSyncEnabled;

        return _host.ScrollToEdgeAsync(ActivePane, toEnd, both);
    }

    // ----------------------------------------------------------------- authoring

    /// <summary>
    /// Applies one of the Format menu's markdown commands to whatever is selected.
    ///
    /// One command covers the whole menu, taking the construct's name as its parameter,
    /// the same way <see cref="EditActionCommand"/> covers the Edit menu. Unlike the
    /// formatter, this reads the selection and the lines around it back from the editor
    /// rather than using this class's own copy of the document, which trails a keystroke
    /// behind and would make a fast Ctrl+B act on stale text.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanFormat))]
    private async Task ApplyMarkdownAsync(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        if (!TryParseMarkdownCommand(command, out MarkdownEditCommand parsed))
        {
            _logger.LogDebug("Ignoring unknown authoring command {Command}.", command);

            return;
        }

        await RunEditAsync(context => _editor.Apply(parsed, context), command).ConfigureAwait(true);
    }

    /// <summary>Puts a snippet in at the caret.</summary>
    [RelayCommand(CanExecute = nameof(CanFormat))]
    private async Task InsertSnippetAsync(Snippet? snippet)
    {
        if (snippet is null)
        {
            return;
        }

        // Read now rather than when the menu was built, so a snippet edited in another
        // editor since then is the one that gets inserted.
        string? body = await _snippets.ReadBodyAsync(snippet).ConfigureAwait(true);

        if (string.IsNullOrEmpty(body))
        {
            StatusText = $"Could not read {snippet.Name}";

            return;
        }

        await RunEditAsync(context => _editor.Insert(body, context), snippet.Name).ConfigureAwait(true);

        StatusText = $"Inserted {snippet.Name}";
    }

    /// <summary>
    /// The snippets to show in a menu. Gathered afresh every time one opens, which is what
    /// spares the app a folder watcher.
    /// </summary>
    public IReadOnlyList<Snippet> ListSnippets(SnippetGroup group) => _snippets.List(group);

    /// <summary>
    /// Applied from the shell as the caret moves. A null context means the selection was
    /// too large to report on, which reads as nothing active.
    ///
    /// Every property is re-announced afterwards whether or not it changed, and that is the
    /// whole point of doing it by hand. A ToggleButton flips itself the moment it is
    /// clicked, and a one-way binding only pushes back when the source raises a change — so
    /// a click that leaves the state exactly as it was would leave the button stuck on,
    /// with nothing to ever turn it off again.
    /// </summary>
    public void UpdateMarkState(EditContext? context)
    {
        MarkdownMarkState marks = context is null ? MarkdownMarkState.None : _editor.Describe(context);

        IsBoldActive = marks.Bold;
        IsItalicActive = marks.Italic;
        IsStrikethroughActive = marks.Strikethrough;
        IsInlineCodeActive = marks.InlineCode;
        IsBulletListActive = marks.BulletList;
        IsNumberedListActive = marks.NumberedList;
        IsTaskListActive = marks.TaskList;
        IsBlockquoteActive = marks.Blockquote;
        HeadingLabel = marks.HeadingLevel > 0 ? $"H{marks.HeadingLevel}" : "Heading";

        OnPropertyChanged(nameof(IsBoldActive));
        OnPropertyChanged(nameof(IsItalicActive));
        OnPropertyChanged(nameof(IsStrikethroughActive));
        OnPropertyChanged(nameof(IsInlineCodeActive));
        OnPropertyChanged(nameof(IsBulletListActive));
        OnPropertyChanged(nameof(IsNumberedListActive));
        OnPropertyChanged(nameof(IsTaskListActive));
        OnPropertyChanged(nameof(IsBlockquoteActive));
        OnPropertyChanged(nameof(HeadingLabel));
    }

    /// <summary>
    /// Applied from the shell whenever the undo stack could have moved: an edit, an undo,
    /// a redo, a reformat, or a switch to a tab with a history of its own.
    ///
    /// Unlike <see cref="UpdateMarkState"/> this announces only on a real change. These
    /// drive IsEnabled rather than IsChecked, and nothing flips them behind the binding's
    /// back the way a clicked ToggleButton does, so there is nothing to correct.
    /// </summary>
    public void UpdateHistoryState(bool canUndo, bool canRedo)
    {
        if (_shellCanUndo != canUndo)
        {
            _shellCanUndo = canUndo;
            OnPropertyChanged(nameof(CanUndo));
        }

        if (_shellCanRedo != canRedo)
        {
            _shellCanRedo = canRedo;
            OnPropertyChanged(nameof(CanRedo));
        }
    }

    /// <summary>
    /// The shape every authoring action takes: read the live selection from the editor,
    /// work out the edits, hand them back.
    ///
    /// Shared between the markdown commands and snippet insertion so the focus handling
    /// and the error handling exist once rather than twice.
    /// </summary>
    private async Task RunEditAsync(Func<EditContext, EditResult> compute, string what)
    {
        // Checked here rather than trusting CanExecute: the accelerators call Execute
        // directly, and ICommand.Execute does not consult it. One predicate, four entry
        // points — menu, toolbar, window accelerator and the shell's own keybindings.
        if (_host is null || !CanFormat)
        {
            return;
        }

        try
        {
            if (await _host.GetEditContextAsync().ConfigureAwait(true) is not { } context)
            {
                return;
            }

            EditResult result = compute(context);

            if (!result.IsEmpty)
            {
                await _host.ApplyEditsAsync(result).ConfigureAwait(true);
            }
            else
            {
                // Applying edits hands focus back to the editor. A command that decided to
                // do nothing must too, or focus stays on the toolbar button that was just
                // clicked and the next keystroke is lost.
                await _host.FocusEditorAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{What} failed.", what);
        }
    }

    /// <summary>
    /// Matches a menu parameter or shortcut name against the command enum. The names line
    /// up by design, so the menu, the accelerators and the shell all spell each command
    /// the same way.
    /// </summary>
    private static bool TryParseMarkdownCommand(string name, out MarkdownEditCommand command)
    {
        command = default;
        string trimmed = name.Trim();

        // Enum.TryParse would happily take "3" and hand back whatever is third.
        return trimmed.Length > 0
            && !char.IsDigit(trimmed[0])
            && Enum.TryParse(trimmed, ignoreCase: true, out command)
            && Enum.IsDefined(command);
    }

    // ---------------------------------------------------------------- formatting

    /// <summary>
    /// Tidies the active document, or just the selected lines when there is a selection.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanActOnContent))]
    private async Task FormatDocumentAsync()
    {
        _logger.LogInformation("Format Document invoked. host={Host} content={Content}", _host is not null, HasContent);

        if (_workspace.Active is not { } document || _host is null)
        {
            return;
        }

        LineRange? selection = await _host.GetSelectionRangeAsync().ConfigureAwait(true);

        await ApplyFormatAsync(document.Id, document.Text, selection, announce: true).ConfigureAwait(true);
    }

    /// <summary>Opens the rules dialog, and formats if the user chooses to.</summary>
    [RelayCommand(CanExecute = nameof(CanActOnContent))]
    private async Task ShowFormatOptionsAsync()
    {
        // The selection is read before the dialog opens: showing it steals focus from the
        // editor, and by the time it closes there is nothing selected to ask about.
        LineRange? selection = _host is null
            ? null
            : await _host.GetSelectionRangeAsync().ConfigureAwait(true);

        int selectedLines = selection is { } range ? range.End - range.Start + 1 : 0;

        FormatChoice? chosen = await _formatDialogs
            .RequestFormatRulesAsync(_settings.Current.Formatting, selectedLines)
            .ConfigureAwait(true);

        if (chosen is not { } choice)
        {
            return;
        }

        /*
            Every rule the dialog offers is remembered, with one deliberate exception: the
            wrap column is put back to the saved one before the rules are stored, so a width
            typed on the dialog governs this reformat and nothing after it.

            The reasoning is that the other controls answer "how should my markdown look",
            which is a standing preference, while the wrap width is routinely a one-off - a
            file that has to fit someone else's 72, a table of contents pulled in at 100 -
            and having that silently become the new default is how a whole tree of documents
            ends up rewrapped by accident. The width that sticks is set in one place only,
            Preferences | Editor, and the dialog says so under the box.

            The typed width still reaches the formatter: choice.Rules is passed to
            ApplyFormatAsync below rather than being read back out of the settings.
        */
        _settings.Update(s => s with
        {
            FormatRules = choice.Rules with { WrapColumn = s.Formatting.WrapColumn },
        });

        if (!HasContent)
        {
            return;
        }

        await ApplyFormatAsync(
            _workspace.Active!.Id,
            _workspace.Active!.Text,
            choice.SelectionOnly ? selection : null,
            announce: true,
            overrideOptions: choice.Rules).ConfigureAwait(true);
    }

    /// <summary>Tidies every open document in one go.</summary>
    [RelayCommand(CanExecute = nameof(CanActOnContent))]
    private async Task FormatAllDocumentsAsync()
    {
        if (_host is null || _workspace.Documents.Count == 0)
        {
            return;
        }

        ConfirmResult answer = await _dialogs.ConfirmAsync(
            "Format every open document?",
            $"{_workspace.Documents.Count} documents will be reformatted. Each becomes unsaved, "
                + "and each can be undone separately with Ctrl+Z.",
            "Format all").ConfigureAwait(true);

        if (answer != ConfirmResult.Primary)
        {
            return;
        }

        int touched = 0;

        // A copy, because formatting raises workspace events that can reorder the list.
        foreach (MarkdownDocument document in _workspace.Documents.ToList())
        {
            if (await ApplyFormatAsync(document.Id, document.Text, null, announce: false).ConfigureAwait(true))
            {
                touched++;
            }
        }

        StatusText = touched == 0
            ? "Every open document was already tidy"
            : $"Formatted {touched} of {_workspace.Documents.Count} documents";
    }

    /// <summary>"1 line" or "4 lines", for status text that reads as a sentence.</summary>
    private static string Lines(int count) => count == 1 ? "1 line was" : $"{count} lines were";

    /// <summary>
    /// Runs the formatter over one document and pushes the result into the editor.
    /// </summary>
    /// <param name="overrideOptions">
    /// Rules to format with just this once, or null to use the saved ones. Only the Format
    /// Markdown dialog passes it, and only so the wrap width typed there can be honoured
    /// without being written to the settings - see ShowFormatOptionsAsync.
    /// </param>
    /// <returns>True when the document actually changed.</returns>
    private async Task<bool> ApplyFormatAsync(
        Guid documentId,
        string text,
        LineRange? selection,
        bool announce,
        FormatOptions? overrideOptions = null)
    {
        if (_host is null)
        {
            return false;
        }

        _logger.LogInformation(
            "Formatting {Id}: {Length} characters, selection={Selection}",
            documentId, text.Length, selection is null ? "none" : "yes");

        FormatOptions options = overrideOptions ?? _settings.Current.Formatting;

        try
        {
            // Formatting a long document is measurable, and it must not stall the UI.
            FormattedMarkdown result = await Task.Run(() => selection is { } range
                ? _formatter.FormatLines(text, range.Start, range.End, options)
                : _formatter.Format(text, options)).ConfigureAwait(true);

            _logger.LogInformation(
                "Formatter finished {Id}: {Changed} changed lines, {Before} -> {After} characters.",
                documentId, result.ChangedLines, text.Length, result.Text.Length);

            if (result.IsUnchanged)
            {
                if (announce)
                {
                    // Said plainly, because the alternative reading of a formatter that does
                    // nothing is that it is broken. Naming the scope also makes it obvious
                    // when only a selection was considered.
                    StatusText = selection is { } tidy
                        ? $"Nothing to change: {Lines(tidy.End - tidy.Start + 1)} already tidy"
                        : "Nothing to change: this document is already tidy";
                }

                return false;
            }

            _workspace.ApplyEdit(documentId, result.Text);

            RenderedMarkdown rendered = await RenderAsync(result.Text).ConfigureAwait(true);
            await _host.ReplaceTextAsync(documentId, result.Text, rendered).ConfigureAwait(true);

            // Formatting fixes most of the style hints outright, so the marks want clearing
            // straight away rather than at the next keystroke.
            await PublishDiagnosticsAsync(
                documentId, result.Text, _workspace.Find(documentId)?.Path, rendered).ConfigureAwait(true);

            _logger.LogInformation("Sent the formatted text for {Id} to the editor.", documentId);

            if (announce)
            {
                StatusText = selection is null
                    ? $"Formatted {result.ChangedLines} line{(result.ChangedLines == 1 ? string.Empty : "s")}"
                    : $"Formatted the selection ({result.ChangedLines} lines)";
            }

            return true;
        }
        catch (Exception ex)
        {
            // A formatter fault must never cost the user their document.
            _logger.LogError(ex, "Formatting failed; the document was left unchanged.");

            if (announce)
            {
                await _dialogs.ShowMessageAsync(
                    "Could not format",
                    "Something went wrong while formatting, so the document was left alone.")
                    .ConfigureAwait(true);
            }

            return false;
        }
    }

    // --------------------------------------------------------------- cheatsheet

    /// <summary>
    /// Ticks the Tools menu item while the cheatsheet is on screen.
    ///
    /// Driven by the window's own visibility rather than by this command, so dismissing the
    /// cheatsheet with its close button unticks the menu item too.
    /// </summary>
    [ObservableProperty]
    public partial bool IsCheatsheetVisible { get; set; }

    /// <summary>
    /// Shows, raises or dismisses the floating markdown cheatsheet.
    ///
    /// Unlike the exports this is not gated on a document being open: the point of the
    /// cheatsheet is to consult it while writing, which includes the moment before there is
    /// anything to write in.
    /// </summary>
    /// <remarks>
    /// <c>AllowConcurrentExecutions</c> because an async command disables itself while it
    /// runs, and bringing the cheatsheet's WebView up the first time takes about a second.
    /// The menu item would be dead for that whole second, so a second click did nothing and
    /// the feature felt like it needed several attempts. The service ignores overlapping
    /// toggles itself, which is the right place for that guard.
    /// </remarks>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ToggleCheatsheetAsync()
    {
        await _cheatsheet.ToggleAsync().ConfigureAwait(true);

        // ToggleMenuFlyoutItem flips its own IsChecked the moment it is clicked. Push the
        // real state back unconditionally: if the toggle did not change visibility, the
        // binding would see no change and leave the tick showing the wrong thing.
        OnPropertyChanged(nameof(IsCheatsheetVisible));
    }

    /// <summary>How many diagram pop-outs are open, which is what enables the menu item.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloseAllDiagramWindowsCommand))]
    public partial int OpenDiagramWindowCount { get; set; }

    /// <summary>
    /// Shows a diagram in a window of its own, or raises the one already showing it.
    ///
    /// Concurrent execution is allowed for the same reason as the cheatsheet: opening a
    /// WebView takes a moment, and a command that disabled itself for that moment would make
    /// double-clicking a second diagram feel broken. The service does the de-duplicating.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ShowDiagramAsync(DiagramActivatedEventArgs activated)
    {
        // Read now rather than on demand: the window has to keep naming its document after
        // that tab has closed, which is the case where the name matters most. The full path
        // goes too, for the header printed on the page.
        DocumentTabViewModel? tab = FindTab(activated.DocumentId);

        string document = tab?.Title ?? string.Empty;
        string path = tab?.Path ?? string.Empty;

        _logger.LogInformation("Opening a diagram from {Document} in its own window.", document);

        await _diagramWindows
            .ShowAsync(activated.DocumentId, activated.Index, activated.Hash, activated.Svg, document, path)
            .ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanCloseAllDiagramWindows))]
    private void CloseAllDiagramWindows()
    {
        _diagramWindows.CloseAll();

        RestoreDocumentFocusAfterChrome();
    }

    private bool CanCloseAllDiagramWindows() => OpenDiagramWindowCount > 0;

    [RelayCommand]
    private void Exit() => ExitRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void About() => AboutRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Support() => SupportRequested?.Invoke(this, EventArgs.Empty);

    // ---------------------------------------------------------------- plumbing

    private Task<RenderedMarkdown> RenderAsync(string text) => Task.Run(() => _renderer.Render(text));

    private async void OnEditorTextChanged(object? sender, EditorTextChangedEventArgs e)
    {
        try
        {
            _workspace.ApplyEdit(e.DocumentId, e.Text);

            // Rendering a large document is measured in milliseconds but happens on every
            // keystroke burst, so it stays off the UI thread.
            RenderedMarkdown rendered = await RenderAsync(e.Text).ConfigureAwait(true);

            if (_host is not null)
            {
                await _host.UpdatePreviewAsync(e.DocumentId, rendered).ConfigureAwait(true);
            }

            await PublishDiagnosticsAsync(
                e.DocumentId, e.Text, _workspace.Find(e.DocumentId)?.Path, rendered).ConfigureAwait(true);

            RestartAutoSaveTimer();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to re-render after an edit.");
        }
    }

    // -------------------------------------------------------------------- autosave

    /// <summary>
    /// Pending delayed autosave, cancelled and replaced on each edit so the countdown
    /// measures a pause in typing rather than time since the first keystroke.
    /// </summary>
    private CancellationTokenSource? _autoSaveCountdown;

    private void RestartAutoSaveTimer()
    {
        AppSettings current = _settings.Current;

        _autoSaveCountdown?.Cancel();
        _autoSaveCountdown?.Dispose();
        _autoSaveCountdown = null;

        if (current.AutoSave != AutoSaveMode.AfterDelay)
        {
            return;
        }

        _autoSaveCountdown = new CancellationTokenSource();

        _ = AutoSaveAfterDelayAsync(
            TimeSpan.FromSeconds(Math.Clamp(
                current.AutoSaveDelaySeconds,
                AppSettings.MinimumAutoSaveDelaySeconds,
                AppSettings.MaximumAutoSaveDelaySeconds)),
            _autoSaveCountdown.Token);
    }

    private async Task AutoSaveAfterDelayAsync(TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a later keystroke, which started its own countdown.
            return;
        }

        await AutoSaveAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Writes every modified document that already has a file, quietly.
    ///
    /// Deliberately not the Save command. Autosave must never put a dialog on screen: it
    /// fires when the user has just clicked into another application, or has paused typing,
    /// and neither is a moment to interrupt them. So an untitled document is skipped rather
    /// than prompting for a location, and a write that fails is logged and shown in the
    /// status bar rather than raised - the document keeps its unsaved changes and Ctrl+S
    /// still reports the problem properly when the user asks for it.
    ///
    /// Format-on-save is honoured, because the user asked for their file to be formatted
    /// when it is written and this is a write.
    /// </summary>
    private async Task AutoSaveAsync()
    {
        // Copied first: saving mutates the workspace, and format-on-save replaces a
        // document's text partway through.
        //
        // A document with an external change waiting is left alone. Its file has been edited
        // or deleted by something else, and writing over that without asking is precisely
        // what the reload prompt exists to prevent - Ctrl+S is where the user says they
        // meant it.
        List<Guid> pending =
        [
            .. _workspace.Documents
                .Where(d => d.IsDirty && !d.IsUntitled && !d.HasExternalChange)
                .Select(d => d.Id)
        ];

        if (pending.Count == 0)
        {
            return;
        }

        int saved = 0;

        foreach (Guid id in pending)
        {
            try
            {
                await FormatBeforeSaveAsync(id).ConfigureAwait(true);

                if (_workspace.Find(id) is null)
                {
                    continue;
                }

                await _workspace.SaveAsync(id).ConfigureAwait(true);
                saved++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or DirectoryNotFoundException)
            {
                _logger.LogWarning(ex, "Autosave could not write document {DocumentId}.", id);
            }
        }

        if (saved > 0)
        {
            StatusText = saved == 1 ? "Autosaved" : $"Autosaved {saved} documents";
        }
    }

    private void OnRecentFilesChanged(object? sender, EventArgs e) => _ui.Post(RefreshRecentFiles);

    private void RefreshRecentFiles()
    {
        RecentFiles.Clear();

        foreach (RecentFile file in _recent.Items)
        {
            RecentFiles.Add(new RecentFileViewModel(file));
        }

        OnPropertyChanged(nameof(HasRecentFiles));
    }

    private async void OnEffectiveThemeChanged(object? sender, AppTheme effective)
    {
        try
        {
            if (_host is not null)
            {
                await _host.SetThemeAsync(effective).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to push the theme to the preview.");
        }
    }

    private void OnHostZoomChanged(object? sender, ZoomChangedEventArgs e)
    {
        // Originated in the shell, so only the local mirror and settings need updating.
        ActivePane = e.Pane;
        ActiveZoomPercent = e.Percent;
        PersistZoom(e.Pane, e.Percent);
    }

    private void PersistZoom(EditorPane pane, int percent) =>
        _settings.Update(s => pane == EditorPane.Source
            ? s with { SourceZoomPercent = percent }
            : s with { PreviewZoomPercent = percent });

    private void OnSplitterMoved(object? sender, double position) =>
        _settings.Update(s => s with { SplitterPosition = position });

    /// <summary>
    /// Returns the split to an even one. Bound to a double-click on the Split button as well
    /// as on the divider itself, since the divider is a six-pixel target and the button is
    /// the thing people actually aim at.
    /// </summary>
    [RelayCommand]
    private async Task ResetSplitAsync()
    {
        if (_host is null)
        {
            return;
        }

        // Switch to split view first: recentring a divider that is not on screen would look
        // like the double-click did nothing.
        if (ViewMode != ViewMode.SideBySide)
        {
            SetViewModeCommand.Execute("SideBySide");
        }

        await _host.ResetSplitterAsync().ConfigureAwait(true);
        StatusText = "Split reset to an even one";
    }

    private async void OnHostCommand(object? sender, string command)
    {
        try
        {
            // Keyboard shortcuts pressed while the WebView holds the keyboard arrive here,
            // because XAML accelerators do not fire while it does. That is both panes: the
            // editor when the caret is in it, and the preview when someone is reading.
            // HOST_SHORTCUTS in app.js is the list of what can turn up.
            switch (command)
            {
                case "save" when SaveCommand.CanExecute(null):
                    await SaveCommand.ExecuteAsync(null).ConfigureAwait(true);
                    break;

                case "saveAll" when SaveAllCommand.CanExecute(null):
                    await SaveAllCommand.ExecuteAsync(null).ConfigureAwait(true);
                    break;

                case "saveAs" when SaveAsCommand.CanExecute(null):
                    await SaveAsCommand.ExecuteAsync(null).ConfigureAwait(true);
                    break;

                case "open":
                    await OpenCommand.ExecuteAsync(null).ConfigureAwait(true);
                    break;

                case "openFolder":
                    await OpenFolderCommand.ExecuteAsync(null).ConfigureAwait(true);
                    break;

                case "newTab":
                    NewTabCommand.Execute(null);
                    break;

                case "findAll":
                    await FindAllCommand.ExecuteAsync(null).ConfigureAwait(true);
                    break;

                case "close" when CloseTabCommand.CanExecute(null):
                    await CloseTabCommand.ExecuteAsync(null).ConfigureAwait(true);
                    break;

                case "closeAll" when CloseAllTabsCommand.CanExecute(null):
                    await CloseAllTabsCommand.ExecuteAsync(null).ConfigureAwait(true);
                    break;

                case "print" when PrintCommand.CanExecute(null):
                    await PrintCommand.ExecuteAsync(null).ConfigureAwait(true);
                    break;

                case "nextTab":
                    NextTabCommand.Execute(null);
                    break;

                case "previousTab":
                    PreviousTabCommand.Execute(null);
                    break;

                // Ctrl+1 to Ctrl+9. The last tab has its own name rather than a number,
                // because which number it is depends on how many are open.
                case "tab.last":
                    ActivateLastTab();
                    break;

                case not null when command.StartsWith("tab.", StringComparison.Ordinal)
                    && int.TryParse(command["tab.".Length..], CultureInfo.InvariantCulture, out int tabNumber):
                    ActivateTabByNumber(tabNumber);
                    break;

                case "wordWrap":
                    await ToggleWordWrapCommand.ExecuteAsync(null).ConfigureAwait(true);
                    break;

                case "formatDocument" when FormatDocumentCommand.CanExecute(null):
                    await FormatDocumentCommand.ExecuteAsync(null).ConfigureAwait(true);
                    break;

                case "cheatsheet":
                    await ToggleCheatsheetCommand.ExecuteAsync(null).ConfigureAwait(true);
                    break;

                case "viewSource":
                    await SetViewModeCommand.ExecuteAsync("Source").ConfigureAwait(true);
                    break;

                case "viewSplit":
                    await SetViewModeCommand.ExecuteAsync("SideBySide").ConfigureAwait(true);
                    break;

                case "viewPreview":
                    await SetViewModeCommand.ExecuteAsync("Preview").ConfigureAwait(true);
                    break;

                // An Edit command was invoked while only the preview was showing.
                case "showSource":
                    await SetViewModeCommand.ExecuteAsync("SideBySide").ConfigureAwait(true);
                    break;

                // The same, for a Find command: Ctrl+F and its family are dead keys in
                // preview view, where neither Monaco nor a XAML accelerator is listening.
                //
                // Split rather than source only, so the preview being read stays beside it,
                // and remembered like any other view change - it is the view the user is now
                // in, however they arrived.
                //
                // Focus is deliberately not taken. The shell puts the keyboard in the search
                // box itself once this switch has laid the pane out; the restore that
                // takeFocus queues would arrive afterwards and pull it back into the
                // document, leaving the widget open and the typing going elsewhere.
                case "showSourceForFind":
                    await ApplyViewModeAsync(ViewMode.SideBySide, persist: true, takeFocus: false)
                        .ConfigureAwait(true);
                    break;

                // Alt+letter pressed while the editor had the keyboard, so it arrived here
                // rather than reaching the window's own accelerators.
                case not null when command.StartsWith("menu.", StringComparison.Ordinal):
                    MenuRequested?.Invoke(this, command["menu.".Length..]);
                    break;

                case "copyRichText" when CopyAsRichTextCommand.CanExecute(null):
                    await CopyAsRichTextCommand.ExecuteAsync(null).ConfigureAwait(true);
                    break;

                // The Format menu's shortcuts, which Monaco owns while the editor has
                // focus and forwards here under an "md." prefix.
                case not null when command.StartsWith("md.", StringComparison.Ordinal):
                    await ApplyMarkdownCommand.ExecuteAsync(command["md.".Length..]).ConfigureAwait(true);
                    break;

                default:
                    _logger.LogDebug("Ignoring unknown shell command {Command}.", command);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Shell command {Command} failed.", command);
        }
    }

    private async void OnExternalLinkActivated(object? sender, Uri uri)
    {
        try
        {
            // Two things arrive here as file URIs: a link to a sibling document, and a file
            // dropped onto the preview, which the browser turns into a navigation.
            if (uri.IsFile && File.Exists(uri.LocalPath))
            {
                if (MarkdownFileTypes.IsSupported(uri.LocalPath))
                {
                    await OpenPathAsync(uri.LocalPath).ConfigureAwait(true);
                }
                else
                {
                    await _dialogs.ShowMessageAsync(
                        "Unsupported file",
                        "Marqora opens markdown files: " + string.Join(", ", MarkdownFileTypes.Extensions))
                        .ConfigureAwait(true);
                }

                return;
            }

            if (uri.Scheme is "http" or "https" or "mailto")
            {
                await Windows.System.Launcher.LaunchUriAsync(uri);
                return;
            }

            _logger.LogInformation("Ignoring link with unsupported scheme {Scheme}.", uri.Scheme);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not follow link {Uri}.", uri);
        }
    }

    /// <summary>Applied from the shell's status reports.</summary>
    public void UpdateStats(int line, int column, int words, int characters)
    {
        CursorLine = line;
        CursorColumn = column;
        WordCount = words;
        CharacterCount = characters;
    }

    /// <summary>Records which pane zoom commands should target.</summary>
    public void SetActivePane(EditorPane pane)
    {
        ActivePane = pane;

        AppSettings current = _settings.Current;
        ActiveZoomPercent = pane == EditorPane.Source
            ? current.SourceZoomPercent
            : current.PreviewZoomPercent;
    }
}
