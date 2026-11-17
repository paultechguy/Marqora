// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Folders;

/// <summary>
/// Watches the whole root, below every folder, and hands over the changes in batches.
///
/// The settings are P18's. The buffer is 64 KB, the most a network share allows. Only names are
/// watched unless the tree sorts by date, because watching writes as well floods the buffer
/// while a build runs; so without the date sort, an edit to a <c>.gitignore</c> that only
/// rewrites it in place is picked up by <em>Refresh</em> rather than live. A watcher error (a
/// buffer overflow, a share dropping) asks for a rescan, at most once every five seconds. A watch
/// that cannot start, as on some shares, leaves <see cref="IsWatching"/> false, and the panel
/// relies on Refresh.
///
/// <see cref="ChangesReady"/> is raised on a pool thread.
/// </summary>
public sealed class FolderTreeWatcher : IDisposable
{
    public const int BufferSize = 64 * 1024;

    private readonly object _gate = new();
    private readonly FolderChangeCoalescer _coalescer;
    private readonly FileSystemWatcher? _watcher;
    private readonly Timer _timer;
    private readonly TimeProvider _time;
    private bool _disposed;

    public FolderTreeWatcher(string root, bool watchWrites, TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        _coalescer = new FolderChangeCoalescer(root);
        _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);

        try
        {
            var watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                InternalBufferSize = BufferSize,
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | (watchWrites ? NotifyFilters.LastWrite : 0),
            };

            watcher.Created += (_, e) => Add(new FolderChange(FolderChangeKind.Created, e.FullPath));
            watcher.Deleted += (_, e) => Add(new FolderChange(FolderChangeKind.Deleted, e.FullPath));
            watcher.Changed += (_, e) => Add(new FolderChange(FolderChangeKind.Changed, e.FullPath));
            watcher.Renamed += (_, e) => Add(new FolderChange(FolderChangeKind.Renamed, e.FullPath, e.OldFullPath));
            watcher.Error += (_, _) => Add(new FolderChange(FolderChangeKind.Rescan, root));

            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
            IsWatching = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // The panel still works; it just does not update on its own.
            IsWatching = false;
        }
    }

    /// <summary>False when the folder could not be watched; the panel then relies on Refresh.</summary>
    public bool IsWatching { get; }

    public event EventHandler<IReadOnlyList<FolderChange>>? ChangesReady;

    private void Add(FolderChange change)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _coalescer.Add(change, _time.GetUtcNow());
            Arm();
        }
    }

    private void Flush()
    {
        IReadOnlyList<FolderChange>? batch;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            batch = _coalescer.TakeIfQuiet(_time.GetUtcNow());

            if (batch is null)
            {
                Arm();
                return;
            }

            if (_coalescer.HasPending)
            {
                Arm();
            }
        }

        ChangesReady?.Invoke(this, batch);
    }

    private void Arm()
    {
        if (_coalescer.DueIn(_time.GetUtcNow()) is { } wait)
        {
            _timer.Change(wait, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _watcher?.Dispose();
        _timer.Dispose();
    }
}
