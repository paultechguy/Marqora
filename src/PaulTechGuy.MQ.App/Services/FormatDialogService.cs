// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.App.Views;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>Shows the formatter's rule list and hands back the chosen rules.</summary>
public sealed class FormatDialogService(WindowContext window, ILogger<FormatDialogService> logger)
    : IFormatDialogService
{
    public async Task<FormatChoice?> RequestFormatRulesAsync(
        FormatOptions current,
        int selectedLines,
        CancellationToken cancellationToken = default)
    {
        if (window.XamlRoot is null)
        {
            logger.LogWarning("Cannot show the formatting rules: no window is available yet.");
            return null;
        }

        try
        {
            var dialog = new FormatOptionsDialog(current, selectedLines).AnchorTo(window.Root);

            return await dialog.ShowAsync() == ContentDialogResult.Primary
                ? new FormatChoice(dialog.Options, dialog.SelectionOnly)
                : null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The formatting rules dialog failed.");
            return null;
        }
    }
}
