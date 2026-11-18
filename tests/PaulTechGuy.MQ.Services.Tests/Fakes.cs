// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Abstractions;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Folders;

namespace PaulTechGuy.MQ.Services.Tests;

/// <summary>
/// A watcher that watches nothing and reports whatever a test tells it to.
///
/// The real one is a thin wrapper over FileSystemWatcher, whose timing a test cannot control:
/// notifications arrive on their own thread after a debounce, and a test that waits for them
/// is a test that fails on a slow machine. Driving the events by hand leaves the workspace's
/// own decisions - compare, reload, mark, stay quiet - as the only thing under test.
/// </summary>
internal sealed class FakeFileWatcher : IFileWatcher
{
    public string? WatchedPath { get; private set; }

    public bool IsDisposed { get; private set; }

    public event EventHandler<string>? FileChanged;

    public event EventHandler<string>? FileRemoved;

    public void Watch(string path) => WatchedPath = path;

    public void StopWatching() => WatchedPath = null;

    /// <summary>Reports the watched file as rewritten, the way a debounced burst would.</summary>
    public void RaiseChanged() => FileChanged?.Invoke(this, WatchedPath!);

    /// <summary>Reports the watched file as gone.</summary>
    public void RaiseRemoved() => FileRemoved?.Invoke(this, WatchedPath!);

    /// <summary>
    /// Reports a path as gone, whether or not this watcher still watches it: a callback that read
    /// its path before a rename and lands after it.
    /// </summary>
    public void RaiseRemoved(string path) => FileRemoved?.Invoke(this, path);

    /// <summary>Reports a path as rewritten, as a late callback would.</summary>
    public void RaiseChanged(string path) => FileChanged?.Invoke(this, path);

    public void Dispose() => IsDisposed = true;
}

internal sealed class FakeFileWatcherFactory : IFileWatcherFactory
{
    public List<FakeFileWatcher> Created { get; } = [];

    /// <summary>The watcher for a path, or null when nothing is watching it.</summary>
    public FakeFileWatcher? For(string path) =>
        Created.LastOrDefault(w => string.Equals(w.WatchedPath, path, StringComparison.OrdinalIgnoreCase));

    public IFileWatcher Create()
    {
        var watcher = new FakeFileWatcher();
        Created.Add(watcher);

        return watcher;
    }
}

/// <summary>Settings held in memory, with no file behind them and no debounce to wait out.</summary>
internal sealed class FakeSettingsService(AppSettings? initial = null) : ISettingsService
{
    public AppSettings Current { get; private set; } = initial ?? AppSettings.Default;

    public event EventHandler<AppSettings>? SettingsChanged;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Update(Func<AppSettings, AppSettings> mutate)
    {
        Current = mutate(Current);
        SettingsChanged?.Invoke(this, Current);
    }

    public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>A scratch folder that cleans itself up, for the few tests that need real files.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "marqora-tests",
            Guid.NewGuid().ToString("n"));

        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Write(string name, string text)
    {
        string full = System.IO.Path.Combine(Path, name);
        File.WriteAllText(full, text);

        return full;
    }

    /// <summary>
    /// Rewrites a file and guarantees the timestamp moves.
    ///
    /// Windows file times have coarse resolution, so writing twice in the same test can leave
    /// the stamp identical - and the workspace would rightly conclude nothing had happened.
    /// A real external edit is separated by more than a few ticks; this restores that.
    /// </summary>
    public static void Rewrite(string full, string text)
    {
        File.WriteAllText(full, text);
        File.SetLastWriteTimeUtc(full, DateTime.UtcNow.AddSeconds(5));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover scratch folder is not worth failing a green test over.
        }
    }
}

/// <summary>
/// Read-only marks held in memory, with no file behind them.
///
/// The real service normalizes paths and prunes entries whose file is gone, both of which need
/// a disk to be interesting; tests that care about those drive the real one. What the workspace
/// needs from this is only the question "is this path marked", answered the same way twice.
/// </summary>
internal sealed class FakeDocumentLocks : IDocumentLocks
{
    private readonly HashSet<string> _locked = new(StringComparer.OrdinalIgnoreCase);

    public int DroppedOnLoad { get; }

    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public bool IsLocked(string? path) =>
        !string.IsNullOrWhiteSpace(path) && _locked.Contains(System.IO.Path.GetFullPath(path));

    public Task SetAsync(string path, bool locked, CancellationToken cancellationToken = default)
    {
        string key = System.IO.Path.GetFullPath(path);

        if (locked)
        {
            _locked.Add(key);
        }
        else
        {
            _locked.Remove(key);
        }

        return Task.CompletedTask;
    }

    /// <summary>The paths marked, as stored, for a test to check that a move left nothing behind.</summary>
    public IReadOnlyCollection<string> Stored => _locked;

    public PreparedMove PrepareMove(string from) => FakeMoves.Prepare(_locked, from);

    public Task CompleteMoveAsync(PreparedMove prepared, string destination, CancellationToken cancellationToken = default)
    {
        FakeMoves.Complete(_locked, prepared, destination);
        return Task.CompletedTask;
    }

    public bool AnyUnder(string folder) =>
        _locked.Any(k => FolderPaths.IsSameOrUnder(k, System.IO.Path.GetFullPath(folder)));
}

/// <summary>
/// A Recycle Bin that deletes for real, so a test can see the file go, or refuses when told to.
/// <see cref="Entered"/> and <see cref="Release"/> let a test hold a delete half-way, to see what
/// a save arriving meanwhile does.
/// </summary>
internal sealed class FakeRecycleBin : IRecycleBin
{
    private TaskCompletionSource? _pause;

    public string? FailWith { get; set; }

    public List<string> Recycled { get; } = [];

    /// <summary>Completes when a delete has started and is waiting to be released.</summary>
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Makes the next delete wait until <see cref="Release"/>.</summary>
    public void PauseNext() => _pause = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => _pause?.TrySetResult();

    public RecycleForecast Forecast(string path) => new(true, 1, 0);

    public async Task<RecycleResult> RecycleAsync(string path, CancellationToken cancellationToken = default)
    {
        if (_pause is { } pause)
        {
            Entered.TrySetResult();
            await pause.Task.ConfigureAwait(false);
        }

        if (FailWith is { } error)
        {
            return RecycleResult.Failed(error);
        }

        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        else
        {
            File.Delete(path);
        }

        Recycled.Add(path);
        return RecycleResult.Recycled;
    }
}

/// <summary>The fakes' version of a move: full paths only, no link resolution.</summary>
internal static class FakeMoves
{
    public static PreparedMove Prepare(HashSet<string> set, string from)
    {
        string full = System.IO.Path.GetFullPath(from);
        return new PreparedMove(full, full, [.. set.Where(k => FolderPaths.IsSameOrUnder(k, full))]);
    }

    public static void Complete(HashSet<string> set, PreparedMove prepared, string to)
    {
        string toFull = System.IO.Path.GetFullPath(to);

        foreach (string key in prepared.Keys)
        {
            if (set.Remove(key))
            {
                set.Add(FolderPaths.Rebase(key, prepared.From, toFull));
            }
        }
    }
}

/// <summary>
/// Pins held in memory, with no file behind them. The twin of <see cref="FakeDocumentLocks"/>
/// above, and stood up for the same reason: the workspace only ever asks this one question, and
/// the pruning and path-keying that make the real service interesting want a disk.
///
/// <see cref="DocumentPinsTests"/> drives the real one.
/// </summary>
internal sealed class FakeDocumentPins : IDocumentPins
{
    private readonly HashSet<string> _pinned = new(StringComparer.OrdinalIgnoreCase);

    public int DroppedOnLoad { get; }

    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public bool IsPinned(string? path) =>
        !string.IsNullOrWhiteSpace(path) && _pinned.Contains(System.IO.Path.GetFullPath(path));

    public Task SetAsync(string path, bool pinned, CancellationToken cancellationToken = default)
    {
        string key = System.IO.Path.GetFullPath(path);

        if (pinned)
        {
            _pinned.Add(key);
        }
        else
        {
            _pinned.Remove(key);
        }

        return Task.CompletedTask;
    }

    /// <summary>The paths pinned, as stored, for a test to check that a move left nothing behind.</summary>
    public IReadOnlyCollection<string> Stored => _pinned;

    public PreparedMove PrepareMove(string from) => FakeMoves.Prepare(_pinned, from);

    public Task CompleteMoveAsync(PreparedMove prepared, string destination, CancellationToken cancellationToken = default)
    {
        FakeMoves.Complete(_pinned, prepared, destination);
        return Task.CompletedTask;
    }

    public Task RemoveUnderAsync(string path, CancellationToken cancellationToken = default)
    {
        string full = System.IO.Path.GetFullPath(path);
        _pinned.RemoveWhere(k => FolderPaths.IsSameOrUnder(k, full));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Paths pointed at a scratch folder: an "install" directory holding what ships, and a data
/// directory standing in for %LOCALAPPDATA%. The real implementation lives in the
/// repositories layer, which these tests do not reference.
/// </summary>
internal sealed class FakeAppPaths(string root) : IAppPaths
{
    public string DataDirectory { get; } = System.IO.Path.Combine(root, "data");

    public string InstallDirectory { get; } = System.IO.Path.Combine(root, "install");

    public string SettingsFilePath => System.IO.Path.Combine(DataDirectory, "settings.json");

    public string RecentFilesFilePath => System.IO.Path.Combine(DataDirectory, "recent-files.json");

    public string DocumentLocksFilePath => System.IO.Path.Combine(DataDirectory, "document-locks.json");

    public string DocumentPinsFilePath => System.IO.Path.Combine(DataDirectory, "document-pins.json");

    public string UserDictionaryPath => System.IO.Path.Combine(DataDirectory, "user-dictionary.txt");

    public string LogDirectory => System.IO.Path.Combine(DataDirectory, "logs");

    public string SnippetsDirectory => System.IO.Path.Combine(DataDirectory, "snippets");

    public string RecycleDirectory => System.IO.Path.Combine(DataDirectory, "recycle");

    public string WebAssetsDirectory => System.IO.Path.Combine(InstallDirectory, "Assets", "web");

    public string WelcomeTemplatePath =>
        System.IO.Path.Combine(InstallDirectory, "Assets", "Welcome to Marqora.md");

    public string WelcomeDocumentPath =>
        System.IO.Path.Combine(DataDirectory, "Welcome to Marqora.md");

    public string CliManualPath => System.IO.Path.Combine(InstallDirectory, "docs", "CLI.md");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(System.IO.Path.Combine(InstallDirectory, "Assets"));
    }
}
