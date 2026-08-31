// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Abstractions.Ui;

/// <summary>Wraps the WinUI file pickers so view models stay free of window handles.</summary>
public interface IFileDialogService
{
    Task<string?> PickOpenFileAsync(CancellationToken cancellationToken = default);

    Task<string?> PickSaveFileAsync(string? suggestedFileName = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Save dialog for an export, with its own file type rather than markdown.
    /// </summary>
    Task<string?> PickExportFileAsync(
        string suggestedFileName,
        string filterLabel,
        IReadOnlyList<string> extensions,
        CancellationToken cancellationToken = default);

    /// <summary>Picks a folder, for opening every markdown file inside it.</summary>
    Task<string?> PickFolderAsync(CancellationToken cancellationToken = default);
}
