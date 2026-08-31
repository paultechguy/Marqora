// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Abstractions.Ui;

/// <summary>
/// The Windows print dialog - printer, copies, page range.
///
/// Separate from <see cref="IFileDialogService"/> only because it answers with a
/// <see cref="PrintJob"/> rather than a path; it is the same kind of thing, and is shown
/// the same way.
/// </summary>
public interface IPrintDialogService
{
    /// <summary>
    /// Shows the print dialog. Returns null when the user cancels, or when no printer is
    /// installed and the dialog therefore has nothing to offer.
    /// </summary>
    Task<PrintJob?> PickPrinterAsync(PdfPageSetup defaults, CancellationToken cancellationToken = default);
}
