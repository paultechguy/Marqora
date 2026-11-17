// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Abstractions.Ui;

/// <summary>Wraps the WinUI file pickers so view models stay free of window handles.</summary>
public interface IFileDialogService
{
    /// <summary>
    /// Open dialog for documents, with several files allowed. Empty when the user cancels.
    /// The order is the dialog's own, so a caller that cares about order sorts.
    /// </summary>
    Task<IReadOnlyList<string>> PickOpenFilesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Save dialog for a markdown document. <paramref name="folder"/>, when given, is where it
    /// opens: an untitled document made by the folder panel's New Document Here starts in that
    /// folder (P13). Null keeps the folder Windows remembers.
    /// </summary>
    Task<string?> PickSaveFileAsync(string? suggestedFileName = null, string? folder = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Save dialog for an export, with its own file type rather than markdown.
    /// </summary>
    Task<string?> PickExportFileAsync(
        string suggestedFileName,
        string filterLabel,
        IReadOnlyList<string> extensions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Save dialog for a shared review page.
    ///
    /// Its own folder memory, kept by Windows rather than a setting: the first review is offered
    /// in Documents, and every one after that where the last was saved. A review usually leaves
    /// the machine, so the document's own folder - often the author's repository - is the wrong
    /// first guess.
    /// </summary>
    /// <param name="folder">
    /// A folder to open in whatever Windows remembers: the one a resumed review's page is in, so
    /// that sharing again finds the page it came from. Null keeps the memory.
    /// </param>
    Task<string?> PickReviewFileAsync(string suggestedFileName, string? folder = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Open dialog for a file that is not a document: a preferences file to import.
    ///
    /// Separate from <see cref="PickOpenFilesAsync"/> rather than a parameter on it, because
    /// that one carries the markdown types and the recent-file behavior that go with opening
    /// a document, and neither applies to a file the app reads once and forgets.
    /// </summary>
    Task<string?> PickImportFileAsync(
        string title,
        string filterLabel,
        IReadOnlyList<string> extensions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Picks a folder. The title says what it is for, because the one dialog serves Open Folder
    /// and the Folio's destination and unpacking, and a title written for one misleads the others.
    /// </summary>
    Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default);
}
