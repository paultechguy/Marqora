// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Folders;

/// <summary>
/// Gathers the watcher's reports into batches, handed over once the folder has been quiet for
/// <see cref="Quiet"/>, the same wait the disk watch uses for an open file.
///
/// Separate from the watcher, and given the time rather than reading a clock, so the batching can
/// be tested without waiting. Not thread-safe; the watcher locks around it.
/// </summary>
public sealed class FolderChangeCoalescer
{
    public static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(300);

    /// <summary>A rescan reads every loaded folder again, so a watcher in trouble may ask for one at most this often (P18).</summary>
    public static readonly TimeSpan RescanInterval = TimeSpan.FromSeconds(5);

    private readonly string _root;
    private readonly List<FolderChange> _pending = [];
    private DateTimeOffset _lastAdded;
    private DateTimeOffset? _lastRescan;
    private bool _rescanWanted;

    public FolderChangeCoalescer(string root) => _root = FolderPaths.TrimEnd(Path.GetFullPath(root));

    public bool HasPending => _pending.Count > 0 || _rescanWanted;

    public void Add(FolderChange change, DateTimeOffset at)
    {
        if (change.Kind == FolderChangeKind.Rescan)
        {
            _rescanWanted = true;
            _lastAdded = at;
            return;
        }

        // Inside .git, node_modules and the rest, nothing the tree shows can change. A busy
        // build or a git command writes thousands of these.
        if (IsInsideHidden(change.Path) && (change.OldPath is null || IsInsideHidden(change.OldPath)))
        {
            return;
        }

        _lastAdded = at;

        if (change.Kind == FolderChangeKind.Changed
            && _pending.Any(p => p.Kind is FolderChangeKind.Changed or FolderChangeKind.Created
                && FolderPaths.Equal(p.Path, change.Path)))
        {
            return;
        }

        if (_pending.Count > 0 && _pending[^1] == change)
        {
            return;
        }

        _pending.Add(change);
    }

    /// <summary>How long until a batch could be handed over, or null when nothing is waiting.</summary>
    public TimeSpan? DueIn(DateTimeOffset now)
    {
        if (!HasPending)
        {
            return null;
        }

        DateTimeOffset due = _lastAdded + Quiet;

        if (_rescanWanted && _lastRescan is { } last && last + RescanInterval > due)
        {
            due = last + RescanInterval;
        }

        TimeSpan wait = due - now;
        return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
    }

    /// <summary>The batch, once the folder has been quiet long enough; otherwise null.</summary>
    public IReadOnlyList<FolderChange>? TakeIfQuiet(DateTimeOffset now)
    {
        if (DueIn(now) is not { } wait || wait > TimeSpan.Zero)
        {
            return null;
        }

        if (_rescanWanted)
        {
            // A rescan reads everything again, so the single changes are already covered.
            _rescanWanted = false;
            _pending.Clear();
            _lastRescan = now;
            return [new FolderChange(FolderChangeKind.Rescan, _root)];
        }

        List<FolderChange> batch = [.. _pending];
        _pending.Clear();
        return batch;
    }

    /// <summary>Whether any folder between the root and the path is one the tree never shows.</summary>
    private bool IsInsideHidden(string path)
    {
        if (!FolderPaths.IsUnder(path, _root))
        {
            return false;
        }

        string rest = FolderPaths.TrimEnd(path)[_root.Length..].TrimStart('\\', '/');
        string[] parts = rest.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);

        // The last part is the thing that changed; only the folders it is inside count.
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (FolderListing.IsAlwaysHiddenFolder(parts[i]))
            {
                return true;
            }
        }

        return false;
    }
}
