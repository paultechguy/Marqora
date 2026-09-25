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

    public async Task<HeadingNumberChoice?> RequestHeadingNumbersAsync(
        HeadingNumbering suggested,
        HeadingNumberStyle style,
        IReadOnlyList<int> headingLevels,
        bool renumbering,
        CancellationToken cancellationToken = default)
    {
        if (window.XamlRoot is null)
        {
            logger.LogWarning("Cannot show the heading numbering dialog: no window is available yet.");
            return null;
        }

        try
        {
            var dialog = new HeadingNumberDialog(suggested, style, headingLevels, renumbering).AnchorTo(window.Root);

            return await dialog.ShowAsync() == ContentDialogResult.Primary
                ? new HeadingNumberChoice(dialog.Start, dialog.NumberStyle)
                : null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The heading numbering dialog failed.");
            return null;
        }
    }

    public async Task<ListNumbering?> RequestListNumberingAsync(
        OrderedListSummary list,
        CancellationToken cancellationToken = default)
    {
        if (window.XamlRoot is null)
        {
            logger.LogWarning("Cannot show the list numbering dialog: no window is available yet.");
            return null;
        }

        try
        {
            var dialog = new ListNumberingDialog(list).AnchorTo(window.Root);

            return await dialog.ShowAsync() == ContentDialogResult.Primary ? dialog.Numbering : null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The list numbering dialog failed.");
            return null;
        }
    }
}
