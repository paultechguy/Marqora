// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.App.Services;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Folders;

namespace PaulTechGuy.MQ.App.ViewModels;

/// <summary>What a folder panel row's Copy submenu puts on the clipboard (P14).</summary>
public enum PanelCopy
{
    Name,
    RelativePath,
    FullPath,
}

/// <summary>
/// Which predicate an edit runs behind (P11). <see cref="Caret"/> is <c>CanFormat</c>, for a
/// command typed or clicked with the editor in mind; <see cref="Writable"/> drops the focus
/// terms, for an insert picked from a side panel while the keyboard is in it.
/// </summary>
public enum EditGate
{
    Caret,
    Writable,
}

/// <summary>
/// The folder panel's file operations (WP-7): rename, delete, New Document Here, Open All in
/// Tabs, and the Copy and Explorer items.
///
/// The workspace does the work on disk and carries the open documents, their marks and pins
/// (WP-3). This half is everything around it: the recent files and closed-tab records that
/// would otherwise lead to "File not found" (P23), the confirmation a delete asks for, and the
/// one-line notice that says what happened, with Undo after a rename.
/// </summary>
public sealed partial class MainViewModel
{
    // ---------------------------------------------------------------- rename

    /// <summary>
    /// Renames a file or folder from the panel. True when it moved. Says so in the panel's notice,
    /// with Undo, or says why not. <paramref name="isUndo"/> is the notice's own Undo, which
    /// says what it put back and offers no Undo of its own.
    /// </summary>
    public async Task<bool> RenameFromPanelAsync(string from, string destination, bool isUndo = false)
    {
        string oldName = Path.GetFileName(from);
        string newName = Path.GetFileName(destination);

        FileOperationResult result = await _workspace.RenameAsync(from, destination).ConfigureAwait(true);

        if (!result.Succeeded)
        {
            FolderPanel.ShowNotice(
                isUndo
                    ? $"Could not rename “{newName}” back. {result.Reason}"
                    : $"Could not rename “{oldName}”. {result.Reason}",
                isError: true);

            return false;
        }

        // The paths nothing in the workspace knows about: recent files and the closed-tab
        // records Reopen Closed Tab follows (P23).
        await _recent.RenameAsync(from, destination).ConfigureAwait(true);
        RebaseClosedTabs(from, destination);

        // Landed on the tree now rather than when the watcher reports it 300 ms later; the
        // watcher's report then changes nothing (WP-1).
        FolderPanel.ApplyLocalChange(new FolderChange(FolderChangeKind.Renamed, destination, from));

        if (isUndo)
        {
            FolderPanel.ShowNotice($"Renamed “{oldName}” back to “{newName}”.");
        }
        else
        {
            FolderPanel.ShowNotice(
                $"Renamed “{oldName}” to “{newName}”.",
                undo: () => RenameFromPanelAsync(destination, from, isUndo: true));
        }

        StatusText = $"Renamed {oldName}";

        return true;
    }

    /// <summary>
    /// Asks before a rename that takes a markdown file out of the tree, by giving it another
    /// extension while Show All Files is off: it is not lost, but it will not be listed.
    /// </summary>
    public async Task<bool> ConfirmRenameLeavesTreeAsync(string oldName, string newName)
    {
        ConfirmResult answer = await _dialogs.ConfirmAsync(
            "Rename",
            $"“{newName}” is not a markdown file name, so it will no longer be listed in the "
            + "folder panel unless Show All Files is on. It stays on disk under its new name.",
            primaryText: $"Rename to {newName}").ConfigureAwait(true);

        return answer == ConfirmResult.Primary;
    }

    /// <summary>A folder row's Set as Root.</summary>
    public Task UseFolderAsRootFromPanelAsync(string folder) => UseFolderAsRootAsync(folder);

    private void RebaseClosedTabs(string from, string destination)
    {
        for (int i = 0; i < _closedTabs.Count; i++)
        {
            ClosedTabRecord record = _closedTabs[i];

            if (FolderPaths.IsSameOrUnder(record.Path, from))
            {
                _closedTabs[i] = record with { Path = FolderPaths.Rebase(record.Path, from, destination) };
            }
        }
    }

    // ---------------------------------------------------------------- delete

    /// <summary>
    /// Why Delete is grayed for a path, or null when it is not: a document marked read-only, a
    /// folder holding one, and a document under review (P8, P25). The tooltip says it, and the
    /// Delete key says it on the status line.
    /// </summary>
    public string? WhyCannotDelete(string path)
    {
        string name = Path.GetFileName(path);

        if (_locks.AnyUnder(path))
        {
            return Directory.Exists(path)
                ? $"“{name}” holds a document marked read-only"
                : $"“{name}” is marked read-only";
        }

        if (_workspace.Documents.Any(d => d.IsUnderReview && d.Path is { } p && FolderPaths.IsSameOrUnder(p, path)))
        {
            return Directory.Exists(path)
                ? $"“{name}” holds a document under review"
                : $"“{name}” is under review";
        }

        return null;
    }

    /// <summary>
    /// Deletes a file or folder from the panel, after one confirmation that names what goes, says
    /// so when unsaved work goes with it, and says "permanently" when the Recycle Bin will not take
    /// it (P7). Cancel is the default; the verb is explicit and its button neutral.
    /// </summary>
    public async Task DeleteFromPanelAsync(string path)
    {
        if (WhyCannotDelete(path) is { } refused)
        {
            StatusText = $"Cannot delete: {refused}";
            return;
        }

        string name = Path.GetFileName(path);
        bool isFolder = Directory.Exists(path);
        RecycleForecast forecast = _recycleBin.Forecast(path);

        List<MarkdownDocument> unsaved =
            [.. _workspace.Documents.Where(d => d.IsDirty && d.Path is { } p && FolderPaths.IsSameOrUnder(p, path))];

        string what = isFolder
            ? $"the folder “{name}” and the {FileCount(forecast.FileCount)} in it"
            : $"“{name}”";

        string message = forecast.WillRecycle
            ? $"Move {what} to the Recycle Bin?"
            : $"The Recycle Bin will not take {what}, so it will be deleted permanently. This cannot be undone.";

        // The read-only box in a file's Windows properties does not stop a delete, in Explorer or
        // here, but somebody ticked it for a reason. It goes in a warning bar above the question,
        // and into the title, because a line at the foot of the message was read past.
        string? readOnlyWarning = forecast.ReadOnlyCount switch
        {
            0 => null,
            _ when !isFolder => $"“{name}” is marked read-only in its Windows properties.",
            1 => "1 file in this folder is marked read-only in its Windows properties.",
            int n => $"{n} files in this folder are marked read-only in their Windows properties.",
        };

        if (unsaved.Count > 0)
        {
            message += unsaved.Count == 1
                ? $"\n\n“{unsaved[0].DisplayName}” has unsaved changes, which will be lost."
                : $"\n\n{unsaved.Count} open documents in it have unsaved changes, which will be lost.";
        }

        string verb = unsaved.Count > 0
            ? "Delete and discard changes"
            : forecast.WillRecycle ? "Move to Recycle Bin" : "Delete permanently";

        string title = (isFolder, readOnlyWarning is not null) switch
        {
            (false, true) => "Delete read-only file",
            (true, true) => "Delete folder with read-only files",
            (true, false) => "Delete folder",
            _ => "Delete file",
        };

        ConfirmResult answer = await _dialogs.ConfirmAsync(
            title,
            message,
            primaryText: verb,
            destructivePrimary: true,
            warning: readOnlyWarning).ConfigureAwait(true);

        if (answer != ConfirmResult.Primary)
        {
            return;
        }

        FileOperationResult result = await _workspace.DeleteAsync(path).ConfigureAwait(true);

        if (!result.Succeeded)
        {
            FolderPanel.ShowNotice($"Could not delete “{name}”. {result.Reason}", isError: true);
            return;
        }

        await _recent.RemoveUnderAsync(path).ConfigureAwait(true);
        _closedTabs.RemoveAll(record => FolderPaths.IsSameOrUnder(record.Path, path));

        FolderPanel.ApplyLocalChange(new FolderChange(FolderChangeKind.Deleted, path));

        // No Undo: pulling a file back out of the Recycle Bin is awkward from a program, and the
        // Recycle Bin is already a delete's undo.
        FolderPanel.ShowNotice(result.Permanent
            ? $"Deleted “{name}” permanently."
            : $"Moved “{name}” to the Recycle Bin.");

        StatusText = result.Permanent ? $"Deleted {name}" : $"Moved {name} to the Recycle Bin";
    }

    private static string FileCount(int count) => count == 1 ? "1 file" : $"{count} files";

    // ---------------------------------------------------------------- images

    /// <summary>
    /// Why an image cannot be linked into the active document from the panel, or null when it
    /// can (P24, WP-8). Asked by the menu item, which grays with it as its tooltip, by the drag
    /// before it starts, and by the insert itself.
    ///
    /// Only an image in the document's folder or below it: the preview serves nothing else, the
    /// link check reports anything else, and a PDF leaves it out (C21). A link that the app
    /// itself would then call broken is not one to write.
    /// </summary>
    public string? WhyCannotLinkImage(string imagePath)
    {
        if (_workspace.Active is not { } document)
        {
            return "Open a document to link the image into";
        }

        if (document.Path is not { } documentPath)
        {
            return "Save the document first; a link is measured from where it is saved";
        }

        if (document.IsReadOnly)
        {
            return document.IsUnderReview
                ? $"“{document.DisplayName}” is under review"
                : $"“{document.DisplayName}” is marked read-only";
        }

        if (ViewMode is ViewMode.Preview)
        {
            return "Switch to Source or Side by Side; links go into the source";
        }

        if (Path.GetDirectoryName(documentPath) is not { } folder || !FolderPaths.IsUnder(imagePath, folder))
        {
            return $"Only images in “{document.DisplayName}”’s folder or below it can be shown in its preview";
        }

        return null;
    }

    /// <summary>
    /// The markdown for an image, relative to the active document (P24): the file name without
    /// its extension as the alt text, and the target in angle brackets when it needs them. Null
    /// when <see cref="WhyCannotLinkImage"/> has a reason.
    /// </summary>
    public string? ImageLinkFor(string imagePath) =>
        WhyCannotLinkImage(imagePath) is null && Path.GetDirectoryName(_workspace.Active?.Path) is { } folder
            ? ImageLinks.For(folder, imagePath)
            : null;

    /// <summary>
    /// Insert Link in Source: the image's link at the caret, replacing any selection, through the
    /// edit runner so <c>Ctrl+Z</c> takes it back. The caret lands after it, and focus goes back
    /// to the editor.
    /// </summary>
    public async Task InsertImageLinkFromPanelAsync(string imagePath)
    {
        if (WhyCannotLinkImage(imagePath) is { } why)
        {
            StatusText = why;
            return;
        }

        if (ImageLinkFor(imagePath) is not { } link)
        {
            return;
        }

        await RunEditAsync(context => ReplaceSelection(context, link), "insert a link", gate: EditGate.Writable)
            .ConfigureAwait(true);

        FolderPanelHasFocus = false;

        // The panel slid out over the text it just wrote into. Inserting is opening a file's
        // twin, so it follows that preference rather than having a fifth.
        if (SlidOutPanel == SidePanel.Folder)
        {
            SlideAwayIf(_settings.Current.SlideAwayOnOpenFile);
        }

        StatusText = $"Linked {Path.GetFileName(imagePath)}";
    }

    /// <summary>
    /// An image row dropped on the web view at a point in device-independent pixels. Checked
    /// again here, since the document may have changed since the drag began; the source pane
    /// inserts at the line and column under the point, or nothing when the point is over the
    /// preview.
    /// </summary>
    public async Task DropImageLinkAsync(string imagePath, double x, double y)
    {
        if (WhyCannotLinkImage(imagePath) is { } why)
        {
            StatusText = why;
            return;
        }

        if (_host is null || ImageLinkFor(imagePath) is not { } link)
        {
            return;
        }

        await _host.InsertTextAtPointAsync(link, x, y).ConfigureAwait(true);

        FolderPanelHasFocus = false;

        if (SlidOutPanel == SidePanel.Folder)
        {
            SlideAwayIf(_settings.Current.SlideAwayOnOpenFile);
        }
    }

    /// <summary>
    /// Whether a point across the web view, in device-independent pixels from its left edge,
    /// is over the source pane. Worked out from the view mode and the splitter position rather
    /// than asked of the page, because a drag's cursor is decided on every mouse move and needs
    /// the answer at once.
    ///
    /// This repeats the page's layout rule, <c>.mq-root</c> in <c>webshell/app.css</c>: in Side
    /// by Side the source pane is the splitter's share of the width, then a 6px splitter, then
    /// the preview, each the full height. A change there has to be made here too.
    /// </summary>
    public bool IsSourcePaneAt(double x, double width) => ViewMode switch
    {
        ViewMode.Source => true,
        ViewMode.SideBySide => x < _settings.Current.SplitterPosition * width,
        _ => false,
    };

    private static EditResult ReplaceSelection(EditContext context, string text)
    {
        TextRange selection = context.Selection.Ordered;
        TextPosition end = new(selection.Start.Line, selection.Start.Column + text.Length);

        return new EditResult([new TextEdit(selection, text)], TextRange.At(end));
    }

    /// <summary>Opens an image in whatever Windows opens it with: a row's double-click and Enter.</summary>
    public void LaunchImageFromPanel(string imagePath)
    {
        LaunchPath(imagePath);
        StatusText = $"Opened {Path.GetFileName(imagePath)}";
    }

    // ------------------------------------------------------------ the others

    /// <summary>New Document Here: an untitled tab whose first Save As starts in the folder (P13). Nothing is written yet.</summary>
    public void NewDocumentHere(string folder)
    {
        _workspace.CreateUntitled(saveFolder: folder);
        FolderPanelHasFocus = false;
        StatusText = $"New document in {Path.GetFileName(FolderPaths.TrimEnd(folder))}";

        RestoreDocumentFocusAfterChrome();
    }

    /// <summary>Open All in Tabs: what File, Open Folder did before the folder panel.</summary>
    public Task OpenAllInTabsAsync(string folder)
    {
        FolderPanelHasFocus = false;

        return OpenFolderInTabsAsync(folder);
    }

    /// <summary>
    /// The Copy submenu. A relative path is measured from the active document, since that is what
    /// someone pasting a link wants; null when there is no saved document to measure from.
    /// </summary>
    public string? CopyTextFor(string path, PanelCopy kind) => kind switch
    {
        PanelCopy.Name => Path.GetFileName(path),
        PanelCopy.FullPath => path,
        PanelCopy.RelativePath => RelativeLink(Path.GetDirectoryName(_workspace.Active?.Path), path),
        _ => null,
    };

    public void CopyFromPanel(string path, PanelCopy kind)
    {
        if (CopyTextFor(path, kind) is not { } text)
        {
            return;
        }

        StatusText = ClipboardText.Set(text, _logger)
            ? $"Copied “{text}”"
            : "Could not copy";
    }
}
