// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.App.ViewModels;
using PaulTechGuy.MQ.App.Views;
using Windows.Graphics;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// Owns the preferences window.
///
/// The view model is resolved when the window opens rather than injected here, and that is not
/// laziness: <see cref="PreferencesViewModel"/> depends on <see cref="MainViewModel"/>, which in
/// turn reaches this service to open the window. Taking it as a constructor parameter would
/// close that loop and the container would refuse to build either one.
///
/// A fresh view model and a fresh window every time, rather than the cheatsheet's hide-and-show.
/// That is deliberate: the view model snapshots the settings as it opens, and Cancel means "go
/// back to that". Reusing an instance would leave the snapshot describing some earlier visit.
/// </summary>
public sealed class PreferencesDialogService(
    IServiceProvider services,
    WindowContext window,
    ISettingsService settings,
    IThemeService theme,
    ILoggerFactory loggerFactory,
    ILogger<PreferencesDialogService> logger)
    : IPreferencesDialogService
{
    private PreferencesWindow? _window;
    private PreferencesViewModel? _viewModel;

    /// <summary>
    /// Opens preferences, or brings the open one forward.
    ///
    /// Returns as soon as the window is up rather than when it closes. It is a window now and
    /// not a modal dialog, so there is nothing to wait for: the document stays editable
    /// alongside it, which is the point of the change.
    /// </summary>
    public Task ShowPreferencesAsync(CancellationToken cancellationToken = default)
    {
        if (window.XamlRoot is null)
        {
            logger.LogWarning("Cannot show preferences: no window is available yet.");

            return Task.CompletedTask;
        }

        if (_window is not null)
        {
            // Already open. Asking again means "let me see it", not "give me a second one".
            _window.ShowNear(MainWindowBounds());

            return Task.CompletedTask;
        }

        return OpenAsync();
    }

    /// <summary>Closes the window as the application exits, so it cannot outlive the editor.</summary>
    public void Shutdown()
    {
        if (_window is null)
        {
            return;
        }

        try
        {
            _window.Shutdown();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The preferences window did not close cleanly.");
        }
        finally
        {
            _window = null;
            _viewModel = null;
        }
    }

    private async Task OpenAsync()
    {
        PreferencesViewModel viewModel;

        try
        {
            viewModel = ActivatorUtilities.CreateInstance<PreferencesViewModel>(services);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The preferences window could not be built.");

            return;
        }

        // Both panes for as long as preferences are up, so a change that only shows in one of
        // them can be watched landing. Before the window is created, so a failure to switch
        // cannot leave the restore below running against a mode that was never changed.
        await viewModel.ShowBothPanesAsync().ConfigureAwait(true);

        try
        {
            _viewModel = viewModel;

            _window = new PreferencesWindow(
                viewModel,
                settings,
                theme,
                window.WindowHandle,
                loggerFactory.CreateLogger<PreferencesWindow>());

            _window.Closed += OnClosed;
            _window.ShowNear(MainWindowBounds());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The preferences window failed to open.");

            _window = null;
            _viewModel = null;

            await RestoreViewModeAsync(viewModel).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// However the window ended - OK, Cancel, Escape, the caption's X, or falling over - the
    /// panes go back to whatever the settings finally say. Cancel has already put the view mode
    /// setting back by the time this runs, so this applies the reverted value rather than
    /// fighting it.
    /// </summary>
    private async void OnClosed(object sender, Microsoft.UI.Xaml.WindowEventArgs args)
    {
        PreferencesViewModel? viewModel = _viewModel;

        if (_window is not null)
        {
            _window.Closed -= OnClosed;
        }

        _window = null;
        _viewModel = null;

        if (viewModel is not null)
        {
            await RestoreViewModeAsync(viewModel).ConfigureAwait(true);
        }
    }

    private async Task RestoreViewModeAsync(PreferencesViewModel viewModel)
    {
        try
        {
            await viewModel.RestoreViewModeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not put the view mode back after preferences.");
        }
    }

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
