// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Abstractions.Ui;

public enum ConfirmResult
{
    Primary,
    Secondary,
    Cancel,
}

/// <summary>
/// Which window a prompt belongs to.
///
/// A ContentDialog is drawn in one window's popup root, and a palette window is owned by the
/// main one - which means it always floats above it. A prompt raised by a click inside a
/// palette but anchored to the main window therefore opens underneath the palette, invisible,
/// with the app waiting on an answer to a question nobody can see.
///
/// Naming the anchor rather than passing a XamlRoot keeps this interface free of the UI
/// framework, which is what lets a view model be exercised without one.
/// </summary>
public enum DialogAnchor
{
    /// <summary>The main window. Everything the editor itself asks.</summary>
    MainWindow,

    /// <summary>The Find All window, for a prompt raised by a click inside it.</summary>
    FindAll,
}

/// <summary>Modal prompts, injected so view models can be exercised without a UI thread.</summary>
public interface IDialogService
{
    Task ShowMessageAsync(string title, string message, CancellationToken cancellationToken = default);

    /// <param name="destructivePrimary">
    /// True when the primary action throws work away that cannot be recovered.
    ///
    /// It moves the default from the primary button to Cancel, so Enter backs out rather than
    /// destroying something. The caller says what is true about the prompt; what to do about it
    /// belongs to the implementation.
    ///
    /// Reach for it only when the loss is real. "Save changes?" does not qualify - its primary
    /// is Save, which is the safe answer - and marking a harmless prompt makes Enter useless on
    /// a dialog the user meant to confirm.
    /// </param>
    /// <param name="anchor">
    /// Which window the prompt belongs to. Defaults to the main one, which is right for
    /// everything the editor asks; a prompt raised by a click inside a palette window has to
    /// name it, or it opens behind the palette. See <see cref="DialogAnchor"/>.
    /// </param>
    Task<ConfirmResult> ConfirmAsync(
        string title,
        string message,
        string primaryText,
        string? secondaryText = null,
        bool destructivePrimary = false,
        DialogAnchor anchor = DialogAnchor.MainWindow,
        CancellationToken cancellationToken = default);
}
