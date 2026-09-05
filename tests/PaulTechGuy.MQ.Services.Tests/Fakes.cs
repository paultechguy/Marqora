// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Abstractions;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Domain;

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

    public string UserDictionaryPath => System.IO.Path.Combine(DataDirectory, "user-dictionary.txt");

    public string LogDirectory => System.IO.Path.Combine(DataDirectory, "logs");

    public string SnippetsDirectory => System.IO.Path.Combine(DataDirectory, "snippets");

    public string WebAssetsDirectory => System.IO.Path.Combine(InstallDirectory, "Assets", "web");

    public string WelcomeTemplatePath =>
        System.IO.Path.Combine(InstallDirectory, "Assets", "Welcome to Marqora.md");

    public string WelcomeDocumentPath =>
        System.IO.Path.Combine(DataDirectory, "Welcome to Marqora.md");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(System.IO.Path.Combine(InstallDirectory, "Assets"));
    }
}
