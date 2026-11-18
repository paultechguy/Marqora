// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.App.Services;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.App.ViewModels;

/// <summary>One of the two side panels, or neither.</summary>
public enum SidePanel
{
    None,
    Folder,
    Outline,
}

/// <summary>
/// The main window's side of the two side panels: where each lives, the keys, the focus latch,
/// and the File menu's Open Folder and Close Folder.
///
/// Each panel is docked in the column or lives on the window's edge (FolderPanel P29): pinned
/// or not (<see cref="FolderPanelPinned"/>, <see cref="OutlinePinned"/>, session state). Nothing in
/// the window makes a panel disappear. <see cref="ShowFolderPanel"/> and <see cref="ShowOutline"/>
/// remain as the preferences that switch a panel off altogether (the folder panel's is off until a
/// folder is first chosen). A panel on the edge can be slid out over the text; <see cref="SlidOutPanel"/>
/// says which one is, and it is never remembered, so a restart never opens one.
///
/// The folder panel's own state (root, tree, filter) is <see cref="FolderPanel"/>'s.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>How long after a click opened a file the list may take the keyboard back and be handed off again.</summary>
    private static readonly TimeSpan FolderPanelBounceWindow = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long a slid-out panel that was told to stay out ignores the text taking the keyboard.
    /// Opening a file or picking a heading with its preference off still hands the keyboard to the
    /// editor, and that must not count as the click outside it.
    /// </summary>
    private static readonly TimeSpan StayOutWindow = TimeSpan.FromSeconds(1);

    /// <summary>When a click in the panel last handed the keyboard to the editor; see <see cref="NotifyFolderPanelFocused"/>.</summary>
    private DateTime _leftFolderPanelUtc = DateTime.MinValue;

    /// <summary>Until when the text taking the keyboard leaves a slid-out panel out; see <see cref="StayOutWindow"/>.</summary>
    private DateTime _stayOutUntilUtc = DateTime.MinValue;

    public FolderPanelViewModel FolderPanel { get; }

    /// <summary>The Folder Panel preference: not closed (P10, P29). Off until a root is first chosen (P6).</summary>
    [ObservableProperty]
    public partial bool ShowFolderPanel { get; set; }

    /// <summary>The folder panel is pinned in the column rather than living on the edge (P29). Session state.</summary>
    [ObservableProperty]
    public partial bool FolderPanelPinned { get; set; } = true;

    /// <summary>The outline is pinned in the column rather than living on the edge (P29). Session state.</summary>
    [ObservableProperty]
    public partial bool OutlinePinned { get; set; } = true;

    /// <summary>The panel slid out over the text from its edge tab, if any. Never remembered.</summary>
    [ObservableProperty]
    public partial SidePanel SlidOutPanel { get; set; }

    /// <summary>
    /// Whether the folder panel has anything to show. With no document open it has only once a
    /// root is chosen, so someone who never opens a folder keeps today's landing page, centered
    /// in the whole window (C7).
    /// </summary>
    private bool IsFolderPanelPresent => HasDocument || FolderPanel.HasRoot;

    /// <summary>The folder panel is docked in the left column.</summary>
    public bool IsFolderSectionVisible => ShowFolderPanel && FolderPanelPinned && IsFolderPanelPresent;

    /// <summary>The folder panel lives on the edge: its tab is there.</summary>
    public bool IsFolderOnEdge => ShowFolderPanel && !FolderPanelPinned && IsFolderPanelPresent;

    /// <summary>The outline lives on the edge: its tab is there.</summary>
    public bool IsOutlineOnEdge => ShowOutline && !OutlinePinned && HasDocument;

    /// <summary>The edge strip shows only while some panel lives there.</summary>
    public bool IsEdgeStripVisible => IsFolderOnEdge || IsOutlineOnEdge;

    /// <summary>The folder panel is on screen, docked or slid out: what the keyboard can be sent to.</summary>
    public bool IsFolderPanelOnScreen => IsFolderSectionVisible || SlidOutPanel == SidePanel.Folder;

    /// <summary>The outline is on screen, docked or slid out.</summary>
    public bool IsOutlineOnScreen => IsOutlineVisible || SlidOutPanel == SidePanel.Outline;

    /// <summary>Whether the left column holds a docked panel at all.</summary>
    public bool IsLeftColumnVisible => IsFolderSectionVisible || IsOutlineVisible;

    /// <summary>Anything at the window's left (the edge strip or the column): the landing page then centers beside it.</summary>
    public bool HasLeftChrome => IsLeftColumnVisible || IsEdgeStripVisible;

    /// <summary>Both panels docked: they stack, with the splitter between, and each folds from its header.</summary>
    public bool AreSectionsStacked => IsFolderSectionVisible && IsOutlineVisible;

    /// <summary>The outline's share of the column, 0 to 1 (P10). Kept as a proportion so both halves grow with the window.</summary>
    [ObservableProperty]
    public partial double FolderSplit { get; set; } = AppSettings.DefaultFolderSplit;

    /// <summary>
    /// Whether the keyboard is in the folder panel. A latch, for the reasons
    /// <see cref="OutlineHasFocus"/> is one, and asked by the same readers: the format commands,
    /// Paste, Copy, and where the keyboard goes back after a menu (C8).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFormat))]
    [NotifyPropertyChangedFor(nameof(CanEditText))]
    [NotifyPropertyChangedFor(nameof(CanAlterText))]
    [NotifyCanExecuteChangedFor(nameof(FormatDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(FormatAllDocumentsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyMarkdownCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenumberListCommand))]
    [NotifyCanExecuteChangedFor(nameof(InsertSnippetCommand))]
    public partial bool FolderPanelHasFocus { get; set; }

    /// <summary>Asks the view to put the keyboard in the folder panel.</summary>
    public event EventHandler? FolderPanelFocusRequested;

    // ------------------------------------------------------------- layout

    /// <summary>Every property the layout derives from the panels' places, raised together.</summary>
    private static readonly string[] PanelLayoutProperties =
    [
        nameof(IsOutlineVisible),
        nameof(IsFolderSectionVisible),
        nameof(IsFolderOnEdge),
        nameof(IsOutlineOnEdge),
        nameof(IsEdgeStripVisible),
        nameof(IsFolderPanelOnScreen),
        nameof(IsOutlineOnScreen),
        nameof(IsLeftColumnVisible),
        nameof(HasLeftChrome),
        nameof(AreSectionsStacked),
        nameof(IsFolderPanelDocked),
        nameof(IsOutlineDocked),
    ];

    private void RaisePanelLayout()
    {
        // A panel that stopped living on the edge (pinned, closed, or nothing left to show)
        // cannot stay slid out.
        if ((SlidOutPanel == SidePanel.Folder && !IsFolderOnEdge)
            || (SlidOutPanel == SidePanel.Outline && !IsOutlineOnEdge))
        {
            SlidOutPanel = SidePanel.None;
        }

        foreach (string name in PanelLayoutProperties)
        {
            OnPropertyChanged(name);
        }
    }

    partial void OnHasDocumentChanged(bool value) => RaisePanelLayout();

    partial void OnShowOutlineChanged(bool value) => RaisePanelLayout();

    partial void OnOutlinePinnedChanged(bool value) => RaisePanelLayout();

    partial void OnShowFolderPanelChanged(bool value) => RaisePanelLayout();

    partial void OnFolderPanelPinnedChanged(bool value) => RaisePanelLayout();

    partial void OnSlidOutPanelChanged(SidePanel value)
    {
        OnPropertyChanged(nameof(IsFolderPanelOnScreen));
        OnPropertyChanged(nameof(IsOutlineOnScreen));
    }

    /// <summary>The panel's root came or went: what the column shows, and what the File menu offers, follow.</summary>
    private void OnFolderPanelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FolderPanelViewModel.HasRoot):
                RaisePanelLayout();
                CloseFolderCommand.NotifyCanExecuteChanged();
                break;

            case nameof(FolderPanelViewModel.ActiveFolder):
            case nameof(FolderPanelViewModel.CanUseActiveFolder):
                UseActiveDocumentFolderCommand.NotifyCanExecuteChanged();
                break;

            default:
                break;
        }
    }

    // ------------------------------------------------- docked, edge, closed

    /// <summary>
    /// Alt+6 and View > Folder Panel: docks the panel in the column, with the keyboard in it, or
    /// sends a docked one to its tab on the window's edge. The same as the pin button in its
    /// header. Nothing makes it disappear: a panel always has a place, docked or on the edge.
    /// </summary>
    [RelayCommand]
    private async Task ToggleFolderPanelAsync()
    {
        if (IsFolderPanelDocked)
        {
            await SetPinnedAsync(SidePanel.Folder, pinned: false).ConfigureAwait(true);
            return;
        }

        await SetPinnedAsync(SidePanel.Folder, pinned: true).ConfigureAwait(true);
        await ShowFolderPanelWhereItWasAsync().ConfigureAwait(true);
    }

    /// <summary>The folder panel is docked, or would be once there is something to show: what View > Folder Panel checks.</summary>
    public bool IsFolderPanelDocked => ShowFolderPanel && FolderPanelPinned;

    /// <summary>The outline is docked: what View > Outline checks.</summary>
    public bool IsOutlineDocked => ShowOutline && OutlinePinned;

    /// <summary>Pin and unpin in the folder panel's header: docked in the column, or a tab on the edge.</summary>
    [RelayCommand]
    private Task ToggleFolderPanelPinnedAsync() => SetPinnedAsync(SidePanel.Folder, !FolderPanelPinned);

    /// <summary>Pin and unpin in the outline's header.</summary>
    [RelayCommand]
    private Task ToggleOutlinePinnedAsync() => SetPinnedAsync(SidePanel.Outline, !OutlinePinned);

    private async Task SetPinnedAsync(SidePanel panel, bool pinned)
    {
        bool hadFocus = panel == SidePanel.Folder ? FolderPanelHasFocus : OutlineHasFocus;

        if (panel == SidePanel.Folder)
        {
            FolderPanelPinned = pinned;
            _settings.Update(s => s with { FolderPanelPinned = pinned });
        }
        else
        {
            OutlinePinned = pinned;
            _settings.Update(s => s with { OutlinePinned = pinned });
        }

        if (pinned)
        {
            // Pinned from slid out: it is docked now, and keeps the keyboard if it had it.
            if (SlidOutPanel == panel)
            {
                SlidOutPanel = SidePanel.None;
            }

            if (hadFocus)
            {
                RequestFocus(panel);
            }

            return;
        }

        // Unpinned: it goes to its edge tab, out of the way, and the keyboard goes back to the text.
        if (hadFocus)
        {
            await LeavePanelAsync(panel).ConfigureAwait(true);
        }
        else
        {
            RestoreDocumentFocusAfterChrome();
        }
    }

    /// <summary>A click on the folder panel's edge tab.</summary>
    [RelayCommand]
    private void ToggleFolderSlideOut() => ToggleSlideOut(SidePanel.Folder);

    /// <summary>A click on the outline's edge tab.</summary>
    [RelayCommand]
    private void ToggleOutlineSlideOut() => ToggleSlideOut(SidePanel.Outline);

    /// <summary>A click on a panel's edge tab: slides it out with the keyboard in it, or back if it is out.</summary>
    private void ToggleSlideOut(SidePanel panel)
    {
        if (SlidOutPanel == panel)
        {
            SlideAwayIf(true);
            RestoreDocumentFocusAfterChrome();
            return;
        }

        SlideOut(panel);
        RequestFocus(panel);
    }

    /// <summary>Slides a panel that lives on the edge out over the text.</summary>
    private void SlideOut(SidePanel panel)
    {
        bool onEdge = panel == SidePanel.Folder ? IsFolderOnEdge : IsOutlineOnEdge;

        if (onEdge)
        {
            SlidOutPanel = panel;
        }
    }

    /// <summary>
    /// Slides the slid-out panel back to its edge when <paramref name="slideAway"/> is true (its
    /// preference). When false it stays out, and the editor taking the keyboard in the next moment
    /// is not read as a click outside it.
    /// </summary>
    private void SlideAwayIf(bool slideAway)
    {
        if (SlidOutPanel == SidePanel.None)
        {
            return;
        }

        if (slideAway)
        {
            SlidOutPanel = SidePanel.None;
            _stayOutUntilUtc = DateTime.MinValue;
        }
        else
        {
            _stayOutUntilUtc = DateTime.UtcNow + StayOutWindow;
        }
    }

    /// <summary>
    /// A press outside the slid-out panel: in the text (a pane taking the keyboard), or on the
    /// window's own chrome. It slides back if the preference says so.
    /// </summary>
    public void NotifyClickedOutsideSlidOutPanel()
    {
        if (SlidOutPanel == SidePanel.None || DateTime.UtcNow < _stayOutUntilUtc)
        {
            return;
        }

        if (_settings.Current.SlideAwayOnClickOutside)
        {
            SlidOutPanel = SidePanel.None;
            OutlineHasFocus = false;
            FolderPanelHasFocus = false;
        }
    }

    public async Task SetShowFolderPanelAsync(bool show)
    {
        if (ShowFolderPanel == show)
        {
            return;
        }

        ShowFolderPanel = show;
        _settings.Update(s => s with { ShowFolderPanel = show });

        // The keyboard cannot stay in a panel that is no longer on screen.
        if (!show && FolderPanelHasFocus)
        {
            await LeaveFolderPanelAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Shows the folder panel docked, or slid out from its edge tab, and puts the keyboard in it.</summary>
    private async Task ShowFolderPanelWhereItWasAsync()
    {
        await SetShowFolderPanelAsync(true).ConfigureAwait(true);

        // With no document and no root the panel has nothing to show: the landing page is the
        // whole window, and its own Open folder button is the way in.
        if (!IsFolderPanelPresent)
        {
            StatusText = "Choose a folder with File > Open Folder";
            return;
        }

        if (!FolderPanelPinned)
        {
            SlideOut(SidePanel.Folder);
        }

        RequestFolderPanelFocus();
    }

    /// <summary>
    /// Alt+Shift+6: goes to the panel, and pressed again, back. A closed panel comes back where it
    /// was, and one on the edge slides out, because being sent somewhere that is not there is not
    /// an answer. With no root it lands on the Open Folder button.
    /// </summary>
    [RelayCommand]
    private async Task FocusFolderPanelAsync()
    {
        if (FolderPanelHasFocus)
        {
            await LeaveFolderPanelAsync().ConfigureAwait(true);
            return;
        }

        await ShowFolderPanelWhereItWasAsync().ConfigureAwait(true);
    }

    /// <summary>Deferred for the reasons <see cref="RequestOutlineFocus"/> is: a menu holds the keyboard until it has closed.</summary>
    private void RequestFolderPanelFocus() =>
        _ui.PostAfterRender(() => FolderPanelFocusRequested?.Invoke(this, EventArgs.Empty));

    private void RequestFocus(SidePanel panel)
    {
        if (panel == SidePanel.Folder)
        {
            RequestFolderPanelFocus();
        }
        else
        {
            RequestOutlineFocus();
        }
    }

    private Task LeavePanelAsync(SidePanel panel) =>
        panel == SidePanel.Folder ? LeaveFolderPanelAsync() : LeaveOutlineAsync();

    // --------------------------------------------------------------- focus

    /// <summary>Called by the panel as the keyboard arrives in it. See <see cref="FolderPanelHasFocus"/>.</summary>
    public void NotifyFolderPanelFocused()
    {
        // The list taking the keyboard back just after a click handed it to the editor: the
        // bounce GoToOutlineRowAsync describes for the outline. Answered by handing it over
        // again, once, and only shortly after the click, so a later return is believed.
        if (DateTime.UtcNow - _leftFolderPanelUtc < FolderPanelBounceWindow)
        {
            _leftFolderPanelUtc = DateTime.MinValue;

            if (_host is not null && HasDocument)
            {
                _ = _host.FocusEditorAsync();
            }

            return;
        }

        OutlineHasFocus = false;
        FolderPanelHasFocus = true;
    }

    /// <summary>
    /// Escape in the panel, and Alt+Shift+6 from inside it: back to the pane the keyboard came
    /// from. A slid-out panel slides back with it, if the Escape preference says so.
    /// </summary>
    public async Task LeaveFolderPanelAsync()
    {
        FolderPanelHasFocus = false;

        if (SlidOutPanel == SidePanel.Folder)
        {
            SlideAwayIf(_settings.Current.SlideAwayOnEscape);
        }

        if (_host is not null && HasDocument)
        {
            await _host.FocusPaneAsync(ActivePane).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// A markdown file clicked or entered in the panel: it opens, or its tab comes forward, and the
    /// editor takes the keyboard. Opening is the one case where a command from the panel does not
    /// send the keyboard back to it. A slid-out panel slides back if its preference says so.
    /// </summary>
    public async Task OpenFromFolderPanelAsync(string path)
    {
        FolderPanelHasFocus = false;
        _leftFolderPanelUtc = DateTime.UtcNow;

        if (SlidOutPanel == SidePanel.Folder)
        {
            SlideAwayIf(_settings.Current.SlideAwayOnOpenFile);
        }

        await OpenPathAsync(path).ConfigureAwait(true);
    }

    /// <summary>Ctrl+C with the keyboard in the panel: the selected row's full path, as a focused list copies its selection.</summary>
    private void CopySelectedFolderPath()
    {
        if (FolderPanel.SelectedPath is not { } path)
        {
            return;
        }

        StatusText = ClipboardText.Set(path, _logger)
            ? $"Copied “{path}”"
            : "Could not copy the path";
    }

    // ------------------------------------------------------------ the root

    /// <summary>File > Open Folder, Ctrl+Shift+O and the landing page: makes a folder the root and shows the panel. Opens no tabs.</summary>
    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        string? folder = await _fileDialogs.PickFolderAsync("Open Folder").ConfigureAwait(true);

        if (!string.IsNullOrWhiteSpace(folder))
        {
            await UseFolderAsRootAsync(folder).ConfigureAwait(true);
        }
        else
        {
            RestoreDocumentFocusAfterChrome();
        }
    }

    /// <summary>"Use this document's folder" and "Show its folder": the active document's folder becomes the root.</summary>
    [RelayCommand(CanExecute = nameof(CanUseActiveDocumentFolder))]
    private async Task UseActiveDocumentFolderAsync()
    {
        if (FolderPanel.ActiveFolder is { } folder)
        {
            await UseFolderAsRootAsync(folder).ConfigureAwait(true);
        }
    }

    private bool CanUseActiveDocumentFolder() => FolderPanel.CanUseActiveFolder;

    /// <summary>A tab's Use This Document's Folder: the folder its file is in becomes the root.</summary>
    public async Task UseDocumentFolderAsync(string documentPath)
    {
        if (Path.GetDirectoryName(documentPath) is { } folder)
        {
            await UseFolderAsRootAsync(folder).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// A folder picked from the root header's Recent Folders. One that has gone since is said so,
    /// and dropped from the list rather than offered again.
    /// </summary>
    public async Task OpenRecentFolderAsync(string folder)
    {
        if (!Directory.Exists(folder))
        {
            FolderPanel.ForgetRecentFolder(folder);
        }

        await UseFolderAsRootAsync(folder).ConfigureAwait(true);
    }

    /// <summary>File > Close Folder: no root, and the panel's empty state.</summary>
    [RelayCommand(CanExecute = nameof(CanCloseFolder))]
    private void CloseFolder()
    {
        FolderPanel.CloseRoot();
        StatusText = "Closed the folder";
    }

    private bool CanCloseFolder() => FolderPanel.HasRoot;

    private async Task UseFolderAsRootAsync(string folder)
    {
        if (!FolderPanel.SetRoot(folder))
        {
            await _dialogs.ShowMessageAsync("Could not open folder", $"{folder} could not be found.").ConfigureAwait(true);
            RestoreDocumentFocusAfterChrome();
            return;
        }

        StatusText = $"Opened the folder {FolderPanel.RootName}";

        // Choosing a root is asking to see it (P6): the panel comes back where it was, docked
        // the first time.
        await ShowFolderPanelWhereItWasAsync().ConfigureAwait(true);
    }

    // ------------------------------------------------------------- the split

    /// <summary>Remembers the outline's share of the column, from a drag or the splitter's double-click.</summary>
    public void SetFolderSplit(double split)
    {
        double clamped = Math.Clamp(split, MinimumFolderSplit, 1 - MinimumFolderSplit);

        if (Math.Abs(clamped - FolderSplit) < 0.001)
        {
            return;
        }

        FolderSplit = clamped;
        _settings.Update(s => s with { FolderSplit = clamped });
    }

    /// <summary>Neither section is dragged smaller than this share of the column.</summary>
    private const double MinimumFolderSplit = 0.1;

    /// <summary>Restores the panels' places and the folder panel's root at start, whatever the startup preference (P21).</summary>
    private void RestoreFolderPanel(AppSettings current)
    {
        FolderSplit = double.IsFinite(current.FolderSplit)
            ? Math.Clamp(current.FolderSplit, MinimumFolderSplit, 1 - MinimumFolderSplit)
            : AppSettings.DefaultFolderSplit;

        OutlinePinned = current.OutlinePinned;
        FolderPanelPinned = current.FolderPanelPinned;
        ShowFolderPanel = current.ShowFolderPanel;
        FolderPanel.Restore(current);

        _logger.LogDebug(
            "Side panels restored: folder shown {Folder} pinned {FolderPinned}, outline pinned {OutlinePinned}, root {Root}.",
            ShowFolderPanel,
            FolderPanelPinned,
            OutlinePinned,
            FolderPanel.Root ?? "(none)");
    }
}
