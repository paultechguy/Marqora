// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Abstractions.Services;

public enum WorkspaceChange
{
    /// <summary>A document was added to the workspace.</summary>
    Opened,

    /// <summary>A document was removed.</summary>
    Closed,

    /// <summary>A different document became active.</summary>
    Activated,

    /// <summary>The in-memory buffer changed.</summary>
    Edited,

    Saved,

    ReloadedFromDisk,

    /// <summary>Tab order changed.</summary>
    Reordered,

    /// <summary>
    /// The document's relationship to its file changed - it went stale, went missing, or was
    /// brought back into line.
    ///
    /// Travels the same queue as every other change rather than on an event of its own, so it
    /// cannot arrive before the tab it describes exists. A file rewritten while its Opened is
    /// still awaiting a render would otherwise try to mark a tab that had not been added yet.
    /// </summary>
    ExternalStateChanged,
}

/// <summary>Describes one change to the workspace. <see cref="Document"/> is null for a close.</summary>
public sealed class WorkspaceChangedEventArgs(WorkspaceChange change, MarkdownDocument? document, Guid documentId)
    : EventArgs
{
    public WorkspaceChange Change { get; } = change;

    public MarkdownDocument? Document { get; } = document;

    /// <summary>Always set, including for a close where the document itself is gone.</summary>
    public Guid DocumentId { get; } = documentId;
}

/// <summary>
/// Owns every open document and which one is active, plus all file I/O for them.
///
/// Documents are immutable records held in an ordered list, so a change replaces an entry
/// rather than mutating it. Callers address documents by <see cref="MarkdownDocument.Id"/>,
/// which survives a rename through Save As.
/// </summary>
public interface IWorkspaceService
{
    /// <summary>Open documents, in tab order.</summary>
    IReadOnlyList<MarkdownDocument> Documents { get; }

    MarkdownDocument? Active { get; }

    bool HasDocuments { get; }

    event EventHandler<WorkspaceChangedEventArgs>? Changed;

    /// <summary>
    /// Opens a file, or activates it if already open. Returns the document either way, so
    /// opening the same file twice never produces a duplicate tab.
    /// </summary>
    Task<MarkdownDocument> OpenAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Adds an empty in-memory document and makes it active.</summary>
    /// <summary>
    /// Adds an in-memory document, optionally already holding <paramref name="text"/>.
    ///
    /// Content belongs here rather than in an edit applied afterwards: opening the tab is
    /// what hands the text to the editor, and an edit raised after that only updates the
    /// model - the editor would keep the empty buffer it was opened with.
    /// </summary>
    MarkdownDocument CreateUntitled(string? text = null);

    /// <summary>Reopens a saved session. Paths that no longer exist are skipped.</summary>
    Task RestoreAsync(
        IReadOnlyList<string> paths,
        int activeIndex,
        CancellationToken cancellationToken = default);

    void Activate(Guid id);

    /// <summary>Updates the in-memory buffer from the editor. Does not touch disk.</summary>
    void ApplyEdit(Guid id, string text);

    /// <summary>Writes the document. A document with no path is a no-op; use SaveAsAsync.</summary>
    Task SaveAsync(Guid id, CancellationToken cancellationToken = default);

    Task SaveAsAsync(Guid id, string path, CancellationToken cancellationToken = default);

    Task ReloadAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Accepts the buffer as the answer to a pending external change and clears the marker,
    /// leaving the text alone. This is "Keep Mine": the user has decided.
    ///
    /// Does nothing for a missing file. The marker there is a statement of fact rather than a
    /// question, and it stands until the file is written back.
    /// </summary>
    void ResolveExternalChange(Guid id);

    void Close(Guid id);

    /// <summary>Moves a document to a new index, for drag-reordered tabs.</summary>
    void Move(Guid id, int newIndex);

    MarkdownDocument? Find(Guid id);
}
