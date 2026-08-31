// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Services;

namespace PaulTechGuy.MQ.Services;

/// <summary>
/// Watches one file for external edits.
///
/// Editors rarely produce a single notification: many write a temporary file and rename it
/// over the original, which surfaces as a burst of Changed, Deleted and Created events. All
/// of it is coalesced into one FileChanged after a short quiet period.
/// </summary>
public sealed class FileWatcher(ILogger<FileWatcher> logger) : IFileWatcher
{
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(300);

    private readonly Lock _sync = new();

    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private string? _watchedPath;
    private bool _disposed;

    public string? WatchedPath
    {
        get
        {
            lock (_sync)
            {
                return _watchedPath;
            }
        }
    }

    public event EventHandler<string>? FileChanged;

    public event EventHandler<string>? FileRemoved;

    public void Watch(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ObjectDisposedException.ThrowIf(_disposed, this);

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        string fileName = Path.GetFileName(fullPath);

        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
        {
            logger.LogWarning("Cannot watch {Path}: not a rooted file path.", path);
            return;
        }

        lock (_sync)
        {
            StopWatchingCore();

            try
            {
                _watcher = new FileSystemWatcher(directory, fileName)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    IncludeSubdirectories = false,
                };

                _watcher.Changed += OnChanged;
                _watcher.Created += OnChanged;
                _watcher.Renamed += OnRenamed;
                _watcher.Deleted += OnChanged;
                _watcher.Error += OnError;
                _watcher.EnableRaisingEvents = true;

                _watchedPath = fullPath;
                logger.LogDebug("Watching {Path} for external changes.", fullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // A failed watch degrades reload-on-change; it must not stop the file opening.
                logger.LogWarning(ex, "Could not watch {Path}.", path);
                StopWatchingCore();
            }
        }
    }

    public void StopWatching()
    {
        lock (_sync)
        {
            StopWatchingCore();
        }
    }

    private void StopWatchingCore()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnChanged;
            _watcher.Created -= OnChanged;
            _watcher.Renamed -= OnRenamed;
            _watcher.Deleted -= OnChanged;
            _watcher.Error -= OnError;
            _watcher.Dispose();
            _watcher = null;
        }

        _debounce?.Dispose();
        _debounce = null;
        _watchedPath = null;
    }

    private void OnChanged(object sender, FileSystemEventArgs e) => QueueNotification();

    private void OnRenamed(object sender, RenamedEventArgs e) => QueueNotification();

    /// <summary>
    /// A watch that broke. Two things cause this and they want opposite responses, so the file
    /// itself is asked which happened.
    ///
    /// Gone means the folder went with it, and the document is missing. Still there means the
    /// watcher's internal buffer most likely overflowed and events were dropped, so the watch
    /// is re-armed and a change reported - the caller compares content before telling anyone,
    /// which makes a false alarm here cost nothing.
    ///
    /// Previously this only logged, so deleting a document's folder left the tab watching
    /// nothing with no sign of it anywhere but the log.
    /// </summary>
    private void OnError(object sender, ErrorEventArgs e)
    {
        string? path = WatchedPath;
        logger.LogWarning(e.GetException(), "File watcher reported an error for {Path}.", path);

        if (path is null)
        {
            return;
        }

        // Both branches below dispose the FileSystemWatcher, and this runs on that watcher's
        // own callback. Disposing it from inside its own event is a good way to deadlock, so
        // the recovery happens on a borrowed thread instead.
        _ = Task.Run(() => Recover(path));
    }

    private void Recover(string path)
    {
        if (_disposed)
        {
            return;
        }

        if (!File.Exists(path))
        {
            StopWatching();
            FileRemoved?.Invoke(this, path);
            return;
        }

        Watch(path);
        QueueNotification();
    }

    private void QueueNotification()
    {
        lock (_sync)
        {
            if (_watchedPath is null)
            {
                return;
            }

            _debounce?.Dispose();
            _debounce = new Timer(_ => Notify(), null, QuietPeriod, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Runs once the file has been quiet. Existence is checked here rather than in the
    /// event handlers, because the delete half of a replace is transient.
    /// </summary>
    private void Notify()
    {
        string? path = WatchedPath;

        if (path is null)
        {
            return;
        }

        if (File.Exists(path))
        {
            FileChanged?.Invoke(this, path);
        }
        else
        {
            logger.LogInformation("Watched file {Path} is no longer present.", path);
            FileRemoved?.Invoke(this, path);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopWatching();
    }
}
