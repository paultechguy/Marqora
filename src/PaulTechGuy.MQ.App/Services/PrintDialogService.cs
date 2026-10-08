// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.App.Views;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// The print dialog.
///
/// The same shape as <see cref="ExportDialogService"/>, and for the same reason: what it shows
/// is a ContentDialog of Marqora's own, anchored to the window so that it carries the theme
/// the app is actually using. It used to call the Windows common dialog, as Open and Save
/// still do; <see cref="PrintDialog"/> records why that stopped being possible.
/// </summary>
public sealed class PrintDialogService(WindowContext window, ILogger<PrintDialogService> logger)
    : IPrintDialogService
{
    public async Task<ExportChoice<PrintJob>?> PickPrinterAsync(
        PdfPageSetup defaults,
        ExportLayout layout,
        CancellationToken cancellationToken = default)
    {
        if (window.Root is null)
        {
            logger.LogWarning("Cannot ask which printer: no window is available yet.");
            return null;
        }

        try
        {
            ExportChoice<PrintJob>? choice = await PrintDialog.ShowAsync(window.Root, defaults, layout);

            logger.LogInformation(
                "Print dialog returned {Result}.",
                choice is null ? "(cancelled)" : choice.Setup.PrinterName);

            return choice;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The print dialog failed.");
            return null;
        }
    }
}
