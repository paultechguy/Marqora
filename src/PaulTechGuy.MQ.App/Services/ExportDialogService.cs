// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.App.Views;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>Shows the page-setup dialogs and hands back the chosen values.</summary>
public sealed class ExportDialogService(WindowContext window, ILogger<ExportDialogService> logger)
    : IExportDialogService
{
    public async Task<ExportChoice<DocxExportSetup>?> RequestDocxSetupAsync(
        string documentName,
        DocxExportSetup current,
        ExportLayout layout,
        CancellationToken cancellationToken = default)
    {
        if (window.XamlRoot is null)
        {
            logger.LogWarning("Cannot ask for page setup: no window is available yet.");
            return null;
        }

        try
        {
            var dialog = new WordExportDialog(documentName, current, layout).AnchorTo(window.Root);

            return await dialog.ShowAsync() == ContentDialogResult.Primary ? dialog.Choice : null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The Word page-setup dialog failed.");
            return null;
        }
    }

    public async Task<ExportChoice<PdfPageSetup>?> RequestPdfSetupAsync(
        string documentName,
        PdfPageSetup current,
        ExportLayout layout,
        CancellationToken cancellationToken = default)
    {
        if (window.XamlRoot is null)
        {
            logger.LogWarning("Cannot ask for page setup: no window is available yet.");
            return null;
        }

        try
        {
            var dialog = new PdfExportDialog(documentName, current, layout).AnchorTo(window.Root);

            return await dialog.ShowAsync() == ContentDialogResult.Primary ? dialog.Choice : null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The PDF page-setup dialog failed.");
            return null;
        }
    }
}
