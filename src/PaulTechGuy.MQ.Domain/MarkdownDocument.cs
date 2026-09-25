// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// One open document, which is to say one tab.
///
/// <see cref="Text"/> is the in-memory buffer and <see cref="SavedText"/> is what is on disk,
/// so dirty state is a comparison rather than a mutable flag that can drift out of sync.
///
/// <see cref="Path"/> is null for a document that has never been saved. Those live only in
/// memory until the first save gives them a location.
/// </summary>
public sealed record MarkdownDocument
{
    /// <summary>Identity that survives a rename, so tabs and editor models stay paired.</summary>
    public required Guid Id { get; init; }

    /// <summary>Absolute path on disk, or null for a document that has never been saved.</summary>
    public string? Path { get; init; }

    /// <summary>Name shown for an unsaved document, such as "Untitled 2".</summary>
    public string UntitledName { get; init; } = "Untitled";

    public required string Text { get; init; }

    public required string SavedText { get; init; }

    public required DateTimeOffset LoadedUtc { get; init; }

    /// <summary>
    /// How this document stands in relation to the file behind it. Set by the workspace as it
    /// hears from the file watcher, and read by the tab strip and the change banner.
    /// </summary>
    public ExternalState External { get; init; } = ExternalState.InSync;

    /// <summary>
    /// The file as it was last seen on disk, or null for a document with no file yet. Lets a
    /// watcher event that reports nothing new be discarded before anyone is asked about it.
    /// </summary>
    public FileStamp? Stamp { get; init; }

    /// <summary>
    /// When the workspace last took new content from disk without asking, or null if it never
    /// has.
    ///
    /// Only the automatic reload sets this. A reload the user asked for is not news to them,
    /// and neither is a save, which is why <see cref="AsSaved"/> clears it: once this text has
    /// been written back, "something arrived here that you may not have read" has stopped
    /// being true.
    /// </summary>
    public DateTimeOffset? AutoReloadedUtc { get; init; }

    /// <summary>
    /// The read-only mark the user put on this document, restored by path when it is opened.
    ///
    /// Marqora's own record of it. The file's read-only attribute is a separate thing that this
    /// deliberately neither reads nor writes, so a mark here says only that Marqora will refuse
    /// to write the file - never that Windows would.
    /// </summary>
    public bool IsLocked { get; init; }

    /// <summary>
    /// Whether the user has pinned this document to the left of the tab strip, restored by path
    /// when it is opened.
    ///
    /// Kept with the document rather than with its tab for the reason the mark above is: it is
    /// remembered per path, and Save As is the one moment both the old path and the new one are
    /// in the same hand. A pin held only by the view model would need a second place to know
    /// what a rename means.
    ///
    /// Unlike the mark, this says nothing about whether the file may be written. It moves the
    /// tab to the front of the strip, takes its close button away, and keeps the document out of
    /// the three bulk close commands - and that is the whole of it.
    /// </summary>
    public bool IsPinned { get; init; }

    /// <summary>
    /// Whether a reviewer is commenting on this document.
    ///
    /// Held only in memory, unlike the mark and the pin: a review lasts from Start Commenting to
    /// End, and a document reopened tomorrow is not under review. It folds into
    /// <see cref="IsReadOnly"/>, which is what makes every refusal the mark already has - the
    /// buffer, the editor, save, autosave, Replace All, the formatter - apply here too without a
    /// second list of call sites to keep in step. The comments are anchored in the text as it
    /// stood when the review began, and a document that changed under them would move every one.
    /// </summary>
    public bool IsUnderReview { get; init; }

    public bool IsUntitled => Path is null;

    /// <summary>Tab label: the file name, or the placeholder name when never saved.</summary>
    public string DisplayName => Path is null ? UntitledName : System.IO.Path.GetFileName(Path);

    /// <summary>Full path for tooltips, or the placeholder name when there is no file yet.</summary>
    public string DisplayPath => Path ?? UntitledName;

    /// <summary>
    /// Whether there is anything here that disk does not have.
    ///
    /// A missing file counts, and that single clause is the whole of the deleted-file
    /// behavior: the tab's dot appears, the close prompt offers to save, <c>CanSave</c> turns
    /// on and the buffer is written back on the next Ctrl+S. Faking it by writing a sentinel
    /// into <see cref="SavedText"/> would do the same on the surface and quietly break reload
    /// and the close prompt, which is exactly what a comparison rather than a mutable flag
    /// exists to prevent.
    /// </summary>
    public bool IsDirty => External == ExternalState.Missing
        || !string.Equals(Text, SavedText, StringComparison.Ordinal);

    /// <summary>Whether an external change is waiting for the user to say what to do about it.</summary>
    public bool HasExternalChange => External != ExternalState.InSync;

    /// <summary>
    /// Whether this document refuses to be written.
    ///
    /// A missing file is deliberately not protected, and that clause is load-bearing rather than
    /// tidy-mindedness. <see cref="IsDirty"/> counts a missing file as unsaved precisely so the
    /// buffer can be written back over it - which is the one case where rewriting an unedited
    /// document is the point. Leaving the mark in force there would take the only way back and
    /// leave the user holding text with nowhere to put it.
    ///
    /// Computed rather than stored, for the reason <see cref="IsDirty"/> gives: a second flag
    /// saying what two others already say is a flag that can disagree with them.
    /// </summary>
    /// <summary>
    /// Whether this document refuses to be written, and to be changed.
    ///
    /// One answer for both, because the editor is told it and the buffer enforces it, and the
    /// two cannot be allowed to disagree: a keystroke is either turned away in both places or
    /// taken in both. Letting one hold text the other does not would be worse than having no
    /// mark at all.
    ///
    /// It applies whether or not the document is already dirty. An earlier version stood down
    /// over unsaved edits, so that Monaco's readOnly - which switches off undo and redo along
    /// with typing - would not freeze work somebody still needed to get back. That turned out
    /// to be the more dangerous of the two: undoing to the point where the buffer matched the
    /// file made the document clean, the editor locked on that transition, and redo went with
    /// it, so the edits could not be recovered at all. Locking at once loses undo but loses
    /// nothing else, and taking the mark off hands it straight back.
    ///
    /// A review holds even over a missing file. The missing-file clause exists so the buffer can
    /// be written back, and a reviewer who wants that has End Commenting one click away; letting
    /// the text move under the comments instead would silently misplace every one of them.
    /// </summary>
    public bool IsReadOnly => (IsLocked && External != ExternalState.Missing) || IsUnderReview;

    public MarkdownDocument WithText(string text) => this with { Text = text };

    /// <summary>
    /// Marks the buffer as written. Clears any external state with it: whatever the file did,
    /// this document has just decided what it holds.
    /// </summary>
    public MarkdownDocument AsSaved(FileStamp? stamp = null) => this with
    {
        SavedText = Text,
        External = ExternalState.InSync,
        Stamp = stamp ?? Stamp,
        AutoReloadedUtc = null,
    };

    /// <summary>A new, empty document that exists only in memory.</summary>
    public static MarkdownDocument CreateUntitled(string untitledName) => new()
    {
        Id = Guid.NewGuid(),
        Path = null,
        UntitledName = untitledName,
        Text = string.Empty,
        SavedText = string.Empty,
        LoadedUtc = DateTimeOffset.UtcNow,
    };
}
