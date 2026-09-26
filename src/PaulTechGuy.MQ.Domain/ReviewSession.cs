// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// A reviewer's comments on one document, held in memory from Start Commenting to End.
///
/// Nothing here is ever written to disk. Share turns the session into a file somewhere the
/// reviewer chose, and ending it throws the comments away. The page a share wrote carries a
/// <see cref="ReviewState"/>, and <see cref="Restore"/> takes the review back up from it - so
/// after its first share a review's save file is its page, and there is still no file of
/// Marqora's own beside the document.
///
/// <see cref="SourceText"/> is the document as it stood when the session began. The workspace
/// refuses every change to a document under review, so it is also the document as it stands
/// now - but the copy is kept rather than read back, because the comments are about the text
/// that was reviewed and the CriticMarkup copy must be built from exactly that.
///
/// Sharing is tracked as a revision number rather than a flag. Every change moves
/// <see cref="Revision"/>, a share records the revision it carried, and "unshared" is the two
/// disagreeing - so adding a comment and deleting it again still counts as a change, which is
/// the honest answer: the reviewer may have meant to say something else.
/// </summary>
public sealed class ReviewSession
{
    private readonly List<ReviewComment> _comments = [];

    /// <param name="sessionId">
    /// The review this continues, when it was resumed from a shared page or restarted on a
    /// snapshot of one; a new review gets a new id.
    /// </param>
    public ReviewSession(Guid documentId, string sourceText, DateTimeOffset startedUtc, Guid? sessionId = null)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        DocumentId = documentId;
        SourceText = sourceText;
        StartedUtc = startedUtc;
        SessionId = sessionId ?? Guid.NewGuid();
    }

    /// <summary>
    /// A review taken back up from the page it was shared to: the same text, the same comments
    /// with their ids, and counted as shared - the page holds exactly this, so ending or closing
    /// straight away loses nothing and asks nothing.
    /// </summary>
    public static ReviewSession Restore(Guid documentId, ReviewState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var session = new ReviewSession(documentId, state.Source, state.StartedUtc, state.SessionId);

        foreach (ReviewStateComment comment in state.Comments)
        {
            session.Add(
                new ReviewAnchor(comment.Line, comment.Index, comment.Start, comment.End, comment.Quote),
                comment.Note,
                comment.Id);
        }

        session.MarkShared(state.SharedUtc);

        return session;
    }

    public Guid DocumentId { get; }

    /// <summary>
    /// Which review this is, across the pages it has been shared to and the sittings it has
    /// been resumed in. It is what lets a share recognize a page as this review's own.
    /// </summary>
    public Guid SessionId { get; }

    /// <summary>The document's text when the session began, and the text every comment is about.</summary>
    public string SourceText { get; }

    public DateTimeOffset StartedUtc { get; }

    /// <summary>Moves on every change to the comments.</summary>
    public int Revision { get; private set; }

    /// <summary>The revision the last share carried, or -1 before the first.</summary>
    public int SharedRevision { get; private set; } = -1;

    /// <summary>When the last share happened, or null before the first.</summary>
    public DateTimeOffset? SharedUtc { get; private set; }

    public int Count => _comments.Count;

    /// <summary>
    /// Whether ending now would lose something the reviewer has not sent anywhere.
    ///
    /// An empty session has nothing to lose, even if comments came and went: a warning about
    /// zero comments would be a warning about nothing.
    /// </summary>
    public bool HasUnshared => _comments.Count > 0 && Revision != SharedRevision;

    /// <summary>Whether a share has happened and nothing has changed since.</summary>
    public bool IsShared => SharedRevision >= 0 && Revision == SharedRevision;

    /// <summary>
    /// The comments in reading order: by source line, then by which element carries that line
    /// (the cells of one table row), then by where they start in it.
    ///
    /// Reading order rather than the order they were written, because the numbers a reader sees
    /// in the margin have to run down the page.
    /// </summary>
    public IReadOnlyList<ReviewComment> Ordered =>
        [.. _comments
            .OrderBy(c => c.Anchor.Line)
            .ThenBy(c => c.Anchor.Index)
            .ThenBy(c => c.Anchor.Start)
            .ThenBy(c => c.Anchor.End)];

    public ReviewComment? Find(Guid id) => _comments.Find(c => c.Id == id);

    /// <summary>The comment's number as the margin shows it, counting from one, or 0 if it is not here.</summary>
    public int NumberOf(Guid id)
    {
        IReadOnlyList<ReviewComment> ordered = Ordered;

        for (int i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Id == id)
            {
                return i + 1;
            }
        }

        return 0;
    }

    public ReviewComment Add(ReviewAnchor anchor, string note, Guid? id = null)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(note);

        var comment = new ReviewComment(id ?? Guid.NewGuid(), anchor, note);

        _comments.Add(comment);
        Revision++;

        return comment;
    }

    /// <summary>Replaces a comment's note. Returns false if there is no such comment or nothing changed.</summary>
    public bool Update(Guid id, string note)
    {
        ArgumentNullException.ThrowIfNull(note);

        int index = _comments.FindIndex(c => c.Id == id);

        if (index < 0 || string.Equals(_comments[index].Note, note, StringComparison.Ordinal))
        {
            return false;
        }

        _comments[index] = _comments[index] with { Note = note };
        Revision++;

        return true;
    }

    public bool Remove(Guid id)
    {
        if (_comments.RemoveAll(c => c.Id == id) == 0)
        {
            return false;
        }

        Revision++;

        return true;
    }

    /// <summary>Records that everything as it stands now has been sent somewhere.</summary>
    public void MarkShared(DateTimeOffset sharedUtc)
    {
        SharedRevision = Revision;
        SharedUtc = sharedUtc;
    }
}
