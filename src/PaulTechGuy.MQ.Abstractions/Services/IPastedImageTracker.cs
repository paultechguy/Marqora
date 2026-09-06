// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Abstractions.Services;

/// <summary>
/// What one reconciliation actually did.
///
/// Returned rather than kept to the log because this feature is otherwise invisible: a file that
/// should have been taken away and was not looks exactly like a file that was never tracked, and
/// nothing on screen distinguishes them. The caller says what happened, so a failure is a
/// sentence rather than a silence.
/// </summary>
/// <param name="Recycled">Files moved aside because nothing refers to them any more.</param>
/// <param name="Restored">Files brought back because a reference returned.</param>
/// <param name="Failed">
/// Files that should have moved and could not - most often because something else has the file
/// open. The reference is already gone from the document, so this is the one outcome the user
/// has to be told about.
/// </param>
public readonly record struct PastedImageReview(int Recycled, int Restored, int Failed)
{
    public static PastedImageReview None { get; }
}

/// <summary>
/// Keeps a pasted image and the reference to it in step, so undoing the paste undoes the file.
///
/// Only ever concerns itself with files this app wrote during this run, and only until the
/// document is saved with the reference in it. After that the image belongs to a saved document
/// and nothing here touches it again.
/// </summary>
public interface IPastedImageTracker
{
    /// <summary>Records that a file was just written for a document that now refers to it.</summary>
    void Track(Guid documentId, string reference, string fullPath);

    /// <summary>
    /// Brings the disk into line with the document after an edit: recycles what is no longer
    /// referenced, restores what has come back.
    /// </summary>
    /// <param name="text">The document as it stands.</param>
    /// <param name="savedText">
    /// The document as last written to disk. A reference present here has been committed and is
    /// never touched again, which is what makes this safe.
    /// </param>
    /// <param name="otherDocuments">
    /// Every other open document's text. Two documents in one folder can reference the same
    /// image, and the second one's claim on it is as good as the first's.
    /// </param>
    /// <remarks>
    /// Asynchronous because a file written a moment ago is often briefly untouchable - Explorer
    /// building a thumbnail for it, or a virus scanner reading it - and the answer to that is to
    /// wait a moment and try again rather than to give up. The waiting must not be done on the
    /// thread that draws the window.
    /// </remarks>
    Task<PastedImageReview> ReviewAsync(
        Guid documentId,
        string text,
        string savedText,
        IReadOnlyList<string> otherDocuments);

    /// <summary>Stops watching a document, leaving every file it wrote where it is.</summary>
    void Forget(Guid documentId);

    /// <summary>Empties the recycle folder. For a clean shutdown only.</summary>
    void CleanUp();
}
