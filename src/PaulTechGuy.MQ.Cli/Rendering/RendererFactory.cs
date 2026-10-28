// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Cli.Reporting;
using PaulTechGuy.MQ.Rendering;
using PaulTechGuy.MQ.WebRender;
using PaulTechGuy.MQ.WebRender.Win32;

namespace PaulTechGuy.MQ.Cli.Rendering;

/// <summary>
/// The run's one <see cref="HeadlessRenderHost"/> (CLI-EX-01, CLI-EX-02, CLI-NF-04).
///
/// <list type="bullet">
/// <item>Nothing starts until the first export asks: a dry run, or a run that fails its output
/// checks, never makes a browser.</item>
/// <item>The WebView2 runtime is looked for first, so a machine without it hears how to install
/// it rather than a COM error.</item>
/// <item>The paged engine prints in the same browser, so a run that prints is still one browser
/// process tree.</item>
/// <item>A browser that has gone - ended by the paged engine after a print that hung, or crashed -
/// is replaced for the next file.</item>
/// <item>Disposed at the end of the run, Ctrl+C included: the browser closed, and its data folder
/// deleted once its process has exited.</item>
/// </list>
/// </summary>
internal sealed class RendererFactory(RenderSetup setup, Func<string?> runtimeVersion) : IRendererFactory
{
    /// <summary>Where the runtime is to be had, for the message that says it is missing.</summary>
    public const string RuntimeLink = "https://developer.microsoft.com/microsoft-edge/webview2/";

    private readonly ReporterLogger _logger = new(setup.Reporter);

    private readonly List<int> _browsers = [];

    private RenderThread? _thread;

    private ParentWindow? _window;

    private HeadlessRenderHost? _host;

    private string? _folder;

    private IWebAssetProvider? _assets;

    /// <summary>Every browser process this run started, in order: one, unless one had to be replaced.</summary>
    public IReadOnlyList<int> BrowsersStarted => _browsers;

    /// <summary>The run's data folder, once a browser has been started.</summary>
    public string? DataFolder => _folder;

    /// <summary>
    /// The installed WebView2 runtime's version, or null when there is none. WinRT's projection
    /// reports a missing runtime as a COM error (file not found) rather than the .NET wrapper's
    /// own exception.
    /// </summary>
    public static string? InstalledRuntime()
    {
        try
        {
            return CoreWebView2Environment.GetAvailableBrowserVersionString() is { Length: > 0 } version ? version : null;
        }
        catch (Exception ex) when (ex is COMException or FileNotFoundException or DllNotFoundException)
        {
            return null;
        }
    }

    public async Task<T> RunAsync<T>(Func<IDocumentRenderer, Task<T>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (_thread is null)
        {
            var assets = new WebAssetProvider(setup.InstallPaths, new ReporterLogger<WebAssetProvider>(setup.Reporter));

            if (!assets.IsAvailable)
            {
                throw new RendererUnavailableException(
                    $"The files that draw documents are incomplete in {assets.RootDirectory} ({string.Join(", ", assets.MissingAssets)}). Reinstall Marqora.");
            }

            _assets = assets;
            _thread = new RenderThread();

            // Asked on the thread WebView2 will live on. Asked from a pool thread, WinRT's
            // activation answered "class not registered" there even with the runtime installed.
            if (await _thread.RunAsync(() => Task.FromResult(runtimeVersion())).ConfigureAwait(false) is null)
            {
                throw new RendererUnavailableException(
                    $"The Microsoft Edge WebView2 runtime is not installed, and exports need it to draw documents. Install it from {RuntimeLink} and run again.");
            }
        }

        return await _thread
            .RunAsync(async () => await work(await HostAsync(cancellationToken).ConfigureAwait(true)).ConfigureAwait(true))
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_thread is null)
        {
            return;
        }

        await _thread.RunAsync(async () =>
        {
            if (_host is { } host)
            {
                _host = null;
                await host.DisposeAsync().ConfigureAwait(true);
            }

            PagedPrintHost.EndBrowser(_logger);
            _window?.Dispose();
            _window = null;

            return 0;
        }).ConfigureAwait(false);

        _thread.Dispose();

        if (_folder is not null)
        {
            RunFolder.Remove(_folder, _logger);
        }
    }

    /// <summary>The running host, started on the render thread if there is none or the last has gone.</summary>
    private async Task<IDocumentRenderer> HostAsync(CancellationToken cancellationToken)
    {
        if (_host is { IsRunning: false } gone)
        {
            setup.Reporter.Warning(null, $"The browser that renders documents, process {gone.BrowserProcessId}, has gone; starting another.");
            _host = null;
            HeadlessRenderHost.EndProcessTree(gone.BrowserProcessId, _logger);
        }

        if (_host is null)
        {
            var started = Stopwatch.StartNew();

            try
            {
                _folder ??= RunFolder.Create(_logger);
                _window ??= ParentWindow.Create(ParentWindowKind.MessageOnly, 1280, 1600);

                _host = await HeadlessRenderHost
                    .StartAsync(
                        _window.Handle,
                        _assets!,
                        Path.Combine(_folder, "WebView2"),
                        setup.Themes,
                        setup.ThemeId,
                        new MarkdigMarkdownRenderer(new ReporterLogger<MarkdigMarkdownRenderer>(setup.Reporter)),
                        setup.Timeout,
                        _logger,
                        cancellationToken)
                    .ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or TimeoutException or IOException or UnauthorizedAccessException)
            {
                throw new RendererUnavailableException($"The browser that renders documents could not be started: {ex.Message}", ex);
            }

            HeadlessRenderHost host = _host;

            // The paged engine prints in this browser rather than starting one of its own.
            PagedPrintHost.Configure(() => Task.FromResult(host.BrowserEnvironment));

            _browsers.Add(host.BrowserProcessId);
            setup.Reporter.Detail(null, $"Started the renderer in {started.ElapsedMilliseconds} ms, browser process {host.BrowserProcessId}.");
        }

        _host.ApplyPreferences(setup.Preferences);

        return _host;
    }
}
