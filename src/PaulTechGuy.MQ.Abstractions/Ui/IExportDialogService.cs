// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Abstractions.Ui;

/// <summary>
/// Asks the user how a PDF should be laid out.
///
/// Kept separate from <see cref="IDialogService"/>, which deals in plain messages and
/// confirmations, so the view model can request page setup without knowing that a WinUI
/// ContentDialog is what answers.
/// </summary>
public interface IExportDialogService
{
    /// <summary>Returns the chosen page setup, or null when the user cancels.</summary>
    Task<PdfPageSetup?> RequestPdfSetupAsync(string documentName, CancellationToken cancellationToken = default);
}
