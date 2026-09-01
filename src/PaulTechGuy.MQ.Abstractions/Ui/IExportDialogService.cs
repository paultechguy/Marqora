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
    /// <summary>
    /// Returns the chosen page setup, or null when the user cancels.
    /// </summary>
    /// <param name="current">
    /// What the dialog opens on - the setup saved in preferences. Passed in rather than
    /// remembered by the dialog itself, so that the answer survives a restart and there is
    /// one record of it rather than two that can disagree.
    /// </param>
    Task<PdfPageSetup?> RequestPdfSetupAsync(
        string documentName,
        PdfPageSetup current,
        CancellationToken cancellationToken = default);
}
