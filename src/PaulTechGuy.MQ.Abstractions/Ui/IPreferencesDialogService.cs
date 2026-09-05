// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Abstractions.Ui;

/// <summary>
/// Shows the preferences dialog.
///
/// Nothing comes back. Unlike the formatting dialog, which collects an answer and hands it
/// over when the user accepts, the preferences dialog settles everything itself: changes
/// apply as they are made, Cancel puts them back, and OK commits the few that were held
/// until then. However it is closed, there is nothing left for the caller to do.
///
/// Showing it also puts both panes on screen for as long as it is up, so that a preference
/// which only shows in one of them can still be seen taking effect, and puts the view back
/// afterwards. That is a display state for the duration and is never saved as the mode the
/// app starts in.
/// </summary>
public interface IPreferencesDialogService
{
    Task ShowPreferencesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the window as the application exits.
    ///
    /// Preferences is a window rather than a modal dialog, and WinUI keeps the process alive
    /// until every window is closed - so one left open would outlive the editor.
    /// </summary>
    void Shutdown();
}
