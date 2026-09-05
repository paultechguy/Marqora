// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using PaulTechGuy.MQ.Abstractions.Ui;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>Modal prompts backed by ContentDialog.</summary>
public sealed class DialogService(WindowContext window, ILogger<DialogService> logger) : IDialogService, IDisposable
{
    /// <summary>
    /// WinUI allows only one ContentDialog at a time and throws on a second. A gate turns
    /// that race, which is easy to hit when a file-watch prompt collides with a user action,
    /// into an orderly queue.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task ShowMessageAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        await ShowAsync(
            title,
            message,
            primaryText: "OK",
            secondaryText: null,
            showCancel: false,
            destructivePrimary: false,
            cancellationToken).ConfigureAwait(true);
    }

    public async Task<ConfirmResult> ConfirmAsync(
        string title,
        string message,
        string primaryText,
        string? secondaryText = null,
        bool destructivePrimary = false,
        CancellationToken cancellationToken = default)
    {
        ContentDialogResult result = await ShowAsync(
            title,
            message,
            primaryText,
            secondaryText,
            showCancel: true,
            destructivePrimary,
            cancellationToken).ConfigureAwait(true);

        return result switch
        {
            ContentDialogResult.Primary => ConfirmResult.Primary,
            ContentDialogResult.Secondary => ConfirmResult.Secondary,
            _ => ConfirmResult.Cancel,
        };
    }

    private async Task<ContentDialogResult> ShowAsync(
        string title,
        string message,
        string primaryText,
        string? secondaryText,
        bool showCancel,
        bool destructivePrimary,
        CancellationToken cancellationToken)
    {
        if (window.XamlRoot is null)
        {
            logger.LogWarning("Suppressed dialog '{Title}': no window is available yet.", title);
            return ContentDialogResult.None;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(true);

        try
        {
            /*
                Which button Enter reaches.

                Primary normally: the prompt exists because the caller wants an answer, and the
                answer it is asking for is the primary one.

                Close when the primary throws work away. This used to be hard-coded to Primary,
                and the reload-from-disk prompt carried a comment at its call site saying Cancel
                was the default so a stray Enter could not discard an afternoon's work - which it
                was not, and it could. A destructive prompt always has a Cancel to fall back to,
                because ConfirmAsync is the only route in and it always asks for one.
            */
            var dialog = new ContentDialog
            {
                Title = title,
                Content = new TextBlock { Text = message, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
                PrimaryButtonText = primaryText,
                DefaultButton = destructivePrimary && showCancel
                    ? ContentDialogButton.Close
                    : ContentDialogButton.Primary,
            }.AnchorTo(window.Root);

            if (!string.IsNullOrEmpty(secondaryText))
            {
                dialog.SecondaryButtonText = secondaryText;
            }

            if (showCancel)
            {
                dialog.CloseButtonText = "Cancel";
            }

            return await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to show dialog '{Title}'.", title);
            return ContentDialogResult.None;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Releases the single-dialog gate. Called by the DI container at shutdown.</summary>
    public void Dispose() => _gate.Dispose();
}
