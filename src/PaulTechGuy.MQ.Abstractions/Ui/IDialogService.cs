// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Abstractions.Ui;

public enum ConfirmResult
{
    Primary,
    Secondary,
    Cancel,
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
    Task<ConfirmResult> ConfirmAsync(
        string title,
        string message,
        string primaryText,
        string? secondaryText = null,
        bool destructivePrimary = false,
        CancellationToken cancellationToken = default);
}
