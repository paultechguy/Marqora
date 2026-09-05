// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Abstractions.Ui;

/// <summary>
/// The print dialog - printer, copies, page range, paper, and what the printer can do with
/// ink and paper sides.
///
/// Marqora's own dialog rather than the Windows one, which is where it differs from
/// <see cref="IFileDialogService"/>: Windows 11 answers a PrintDlg call with its own modern
/// print experience, themed by the system rather than by the app and handing back settings
/// the app then has no way to honour. See docs/DialogTheming.md.
///
/// Asynchronous because it is a ContentDialog and genuinely awaits the user, where the file
/// dialogs run their own modal loop and answer with a completed task.
/// </summary>
public interface IPrintDialogService
{
    /// <summary>
    /// Shows the print dialog. Returns null when the user cancels, which includes the case of
    /// a machine with no printer installed: the dialog says so and offers only Cancel.
    /// </summary>
    Task<PrintJob?> PickPrinterAsync(PdfPageSetup defaults, CancellationToken cancellationToken = default);
}
