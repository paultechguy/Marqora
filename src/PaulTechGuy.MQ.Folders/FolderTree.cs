// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Folders;

/// <summary>One file or folder the tree knows about.</summary>
public sealed class FolderNode
{
    private readonly List<FolderNode>? _children;

    internal FolderNode(FolderEntry entry, FolderNode? parent, bool isIgnored)
    {
        Name = entry.Name;
        FullPath = entry.FullPath;
        Kind = entry.Kind;
        IsReparsePoint = entry.IsReparsePoint;
        LastWriteUtc = entry.LastWriteUtc;
        Parent = parent;
        IsIgnored = isIgnored;

        if (entry.IsFolder)
        {
            _children = [];
        }
    }

    public string Name { get; internal set; }

    public string FullPath { get; internal set; }

    public FolderEntryKind Kind { get; internal set; }

    public bool IsFolder => Kind == FolderEntryKind.Folder;

    public bool IsReparsePoint { get; internal set; }

    public DateTime LastWriteUtc { get; internal set; }

    public FolderNode? Parent { get; internal set; }

    /// <summary>Named by a <c>.gitignore</c>. Grayed, never hidden.</summary>
    public bool IsIgnored { get; internal set; }

    public bool IsExpanded { get; internal set; }

    /// <summary>Whether the folder's own entries have been read. Never true for a file.</summary>
    public bool IsLoaded { get; internal set; }

    /// <summary>What a look below this folder found. Meaningless for a file.</summary>
    public ProbeResult Probe { get; internal set; } = ProbeResult.Pending;

    /// <summary>The folder's entries, empty until <see cref="IsLoaded"/>.</summary>
    public IReadOnlyList<FolderNode> Children => _children ?? [];

    internal List<FolderNode> MutableChildren =>
        _children ?? throw new InvalidOperationException($"'{FullPath}' is a file and has no children.");

    public override string ToString() => FullPath;
}

/// <summary>What a change to the disk, reported by the watcher or made by the panel, was.</summary>
public enum FolderChangeKind
{
    Created,
    Deleted,
    Renamed,

    /// <summary>A file was written; only its date (and, for <c>.gitignore</c>, the rules) can have changed.</summary>
    Changed,

    /// <summary>The watcher lost track (its buffer overflowed). Every loaded folder is read again.</summary>
    Rescan,
}

/// <param name="OldPath">For <see cref="FolderChangeKind.Renamed"/> only: the path before.</param>
public sealed record FolderChange(FolderChangeKind Kind, string Path, string? OldPath = null);

/// <summary>What the tree needs from its owner after a change: more reading.</summary>
/// <param name="FoldersToProbe">Folders whose "leads to anything" answer is now unknown.</param>
/// <param name="FoldersToReload">Loaded folders whose entries should be read again.</param>
/// <param name="IgnoreRulesChanged">A <c>.gitignore</c> changed; the ignored flags are being recomputed.</param>
public sealed record FolderWork(
    IReadOnlyList<string> FoldersToProbe,
    IReadOnlyList<string> FoldersToReload,
    bool IgnoreRulesChanged)
{
    public static FolderWork None { get; } = new([], [], false);

    public bool IsEmpty => FoldersToProbe.Count == 0 && FoldersToReload.Count == 0 && !IgnoreRulesChanged;
}

/// <summary>
/// The folder panel's tree: what is known below the root, what is expanded, and how the disk's
/// changes land on it.
///
/// It is loaded lazily. A folder's entries are read when it is first expanded, by the owner, which
/// hands them over with <see cref="SetChildren"/>; the tree itself does no slow I/O on the thread
/// that owns it, apart from describing a single path when the watcher reports one. Every method
/// here is meant to run on one thread, the UI's.
///
/// <see cref="Apply"/> is idempotent. The panel's own rename is applied the moment it succeeds,
/// and the watcher's report of the same rename arrives 300 ms later; applying that second report
/// changes nothing. While a rename box is open (<see cref="Hold"/>), changes are queued, so a row
/// cannot move out from under the text box; they are applied when it closes.
/// </summary>
public sealed class FolderTree
{
    private readonly Func<string, bool, bool> _isIgnored;
    private readonly List<FolderChange> _held = [];
    private int _holds;

    /// <param name="root">The folder the panel shows. Its own row is the header, not a tree row.</param>
    /// <param name="isIgnored">Whether a full path (and whether it is a folder) is named by .gitignore.</param>
    public FolderTree(string root, bool showAllFiles, Func<string, bool, bool>? isIgnored = null)
    {
        string full = FolderPaths.TrimEnd(Path.GetFullPath(root));
        _isIgnored = isIgnored ?? ((_, _) => false);
        ShowAllFiles = showAllFiles;

        string name = Path.GetFileName(full);

        Root = new FolderNode(
            new FolderEntry(name.Length == 0 ? full : name, full, FolderEntryKind.Folder, false, default),
            parent: null,
            isIgnored: false)
        {
            IsExpanded = true,
            Probe = ProbeResult.Leads,
        };
    }

    public FolderNode Root { get; }

    public bool ShowAllFiles { get; }

    public bool IsHeld => _holds > 0;

    /// <summary>Whether a folder is shown: always with Show all files, otherwise once a look finds something (or cannot tell).</summary>
    public bool IsShown(FolderNode node) =>
        !node.IsFolder
        || node == Root
        || ShowAllFiles
        || node.Probe is ProbeResult.Leads or ProbeResult.Unknown;

    public FolderNode? Find(string path)
    {
        string full = FolderPaths.TrimEnd(path);

        if (FolderPaths.Equal(full, Root.FullPath))
        {
            return Root;
        }

        if (!FolderPaths.IsUnder(full, Root.FullPath))
        {
            return null;
        }

        string rest = full[Root.FullPath.Length..].TrimStart('\\', '/');
        FolderNode node = Root;

        foreach (string part in rest.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            FolderNode? next = null;

            foreach (FolderNode child in node.Children)
            {
                if (string.Equals(child.Name, part, StringComparison.OrdinalIgnoreCase))
                {
                    next = child;
                    break;
                }
            }

            if (next is null)
            {
                return null;
            }

            node = next;
        }

        return node;
    }

    /// <summary>
    /// Hands over a folder's entries, read by the owner. A child already known keeps its state
    /// (expansion, its own entries, its look's answer), so reading a folder again after a rescan
    /// does not collapse anything. Answers which new child folders need a look.
    /// </summary>
    public FolderWork SetChildren(string folder, IReadOnlyList<FolderEntry> entries)
    {
        FolderNode? node = Find(folder);

        if (node is null || !node.IsFolder)
        {
            return FolderWork.None;
        }

        var keep = node.Children.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var children = new List<FolderNode>(entries.Count);
        var toProbe = new List<string>();

        foreach (FolderEntry entry in entries)
        {
            if (keep.TryGetValue(entry.Name, out FolderNode? known) && known.Kind == entry.Kind)
            {
                known.Name = entry.Name;
                known.LastWriteUtc = entry.LastWriteUtc;
                children.Add(known);
                continue;
            }

            FolderNode child = NewNode(entry, node);
            children.Add(child);

            if (child.IsFolder && !ShowAllFiles)
            {
                toProbe.Add(child.FullPath);
            }
        }

        node.MutableChildren.Clear();
        node.MutableChildren.AddRange(children);
        node.IsLoaded = true;

        RecomputeLocally(node);

        return new FolderWork(toProbe, [], false);
    }

    public void SetProbe(string folder, ProbeResult result)
    {
        if (Find(folder) is { IsFolder: true } node && node != Root)
        {
            node.Probe = result;

            if (node.Parent is { } parent)
            {
                RecomputeUpward(parent);
            }
        }
    }

    /// <summary>Opens a folder. True when its entries still have to be read.</summary>
    public bool Expand(string folder)
    {
        if (Find(folder) is not { IsFolder: true } node)
        {
            return false;
        }

        node.IsExpanded = true;
        return !node.IsLoaded;
    }

    public void Collapse(string folder)
    {
        if (Find(folder) is { IsFolder: true } node && node != Root)
        {
            node.IsExpanded = false;
        }
    }

    public void CollapseAll()
    {
        foreach (FolderNode node in Walk(Root))
        {
            if (node != Root)
            {
                node.IsExpanded = false;
            }
        }
    }

    /// <summary>The folders that are open, so a fresh tree (after Show all files changes) can open them again.</summary>
    public IReadOnlyList<string> ExpandedFolders() =>
        [.. Walk(Root).Where(n => n != Root && n.IsFolder && n.IsExpanded).Select(n => n.FullPath)];

    /// <summary>
    /// Opens every folder on the way to <paramref name="path"/>. Answers the first folder on that
    /// way whose entries have not been read, for the owner to read and hand over before asking
    /// again; null once the way is open (or the path is not under the root).
    /// </summary>
    public string? Reveal(string path)
    {
        string full = FolderPaths.TrimEnd(path);

        if (!FolderPaths.IsUnder(full, Root.FullPath))
        {
            return null;
        }

        string rest = full[Root.FullPath.Length..].TrimStart('\\', '/');
        string[] parts = rest.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        FolderNode node = Root;

        // Every part but the last is a folder to open.
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (!node.IsLoaded)
            {
                return node.FullPath;
            }

            FolderNode? next = node.Children.FirstOrDefault(
                c => c.IsFolder && string.Equals(c.Name, parts[i], StringComparison.OrdinalIgnoreCase));

            if (next is null)
            {
                return null;
            }

            next.IsExpanded = true;
            node = next;
        }

        return node.IsLoaded ? null : node.FullPath;
    }

    /// <summary>Stops changes landing until <see cref="Release"/>; while a rename box is open.</summary>
    public void Hold() => _holds++;

    /// <summary>Applies whatever arrived while held.</summary>
    public FolderWork Release()
    {
        if (_holds == 0)
        {
            return FolderWork.None;
        }

        if (--_holds > 0 || _held.Count == 0)
        {
            return FolderWork.None;
        }

        List<FolderChange> queued = [.. _held];
        _held.Clear();

        return Apply(queued);
    }

    /// <summary>Lands a batch of disk changes on the tree. Safe to repeat.</summary>
    public FolderWork Apply(IReadOnlyList<FolderChange> changes)
    {
        if (IsHeld)
        {
            _held.AddRange(changes);
            return FolderWork.None;
        }

        var probe = new List<string>();
        var reload = new List<string>();
        bool rules = false;

        foreach (FolderChange change in changes)
        {
            switch (change.Kind)
            {
                case FolderChangeKind.Created:
                    Created(change.Path, probe);
                    break;

                case FolderChangeKind.Deleted:
                    Deleted(change.Path, probe);
                    break;

                case FolderChangeKind.Renamed when change.OldPath is not null:
                    Renamed(change.OldPath, change.Path, probe);
                    break;

                case FolderChangeKind.Changed:
                    if (Find(change.Path) is { } changed && !changed.IsFolder)
                    {
                        changed.LastWriteUtc = File.GetLastWriteTimeUtc(changed.FullPath);
                    }

                    break;

                case FolderChangeKind.Rescan:
                    reload.AddRange(Walk(Root).Where(n => n.IsFolder && n.IsLoaded).Select(n => n.FullPath));
                    break;
            }

            rules |= IsGitIgnore(change.Path) || (change.OldPath is not null && IsGitIgnore(change.OldPath));
        }

        if (rules)
        {
            RecomputeIgnored();
        }

        return new FolderWork(
            [.. probe.Distinct(StringComparer.OrdinalIgnoreCase)],
            [.. reload.Distinct(StringComparer.OrdinalIgnoreCase)],
            rules);
    }

    /// <summary>Asks the ignore rules again for every known node, after a <c>.gitignore</c> changed.</summary>
    public void RecomputeIgnored()
    {
        foreach (FolderNode node in Walk(Root))
        {
            if (node != Root)
            {
                node.IsIgnored = _isIgnored(node.FullPath, node.IsFolder);
            }
        }
    }

    public static IEnumerable<FolderNode> Walk(FolderNode from)
    {
        var stack = new Stack<FolderNode>();
        stack.Push(from);

        while (stack.TryPop(out FolderNode? node))
        {
            yield return node;

            for (int i = node.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(node.Children[i]);
            }
        }
    }

    private void Created(string path, List<string> probe)
    {
        if (Path.GetDirectoryName(FolderPaths.TrimEnd(path)) is not { } parentPath)
        {
            return;
        }

        FolderNode? parent = Find(parentPath);

        if (parent is null || !parent.IsLoaded)
        {
            // Below a folder not read yet. Its entries will be read when it opens, but whether it
            // leads to anything may just have changed.
            if (FolderListing.KindOfFile(Path.GetFileName(path)) is FolderEntryKind.Markdown or FolderEntryKind.Image)
            {
                MarkLeadsUpTo(parentPath);
            }
            else if (NearestKnown(parentPath) is { } near && near != Root && near.Probe == ProbeResult.Empty)
            {
                probe.Add(near.FullPath);
            }

            return;
        }

        FolderEntry? entry = FolderListing.Describe(path, ShowAllFiles);

        if (entry is null || (entry.IsFolder && FolderListing.IsAlwaysHiddenFolder(entry.Name)))
        {
            return;
        }

        FolderNode? existing = parent.Children.FirstOrDefault(
            c => string.Equals(c.Name, entry.Name, StringComparison.OrdinalIgnoreCase));

        if (existing is not null && existing.Kind == entry.Kind)
        {
            existing.Name = entry.Name;
            existing.LastWriteUtc = entry.LastWriteUtc;
            return;
        }

        if (existing is not null)
        {
            parent.MutableChildren.Remove(existing);
        }

        FolderNode node = NewNode(entry, parent);
        parent.MutableChildren.Add(node);

        if (node.IsFolder && !ShowAllFiles)
        {
            probe.Add(node.FullPath);
        }

        RecomputeUpward(parent);
    }

    private void Deleted(string path, List<string> probe)
    {
        FolderNode? node = Find(path);

        if (node is null)
        {
            // Below a folder not read yet: if that folder was thought to lead somewhere, it may
            // not any more.
            if (Path.GetDirectoryName(FolderPaths.TrimEnd(path)) is { } parentPath
                && NearestKnown(parentPath) is { } near
                && near != Root
                && !near.IsLoaded
                && near.Probe == ProbeResult.Leads)
            {
                probe.Add(near.FullPath);
            }

            return;
        }

        if (node == Root || node.Parent is not { } parent)
        {
            return;
        }

        parent.MutableChildren.Remove(node);
        RecomputeUpward(parent, probe);
    }

    private void Renamed(string oldPath, string newPath, List<string> probe)
    {
        FolderNode? node = Find(oldPath);
        FolderNode? already = Find(newPath);

        if (node is null || (already is not null && already != node))
        {
            // Applied already (the panel did it), or the old name was never known: make sure the
            // old one is gone and the new one is there.
            if (node is not null)
            {
                Deleted(oldPath, probe);
            }

            if (already is null)
            {
                Created(newPath, probe);
            }

            return;
        }

        string? oldParent = Path.GetDirectoryName(FolderPaths.TrimEnd(oldPath));
        string? newParent = Path.GetDirectoryName(FolderPaths.TrimEnd(newPath));
        string newName = Path.GetFileName(FolderPaths.TrimEnd(newPath));

        bool sameFolder = oldParent is not null && newParent is not null && FolderPaths.Equal(oldParent, newParent);
        bool stillListed = node.IsFolder
            ? !FolderListing.IsAlwaysHiddenFolder(newName)
            : FolderListing.IsListedFile(newName, ShowAllFiles);

        if (!sameFolder || !stillListed)
        {
            Deleted(oldPath, probe);

            if (stillListed)
            {
                Created(newPath, probe);
            }

            return;
        }

        // A rename in place keeps the node, so its expansion and everything read below it stay.
        string from = node.FullPath;
        string to = Path.Combine(newParent!, newName);

        foreach (FolderNode n in Walk(node))
        {
            n.FullPath = FolderPaths.Rebase(n.FullPath, from, to);
        }

        node.Name = newName;

        if (!node.IsFolder)
        {
            node.Kind = FolderListing.KindOfFile(newName);
        }

        foreach (FolderNode n in Walk(node))
        {
            n.IsIgnored = _isIgnored(n.FullPath, n.IsFolder);
        }

        if (node.Parent is { } parent)
        {
            RecomputeUpward(parent, probe);
        }
    }

    private FolderNode NewNode(FolderEntry entry, FolderNode parent) =>
        new(entry, parent, _isIgnored(entry.FullPath, entry.IsFolder));

    /// <summary>The deepest known node on the way to a path (the root at least, if the path is under it).</summary>
    private FolderNode? NearestKnown(string path)
    {
        for (string? p = FolderPaths.TrimEnd(path); p is not null; p = Path.GetDirectoryName(p))
        {
            if (Find(p) is { } node)
            {
                return node;
            }

            if (!FolderPaths.IsUnder(p, Root.FullPath))
            {
                return null;
            }
        }

        return null;
    }

    private void MarkLeadsUpTo(string folder)
    {
        for (FolderNode? node = NearestKnown(folder); node is not null && node != Root; node = node.Parent)
        {
            node.Probe = ProbeResult.Leads;
        }
    }

    /// <summary>
    /// Works out a loaded folder's answer from its own entries where they settle it: a document or
    /// a leading subfolder means it leads; nothing at all, or only empty subfolders, means it does not.
    /// </summary>
    private static ProbeResult? Local(FolderNode folder)
    {
        if (!folder.IsLoaded)
        {
            return null;
        }

        bool unsettled = false;

        foreach (FolderNode child in folder.Children)
        {
            if (child.IsFolder)
            {
                switch (child.Probe)
                {
                    case ProbeResult.Leads:
                        return ProbeResult.Leads;
                    case ProbeResult.Pending:
                    case ProbeResult.Unknown:
                        unsettled = true;
                        break;
                }
            }
            else if (child.Kind is FolderEntryKind.Markdown or FolderEntryKind.Image && !child.Name.StartsWith('.'))
            {
                return ProbeResult.Leads;
            }
        }

        return unsettled ? null : ProbeResult.Empty;
    }

    private void RecomputeLocally(FolderNode folder)
    {
        if (folder != Root && Local(folder) is { } answer)
        {
            folder.Probe = answer;
        }
    }

    private void RecomputeUpward(FolderNode from, List<string>? probe = null)
    {
        for (FolderNode? node = from; node is not null && node != Root; node = node.Parent)
        {
            ProbeResult before = node.Probe;
            ProbeResult? answer = Local(node);

            if (answer is { } settled)
            {
                node.Probe = settled;
            }
            else
            {
                // Its own entries do not settle it (a subfolder is still being looked at), and
                // what was known before may no longer hold: look again.
                probe?.Add(node.FullPath);
                break;
            }

            if (node.Probe == before)
            {
                break;
            }
        }
    }

    private static bool IsGitIgnore(string path) =>
        string.Equals(Path.GetFileName(path), ".gitignore", StringComparison.OrdinalIgnoreCase);
}
