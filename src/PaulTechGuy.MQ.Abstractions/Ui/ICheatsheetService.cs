// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Abstractions.Ui;

/// <summary>
/// Owns the floating markdown cheatsheet window.
///
/// The window is created once and thereafter shown and hidden rather than rebuilt, so its
/// scroll position and the loaded page survive being dismissed. It is a genuine top-level
/// window and not a dialog, so the user can keep it beside the editor and carry on typing.
/// </summary>
public interface ICheatsheetService
{
    /// <summary>True while the window exists and is visible.</summary>
    bool IsVisible { get; }

    /// <summary>
    /// Raised whenever the window is shown or hidden, including when the user dismisses it
    /// with its own close button. The menu item's tick follows this rather than the last
    /// command that ran, so it stays truthful however the window was dismissed.
    /// </summary>
    event EventHandler<bool>? VisibilityChanged;

    /// <summary>
    /// Shows the cheatsheet, or hides it if it is already showing.
    ///
    /// A plain toggle is safe here only because the window is owned by the main window and so
    /// always floats above it; it can never be hiding behind the editor when this is called.
    /// Overlapping calls are ignored rather than queued.
    /// </summary>
    Task ToggleAsync();

    /// <summary>
    /// Closes the window for good, as the application exits. A hidden window is still an
    /// open window, and WinUI keeps the process alive until every one of them is closed.
    /// </summary>
    void Shutdown();
}
