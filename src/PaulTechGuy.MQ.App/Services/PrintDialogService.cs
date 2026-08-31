// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// The print dialog.
///
/// The same shape as <see cref="FileDialogService"/>, and for the same reason: it calls the
/// Win32 common dialog rather than anything the WebView offers. The dialog is modal and runs
/// its own message loop, so it is shown on the UI thread and the answer is handed back as a
/// completed task.
/// </summary>
public sealed class PrintDialogService(WindowContext window, ILogger<PrintDialogService> logger) : IPrintDialogService
{
    public Task<PrintJob?> PickPrinterAsync(
        PdfPageSetup defaults,
        CancellationToken cancellationToken = default)
    {
        try
        {
            PrintJob? job = Win32PrintDialog.Show(RequireOwner(), defaults);

            logger.LogInformation(
                "Print dialog returned {Result}.",
                job is null ? "(cancelled)" : job.PrinterName);

            return Task.FromResult(job);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The print dialog failed.");
            return Task.FromResult<PrintJob?>(null);
        }
    }

    /// <summary>Owner handle for the modal dialog, so it centres on and blocks the window.</summary>
    private IntPtr RequireOwner()
    {
        IntPtr handle = window.WindowHandle;

        return handle == IntPtr.Zero
            ? throw new InvalidOperationException("The print dialog was requested before the window existed.")
            : handle;
    }
}
