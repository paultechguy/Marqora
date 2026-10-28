// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions;
using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Themes;
using PaulTechGuy.MQ.WebRender;
using PaulTechGuy.MQ.WebRender.Win32;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// The renderer the app's Word export draws through: a <see cref="HeadlessRenderHost"/> of its
/// own, the one marqora-cli uses, rather than the live preview (CLI-ARC-07).
///
/// The preview answers for whatever it happens to be showing, and a Word export used to take its
/// diagrams, equations and code colors from there: a preview busy re-rendering, or one a reader
/// had just switched away from, cost the file its pictures. This browser renders the document it
/// is asked for, every time, so the .docx is the same however the window is being used, and the
/// same as the CLI's.
///
/// Started on the first Word export, not at launch, and kept for the session: a launch that
/// never exports to Word starts nothing. It has a data folder of its own beside the preview's,
/// a message-only window to belong to, so it is never on screen and can never take focus, and
/// it is ended at shutdown with the paged engine's browser.
///
/// Everything here runs on the UI thread, where the host was made.
/// </summary>
public sealed class ExportRenderHost(
    IAppPaths paths,
    IWebAssetProvider assets,
    ThemeCatalog themes,
    IMarkdownRenderer markdown,
    ILogger<ExportRenderHost> logger)
{
    /// <summary>How long one document's render may take, as marqora-cli allows by default.</summary>
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(60);

    private ParentWindow? _window;

    private HeadlessRenderHost? _host;

    /// <summary>
    /// The running renderer, started if there is none, with the reader's preview preferences
    /// applied. Null when it could not be started, which is logged: the caller then falls back
    /// to the preview, as every Word export did before.
    /// </summary>
    public async Task<IDocumentRenderer?> GetAsync(PreviewPreferences preferences, string colorThemeId)
    {
        if (_host is { IsRunning: false } stopped)
        {
            logger.LogWarning("The export renderer's browser, process {Id}, has gone; starting another.", stopped.BrowserProcessId);
            await EndAsync(stopped).ConfigureAwait(true);
            _host = null;
        }

        if (_host is null)
        {
            var started = Stopwatch.StartNew();

            try
            {
                _window ??= ParentWindow.Create(ParentWindowKind.MessageOnly, 1280, 1600);

                _host = await HeadlessRenderHost
                    .StartAsync(
                        _window.Handle,
                        assets,
                        Path.Combine(paths.DataDirectory, "WebView2-Render"),
                        themes,
                        colorThemeId,
                        markdown,
                        RenderTimeout,
                        logger,
                        CancellationToken.None)
                    .ConfigureAwait(true);

                logger.LogInformation(
                    "Started the export renderer in {Elapsed} ms, browser process {Id}.",
                    started.ElapsedMilliseconds,
                    _host.BrowserProcessId);
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or TimeoutException or IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not start the export renderer; this export uses the preview.");
                return null;
            }
        }

        _host.ApplyPreferences(preferences);

        return _host;
    }

    /// <summary>Ends the browser at shutdown, so nothing outlives the app.</summary>
    public void End()
    {
        if (_host is { } host)
        {
            _host = null;
            HeadlessRenderHost.EndProcessTree(host.BrowserProcessId, logger);
        }

        _window?.Dispose();
        _window = null;
    }

    private async Task EndAsync(HeadlessRenderHost host)
    {
        try
        {
            await host.DisposeAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            logger.LogDebug(ex, "The export renderer that had gone could not be closed.");
        }
    }
}
