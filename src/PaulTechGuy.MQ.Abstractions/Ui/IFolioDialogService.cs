// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Abstractions.Ui;

/// <summary>
/// Shows the author what a Folio would contain, in what order, and asks whether to build it.
///
/// Kept apart from <see cref="IExportDialogService"/>, which asks how one page should be laid
/// out. This asks a different question - what travels, and in what sequence - and has to answer
/// it again every time the author changes their mind, because unticking a document changes which
/// images are still needed and which links have just started pointing outside.
///
/// Both parameters are functions rather than values, and for different reasons: the documents
/// because the surface is a modeless window that can outlive the set it opened on, and the plan
/// because it is remade on every tick, drag and re-sort.
/// </summary>
public interface IFolioDialogService
{
    /// <summary>
    /// Returns what the author settled on, or null when they cancel or simply close the window.
    /// </summary>
    /// <param name="documents">
    /// Every document that could go in - the saved tabs - in the order they are open. Read again
    /// when the author asks for the plan to be rebuilt after the workspace has moved.
    /// </param>
    /// <param name="plan">
    /// Makes a plan for a selection, in the order given. Passed in rather than a finished plan,
    /// because the counts, the warnings and the size all change as documents are ticked,
    /// unticked and dragged - and a preflight showing figures for a set the author has already
    /// changed is worse than no preflight.
    /// </param>
    Task<FolioChoice?> RequestFolioAsync(
        Func<IReadOnlyList<string>> documents,
        Func<IReadOnlyList<string>, int, FolioPlan> plan,
        CancellationToken cancellationToken = default);
}
