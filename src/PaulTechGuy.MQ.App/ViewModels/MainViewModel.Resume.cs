// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Services;

namespace PaulTechGuy.MQ.App.ViewModels;

/// <summary>
/// Resuming a review from the page it was shared to.
///
/// A shared page carries a <see cref="ReviewState"/>: the exact text reviewed and every comment
/// with its anchor. Dropping the page back in - or Review, Resume Shared Review... - opens that
/// text in a tab with no file behind it, labeled "notes.md (review)", with the review running
/// and its comments where they were. The original file may have moved on since, and that does
/// not matter: the comments are about the text the page holds.
///
/// The command line and Recent never reach here. They open markdown, and a page is resumed
/// only by someone handing it over on purpose. docs/Review.md has the design.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>The most of a page that is read. A review page is text and pictures; this is far past any real one.</summary>
    private const long LargestReviewPage = 64L * 1024 * 1024;

    /// <summary>
    /// What a document knows about the review pages it has to do with, kept for as long as its
    /// tab is open - past End, which is when the session itself goes.
    ///
    /// A resumed document needs these after End: its real file name for Save As, its pictures
    /// for the preview, and the page it came from for a review started on it again. Any
    /// reviewed document needs <see cref="KnownWriteIds"/>, which is how Share tells its own
    /// page from one another copy of the review wrote since.
    /// </summary>
    private sealed class ReviewSnapshot
    {
        /// <summary>"notes.md" - what the stamp, the title and the page's name are made from, never the tab's label.</summary>
        public required string FileName { get; init; }

        public required Guid SessionId { get; set; }

        /// <summary>The SHA-256 of the text the review is of, so a changed document is not taken for the same review.</summary>
        public required string SourceSha256 { get; set; }

        /// <summary>The page this review was resumed from or last shared to.</summary>
        public string? PagePath { get; set; }

        /// <summary>The pictures a resumed review's page carried; null for a document with a folder.</summary>
        public IReadOnlyDictionary<string, ReviewAsset>? Assets { get; init; }

        /// <summary>Every write this copy of the review made or read. A page naming another was written elsewhere.</summary>
        public HashSet<Guid> KnownWriteIds { get; } = [];

        public int ShareCount { get; set; }

        /// <summary>Whether the document is a snapshot from a page, rather than a document of its own.</summary>
        public bool IsResumed { get; init; }
    }

    private readonly Dictionary<Guid, ReviewSnapshot> _snapshots = [];

    /// <summary>Whether this run has said, once, that a shared page can be dropped back in.</summary>
    private bool _resumeHintShown;

    /// <summary>Whether the review in progress was resumed from its page and has not been shared since.</summary>
    private bool _resumedThisSitting;

    /// <summary>The file name a review is of: the snapshot's real name, or the document's own.</summary>
    private string ReviewedFileName(MarkdownDocument document) =>
        _snapshots.TryGetValue(document.Id, out ReviewSnapshot? snapshot) ? snapshot.FileName : document.DisplayName;

    /// <summary>
    /// Where an export finds a document's pictures: the page a resumed review came from, while
    /// the tab still holds it, and the document's own folder otherwise. Asked on the UI thread,
    /// before any export goes to the background.
    /// </summary>
    private DocumentImages ImagesFor(MarkdownDocument document) =>
        _snapshots.TryGetValue(document.Id, out ReviewSnapshot? snapshot) && snapshot.Assets is { } assets
            ? DocumentImages.FromPage(assets)
            : DocumentImages.FromDocument(document.Path);

    /// <summary>
    /// Clears what a stopped share left in the temp folder, at the start of every share as well
    /// as at startup, so leftovers from a crash do not wait for the next launch. In the
    /// background, and safe to run beside a share: a folder a running share holds is locked, and
    /// one made a moment ago is too young to take even before its lock is in place.
    /// </summary>
    private void SweepScratchInBackground() =>
        _ = Task.Run(() => FolioScratch.SweepStale(_logger));

    /// <summary>
    /// The document's tab has closed, or it has been saved as a file of its own: what it knew
    /// about pages goes, and with it the only references to a resumed review's pictures - the
    /// preview's copy is dropped here too, so nothing of them outlives the tab.
    /// </summary>
    private void ForgetSnapshotOf(Guid id)
    {
        if (_snapshots.Remove(id, out ReviewSnapshot? snapshot))
        {
            _host?.SetDocumentAssets(id, null);

            _logger.LogDebug(
                "Released what document {Id} knew about its review pages ({Count} picture(s)).",
                id,
                snapshot.Assets?.Count ?? 0);
        }
    }

    // ---------------------------------------------------------------- recognizing a page

    /// <summary>The first few kilobytes of an .html file, or null for anything else or a file that cannot be read.</summary>
    private string? ReadHtmlHead(string path)
    {
        if (!".html".Equals(Path.GetExtension(path), StringComparison.OrdinalIgnoreCase)
            && !".htm".Equals(Path.GetExtension(path), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            using var reader = new StreamReader(path);

            Span<char> head = stackalloc char[4096];

            return new string(head[..reader.Read(head)]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not look at {Path} to see what it is.", path);

            return null;
        }
    }

    /// <summary>Whether a file is a review page that can be resumed, judged from its head.</summary>
    private bool LooksLikeReviewPage(string path) =>
        ReadHtmlHead(path) is { } head && ReviewState.IsReviewPage(head);

    /// <summary>Whether a file is a review page shared before resuming existed.</summary>
    private bool LooksLikeLegacyReviewPage(string path) =>
        ReadHtmlHead(path) is { } head && ReviewState.IsLegacyReviewPage(head);

    private Task ExplainLegacyReviewPageAsync(string path) =>
        _dialogs.ShowMessageAsync(
            "This review can't be resumed",
            $"{Path.GetFileName(path)} was shared by Marqora 1.0.10 or earlier. It holds the comments, "
            + "but not what's needed to resume them. Open it in a browser to read them.");

    // ------------------------------------------------------------------- resuming

    /// <summary>Review, Resume Shared Review...: picks a page and resumes the review it holds.</summary>
    [RelayCommand]
    private async Task ResumeSharedReviewAsync()
    {
        string? path = await _fileDialogs
            .PickImportFileAsync("Resume a Shared Review", "Review page", [".html", ".htm"])
            .ConfigureAwait(true);

        if (!string.IsNullOrWhiteSpace(path))
        {
            if (LooksLikeLegacyReviewPage(path))
            {
                await ExplainLegacyReviewPageAsync(path).ConfigureAwait(true);
            }
            else
            {
                await ResumeReviewAsync(path).ConfigureAwait(true);
            }
        }

        RestoreDocumentFocusAfterChrome();
    }

    /// <summary>
    /// Opens the text a shared page holds in a tab of its own and takes the review back up,
    /// every comment where it was and counted as shared.
    /// </summary>
    private async Task ResumeReviewAsync(string path)
    {
        // The same gap Ctrl+Shift+R guards: a second press landing mid-resume would find a
        // review half-started and end it, or start another beside it.
        if (_reviewStarting)
        {
            return;
        }

        _reviewStarting = true;

        try
        {
            await ResumeReviewCoreAsync(path).ConfigureAwait(true);
        }
        finally
        {
            _reviewStarting = false;
        }
    }

    private async Task ResumeReviewCoreAsync(string path)
    {
        // A share of this page that stopped part way, on a drive other than the temp folder's,
        // left a hidden copy beside it. Opening the page is the next chance to clear it.
        SafeFileWriter.ClearStaleTemporaries(path, _logger);

        string? html = await ReadReviewPageAsync(path).ConfigureAwait(true);

        if (html is null)
        {
            return;
        }

        ReviewState? state = ReviewState.TryDecode(html, out ReviewStateProblem problem);

        if (state is null)
        {
            _logger.LogInformation("{Path} could not be resumed: {Problem}.", path, problem);

            await _dialogs.ShowMessageAsync(
                "This review can't be resumed",
                problem switch
                {
                    ReviewStateProblem.Missing => $"{Path.GetFileName(path)} doesn't hold a review Marqora can resume.",
                    ReviewStateProblem.TooNew => $"{Path.GetFileName(path)} was shared by a newer Marqora, which says this version can't read it. Update Marqora to resume it.",
                    ReviewStateProblem.TooLarge => $"{Path.GetFileName(path)} is larger than any review Marqora writes.",
                    _ => $"{Path.GetFileName(path)} has been damaged or edited since it was shared, so its comments can't be trusted to be where they were.",
                })
                .ConfigureAwait(true);
            return;
        }

        // This review is open already - a tab resumed from this page, or the review it began as.
        if (_snapshots.FirstOrDefault(s => s.Value.SessionId == state.SessionId) is { Value: { } open } entry
            && _workspace.Find(entry.Key) is { } openDocument)
        {
            _workspace.Activate(openDocument.Id);

            if (open.KnownWriteIds.Contains(state.WriteId))
            {
                StatusText = $"{openDocument.DisplayName} is this review, already open";
            }
            else
            {
                await _dialogs.ShowMessageAsync(
                    "This review is already open",
                    $"{openDocument.DisplayName} is this review, and {Path.GetFileName(path)} has changes "
                    + "this copy doesn't - it was shared again from somewhere else. End the review here "
                    + "and close its tab to resume from the page instead.")
                    .ConfigureAwait(true);
            }

            return;
        }

        // One review at a time, as ever: take the reader to the one that is running.
        if (_review is { } running && _workspace.Find(running.DocumentId) is { } reviewed)
        {
            _workspace.Activate(reviewed.Id);
            ShowHighlightedStatus($"Commenting on {reviewed.DisplayName} — end that review first", ReviewGlyph);
            return;
        }

        IReadOnlyDictionary<string, ReviewAsset> assets = ReviewAssets.Extract(html);
        HashSet<string> labels = new(_workspace.Documents.Select(d => d.DisplayName), StringComparer.OrdinalIgnoreCase);

        MarkdownDocument document = _workspace.CreateSnapshot(
            state.Source,
            ReviewPage.ResumeLabel(state.FileName, labels.Contains));

        var snapshot = new ReviewSnapshot
        {
            FileName = state.FileName,
            SessionId = state.SessionId,
            SourceSha256 = state.SourceSha256,
            PagePath = path,
            Assets = assets,
            ShareCount = state.ShareCount,
            IsResumed = true,
        };

        snapshot.KnownWriteIds.Add(state.WriteId);
        _snapshots[document.Id] = snapshot;
        _host?.SetDocumentAssets(document.Id, assets);

        // CreateSnapshot raised Opened and Activated, and the dispatcher ran both posts inline,
        // so they are on the chain already. The shell drops a setReview for a tab it has not
        // been given, so the comments go only once the tab exists.
        await _workspaceChain.ConfigureAwait(true);

        ReviewSession session = ReviewSession.Restore(document.Id, state);

        await BeginReviewAsync(document, session).ConfigureAwait(true);

        foreach (ReviewComment comment in session.Ordered)
        {
            InsertInReadingOrder(new ReviewCommentViewModel(comment.Id, comment.Anchor, comment.Note, isDraft: false));
        }

        Renumber();

        LastReviewPath = path;
        HasSharedReviewFile = true;
        _resumedThisSitting = true;
        RefreshReviewState();
        await PushReviewAsync().ConfigureAwait(true);

        _logger.LogInformation(
            "Resumed a review of {FileName} with {Count} comments from {Path}.", state.FileName, session.Count, path);

        ShowHighlightedStatus(
            session.Count == 1
                ? $"Resumed the review of {state.FileName}: 1 comment"
                : $"Resumed the review of {state.FileName}: {session.Count} comments",
            ReviewGlyph);

        if (_host is not null)
        {
            await _host.FocusPaneAsync(EditorPane.Preview).ConfigureAwait(true);
        }
    }

    /// <summary>The whole page, or null having said why not.</summary>
    private async Task<string?> ReadReviewPageAsync(string path)
    {
        try
        {
            var info = new FileInfo(path);

            if (info.Length > LargestReviewPage)
            {
                await _dialogs.ShowMessageAsync(
                    "This review can't be resumed",
                    $"{info.Name} is larger than any review Marqora writes.")
                    .ConfigureAwait(true);
                return null;
            }

            return await File.ReadAllTextAsync(path, Encoding.UTF8).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not read the review page {Path}.", path);

            await _dialogs.ShowMessageAsync("Could not open the review", ex.Message).ConfigureAwait(true);
            return null;
        }
    }

    /// <summary>
    /// The state of the page already at a path Share is about to write, or null for a page that
    /// is not a resumable review - or not a file at all. Read so that a share never quietly
    /// overwrites what another copy of the same review wrote there.
    /// </summary>
    private static ReviewState? ReadExistingState(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > LargestReviewPage)
            {
                return null;
            }

            return ReviewState.TryDecode(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a folder is a sensible place to suggest sharing to again: not a temporary copy -
    /// a page opened from an Outlook attachment or out of a zip lives somewhere the reader will
    /// never find again, and often cannot write to.
    /// </summary>
    private static bool IsLastingFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return false;
        }

        string full = Path.GetFullPath(folder);
        string temp = Path.GetFullPath(Path.GetTempPath());

        return !full.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
            && !full.Contains(@"\INetCache\", StringComparison.OrdinalIgnoreCase)
            && !full.Contains(@"\Temporary Internet Files\", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The shell drew the review, and says which comments it could not find a place for. Marked
    /// on their cards; said once in the status when the number changes, rather than after every
    /// render.
    /// </summary>
    private void OnCommentsPlaced(object? sender, CommentsPlacedEventArgs e) => _ui.Post(() =>
    {
        if (_review?.DocumentId != e.DocumentId)
        {
            return;
        }

        HashSet<Guid> missing = [.. e.Missing];
        int before = ReviewComments.Count(c => c.IsMissing);

        foreach (ReviewCommentViewModel card in ReviewComments)
        {
            card.IsMissing = !card.IsDraft && missing.Contains(card.Id);
        }

        int after = ReviewComments.Count(c => c.IsMissing);

        if (after > 0 && after != before)
        {
            ShowHighlightedStatus(
                after == 1
                    ? "1 comment could not be placed on the page — its passage wasn't found"
                    : $"{after} comments could not be placed on the page — their passages weren't found",
                ReviewGlyph);
        }
    });
}
