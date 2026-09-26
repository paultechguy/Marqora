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

    /// <summary>
    /// A document was marked read-only, or had its mark taken off.
    ///
    /// Its own change rather than an Edited: nothing about the text moved, and the tab strip,
    /// the menus and the editor all need telling for a reason that has nothing to do with the
    /// buffer.
    /// </summary>
    LockChanged,

    /// <summary>
    /// A document was pinned to the left of the tab strip, or had its pin taken off.
    ///
    /// Its own change for the reason <see cref="LockChanged"/> is: nothing about the text moved.
    /// What listens to it is different again - a pin decides where the tab *sits*, so the strip
    /// has to reorder on it, which no other change in this enum asks for except
    /// <see cref="Reordered"/>.
    /// </summary>
    PinChanged,
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

    /// <summary>
    /// Adds an in-memory document holding a copy of text that is kept somewhere else - a review
    /// resumed from its page - under <paramref name="name"/> rather than "Untitled N".
    ///
    /// Clean rather than unsaved: closing it loses nothing, because the text is still where it
    /// came from, so there is nothing to be asked about. Editing it makes it unsaved as usual.
    /// </summary>
    MarkdownDocument CreateSnapshot(string text, string name);

    /// <summary>Reopens a saved session. Paths that no longer exist are skipped.</summary>
    Task RestoreAsync(
        IReadOnlyList<string> paths,
        int activeIndex,
        CancellationToken cancellationToken = default);

    void Activate(Guid id);

    /// <summary>
    /// Updates the in-memory buffer from the editor. Does not touch disk.
    ///
    /// False when nothing changed - the text already matched, the document has gone, or it is
    /// marked read-only. A caller that also pushes this text into the editor has to check:
    /// doing one without the other leaves the buffer and the editor holding different documents.
    /// </summary>
    bool ApplyEdit(Guid id, string text);

    /// <summary>
    /// Writes the document. False when nothing was written - no path yet, or the document is
    /// marked read-only. Callers announce a save as soon as this returns, so the answer matters.
    /// </summary>
    Task<bool> SaveAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the document somewhere else and points it there. The mark does not travel with
    /// it - this is the way out of a marked document - but a destination that is itself marked
    /// is refused, and that is what false means here.
    ///
    /// A pin does travel. The two differ because they mean different things: a mark guards a
    /// file, so leaving it behind is the point, while a pin holds a tab's place and the tab has
    /// not moved.
    /// </summary>
    Task<bool> SaveAsAsync(Guid id, string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the document read-only, or takes the mark off, and remembers it for next time.
    ///
    /// False for an untitled document: there is no file yet for a mark to protect. Marking a
    /// document that has unsaved edits is allowed - the edits stay, and stay unsaveable until
    /// the mark comes off or they are written somewhere else.
    /// </summary>
    Task<bool> SetLockedAsync(Guid id, bool locked, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pins the document to the left of the tab strip, or takes the pin off, and remembers it
    /// for next time.
    ///
    /// False for an untitled document: a pin is remembered by path, and there is not one yet.
    ///
    /// Where the pinned tab lands among the other pinned tabs is not decided here. This says
    /// only that it is pinned; the strip does the moving, on <see cref="WorkspaceChange.PinChanged"/>.
    /// </summary>
    Task<bool> SetPinnedAsync(Guid id, bool pinned, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the file's content from disk. False when nothing was reloaded: no such document, no
    /// file behind it, or a document under review, whose text the comments are anchored in.
    /// </summary>
    Task<bool> ReloadAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts a document under review, or takes it out, raising <see cref="WorkspaceChange.LockChanged"/>.
    ///
    /// Nothing is remembered: a review lives in memory only. While one is on, the document
    /// refuses every change including a reload, and an external change is recorded rather than
    /// taken. Ending it settles that change the way the watcher would have at the time - reloads
    /// a clean document if the preference says so, otherwise leaves the notice standing.
    /// </summary>
    bool SetUnderReview(Guid id, bool underReview);

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
