// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.App.ViewModels;
using PaulTechGuy.MQ.App.Views;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// Shows the preferences dialog.
///
/// The view model is resolved when the dialog opens rather than injected here, and that is
/// not laziness: <see cref="PreferencesViewModel"/> depends on <see cref="MainViewModel"/>,
/// which in turn reaches this service to open the dialog. Taking it as a constructor
/// parameter would close that loop and the container would refuse to build either one.
/// </summary>
public sealed class PreferencesDialogService(
    IServiceProvider services,
    WindowContext window,
    ILogger<PreferencesDialogService> logger)
    : IPreferencesDialogService
{
    public async Task ShowPreferencesAsync(CancellationToken cancellationToken = default)
    {
        if (window.XamlRoot is null)
        {
            logger.LogWarning("Cannot show preferences: no window is available yet.");
            return;
        }

        PreferencesViewModel viewModel;

        try
        {
            viewModel = ActivatorUtilities.CreateInstance<PreferencesViewModel>(services);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The preferences dialog could not be built.");
            return;
        }

        // Both panes for the duration, so a preference that only shows in one of them can be
        // seen landing. Outside the try/finally that restores it, so a failure to switch
        // cannot leave the restore running against a mode that was never changed.
        await viewModel.ShowBothPanesAsync().ConfigureAwait(true);

        try
        {
            await new PreferencesDialog(viewModel, logger)
                .AnchorTo(window.Root)
                .ShowAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The preferences dialog failed.");
        }
        finally
        {
            /*
                However the dialog ended - OK, Cancel, or falling over - the panes go back.

                After ShowAsync returns, so it runs behind the revert that Cancel performs:
                that puts the view mode setting back to what it was, and this then applies
                whatever the setting finally says.
            */
            try
            {
                await viewModel.RestoreViewModeAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not put the view mode back after preferences.");
            }
        }
    }
}
