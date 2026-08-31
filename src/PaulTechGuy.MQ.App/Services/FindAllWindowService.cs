// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.App.Views;
using Windows.Graphics;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// Owns the one Find All window.
///
/// Built on first use and kept afterwards. Dismissing it hides it, so calling it up again is
/// instant and the last search is still there — which is what makes it a panel to work
/// through rather than a dialog to fill in twice.
/// </summary>
public sealed class FindAllWindowService(
    WindowContext window,
    IWorkspaceService workspace,
    ISettingsService settings,
    IThemeService theme,
    IUiDispatcher ui,
    ILoggerFactory loggerFactory,
    ILogger<FindAllWindowService> logger) : IFindAllWindowService
{
    private FindAllWindow? _window;

    public event EventHandler<FindMatchActivatedEventArgs>? MatchActivated;

    public void Show(string? seedTerm)
    {
        try
        {
            if (_window is null)
            {
                _window = new FindAllWindow(
                    workspace,
                    settings,
                    theme,
                    ui,
                    window.WindowHandle,
                    loggerFactory.CreateLogger<FindAllWindow>());

                _window.MatchActivated += OnMatchActivated;

                logger.LogInformation("Opened the Find All window.");
            }

            _window.Present(seedTerm, MainWindowBounds());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The Find All window could not be opened.");

            if (_window is not null)
            {
                _window.MatchActivated -= OnMatchActivated;
                _window = null;
            }
        }
    }

    public void Shutdown()
    {
        if (_window is null)
        {
            return;
        }

        // Hidden is not closed, and WinUI keeps the process alive until every window has
        // gone. The cheatsheet learned the same lesson.
        logger.LogDebug("Closing Find All as the application exits.");

        try
        {
            _window.MatchActivated -= OnMatchActivated;
            _window.Shutdown();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The Find All window did not close cleanly.");
        }
        finally
        {
            _window = null;
        }
    }

    private void OnMatchActivated(object? sender, FindMatchActivatedEventArgs e) =>
        MatchActivated?.Invoke(this, e);

    /// <summary>Where to put the window the first time, before it has a saved position.</summary>
    private RectInt32 MainWindowBounds()
    {
        if (window.Window?.AppWindow is not { } main)
        {
            return new RectInt32(120, 120, 1_400, 900);
        }

        return new RectInt32(main.Position.X, main.Position.Y, main.Size.Width, main.Size.Height);
    }
}
