// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Services;

/// <summary>
/// Makes undoing a paste undo the file it wrote.
///
/// Ctrl+Z after pasting a screenshot takes the markdown back out, and without this the image
/// stays on disk with nothing pointing at it - so a folder beside a document quietly fills with
/// pictures nobody chose to keep. VS Code and the rest leave them; doing better is cheap here
/// because the app knows which files it wrote and when.
///
/// Two rules keep it safe, and both matter more than the feature does:
///
/// **Files are moved, never deleted.** They go to a recycle folder under the app's own data
/// directory, so a mistake here costs a folder to look in rather than someone's screenshot.
///
/// **The window closes at the first save.** Once a document has been written to disk with the
/// reference in it, the image is part of a saved file and this stops watching it for good. That
/// is what stops a saved-then-closed tab, or a paragraph cut to be moved elsewhere, from
/// recycling a file the user had already committed. The check is against the saved text, not the
/// text on screen.
/// </summary>
public sealed class PastedImageTracker(IAppPaths paths, ILogger<PastedImageTracker> logger)
    : IPastedImageTracker
{
    /// <summary>
    /// How long to wait before each retry of a move, in milliseconds; zero means this is the
    /// last attempt.
    ///
    /// Four tries over about three quarters of a second. A thumbnail or a scan of a file this
    /// size is done well inside that, and the whole sequence is short enough that the status
    /// line still reads as a response to the keystroke that caused it.
    /// </summary>
    private static readonly int[] RetryWaits = [40, 120, 300, 0];

    private readonly Lock _gate = new();

    /// <summary>
    /// What this session wrote and has not yet seen committed, keyed by document.
    ///
    /// Only ever holds files this app created during this run. A file that was already on disk
    /// when the document was opened is nothing to do with us and is never touched.
    /// </summary>
    private readonly Dictionary<Guid, List<TrackedImage>> _byDocument = [];

    public void Track(Guid documentId, string reference, string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        lock (_gate)
        {
            if (!_byDocument.TryGetValue(documentId, out List<TrackedImage>? images))
            {
                images = [];
                _byDocument[documentId] = images;
            }

            images.Add(new TrackedImage(reference, fullPath));

            // Logged because the two halves of this feature are far apart in time - a paste now,
            // an undo whenever - and when it does not work the only question worth answering
            // first is which half failed. A "Tracked" with no later "Recycled" says the watching
            // started and the reconciliation never ran; no "Tracked" at all says the opposite.
            logger.LogInformation(
                "Tracking {Path} for document {DocumentId} as {Reference}.",
                fullPath,
                documentId,
                reference);
        }
    }

    /// <summary>
    /// Reconciles what is on disk with what the document now says, after an edit.
    ///
    /// <paramref name="text"/> is the document as it stands and <paramref name="savedText"/> is
    /// the last thing written to disk - the difference between them is the whole safety rule.
    /// <paramref name="otherDocuments"/> is every other open document's text, because two
    /// documents in one folder can reference the same image and the second one's claim is as
    /// good as the first's.
    /// </summary>
    public async Task<PastedImageReview> ReviewAsync(
        Guid documentId,
        string text,
        string savedText,
        IReadOnlyList<string> otherDocuments)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(savedText);
        ArgumentNullException.ThrowIfNull(otherDocuments);

        TrackedImage[] snapshot;

        // The list is copied under the lock and the filesystem is touched outside it. The work
        // below waits between attempts, and a lock must never be held across a wait.
        lock (_gate)
        {
            // Nothing this session wrote for this document, which is the ordinary case: every
            // debounced keystroke in every document arrives here and almost none of them have a
            // pasted image outstanding.
            if (!_byDocument.TryGetValue(documentId, out List<TrackedImage>? tracked))
            {
                return PastedImageReview.None;
            }

            snapshot = [.. tracked];
        }

        int recycled = 0;
        int restored = 0;
        int failed = 0;
        List<TrackedImage> committed = [];

        foreach (TrackedImage image in snapshot)
        {
            // Committed. The reference is in a file on disk now, so this is somebody's document
            // rather than an uncommitted paste, and we are done with it forever.
            if (Mentions(savedText, image.Reference))
            {
                await RestoreAsync(image).ConfigureAwait(false);
                committed.Add(image);

                continue;
            }

            bool wanted = Mentions(text, image.Reference)
                || otherDocuments.Any(other => Mentions(other, image.Reference));

            if (wanted)
            {
                if (await RestoreAsync(image).ConfigureAwait(false))
                {
                    restored++;
                }
            }
            else if (await RecycleAsync(image).ConfigureAwait(false) is { } moved)
            {
                if (moved)
                {
                    recycled++;
                }
                else
                {
                    failed++;
                }
            }
        }

        lock (_gate)
        {
            if (_byDocument.TryGetValue(documentId, out List<TrackedImage>? tracked))
            {
                foreach (TrackedImage image in committed)
                {
                    tracked.Remove(image);
                }

                if (tracked.Count == 0)
                {
                    _byDocument.Remove(documentId);
                }
            }
        }

        return new PastedImageReview(recycled, restored, failed);
    }

    /// <summary>
    /// Stops watching a document, leaving every file it wrote exactly where it is.
    ///
    /// Closing a tab is not a reason to take anything away: the document may have been saved,
    /// and if it was not, the user has already been asked about the text and answered.
    /// </summary>
    public void Forget(Guid documentId)
    {
        lock (_gate)
        {
            if (_byDocument.Remove(documentId, out List<TrackedImage>? images))
            {
                // Anything currently in the recycle folder for this document goes back first.
                // A tab closed while an image sat recycled must not be the way a file disappears.
                foreach (TrackedImage image in images)
                {
                    // Fire and forget: the tab is going, nothing is waiting on the answer, and
                    // a file that cannot come back right now is reported in the log.
                    _ = RestoreAsync(image);
                }
            }
        }
    }

    /// <summary>
    /// Empties the recycle folder, at a clean shutdown only.
    ///
    /// Deliberately not called on a crash path. A folder of files nobody wanted is a trivial
    /// cost; a folder of files somebody did want, deleted during a failure, is not.
    /// </summary>
    public void CleanUp()
    {
        lock (_gate)
        {
            _byDocument.Clear();

            try
            {
                if (Directory.Exists(paths.RecycleDirectory))
                {
                    Directory.Delete(paths.RecycleDirectory, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not empty the recycle folder.");
            }
        }
    }

    /// <summary>
    /// Whether a document's text still refers to an image.
    ///
    /// A plain substring search rather than a parse. The reference is a percent-encoded relative
    /// path this app minted, so it is distinctive enough that a false positive would take
    /// deliberate effort - and the failure it guards against is one-sided: thinking an image is
    /// still referenced leaves a file alone, which is always the safe way to be wrong.
    /// </summary>
    private static bool Mentions(string text, string reference) =>
        text.Contains(reference, StringComparison.OrdinalIgnoreCase);

    /// <returns>
    /// True when the file was moved aside, false when it should have been and could not, and
    /// null when there was nothing there to move - already recycled, or deleted by hand.
    /// </returns>
    private async Task<bool?> RecycleAsync(TrackedImage image)
    {
        if (!File.Exists(image.FullPath))
        {
            return null;
        }

        Directory.CreateDirectory(paths.RecycleDirectory);

        // A free name, never an overwrite. Everything already in here is a file somebody undid
        // and might still want, so replacing one to make room for another is the exact thing
        // this folder exists to prevent. Keeps the original name so the folder can be read by a
        // human looking for something they want back.
        string name = DocumentAssets.NextFreeName(
            Path.GetFileNameWithoutExtension(image.FullPath),
            Path.GetExtension(image.FullPath),
            [.. Directory.EnumerateFiles(paths.RecycleDirectory).Select(Path.GetFileName).OfType<string>()]);

        string destination = Path.Combine(paths.RecycleDirectory, name);

        if (await MoveAsync(image.FullPath, destination, "recycle").ConfigureAwait(false))
        {
            // Remembered rather than recomputed, so the restore knows which of possibly several
            // files with this name is the one belonging to this reference.
            image.RecycledPath = destination;

            logger.LogInformation("Recycled {Path}; its paste was undone.", image.FullPath);

            return true;
        }

        return false;
    }

    /// <returns>True when a file was actually brought back.</returns>
    private async Task<bool> RestoreAsync(TrackedImage image)
    {
        // Nothing was taken away, so there is nothing to bring back. Asking the folder would
        // guess; the entry knows.
        if (image.RecycledPath is not { } recycled
            || !File.Exists(recycled)
            || File.Exists(image.FullPath))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(image.FullPath)!);

        if (await MoveAsync(recycled, image.FullPath, "restore").ConfigureAwait(false))
        {
            image.RecycledPath = null;

            logger.LogInformation("Restored {Path}; its paste came back.", image.FullPath);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Moves a file, waiting out whatever is briefly holding it.
    ///
    /// A file written seconds ago is routinely untouchable for a moment: Explorer builds a
    /// thumbnail for a new picture in a folder somebody is looking at, and a virus scanner reads
    /// anything that lands in Downloads. Either can make a rename fail with a sharing violation
    /// or an outright access denial, and both are over almost immediately.
    ///
    /// One attempt was not enough, and the symptom was miserable to diagnose: undo removed the
    /// reference, the file stayed, and the same operation succeeded perfectly by the time anyone
    /// went to look at it. The waits are short and rise, so the ordinary case still costs a
    /// single try and the awkward case is survived rather than reported.
    /// </summary>
    private async Task<bool> MoveAsync(string source, string destination, string what)
    {
        foreach (int wait in RetryWaits)
        {
            try
            {
                // No overwrite. Both directions of this move land somewhere that is supposed to
                // be free - a recycle name chosen against the folder's contents, or a document
                // slot the caller has already checked is empty - so a destination that exists
                // means an assumption is wrong, and failing is the right answer to that.
                File.Move(source, destination);

                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (wait == 0)
                {
                    // Out of attempts. Reported rather than swallowed: the reference has already
                    // gone from the document, so a file left here is an orphan with nothing
                    // pointing at it and no way for anyone to notice.
                    logger.LogWarning(ex, "Could not {What} {Path}.", what, source);

                    return false;
                }

                logger.LogDebug(
                    "Could not {What} {Path} yet; waiting {Wait}ms.", what, source, wait);

                await Task.Delay(wait).ConfigureAwait(false);
            }
        }

        return false;
    }

    /// <param name="reference">The relative path as written in the markdown.</param>
    /// <param name="fullPath">Where the file belongs when the document refers to it.</param>
    private sealed class TrackedImage(string reference, string fullPath)
    {
        public string Reference { get; } = reference;

        public string FullPath { get; } = fullPath;

        /// <summary>
        /// Where this file is sitting while recycled, or null when it is in its proper place.
        ///
        /// Recorded rather than derived. It used to be a name computed from the full path, which
        /// meant two recycles of the same path aimed at the same destination - so the second one
        /// had to overwrite the first. That is both a lost file and, as it turned out, a failure:
        /// paste, undo, paste again, undo again is an ordinary thing to do, and the second undo
        /// tried to replace a file the first had put there and was refused.
        /// </summary>
        public string? RecycledPath { get; set; }
    }
}
