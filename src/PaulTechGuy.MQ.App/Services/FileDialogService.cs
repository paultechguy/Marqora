// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Abstractions.Ui;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// File and folder dialogs.
///
/// These call the Win32 common dialogs rather than the WinRT pickers in
/// Windows.Storage.Pickers. Marqora is unpackaged, and in that configuration the WinRT
/// pickers never complete: the returned task simply hangs, with no exception to catch.
/// The Win32 dialogs behave identically packaged or not, and are what desktop apps have
/// always used.
///
/// The dialogs are modal and run their own message loop, so they are shown on the UI
/// thread and the result is handed back as a completed task.
/// </summary>
public sealed class FileDialogService(WindowContext window, ILogger<FileDialogService> logger) : IFileDialogService
{
    public Task<string?> PickOpenFileAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            string? path = Win32Dialogs.OpenFile(
                RequireOwner(),
                "Open a markdown file",
                MarkdownFileTypes.Extensions);

            logger.LogInformation("Open dialog returned {Result}.", path ?? "(cancelled)");
            return Task.FromResult(path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The open dialog failed.");
            return Task.FromResult<string?>(null);
        }
    }

    public Task<string?> PickSaveFileAsync(
        string? suggestedFileName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string? path = Win32Dialogs.SaveFile(
                RequireOwner(),
                "Save markdown file",
                string.IsNullOrWhiteSpace(suggestedFileName) ? "Untitled.md" : suggestedFileName,
                MarkdownFileTypes.FolderExtensions);

            logger.LogInformation("Save dialog returned {Result}.", path ?? "(cancelled)");
            return Task.FromResult(path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The save dialog failed.");
            return Task.FromResult<string?>(null);
        }
    }

    public Task<string?> PickExportFileAsync(
        string suggestedFileName,
        string filterLabel,
        IReadOnlyList<string> extensions,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string? path = Win32Dialogs.SaveFile(
                RequireOwner(),
                $"Export as {filterLabel}",
                suggestedFileName,
                extensions,
                filterLabel);

            logger.LogInformation("Export dialog returned {Result}.", path ?? "(cancelled)");
            return Task.FromResult(path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The export dialog failed.");
            return Task.FromResult<string?>(null);
        }
    }

    public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            string? path = Win32Dialogs.PickFolder(RequireOwner(), "Open every markdown file in a folder");

            logger.LogInformation("Folder dialog returned {Result}.", path ?? "(cancelled)");
            return Task.FromResult(path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The folder dialog failed.");
            return Task.FromResult<string?>(null);
        }
    }

    /// <summary>Owner handle for the modal dialog, so it centres on and blocks the window.</summary>
    private IntPtr RequireOwner()
    {
        IntPtr handle = window.WindowHandle;

        return handle == IntPtr.Zero
            ? throw new InvalidOperationException("A file dialog was requested before the window existed.")
            : handle;
    }
}
