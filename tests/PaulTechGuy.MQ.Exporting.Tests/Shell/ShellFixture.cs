// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Rendering;
using PaulTechGuy.MQ.Repositories;
using PaulTechGuy.MQ.Themes;
using PaulTechGuy.MQ.WebRender;
using PaulTechGuy.MQ.WebRender.Win32;
using Xunit;

namespace PaulTechGuy.MQ.Exporting.Tests.Shell;

/// <summary>
/// The tests that need the shell, one at a time: they share one browser, and the shell has one tab.
/// </summary>
[CollectionDefinition(nameof(ShellCollection), DisableParallelization = true)]
public sealed class ShellCollection : ICollectionFixture<ShellFixture>;

/// <summary>
/// One headless host for the collection, started as marqora-cli starts its own: on a render thread
/// of its own, under a message-only window, in a data folder of its own that is removed after. The
/// shell is the one copied next to these tests, so it is the shell the app and marqora-cli ship.
/// </summary>
public sealed class ShellFixture : IAsyncLifetime
{
    /// <summary>How long a render may take here: long enough for a slow machine, short of a hung test run.</summary>
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(60);

    private readonly RenderThread _thread = new("export tests render");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "marqora-export-tests", Guid.NewGuid().ToString("n"));

    private ParentWindow? _window;

    private HeadlessRenderHost? _host;

    public ThemeCatalog Themes { get; } = ThemeCatalog.Load();

    /// <summary>Runs <paramref name="work"/> with the host, on the thread the host lives on.</summary>
    public Task<T> RunAsync<T>(Func<HeadlessRenderHost, Task<T>> work) =>
        _thread.RunAsync(() => work(_host ?? throw new InvalidOperationException("The shell has not started.")));

    public async ValueTask InitializeAsync()
    {
        var paths = new AppPaths(_folder, AppContext.BaseDirectory);
        var assets = new WebAssetProvider(paths, NullLogger<WebAssetProvider>.Instance);

        if (!assets.IsAvailable)
        {
            throw new InvalidOperationException($"The shell's files are incomplete in {assets.RootDirectory}: {string.Join(", ", assets.MissingAssets)}. Run build/Get-WebAssets.ps1.");
        }

        await _thread.RunAsync(async () =>
        {
            _window = ParentWindow.Create(ParentWindowKind.MessageOnly, 1280, 1600);
            _host = await HeadlessRenderHost
                .StartAsync(
                    _window.Handle,
                    assets,
                    Path.Combine(_folder, "WebView2"),
                    Themes,
                    Themes.Default.Id,
                    new MarkdigMarkdownRenderer(NullLogger<MarkdigMarkdownRenderer>.Instance),
                    RenderTimeout,
                    NullLogger.Instance,
                    CancellationToken.None)
                .ConfigureAwait(true);

            return 0;
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _thread.RunAsync(async () =>
        {
            if (_host is not null)
            {
                await _host.DisposeAsync().ConfigureAwait(true);
            }

            _window?.Dispose();

            return 0;
        });

        _thread.Dispose();

        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A browser file still closing; the temp folder is the system's to clear.
        }
    }
}
