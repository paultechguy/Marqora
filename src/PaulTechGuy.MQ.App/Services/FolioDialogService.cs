// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.App.Views;
using PaulTechGuy.MQ.Domain;
using Windows.Graphics;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// Shows the Folio preflight and waits for it to be answered.
///
/// A new window each time, unlike Find All next door, which keeps its one window and hides it.
/// The difference is what the two are for: Find All is a panel to work through, and the last
/// search still being there is most of its value, while a preflight describes one particular
/// share and has nothing worth carrying into the next.
///
/// Waiting on a modeless window means waiting on the window, not on a dialog: it hands back a
/// task it completes when Share is pressed, or when it is cancelled or simply closed.
/// </summary>
public sealed class FolioDialogService(
    WindowContext window,
    IWorkspaceService workspace,
    ISettingsService settings,
    IThemeService theme,
    IUiDispatcher ui,
    ILoggerFactory loggerFactory,
    ILogger<FolioDialogService> logger) : IFolioDialogService
{
    public async Task<FolioChoice?> RequestFolioAsync(
        Func<IReadOnlyList<string>> documents,
        Func<IReadOnlyList<string>, int, FolioPlan> plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(plan);

        if (window.Window is null)
        {
            logger.LogWarning("Cannot show the Folio preflight: no window is available yet.");

            return null;
        }

        try
        {
            var preflight = new FolioWindow(
                documents,
                plan,
                workspace,
                settings,
                theme,
                ui,
                window.WindowHandle,
                loggerFactory.CreateLogger<FolioWindow>());

            preflight.Present(MainWindowBounds());

            return await preflight.Result.ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The Folio preflight failed.");

            return null;
        }
    }

    /// <summary>Where the editor is, so a first-time Folio window opens over it rather than adrift.</summary>
    private RectInt32 MainWindowBounds() =>
        window.Window?.AppWindow is { } main
            ? new RectInt32(main.Position.X, main.Position.Y, main.Size.Width, main.Size.Height)
            : new RectInt32(120, 120, 1_400, 900);
}
