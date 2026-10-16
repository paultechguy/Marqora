// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.App.Services;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Themes;

namespace PaulTechGuy.MQ.WebRender;

/// <summary>
/// The preview shell, loaded in a WebView2 nobody sees, with one document open at a time.
///
/// A preview with one tab and no screen: it loads the same shell.html, is told the same color
/// themes and paper spec on ready, and opens a document with the same openTab and activateTab
/// the live preview gets. What it hands back - the print markup for now - is therefore what the
/// app would hand the paged engine for the same document, by construction rather than by two
/// renders kept in step (docs/CLI-Implementation-Plan.md, F4).
///
/// Everything here runs on the <see cref="Win32.StaPump"/> thread that created it.
/// </summary>
public sealed class HeadlessRenderHost : IAsyncDisposable
{
    /// <summary>The variable WebView2 reads a data folder from ahead of any folder it is passed.</summary>
    private const string UserDataVariable = "WEBVIEW2_USER_DATA_FOLDER";

    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);

    private readonly CoreWebView2Environment _environment;

    private readonly CoreWebView2Controller _controller;

    private readonly ILogger _logger;

    private readonly Dictionary<Guid, TaskCompletionSource<string>> _requests = [];

    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource _browserExited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private HeadlessRenderHost(CoreWebView2Environment environment, CoreWebView2Controller controller, ILogger logger)
    {
        _environment = environment;
        _controller = controller;
        _logger = logger;

        BrowserProcessId = (int)controller.CoreWebView2.BrowserProcessId;

        _environment.BrowserProcessExited += (_, _) => _browserExited.TrySetResult();
        _controller.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
    }

    /// <summary>The browser process this host started, for counting what a run leaves behind.</summary>
    public int BrowserProcessId { get; }

    /// <summary>
    /// Starts a browser in <paramref name="userDataFolder"/>, loads the shell, and waits until it
    /// reports ready and has its themes and paper spec.
    /// </summary>
    public static async Task<HeadlessRenderHost> StartAsync(
        nint parentWindow,
        IWebAssetProvider assets,
        string userDataFolder,
        ThemeCatalog themes,
        string themeId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(themes);
        ArgumentNullException.ThrowIfNull(logger);

        CoreWebView2Environment environment = await CreateEnvironmentAsync(userDataFolder).ConfigureAwait(true);

        CoreWebView2Controller? controller = null;

        try
        {
            controller = await environment.CreateCoreWebView2ControllerAsync(
                CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)parentWindow));

            return await OpenShellAsync(environment, controller, assets, themes, themeId, logger, cancellationToken)
                .ConfigureAwait(true);
        }
        catch
        {
            // The environment has started a browser whether or not a controller came of it, and
            // nothing else will end it: there is no host to dispose. Seen over SSH, where there
            // is no interactive desktop and the controller is refused (0x80070578) - the browser
            // lingered past the run's cleanup and held its data folder.
            Abandon(environment, controller, logger);
            throw;
        }
    }

    private static async Task<HeadlessRenderHost> OpenShellAsync(
        CoreWebView2Environment environment,
        CoreWebView2Controller controller,
        IWebAssetProvider assets,
        ThemeCatalog themes,
        string themeId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Visible as far as the browser knows, but placed outside the parent's client area so
        // nothing is drawn: the mode PagedPrintHost found to be the one that lays out. A width
        // the preview pane would have on a laptop, so tables wrap where they would on screen.
        const int Width = 1280;
        const int Height = 1600;

        controller.DefaultBackgroundColor = global::Windows.UI.Color.FromArgb(255, 255, 255, 255);
        controller.IsVisible = true;
        controller.Bounds = new global::Windows.Foundation.Rect(-Width - 10_000, -Height - 10_000, Width, Height);

        CoreWebView2 core = controller.CoreWebView2;

        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDevToolsEnabled = false;

        core.SetVirtualHostNameToFolderMapping(
            assets.VirtualHostName,
            assets.RootDirectory,
            CoreWebView2HostResourceAccessKind.Allow);

        var host = new HeadlessRenderHost(environment, controller, logger);

        // Light, always: everything that leaves the app is light (CLAUDE.md, Screen and output).
        // The accent rides in the fragment for the reason WebViewPreviewHost.ShellUriForTheme gives.
        string accent = DocumentAccent.LightHex.TrimStart('#');

        core.Navigate($"{assets.ShellUri}#theme=light&accent={accent}&accentPrint={accent}");

        await host.WaitAsync(host._ready.Task, ReadyTimeout, "The shell did not report ready.", cancellationToken)
            .ConfigureAwait(true);

        // What WebViewPreviewHost.OnShellReady sends ahead of everything else: the themes, then
        // the paper spec. One theme for screen and exports alike - there is no screen.
        host.Post("setColorThemes", ColorThemePayloads.Catalog(themes, themeId, themeId));
        host.Post("setPaper", new { css = PaperSpec.Css() });

        return host;
    }

    /// <summary>
    /// Opens a document as the shell's one tab, waits for its render to settle, and returns the
    /// print markup the paged engine lays out: the shell's own requestPrintHtml answer.
    /// </summary>
    public async Task<string> PrintMarkupAsync(
        string sourceText,
        string renderedHtml,
        string? documentPath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();

        Post("openTab", new { id, text = sourceText, html = renderedHtml });
        Post("activateTab", new { id, documentBaseUrl = "https://marqora.document/", documentPath = documentPath ?? string.Empty });

        try
        {
            await AskAsync("requestRenderSettled", timeout, cancellationToken).ConfigureAwait(true);

            return await AskAsync("requestPrintHtml", timeout, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            Post("closeTab", new { id });
        }
    }

    /// <summary>
    /// Closes the browser and waits for its process to end, so the data folder can be removed.
    /// Ends the process tree itself if the browser does not go within a few seconds.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _controller.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        _controller.Close();

        Task finished = await Task.WhenAny(_browserExited.Task, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(true);

        if (finished != _browserExited.Task)
        {
            _logger.LogWarning("The render browser, process {Id}, did not exit after closing; ending it.", BrowserProcessId);
            EndProcessTree(BrowserProcessId, _logger);
        }
    }

    /// <summary>
    /// Closes what a failed start had made and ends its browser. The browser is found through the
    /// controller when there is one, and otherwise by asking the environment for its processes.
    /// </summary>
    private static void Abandon(CoreWebView2Environment environment, CoreWebView2Controller? controller, ILogger logger)
    {
        int browser = 0;

        try
        {
            if (controller is not null)
            {
                browser = (int)controller.CoreWebView2.BrowserProcessId;
                controller.Close();
            }
            else
            {
                browser = environment.GetProcessInfos()
                    .Where(process => process.Kind == CoreWebView2ProcessKind.Browser)
                    .Select(process => process.ProcessId)
                    .FirstOrDefault();
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            logger.LogDebug(ex, "The failed browser could not be asked about itself.");
        }

        EndProcessTree(browser, logger);
    }

    /// <summary>Ends a browser process and everything under it, if it is still there.</summary>
    public static void EndProcessTree(int processId, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (processId == 0)
        {
            return;
        }

        try
        {
            using Process browser = Process.GetProcessById(processId);

            browser.Kill(entireProcessTree: true);
            browser.WaitForExit(5000);
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning(ex, "Could not end browser process {Id}.", processId);
        }
    }

    /// <summary>
    /// An environment in a data folder of this host's own. The variable WebView2 reads first is
    /// pointed at the folder for the one call that reads it and put back straight after, as
    /// PagedPrintHost does; nothing is awaited in between, so no other environment can be made
    /// while it is changed.
    /// </summary>
    private static async Task<CoreWebView2Environment> CreateEnvironmentAsync(string userDataFolder)
    {
        string? previous = Environment.GetEnvironmentVariable(UserDataVariable);

        global::Windows.Foundation.IAsyncOperation<CoreWebView2Environment> creating;

        Environment.SetEnvironmentVariable(UserDataVariable, userDataFolder);

        try
        {
            creating = CoreWebView2Environment.CreateWithOptionsAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder,
                options: new CoreWebView2EnvironmentOptions());
        }
        finally
        {
            Environment.SetEnvironmentVariable(UserDataVariable, previous);
        }

        return await creating;
    }

    private async Task<string> AskAsync(string message, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        _requests[id] = completion;

        try
        {
            Post(message, new { requestId = id });

            await WaitAsync(completion.Task, timeout, $"The shell did not answer {message}.", cancellationToken)
                .ConfigureAwait(true);

            return await completion.Task.ConfigureAwait(true);
        }
        finally
        {
            _requests.Remove(id);
        }
    }

    private async Task WaitAsync(Task task, TimeSpan timeout, string message, CancellationToken cancellationToken)
    {
        Task finished = await Task.WhenAny(task, Task.Delay(timeout, cancellationToken)).ConfigureAwait(true);

        cancellationToken.ThrowIfCancellationRequested();

        if (finished != task)
        {
            _logger.LogWarning("{Message}", message);
            throw new TimeoutException(message);
        }

        await task.ConfigureAwait(true);
    }

    private void Post(string type, object payload) =>
        _controller.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type, payload }));

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string raw;

        try
        {
            raw = e.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            JsonElement root = document.RootElement;

            string type = root.TryGetProperty("type", out JsonElement t) ? t.GetString() ?? string.Empty : string.Empty;
            JsonElement payload = root.TryGetProperty("payload", out JsonElement p) ? p : default;

            switch (type)
            {
                case "ready":
                    _ready.TrySetResult();
                    break;

                case "printHtml":
                case "renderSettled":
                    if (payload.ValueKind == JsonValueKind.Object
                        && Guid.TryParse(Read(payload, "requestId"), out Guid id)
                        && _requests.TryGetValue(id, out TaskCompletionSource<string>? waiting))
                    {
                        waiting.TrySetResult(Read(payload, "data"));
                    }
                    break;

                case "log":
                case "error":
                case "warning":
                    _logger.LogDebug("Shell {Type}: {Message} {Detail}", type, Read(payload, "message"), Read(payload, "detail"));
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Malformed message from the shell.");
        }
    }

    private static string Read(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
