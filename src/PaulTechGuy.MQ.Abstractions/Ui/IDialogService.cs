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

    Task<ConfirmResult> ConfirmAsync(
        string title,
        string message,
        string primaryText,
        string? secondaryText = null,
        CancellationToken cancellationToken = default);
}
