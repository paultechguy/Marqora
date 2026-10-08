// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Abstractions.Ui;

/// <summary>
/// What an export dialog was answered with: the export's own page setup, and the cover,
/// contents and header boxes both exports share.
/// </summary>
public sealed record ExportChoice<TSetup>(TSetup Setup, ExportLayout Layout);

/// <summary>
/// Asks the user how an exported document should be laid out.
///
/// Kept separate from <see cref="IDialogService"/>, which deals in plain messages and
/// confirmations, so the view model can request page setup without knowing that a WinUI
/// ContentDialog is what answers.
/// </summary>
public interface IExportDialogService
{
    /// <summary>
    /// Returns the chosen page setup and layout, or null when the user cancels.
    /// </summary>
    /// <param name="current">
    /// What the dialog opens on - the setup saved in preferences. Passed in rather than
    /// remembered by the dialog itself, so that the answer survives a restart and there is
    /// one record of it rather than two that can disagree.
    /// </param>
    /// <param name="layout">The shared layout saved in preferences, for the same reason.</param>
    Task<ExportChoice<PdfPageSetup>?> RequestPdfSetupAsync(
        string documentName,
        PdfPageSetup current,
        ExportLayout layout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the chosen Word setup and layout, or null when the user cancels.
    /// </summary>
    /// <param name="current">
    /// What the dialog opens on, for the same reason the PDF one takes it: the answer belongs
    /// in preferences rather than in the dialog, so it survives a restart and there is one
    /// record of it.
    /// </param>
    /// <param name="layout">The shared layout saved in preferences - the same one the PDF dialog opens on.</param>
    Task<ExportChoice<DocxExportSetup>?> RequestDocxSetupAsync(
        string documentName,
        DocxExportSetup current,
        ExportLayout layout,
        CancellationToken cancellationToken = default);
}
