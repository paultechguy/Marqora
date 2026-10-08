// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using System.Text;
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
///
/// Each dialog remembers its own folder, by what it is for: Open, Save As, Open Folder, every
/// kind of export apart and every import apart, so a Word export opens where the last Word
/// export went and not where the last PDF or the last opened file did. Marqora keeps the
/// folders (<see cref="AppSettings.DialogFolders"/>) and opens each dialog in its own; it also
/// gives Windows the same purpose as a client id, which is all the dialogs did at first - but
/// Windows never records a folder under %TEMP%, so a dialog last used there forgot it.
/// </summary>
public sealed class FileDialogService(
    WindowContext window,
    ISettingsService settings,
    ILogger<FileDialogService> logger) : IFileDialogService
{
    private const string OpenPurpose = "open";
    private const string SavePurpose = "save";
    private const string FolderPurpose = "folder";

    public Task<IReadOnlyList<string>> PickOpenFilesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            IReadOnlyList<string> paths = Win32Dialogs.OpenFiles(
                RequireOwner(),
                "Open markdown files",
                MarkdownFileTypes.Extensions,
                extraFilters: [("Folios and review pages", [".html", ".htm"])],
                purpose: PurposeOf(OpenPurpose),
                startFolder: FolderFor(OpenPurpose));

            if (paths.Count > 0)
            {
                RememberFileFolder(OpenPurpose, paths[0]);
            }

            logger.LogInformation(
                "Open dialog returned {Result}.",
                paths.Count == 0 ? "(cancelled)" : string.Join("; ", paths));
            return Task.FromResult(paths);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The open dialog failed.");
            return Task.FromResult<IReadOnlyList<string>>([]);
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
                MarkdownFileTypes.FolderExtensions,
                purpose: PurposeOf(SavePurpose),
                forceFolder: FolderFor(SavePurpose));

            RememberFileFolder(SavePurpose, path);

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
        string purpose = "export " + filterLabel;

        try
        {
            string? path = Win32Dialogs.SaveFile(
                RequireOwner(),
                $"Export as {filterLabel}",
                suggestedFileName,
                extensions,
                filterLabel,
                PurposeOf(purpose),
                forceFolder: FolderFor(purpose));

            RememberFileFolder(purpose, path);

            logger.LogInformation("Export dialog returned {Result}.", path ?? "(cancelled)");
            return Task.FromResult(path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The export dialog failed.");
            return Task.FromResult<string?>(null);
        }
    }

    /// <summary>The review dialog's own folder memory. Fixed forever: changing it forgets where reviews go.</summary>
    private static readonly Guid ReviewDialogPurpose = new("5b0f6d52-8c1e-4e8a-9a51-2f7c3d9e41b6");

    private const string ReviewPurpose = "review";

    public Task<string?> PickReviewFileAsync(string suggestedFileName, string? folder = null, CancellationToken cancellationToken = default)
    {
        try
        {
            // The folder a resumed review came from has the better claim; otherwise the last
            // folder a review was shared to.
            string? path = Win32Dialogs.SaveFile(
                RequireOwner(),
                "Share Review",
                suggestedFileName,
                [".html"],
                "HTML document",
                ReviewDialogPurpose,
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                folder ?? FolderFor(ReviewPurpose));

            RememberFileFolder(ReviewPurpose, path);

            logger.LogInformation("Review dialog returned {Result}.", path ?? "(cancelled)");
            return Task.FromResult(path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The review dialog failed.");
            return Task.FromResult<string?>(null);
        }
    }

    public Task<string?> PickImportFileAsync(
        string title,
        string filterLabel,
        IReadOnlyList<string> extensions,
        CancellationToken cancellationToken = default)
    {
        // By title, not by file type: opening a Folio and resuming a review both read .html,
        // and are still two different places.
        string purpose = "import " + title;

        try
        {
            string? path = Win32Dialogs.OpenFile(
                RequireOwner(),
                title,
                extensions,
                filterLabel,
                PurposeOf(purpose),
                FolderFor(purpose));

            RememberFileFolder(purpose, path);

            logger.LogInformation("Import dialog returned {Result}.", path ?? "(cancelled)");
            return Task.FromResult(path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The import dialog failed.");
            return Task.FromResult<string?>(null);
        }
    }

    public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            string? path = Win32Dialogs.PickFolder(
                RequireOwner(),
                "Open every markdown file in a folder",
                PurposeOf(FolderPurpose),
                FolderFor(FolderPurpose));

            // The folder that was picked, not its parent: the next pick is most often it again,
            // or one beside it.
            if (!string.IsNullOrWhiteSpace(path))
            {
                Remember(FolderPurpose, path);
            }

            logger.LogInformation("Folder dialog returned {Result}.", path ?? "(cancelled)");
            return Task.FromResult(path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The folder dialog failed.");
            return Task.FromResult<string?>(null);
        }
    }

    /// <summary>
    /// The id Windows keeps a dialog's folder under, made from the dialog's purpose. Stable,
    /// because the id is Windows' memory: changing how it is made forgets every folder. Made
    /// from a name rather than written out, so a new export has an id without a table to
    /// extend. The diagram window's exports use it too.
    /// </summary>
    internal static Guid PurposeOf(string action) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes("Marqora file dialog: " + action)).AsSpan(0, 16));

    /// <summary>The folder this purpose's dialog was last used in, or null the first time.</summary>
    private string? FolderFor(string purpose) => settings.Current.DialogFolderFor(purpose);

    /// <summary>Remembers the folder a chosen file is in; nothing for a cancelled dialog.</summary>
    private void RememberFileFolder(string purpose, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Path.GetDirectoryName(path) is { Length: > 0 } folder)
        {
            Remember(purpose, folder);
        }
    }

    private void Remember(string purpose, string folder)
    {
        if (!string.Equals(FolderFor(purpose), folder, StringComparison.OrdinalIgnoreCase))
        {
            settings.Update(s => s.WithDialogFolder(purpose, folder));
        }
    }

    /// <summary>Owner handle for the modal dialog, so it centers on and blocks the window.</summary>
    private IntPtr RequireOwner()
    {
        IntPtr handle = window.WindowHandle;

        return handle == IntPtr.Zero
            ? throw new InvalidOperationException("A file dialog was requested before the window existed.")
            : handle;
    }
}
