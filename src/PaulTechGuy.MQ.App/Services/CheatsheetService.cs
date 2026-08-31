// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.App.Views;
using Windows.Graphics;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// Owns the cheatsheet window and decides what the Tools menu item does to it.
/// </summary>
public sealed class CheatsheetService(
    WindowContext window,
    IWebAssetProvider assets,
    IMarkdownRenderer renderer,
    ISettingsService settings,
    IThemeService theme,
    ILoggerFactory loggerFactory,
    ILogger<CheatsheetService> logger) : ICheatsheetService
{
    private CheatsheetWindow? _window;

    /// <summary>
    /// Guards against a second toggle arriving while the first is still bringing the WebView
    /// up. That takes about a second the first time, and a double click would otherwise open
    /// the window and immediately hide it again.
    /// </summary>
    private bool _isToggling;

    public bool IsVisible => _window is not null && _window.AppWindow.IsVisible;

    public event EventHandler<bool>? VisibilityChanged;

    /// <summary>
    /// Shows the cheatsheet, or hides it if it is already showing.
    ///
    /// This is a plain visible/hidden toggle, which it can afford to be because the window is
    /// owned by the main window and therefore always floats above it. An earlier version
    /// tried to be cleverer — raise it when buried, hide it when the user could see it — and
    /// that was wrong for a reason worth recording: the menu item lives on the main window,
    /// so opening the Tools menu activates the main window and raises it above the
    /// cheatsheet. By the time the command ran, the cheatsheet was always "buried", and with
    /// the default placement the menu item could never hide it at all. The test measured a
    /// state the user's own click had just destroyed.
    /// </summary>
    public async Task ToggleAsync()
    {
        logger.LogInformation(
            "Cheatsheet toggle requested. exists={Exists} visible={Visible} busy={Busy}",
            _window is not null,
            IsVisible,
            _isToggling);

        if (_isToggling)
        {
            logger.LogInformation("Ignoring a cheatsheet toggle; one is already in progress.");
            return;
        }

        _isToggling = true;

        try
        {
            if (_window is not null && IsVisible)
            {
                logger.LogInformation("Hiding the cheatsheet.");
                _window.AppWindow.Hide();
                return;
            }

            await OpenAsync().ConfigureAwait(true);
        }
        finally
        {
            _isToggling = false;
        }
    }

    public void Shutdown()
    {
        if (_window is null)
        {
            return;
        }

        // A hidden window is still open, and WinUI keeps the process alive until every
        // window is closed. Without this the app would linger after the main window goes.
        logger.LogDebug("Closing the cheatsheet as the application exits.");

        try
        {
            _window.ShownOrHidden -= OnWindowVisibilityChanged;
            _window.Shutdown();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The cheatsheet did not close cleanly.");
        }
        finally
        {
            _window = null;
            OnWindowVisibilityChanged(this, false);
        }
    }

    private async Task OpenAsync()
    {
        try
        {
            if (_window is null)
            {
                _window = new CheatsheetWindow(
                    assets,
                    renderer,
                    settings,
                    theme,
                    window.WindowHandle,
                    loggerFactory.CreateLogger<CheatsheetWindow>());

                _window.ShownOrHidden += OnWindowVisibilityChanged;
            }

            await _window.InitializeAsync(MainWindowBounds()).ConfigureAwait(true);

            // Shown without being activated, which matters more than it sounds.
            //
            // Taking focus here was the cause of the menu item appearing to need two clicks:
            // showing the cheatsheet moved focus off the main window, so the user's next
            // click on the Tools menu was consumed re-activating the editor and never
            // reached the item. It is also simply the right behaviour — this is a reference
            // you glance at while typing, and typing should carry on into the document.
            //
            // The window is owned by the main window, so it comes up above the editor
            // regardless of which one holds focus.
            _window.AppWindow.Show(activateWindow: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The cheatsheet window could not be opened.");

            if (_window is not null)
            {
                _window.ShownOrHidden -= OnWindowVisibilityChanged;
                _window = null;
            }

            OnWindowVisibilityChanged(this, false);
        }
    }

    private void OnWindowVisibilityChanged(object? sender, bool visible) =>
        VisibilityChanged?.Invoke(this, visible);

    /// <summary>Where to put the cheatsheet the first time, before it has a saved position.</summary>
    private RectInt32 MainWindowBounds()
    {
        if (window.Window?.AppWindow is not { } main)
        {
            return new RectInt32(120, 120, 1_400, 900);
        }

        return new RectInt32(main.Position.X, main.Position.Y, main.Size.Width, main.Size.Height);
    }
}
