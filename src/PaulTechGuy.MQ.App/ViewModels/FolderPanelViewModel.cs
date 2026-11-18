// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Folders;
using PaulTechGuy.MQ.Services;

namespace PaulTechGuy.MQ.App.ViewModels;

/// <summary>
/// The folder panel: one chosen root, its tree, the filter, and what the open documents look like
/// in it.
///
/// One instance, shared by whichever host shows it (P5): docked in the main window now, and the
/// floating window in WP-9. A host builds its own view over these rows and writes selection only
/// from a user's own gesture; nothing here holds a control.
///
/// Everything runs on the UI thread. Slow work (reading a folder, a look below one) runs on the
/// pool and comes back here with <c>ConfigureAwait(true)</c>; the watcher and the workspace raise
/// their events on other threads and are posted. Every piece of background work carries the
/// root's cancellation token, so a root changed mid-read drops whatever the old one was doing
/// (P18).
/// </summary>
public sealed partial class FolderPanelViewModel : ObservableObject, IDisposable
{
    /// <summary>How long the filter waits for the typing to stop before searching below closed folders (P17).</summary>
    private static readonly TimeSpan FilterDelay = TimeSpan.FromMilliseconds(250);

    private readonly IWorkspaceService _workspace;
    private readonly ISettingsService _settings;
    private readonly IUiDispatcher _ui;
    private readonly ILogger<FolderPanelViewModel> _logger;
    private readonly FolderProbe _probe = new();

    private FolderTree? _tree;
    private GitIgnoreReader? _ignore;
    private FolderTreeWatcher? _watcher;
    private CancellationTokenSource _rootLife = new();
    private CancellationTokenSource? _filterWalk;
    private IReadOnlyList<FolderRow> _shown = [];
    private bool _rebuildQueued;
    private string? _revealedPath;

    public FolderPanelViewModel(
        IWorkspaceService workspace,
        ISettingsService settings,
        IUiDispatcher ui,
        ILogger<FolderPanelViewModel> logger)
    {
        _workspace = workspace;
        _settings = settings;
        _ui = ui;
        _logger = logger;

        _workspace.Changed += OnWorkspaceChanged;
    }

    /// <summary>The visible rows, changed by inserting and removing ranges, never replaced (P1).</summary>
    public ObservableCollection<FolderRowViewModel> Rows { get; } = [];

    /// <summary>The chosen folder, or null when none is chosen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRoot))]
    [NotifyPropertyChangedFor(nameof(RootName))]
    [NotifyPropertyChangedFor(nameof(RootTooltip))]
    [NotifyPropertyChangedFor(nameof(ShowsNoRoot))]
    [NotifyPropertyChangedFor(nameof(EmptyMessage))]
    [NotifyPropertyChangedFor(nameof(ShowsEmptyMessage))]
    [NotifyPropertyChangedFor(nameof(ShowsShowAllFilesLink))]
    public partial string? Root { get; private set; }

    public bool HasRoot => Root is not null;

    /// <summary>The root's own name, shown in the header. A drive's root has no name, so it shows the path.</summary>
    public string RootName => Root is null ? string.Empty : NameOf(Root);

    public string RootTooltip => Root ?? string.Empty;

    /// <summary>The name of a remembered root that was not there on start, for the empty state's line.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoRootMessage))]
    public partial string? MissingRootName { get; private set; }

    /// <summary>No root: the panel shows the Open Folder state.</summary>
    public bool ShowsNoRoot => Root is null;

    public string NoRootMessage => MissingRootName is { } missing
        ? $"{missing} could not be found."
        : "Open a folder to browse its documents.";

    /// <summary>The filter box's text. Narrows the rows; searches below closed folders once the typing stops (P17).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyMessage))]
    [NotifyPropertyChangedFor(nameof(ShowsShowAllFilesLink))]
    public partial string Filter { get; set; } = string.Empty;

    /// <summary>The filter's walk stopped at the ceiling: some folders were not searched.</summary>
    [ObservableProperty]
    public partial bool IsFilterIncomplete { get; private set; }

    /// <summary>Whether the rows are empty. Raised with every rebuild.</summary>
    public bool HasRows => Rows.Count > 0;

    public bool ShowsEmptyMessage => HasRoot && !HasRows;

    public string EmptyMessage => !string.IsNullOrWhiteSpace(Filter)
        ? $"No files match \u201C{Filter.Trim()}\u201D."
        : $"No markdown files in {RootName}.";

    /// <summary>The empty root offers to show every file, which may be what was wanted.</summary>
    public bool ShowsShowAllFilesLink => ShowsEmptyMessage && string.IsNullOrWhiteSpace(Filter) && !ShowAllFiles;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsShowAllFilesLink))]
    public partial bool ShowAllFiles { get; private set; }

    [ObservableProperty]
    public partial bool SortByModified { get; private set; }

    [ObservableProperty]
    public partial bool RevealActive { get; private set; }

    /// <summary>The active document is saved and outside the root: the foot line says so.</summary>
    [ObservableProperty]
    public partial bool IsActiveOutsideRoot { get; private set; }

    /// <summary>The active document's folder, when it has one. What "Use this document's folder" and "Show its folder" set.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseActiveFolder))]
    public partial string? ActiveFolder { get; private set; }

    public bool CanUseActiveFolder => ActiveFolder is not null;

    /// <summary>The row the list has selected, written by the host from a user's gesture. What Copy copies.</summary>
    public string? SelectedPath { get; set; }

    /// <summary>False when the root could not be watched (common on a share); Refresh is then the way to catch up.</summary>
    [ObservableProperty]
    public partial bool IsWatching { get; private set; }

    /// <summary>A markdown file was opened from the panel (click or Enter).</summary>
    public event EventHandler<string>? OpenRequested;

    /// <summary>Asks a host to bring a row into view, after a reveal.</summary>
    public event EventHandler<FolderRowViewModel>? ScrollIntoViewRequested;

    /// <summary>
    /// Asks a host to select a row and bring it into view, whoever has the keyboard: after a
    /// reveal someone asked for, and when a collapse has hidden the selected row.
    /// </summary>
    public event EventHandler<FolderRowViewModel>? SelectRequested;

    /// <summary>
    /// Whether Reveal Active Document has something to reveal: a saved active document inside the
    /// root. Computed, so it follows <see cref="ActiveFolder"/>, <see cref="IsActiveOutsideRoot"/>
    /// and the root as each reports a change.
    /// </summary>
    public bool CanRevealActive => HasRoot && ActiveFolder is not null && !IsActiveOutsideRoot;

    /// <summary>The root changed; a host shows the panel, or settings follow.</summary>
    public event EventHandler? RootChanged;

    // ------------------------------------------------------------------- root

    /// <summary>Reads the preferences and restores the root on start, whatever the startup preference (P21).</summary>
    public void Restore(AppSettings settings)
    {
        ShowAllFiles = settings.FolderShowAllFiles;
        SortByModified = settings.FolderSortByModified;
        RevealActive = settings.FolderRevealActive;

        PruneRecentFolders();

        if (settings.FolderRoot is { } root)
        {
            if (Directory.Exists(root))
            {
                Open(root);
            }
            else
            {
                // Dropped quietly, with a line in the empty state saying which.
                MissingRootName = NameOf(root);
                _settings.Update(s => s with { FolderRoot = null });
            }
        }
    }

    /// <summary>Makes a folder the root, remembering it, and newest in the recent list. True when it was set.</summary>
    public bool SetRoot(string folder)
    {
        string full;

        try
        {
            full = FolderPaths.TrimEnd(Path.GetFullPath(folder));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            _logger.LogWarning(ex, "Not a folder path: {Folder}.", folder);
            return false;
        }

        if (!Directory.Exists(full))
        {
            return false;
        }

        MissingRootName = null;

        _settings.Update(s => s with
        {
            FolderRoot = full,
            RecentFolders = [.. new[] { full }
                .Concat(s.RecentFolderList.Where(f => !FolderPaths.Equal(f, full)))
                .Take(AppSettings.MaximumRecentFolders)],
        });

        if (Root is not null && FolderPaths.Equal(Root, full))
        {
            return true;
        }

        Open(full);
        RootChanged?.Invoke(this, EventArgs.Empty);

        return true;
    }

    /// <summary>File, Close Folder: no root, the empty state.</summary>
    public void CloseRoot()
    {
        if (Root is null)
        {
            return;
        }

        StopRoot();
        Root = null;
        MissingRootName = null;
        _settings.Update(s => s with { FolderRoot = null });
        RootChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Recent folders, newest first, with any that have gone dropped (P10).</summary>
    public IReadOnlyList<string> RecentFolders => _settings.Current.RecentFolderList;

    /// <summary>Drops one folder from the recent list, after it turned out to be gone.</summary>
    public void ForgetRecentFolder(string folder)
    {
        if (_settings.Current.RecentFolderList.Any(f => FolderPaths.Equal(f, folder)))
        {
            _settings.Update(s => s with { RecentFolders = [.. s.RecentFolderList.Where(f => !FolderPaths.Equal(f, folder))] });
        }
    }

    private void PruneRecentFolders()
    {
        IReadOnlyList<string> recent = _settings.Current.RecentFolderList;
        List<string> kept = [.. recent.Where(f => !DocumentPathKeys.FolderHasBeenDeleted(f))];

        if (kept.Count != recent.Count)
        {
            _settings.Update(s => s with { RecentFolders = kept });
        }
    }

    private void Open(string root)
    {
        // StopRoot leaves a fresh token behind for this root's work.
        StopRoot();

        _ignore = GitIgnoreReader.For(root);
        GitIgnoreReader ignore = _ignore;
        _tree = new FolderTree(root, ShowAllFiles, ignore.IsIgnored);
        Root = _tree.Root.FullPath;

        StartWatching();

        _ = LoadAsync(_tree.Root.FullPath, _rootLife.Token);
    }

    private void StopRoot()
    {
        _rootLife.Cancel();
        _rootLife.Dispose();
        _rootLife = new CancellationTokenSource();

        CancelFilterWalk();

        StopWatching();

        _tree = null;
        _ignore = null;
        _revealedPath = null;
        IsFilterIncomplete = false;

        Rebuild();
    }

    private void StartWatching()
    {
        StopWatching();

        if (_tree is null)
        {
            return;
        }

        var watcher = new FolderTreeWatcher(_tree.Root.FullPath, watchWrites: SortByModified);
        watcher.ChangesReady += OnDiskChanged;
        _watcher = watcher;
        IsWatching = watcher.IsWatching;

        if (!watcher.IsWatching)
        {
            _logger.LogInformation("The folder panel cannot watch {Root}; Refresh catches it up.", _tree.Root.FullPath);
        }
    }

    private void StopWatching()
    {
        if (_watcher is { } watcher)
        {
            watcher.ChangesReady -= OnDiskChanged;
            watcher.Dispose();
            _watcher = null;
        }

        IsWatching = false;
    }

    // -------------------------------------------------------------- preferences

    public void SetShowAllFiles(bool value)
    {
        if (ShowAllFiles == value)
        {
            return;
        }

        ShowAllFiles = value;
        _settings.Update(s => s with { FolderShowAllFiles = value });

        // The tree's notion of what is listed is fixed at construction, so it is built again,
        // with the same folders open.
        if (_tree is { } old)
        {
            IReadOnlyList<string> expanded = old.ExpandedFolders();
            string root = old.Root.FullPath;

            Open(root);
            _ = ReopenAsync(expanded, _rootLife.Token);
        }
    }

    public void SetSortByModified(bool value)
    {
        if (SortByModified == value)
        {
            return;
        }

        SortByModified = value;
        _settings.Update(s => s with { FolderSortByModified = value });

        // Dates are only watched while they order the rows (P18).
        if (_tree is not null)
        {
            StartWatching();
        }

        Rebuild();
    }

    public void SetRevealActive(bool value)
    {
        if (RevealActive == value)
        {
            return;
        }

        RevealActive = value;
        _settings.Update(s => s with { FolderRevealActive = value });

        if (value)
        {
            _revealedPath = null;
            FollowActive();
        }
    }

    // ------------------------------------------------------------------ actions

    /// <summary>A row was clicked or Enter was pressed on it: a folder opens or closes, a markdown file opens.</summary>
    public async Task ActivateAsync(FolderRowViewModel row)
    {
        if (row.IsFolder)
        {
            await ToggleAsync(row).ConfigureAwait(true);
        }
        else if (row.IsMarkdown)
        {
            OpenRequested?.Invoke(this, row.FullPath);
        }
    }

    public async Task ToggleAsync(FolderRowViewModel row)
    {
        if (row.Row.IsExpanded)
        {
            Collapse(row);
        }
        else
        {
            await ExpandAsync(row).ConfigureAwait(true);
        }
    }

    public async Task ExpandAsync(FolderRowViewModel row)
    {
        if (_tree is not { } tree || !row.IsFolder || IsFiltering)
        {
            return;
        }

        if (tree.Expand(row.FullPath))
        {
            await LoadAsync(row.FullPath, _rootLife.Token).ConfigureAwait(true);
        }
        else
        {
            Rebuild();
        }
    }

    public void Collapse(FolderRowViewModel row)
    {
        if (_tree is not { } tree || !row.IsFolder || IsFiltering)
        {
            return;
        }

        string? selected = SelectedPath;
        tree.Collapse(row.FullPath);
        Rebuild();
        KeepSelectionShown(selected);
    }

    /// <summary>The row of the folder holding a row, for Left on a child.</summary>
    public FolderRowViewModel? ParentOf(FolderRowViewModel row)
    {
        string? parent = Path.GetDirectoryName(row.FullPath);

        return parent is null ? null : Rows.FirstOrDefault(r => FolderPaths.Equal(r.FullPath, parent));
    }

    public void CollapseAll()
    {
        string? selected = SelectedPath;
        _tree?.CollapseAll();
        Rebuild();
        KeepSelectionShown(selected);
    }

    /// <summary>
    /// After a collapse has folded the selected row away, moves the selection to the folder that
    /// now hides it, as File Explorer does. Without this the selection simply vanished, and after
    /// Collapse All nothing in the tree said which branch the document had been in.
    /// </summary>
    private void KeepSelectionShown(string? selected)
    {
        if (selected is null || Root is not { } root || RowOf(selected) is not null)
        {
            return;
        }

        for (string? folder = Path.GetDirectoryName(selected);
             folder is not null && FolderPaths.IsUnder(folder, root);
             folder = Path.GetDirectoryName(folder))
        {
            if (RowOf(folder) is { } row)
            {
                SelectRequested?.Invoke(this, row);
                return;
            }
        }
    }

    private FolderRowViewModel? RowOf(string path) =>
        Rows.FirstOrDefault(r => FolderPaths.Equal(r.FullPath, path));

    /// <summary>Reads every open folder again. For a share, where the watcher may be silent.</summary>
    public async Task RefreshAsync()
    {
        if (_tree is not { } tree)
        {
            return;
        }

        CancellationToken token = _rootLife.Token;

        _ignore = GitIgnoreReader.For(tree.Root.FullPath);

        foreach (string folder in FolderTree.Walk(tree.Root).Where(n => n.IsFolder && n.IsLoaded).Select(n => n.FullPath).ToList())
        {
            await LoadAsync(folder, token, rebuild: false).ConfigureAwait(true);
        }

        tree.RecomputeIgnored();
        Rebuild();
    }

    // ------------------------------------------------------- file operations

    /// <summary>
    /// The one-line notice at the top of the panel after a file operation: what happened, or why
    /// it did not. It stays until the next operation replaces it or it is dismissed, never on a
    /// timer, which would penalize anyone who reads slowly or looked away.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string? NoticeText { get; private set; }

    [ObservableProperty]
    public partial bool NoticeIsError { get; private set; }

    /// <summary>The notice's Undo, after a rename. Null after a delete, or when there is nothing to undo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUndoNotice))]
    public partial Func<Task<bool>>? NoticeUndo { get; private set; }

    public bool HasNotice => NoticeText is not null;

    public bool CanUndoNotice => NoticeUndo is not null;

    public void ShowNotice(string text, Func<Task<bool>>? undo = null, bool isError = false)
    {
        NoticeUndo = undo;
        NoticeIsError = isError;
        NoticeText = text;
    }

    public void ClearNotice()
    {
        NoticeText = null;
        NoticeUndo = null;
        NoticeIsError = false;
    }

    /// <summary>The notice's Undo: runs it once. The notice it leaves says how that went.</summary>
    public async Task UndoNoticeAsync()
    {
        if (NoticeUndo is not { } undo)
        {
            return;
        }

        NoticeUndo = null;
        await undo().ConfigureAwait(true);
    }

    /// <summary>
    /// Lands a change the panel itself made at once, rather than when the watcher reports it a
    /// moment later; that later report then changes nothing (WP-1, idempotent Apply).
    /// </summary>
    public void ApplyLocalChange(FolderChange change)
    {
        if (_tree is not { } tree)
        {
            return;
        }

        Handle(tree.Apply([change]), _rootLife.Token);
        Rebuild();
    }

    /// <summary>Holds disk changes while a rename box is open, so no row moves out from under it.</summary>
    public void HoldTree() => _tree?.Hold();

    /// <summary>Lets the held changes land, once the rename box has closed.</summary>
    public void ReleaseTree()
    {
        if (_tree is { } tree)
        {
            Handle(tree.Release(), _rootLife.Token);
            Rebuild();
        }
    }

    // -------------------------------------------------------------------- loads

    private bool IsFiltering => !string.IsNullOrWhiteSpace(Filter);

    /// <summary>Reads a folder's entries on the pool and hands them to the tree.</summary>
    private async Task LoadAsync(string folder, CancellationToken token, bool rebuild = true)
    {
        bool showAll = ShowAllFiles;
        IReadOnlyList<FolderEntry> entries;

        try
        {
            entries = await Task.Run(() => FolderListing.Read(folder, showAll, token), token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested || _tree is not { } tree)
        {
            return;
        }

        Handle(tree.SetChildren(folder, entries), token);

        if (rebuild)
        {
            Rebuild();
        }
    }

    private async Task ReopenAsync(IReadOnlyList<string> folders, CancellationToken token)
    {
        // Parents before children: a child folder can only be found once its parent is read.
        foreach (string folder in folders.OrderBy(f => f.Length))
        {
            if (token.IsCancellationRequested || _tree is not { } tree)
            {
                return;
            }

            if (tree.Expand(folder))
            {
                await LoadAsync(folder, token, rebuild: false).ConfigureAwait(true);
            }
        }

        Rebuild();
    }

    /// <summary>Starts what the tree asked for: looks below new folders, and reads of changed ones.</summary>
    private void Handle(FolderWork work, CancellationToken token)
    {
        foreach (string folder in work.FoldersToProbe)
        {
            _ = ProbeAsync(folder, token);
        }

        foreach (string folder in work.FoldersToReload)
        {
            _ = LoadAsync(folder, token);
        }
    }

    private async Task ProbeAsync(string folder, CancellationToken token)
    {
        ProbeResult result;

        try
        {
            result = await _probe.ProbeAsync(folder, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested || _tree is not { } tree)
        {
            return;
        }

        tree.SetProbe(folder, result);
        QueueRebuild();
    }

    // ----------------------------------------------------------------- the disk

    private void OnDiskChanged(object? sender, IReadOnlyList<FolderChange> changes) =>
        _ui.Post(() =>
        {
            if (!ReferenceEquals(sender, _watcher) || _tree is not { } tree)
            {
                return;
            }

            // The reader caches each folder's rules; a changed .gitignore is forgotten before the
            // tree asks again.
            foreach (FolderChange change in changes)
            {
                foreach (string? path in new[] { change.Path, change.OldPath })
                {
                    if (path is not null
                        && string.Equals(Path.GetFileName(path), ".gitignore", StringComparison.OrdinalIgnoreCase)
                        && Path.GetDirectoryName(path) is { } folder)
                    {
                        _ignore?.Forget(folder);
                    }
                }
            }

            Handle(tree.Apply(changes), _rootLife.Token);
            Rebuild();
        });

    // --------------------------------------------------------------- the filter

    private void CancelFilterWalk()
    {
        _filterWalk?.Cancel();
        _filterWalk?.Dispose();
        _filterWalk = null;
    }

    partial void OnFilterChanged(string value)
    {
        CancelFilterWalk();
        IsFilterIncomplete = false;

        Rebuild();

        if (!string.IsNullOrWhiteSpace(value) && _tree is not null)
        {
            var walk = CancellationTokenSource.CreateLinkedTokenSource(_rootLife.Token);
            _filterWalk = walk;
            _ = WalkForFilterAsync(walk.Token);
        }
    }

    /// <summary>
    /// Reads the folders nobody has opened, so the filter can find a file in them (P17). Debounced,
    /// canceled by the next keystroke, and stopped at the probe's ceiling, which the foot of the
    /// list then admits to. Nothing extra is kept: the folders read stay closed.
    /// </summary>
    private async Task WalkForFilterAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(FilterDelay, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_tree is not { } tree)
        {
            return;
        }

        var queue = new Queue<string>(
            FolderTree.Walk(tree.Root)
                .Where(n => n.IsFolder && !n.IsLoaded && !n.IsReparsePoint && n.Probe != ProbeResult.Empty)
                .Select(n => n.FullPath));

        bool showAll = ShowAllFiles;
        int seen = 0;
        int sinceRebuild = 0;

        while (queue.TryDequeue(out string? folder))
        {
            if (token.IsCancellationRequested || !ReferenceEquals(tree, _tree))
            {
                return;
            }

            if (seen >= _probe.Ceiling)
            {
                IsFilterIncomplete = true;
                break;
            }

            IReadOnlyList<FolderEntry> entries;

            try
            {
                entries = await Task.Run(() => FolderListing.Read(folder, showAll, token), token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            seen += entries.Count;

            if (tree.Find(folder) is not { IsLoaded: false } node)
            {
                continue;
            }

            Handle(tree.SetChildren(folder, entries), _rootLife.Token);

            // A folder holding a listed file leads somewhere, and so does every folder above it;
            // said now rather than waiting for each look, so a match shows as soon as it is read.
            if (entries.Any(e => !e.IsFolder))
            {
                for (FolderNode? up = node; up is not null && up != tree.Root; up = up.Parent)
                {
                    if (up.Probe != ProbeResult.Leads)
                    {
                        tree.SetProbe(up.FullPath, ProbeResult.Leads);
                    }
                }
            }

            foreach (FolderNode child in node.Children)
            {
                if (child.IsFolder && !child.IsLoaded && !child.IsReparsePoint)
                {
                    queue.Enqueue(child.FullPath);
                }
            }

            if (++sinceRebuild >= 20)
            {
                sinceRebuild = 0;
                Rebuild();
            }
        }

        Rebuild();
    }

    // ------------------------------------------------------------------- rows

    private void QueueRebuild()
    {
        if (_rebuildQueued)
        {
            return;
        }

        _rebuildQueued = true;
        _ui.Post(() =>
        {
            _rebuildQueued = false;
            Rebuild();
        });
    }

    /// <summary>Builds the rows again and lands only the difference on <see cref="Rows"/> (P1).</summary>
    private void Rebuild()
    {
        IReadOnlyList<FolderRow> next = _tree is { } tree
            ? FolderRows.Build(tree, new RowOptions(Filter, SortByModified))
            : [];

        IReadOnlyList<RowChange> changes = FolderRows.Diff(_shown, next);
        _shown = next;

        if (changes.Count == 0)
        {
            return;
        }

        Dictionary<string, MarkdownDocument> open = OpenDocuments();
        string? active = _workspace.Active?.Path;

        foreach (RowChange change in changes)
        {
            switch (change)
            {
                case RowsRemoved removed:
                    for (int k = 0; k < removed.Count; k++)
                    {
                        Rows.RemoveAt(removed.Index);
                    }

                    break;

                case RowsInserted inserted:
                    for (int k = 0; k < inserted.Rows.Count; k++)
                    {
                        var row = new FolderRowViewModel(inserted.Rows[k]);
                        Decorate(row, open, active);
                        Rows.Insert(inserted.Index + k, row);
                    }

                    break;

                case RowUpdated updated:
                    Rows[updated.Index].Row = updated.Row;
                    Decorate(Rows[updated.Index], open, active);
                    break;
            }
        }

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(ShowsEmptyMessage));
        OnPropertyChanged(nameof(ShowsShowAllFilesLink));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    private Dictionary<string, MarkdownDocument> OpenDocuments()
    {
        var open = new Dictionary<string, MarkdownDocument>(StringComparer.OrdinalIgnoreCase);

        foreach (MarkdownDocument document in _workspace.Documents)
        {
            if (document.Path is { } path)
            {
                open[path] = document;
            }
        }

        return open;
    }

    private static void Decorate(FolderRowViewModel row, Dictionary<string, MarkdownDocument> open, string? active)
    {
        row.Marker = row.IsFolder
            ? FolderRowMarker.None
            : FolderRowViewModel.MarkerFor(open.GetValueOrDefault(row.FullPath));
        row.IsActive = active is not null && FolderPaths.Equal(row.FullPath, active);
    }

    // ------------------------------------------------------- the open documents

    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs e) =>
        _ui.Post(() =>
        {
            Dictionary<string, MarkdownDocument> open = OpenDocuments();
            string? active = _workspace.Active?.Path;

            foreach (FolderRowViewModel row in Rows)
            {
                Decorate(row, open, active);
            }

            FollowActive();
        });

    /// <summary>
    /// Opens the tree to the active document and brings it into view, when Reveal is on and the
    /// document is inside the root. Outside it, the foot line says so; an untitled tab reveals nothing.
    /// </summary>
    public void FollowActive()
    {
        string? path = _workspace.Active?.Path;

        ActiveFolder = path is null ? null : Path.GetDirectoryName(path);
        IsActiveOutsideRoot = path is not null && Root is not null && !FolderPaths.IsUnder(path, Root);

        if (path is null || _tree is null || !RevealActive || IsActiveOutsideRoot)
        {
            _revealedPath = null;
            return;
        }

        // Revealed once per arrival: a document being edited raises changes all the time, and
        // the tree must not keep scrolling back to it while someone browses elsewhere.
        if (_revealedPath is not null && FolderPaths.Equal(_revealedPath, path))
        {
            return;
        }

        _revealedPath = path;
        _ = RevealAsync(path, asked: false, _rootLife.Token);
    }

    /// <summary>
    /// Opens the tree to the active document, selects it and scrolls to it, once, whether or not
    /// Always Reveal is on. The way back to the document after the folders have been collapsed
    /// or browsed away from. A filter that hides the document is cleared first, since the
    /// tree cannot be opened while one is typed.
    /// </summary>
    public async Task RevealActiveNowAsync()
    {
        if (!CanRevealActive || _workspace.Active?.Path is not { } path)
        {
            return;
        }

        if (IsFiltering && RowOf(path) is null)
        {
            Filter = string.Empty;
        }

        await RevealAsync(path, asked: true, _rootLife.Token).ConfigureAwait(true);
    }

    /// <summary>
    /// Opens the folders down to <paramref name="path"/>. When the user <paramref name="asked"/>,
    /// the row is selected and scrolled to; when it is Always Reveal following a tab switch, it
    /// is only scrolled to, and not while someone is browsing the tree.
    /// </summary>
    private async Task RevealAsync(string path, bool asked, CancellationToken token)
    {
        if (_tree is not { } tree)
        {
            return;
        }

        // Each step opens what it can and names the first folder still to read.
        for (string? next = tree.Reveal(path); next is not null; next = tree.Reveal(path))
        {
            await LoadAsync(next, token, rebuild: false).ConfigureAwait(true);

            if (token.IsCancellationRequested || !ReferenceEquals(tree, _tree) || tree.Find(next) is not { IsLoaded: true })
            {
                return;
            }
        }

        // The folders on the way lead to this document, whatever their looks have yet to say.
        if (tree.Find(path) is { IsFolder: false } node)
        {
            for (FolderNode? up = node.Parent; up is not null && up != tree.Root; up = up.Parent)
            {
                if (up.Probe is ProbeResult.Pending or ProbeResult.Empty)
                {
                    tree.SetProbe(up.FullPath, ProbeResult.Leads);
                }
            }
        }

        Rebuild();

        if (RowOf(path) is { } row)
        {
            (asked ? SelectRequested : ScrollIntoViewRequested)?.Invoke(this, row);
        }
    }

    private static string NameOf(string folder)
    {
        string trimmed = FolderPaths.TrimEnd(folder);
        string name = Path.GetFileName(trimmed);

        return name.Length == 0 ? trimmed : name;
    }

    public void Dispose()
    {
        _workspace.Changed -= OnWorkspaceChanged;
        _rootLife.Cancel();
        _rootLife.Dispose();
        CancelFilterWalk();
        StopWatching();
        _probe.Dispose();
    }
}
