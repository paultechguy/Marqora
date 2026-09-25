// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.App.Services;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Markdown;

namespace PaulTechGuy.MQ.App.ViewModels;

/// <summary>
/// Review: a reader commenting on a document the way Acrobat and Word allow, without the
/// document changing.
///
/// A session covers one document and lives in memory from Start Commenting to End. The
/// document is held still for the whole of it - <see cref="MarkdownDocument.IsUnderReview"/>
/// folds into read-only - because every comment is anchored in the text as it stood when the
/// review began. Share writes the preview with the comments in its margin as one HTML file,
/// carrying the reviewed source with the comments written in; Copy as Markdown puts that source
/// on the clipboard. Nothing is written anywhere else, and End throws the comments away.
///
/// docs/Review.md is the design in full.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>Segoe Fluent's comment glyph, in front of a message about a review.</summary>
    private const string ReviewGlyph = "";

    /// <summary>The one review there can be. One document at a time, by design; see docs/Review.md.</summary>
    private ReviewSession? _review;

    /// <summary>Set while a review is starting; see ToggleReviewAsync.</summary>
    private bool _reviewStarting;

    /// <summary>The view mode the reader had when the review started, put back when it ends.</summary>
    private ViewMode? _viewBeforeReview;

    /// <summary>The cards in the sidebar, in reading order, drafts included.</summary>
    public ObservableCollection<ReviewCommentViewModel> ReviewComments { get; } = [];

    /// <summary>Whether the document in front is the one under review, which is when the sidebar shows.</summary>
    [ObservableProperty]
    public partial bool IsReviewPanelVisible { get; set; }

    /// <summary>Whether the sidebar has a card to show, drafts included; the hint shows when not.</summary>
    [ObservableProperty]
    public partial bool HasReviewComments { get; set; }

    /// <summary>Whether any review is on, for the menu's Start/End wording.</summary>
    [ObservableProperty]
    public partial bool IsReviewing { get; set; }

    /// <summary>"6 comments · Shared 2:41 PM", under the sidebar's heading.</summary>
    [ObservableProperty]
    public partial string ReviewStatus { get; set; } = string.Empty;

    /// <summary>The banner above the document while the review is in front.</summary>
    [ObservableProperty]
    public partial string ReviewNotice { get; set; } = string.Empty;

    /// <summary>
    /// Whether the banner is showing. Closable: once the reader has read "the source is locked"
    /// it has done its job. A close holds only until the banner has something new to say - the
    /// file changing on disk, a review saved with its two buttons - because those are news.
    /// </summary>
    [ObservableProperty]
    public partial bool IsReviewBarOpen { get; set; }

    /// <summary>
    /// The messages the reader has closed this review. Per message rather than one flag, so the
    /// lock message closed once does not come back after every share, while news - a changed
    /// file, a saved review - still shows the first time it is said.
    /// </summary>
    private readonly HashSet<string> _dismissedReviewNotices = new(StringComparer.Ordinal);

    // Only a close made while the banner could be seen counts as the reader dismissing it. The
    // bar also shuts because the review's tab went to the back, and that is not a decision.
    partial void OnIsReviewBarOpenChanged(bool value)
    {
        if (!value && IsReviewPanelVisible && !_refreshingReviewBar)
        {
            _dismissedReviewNotices.Add(ReviewNotice);
        }
    }

    /// <summary>Set while RefreshReviewState moves the bar itself, so the move is not read as a close.</summary>
    private bool _refreshingReviewBar;

    /// <summary>The page the last share wrote, for the banner's Show in Folder and Copy File.</summary>
    [ObservableProperty]
    public partial string LastReviewPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasSharedReviewFile { get; set; }

    /// <summary>
    /// Whether End was asked for with comments nobody has seen yet. The footer turns into the
    /// question in place rather than a dialog - nothing in the sidebar blocks.
    /// </summary>
    [ObservableProperty]
    public partial bool IsEndWarningVisible { get; set; }

    [ObservableProperty]
    public partial string EndWarning { get; set; } = string.Empty;

    public string ReviewMenuLabel => IsReviewing ? "End Commenting" : "Start Commenting";

    partial void OnIsReviewingChanged(bool value) => OnPropertyChanged(nameof(ReviewMenuLabel));

    /// <summary>Asks the sidebar to put the keyboard in a card's box.</summary>
    public event EventHandler<ReviewCommentViewModel>? ReviewCommentFocusRequested;

    /// <summary>Asks the sidebar to bring a card into view, for a click on its comment in the preview.</summary>
    public event EventHandler<ReviewCommentViewModel>? ReviewCommentRevealRequested;

    private void AttachReviewHost(IPreviewHost host)
    {
        host.CommentRequested += OnCommentRequested;
        host.CommentActivated += OnCommentActivated;
        host.CommentHovered += OnCommentHovered;
        host.CommentEditRequested += OnCommentEditRequested;
    }

    // ---------------------------------------------------------------- start / end

    /// <summary>Start Commenting, or End it: Ctrl+Shift+R and the Review menu's first item.</summary>
    [RelayCommand]
    private async Task ToggleReviewAsync()
    {
        // Starting awaits a view switch, and a second Ctrl+Shift+R landing in that gap would
        // find a review already on and end it before the first press had finished starting it.
        if (_reviewStarting)
        {
            return;
        }

        if (_review is null)
        {
            _reviewStarting = true;

            try
            {
                await StartReviewAsync().ConfigureAwait(true);
            }
            finally
            {
                _reviewStarting = false;
            }
        }
        else if (_workspace.Active?.Id == _review.DocumentId)
        {
            EndReview();
        }
        else if (_workspace.Find(_review.DocumentId) is { } reviewed)
        {
            // One review at a time. Taking the reader to it is more use than a refusal alone.
            _workspace.Activate(reviewed.Id);
            ShowHighlightedStatus($"Commenting on {reviewed.DisplayName} — end that review first", ReviewGlyph);
        }
    }

    private async Task StartReviewAsync()
    {
        if (_workspace.Active is not { } document)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(document.Text))
        {
            StatusText = "Nothing to comment on";
            return;
        }

        // Unsaved edits in a document with a file would be stranded: a review holds the document
        // read-only, which takes save and autosave with it. Untitled text - pasted AI output,
        // most often - has no autosave to lose and nowhere yet to save to, so it may be reviewed.
        if (!document.IsUntitled && document.IsDirty)
        {
            ShowHighlightedStatus($"Save {document.DisplayName} before commenting on it", ReviewGlyph);
            return;
        }

        _review = new ReviewSession(document.Id, document.Text, DateTimeOffset.Now);
        _workspace.SetUnderReview(document.Id, true);

        IsEndWarningVisible = false;
        HasSharedReviewFile = false;
        LastReviewPath = string.Empty;
        ReviewComments.Clear();

        // Comments are made in the preview, and the source is locked for the whole review, so the
        // review gets the window to itself. Not saved as the preference, and the view the reader
        // had is put back when the review ends - see EndReviewCore.
        _viewBeforeReview = ViewMode;

        if (ViewMode != ViewMode.Preview)
        {
            await ApplyViewModeAsync(ViewMode.Preview, persist: false, takeFocus: false).ConfigureAwait(true);
        }

        RefreshReviewState();
        await PushReviewAsync().ConfigureAwait(true);

        ShowHighlightedStatus("Commenting — select text in the preview, then Add comment (Ctrl+Shift+M)", ReviewGlyph);

        if (_host is not null)
        {
            await _host.FocusPaneAsync(EditorPane.Preview).ConfigureAwait(true);
        }
    }

    /// <summary>End Commenting from the sidebar: asks first, in place, when something would be lost.</summary>
    [RelayCommand]
    private void EndReview()
    {
        if (_review is null)
        {
            return;
        }

        if (_review.HasUnshared || UnsavedEdit() is not null)
        {
            EndWarning = LossDescription(_review);
            IsEndWarningVisible = true;
            return;
        }

        EndReviewCore();
    }

    /// <summary>
    /// A card whose box holds words that have not been saved: a draft with text in it, or an
    /// edit that changed something. The session does not hold those yet, so its own count of
    /// what is unshared cannot see them - and a comment typed and never saved is still a comment
    /// the reader would not want to lose without being asked.
    /// </summary>
    private ReviewCommentViewModel? UnsavedEdit() =>
        ReviewComments.FirstOrDefault(c =>
            c.IsEditing
            && c.EditText.Trim().Length > 0
            && !string.Equals(c.EditText.Trim(), c.Note, StringComparison.Ordinal));

    /// <summary>What ending now would lose, as one sentence.</summary>
    private string LossDescription(ReviewSession review)
    {
        string unshared = !review.HasUnshared ? string.Empty
            : review.Count == 1 ? "1 comment hasn't been shared."
            : $"{review.Count} comments haven't been shared.";

        string writing = UnsavedEdit() is null ? string.Empty : "A comment you're writing hasn't been saved.";

        return $"{unshared} {writing}".Trim();
    }

    /// <summary>
    /// Refuses a share while a comment is half-written, and takes the reader to it. Sharing
    /// without it would send the old words, or none, while the new ones sat in the box.
    /// </summary>
    private bool RefuseWhileWriting()
    {
        if (UnsavedEdit() is not { } card)
        {
            return false;
        }

        ShowHighlightedStatus("Save or cancel the comment you're writing first", ReviewGlyph);
        ReviewCommentFocusRequested?.Invoke(this, card);

        return true;
    }

    [RelayCommand]
    private void DiscardReview() => EndReviewCore();

    [RelayCommand]
    private void KeepCommenting() => IsEndWarningVisible = false;

    /// <summary>
    /// Ends the review without asking: the comments go, the document can be edited again, and a
    /// change on disk that was held back is settled now.
    /// </summary>
    private void EndReviewCore()
    {
        if (_review is not { } review)
        {
            return;
        }

        Guid id = review.DocumentId;

        _review = null;
        ReviewComments.Clear();
        IsEndWarningVisible = false;
        HasSharedReviewFile = false;
        LastReviewPath = string.Empty;

        // The workspace raises LockChanged, which hands the editor its keyboard back.
        _workspace.SetUnderReview(id, false);

        if (_host is not null)
        {
            _ = _host.SetReviewAsync(id, false, []);
        }

        RefreshReviewState();
        RefreshExternalNotice();

        // The view the reader had before the review - unless they have picked another since, which
        // is theirs to keep.
        if (_viewBeforeReview is { } before && before != ViewMode && ViewMode == ViewMode.Preview)
        {
            _ = ApplyViewModeAsync(before, persist: false, takeFocus: false);
        }

        _viewBeforeReview = null;

        StatusText = "Commenting ended";
    }

    /// <summary>
    /// A tab under review is closing. Asked before its save prompt and apart from it, so that
    /// answering Save As there cannot quietly throw the comments away as well.
    /// </summary>
    /// <returns>False to keep the tab open.</returns>
    private async Task<bool> ConfirmEndReviewForCloseAsync(DocumentTabViewModel tab)
    {
        if (_review is not { } review || review.DocumentId != tab.Id)
        {
            return true;
        }

        if (review.HasUnshared || UnsavedEdit() is not null)
        {
            _workspace.Activate(tab.Id);

            ConfirmResult answer = await _dialogs.ConfirmAsync(
                "Discard comments?",
                $"You're commenting on {tab.Title}. {LossDescription(review)}",
                primaryText: "Discard Comments",
                destructivePrimary: true).ConfigureAwait(true);

            if (answer != ConfirmResult.Primary)
            {
                return false;
            }
        }

        // Ended here rather than when the tab goes, so that the save prompt which follows sees
        // an ordinary document - one it can offer to save - rather than a read-only one.
        EndReviewCore();
        return true;
    }

    /// <summary>The review's document has gone; so has the review. Nothing to ask - the tab is already closed.</summary>
    private void ForgetReviewOf(Guid id)
    {
        if (_review?.DocumentId != id)
        {
            return;
        }

        _review = null;
        ReviewComments.Clear();
        IsEndWarningVisible = false;
        HasSharedReviewFile = false;
        LastReviewPath = string.Empty;
        RefreshReviewState();
    }

    // ------------------------------------------------------------------ comments

    /// <summary>Add Comment: Ctrl+Shift+M from the window, and the menu item. The shell reads the selection.</summary>
    [RelayCommand]
    private async Task AddCommentAsync()
    {
        if (_review is null)
        {
            ShowHighlightedStatus("Start commenting first (Ctrl+Shift+R)", ReviewGlyph);
            return;
        }

        if (_host is not null)
        {
            await _host.CaptureCommentAsync().ConfigureAwait(true);
        }
    }

    private void OnCommentRequested(object? sender, CommentRequestedEventArgs e) => _ui.Post(() =>
    {
        if (_review is null || e.DocumentId != _review.DocumentId)
        {
            ShowHighlightedStatus("Start commenting first (Ctrl+Shift+R)", ReviewGlyph);
            return;
        }

        if (e.Anchor is not { } anchor)
        {
            StatusText = e.Problem switch
            {
                "multiBlock" => "Select text within one paragraph, list item or cell to comment on it",
                "overlap" => "That runs into another comment — select around it",
                "unsupported" => "Pictures, diagrams and equations can't be commented on",
                _ => "Select text in the preview to comment on it",
            };
            return;
        }

        // One comment in the writing at a time. A draft with words in it is not thrown away for
        // a stray selection; an empty one is only a place the reader changed their mind about.
        if (ReviewComments.FirstOrDefault(c => c.IsDraft) is { } draft)
        {
            if (!string.IsNullOrWhiteSpace(draft.EditText))
            {
                ShowHighlightedStatus("Save or cancel the comment you're writing first", ReviewGlyph);
                ReviewCommentFocusRequested?.Invoke(this, draft);
                return;
            }

            ReviewComments.Remove(draft);
        }

        // Back to commenting, so the End question asked a moment ago stands down and End
        // Commenting returns; asking again is one click.
        IsEndWarningVisible = false;

        var card = new ReviewCommentViewModel(Guid.NewGuid(), anchor, string.Empty, isDraft: true);

        InsertInReadingOrder(card);
        Renumber();
        _ = PushReviewAsync();

        ReviewCommentFocusRequested?.Invoke(this, card);
    });

    /// <summary>A double-click on a comment in the preview: open its card for editing.</summary>
    private void OnCommentEditRequested(object? sender, CommentActivatedEventArgs e) => _ui.Post(() =>
    {
        if (_review?.DocumentId == e.DocumentId
            && ReviewComments.FirstOrDefault(c => c.Id == e.CommentId) is { } card)
        {
            EditComment(card);
        }
    });

    private void OnCommentHovered(object? sender, CommentHoveredEventArgs e) => _ui.Post(() =>
    {
        if (_review?.DocumentId != e.DocumentId)
        {
            return;
        }

        foreach (ReviewCommentViewModel card in ReviewComments)
        {
            card.IsHighlighted = card.Id == e.CommentId;
        }
    });

    /// <summary>
    /// The pointer entered a card, or left it (null): lights the comment in the preview, and the
    /// card itself, the way the preview lights the card when the pointer is on the comment.
    /// </summary>
    public void HoverCard(ReviewCommentViewModel? card)
    {
        if (_review is null)
        {
            return;
        }

        foreach (ReviewCommentViewModel each in ReviewComments)
        {
            each.IsHighlighted = each == card;
        }

        if (_host is not null)
        {
            _ = _host.HoverCommentAsync(_review.DocumentId, card?.Id);
        }
    }

    private void OnCommentActivated(object? sender, CommentActivatedEventArgs e) => _ui.Post(() =>
    {
        if (ReviewComments.FirstOrDefault(c => c.Id == e.CommentId) is { } card)
        {
            ReviewCommentRevealRequested?.Invoke(this, card);
        }
    });

    [RelayCommand]
    private async Task SaveCommentAsync(ReviewCommentViewModel? card)
    {
        if (card is null || _review is null)
        {
            return;
        }

        string text = card.EditText.Trim();

        // Nothing to save, so nothing happens - the same answer the grayed Save button gives, for
        // Ctrl+Enter, which reaches here without asking the button. An empty draft stays open for
        // its words, and an existing comment emptied is left as it was rather than deleted by
        // accident: Cancel and Delete Comment each say what they do.
        if (!card.CanSave)
        {
            return;
        }

        if (card.IsDraft)
        {
            _review.Add(card.Anchor, text, card.Id);
            card.IsDraft = false;
        }
        else
        {
            _review.Update(card.Id, text);
        }

        card.Note = text;
        card.EditText = text;
        card.IsEditing = false;
        IsEndWarningVisible = false;

        RefreshReviewState();
        await PushReviewAsync().ConfigureAwait(true);

        // Back to the page, where the next passage will be selected. Not to the editor: that would
        // take the preview's selection with it.
        if (_host is not null)
        {
            await _host.FocusPaneAsync(EditorPane.Preview).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task CancelCommentAsync(ReviewCommentViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        if (card.IsDraft)
        {
            ReviewComments.Remove(card);
            Renumber();
            await PushReviewAsync().ConfigureAwait(true);
        }
        else
        {
            card.EditText = card.Note;
            card.IsEditing = false;
        }

        if (_host is not null)
        {
            await _host.FocusPaneAsync(EditorPane.Preview).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void EditComment(ReviewCommentViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        // Already open - a draft, or an edit in progress: take the reader to it, and leave what is
        // in the box alone. Starting over from the saved note would throw their typing away.
        if (card.IsEditing)
        {
            ReviewCommentFocusRequested?.Invoke(this, card);
            return;
        }

        card.EditText = card.Note;
        card.IsEditing = true;
        ReviewCommentFocusRequested?.Invoke(this, card);
    }

    [RelayCommand]
    private async Task DeleteCommentAsync(ReviewCommentViewModel? card)
    {
        if (card is null || _review is null)
        {
            return;
        }

        _review.Remove(card.Id);
        ReviewComments.Remove(card);
        Renumber();
        RefreshReviewState();

        await PushReviewAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RevealCommentAsync(ReviewCommentViewModel? card)
    {
        if (card is null || _review is null || _host is null)
        {
            return;
        }

        await _host.RevealCommentAsync(_review.DocumentId, card.Id).ConfigureAwait(true);
    }

    private void InsertInReadingOrder(ReviewCommentViewModel card)
    {
        int at = 0;

        while (at < ReviewComments.Count && Compare(ReviewComments[at].Anchor, card.Anchor) <= 0)
        {
            at++;
        }

        ReviewComments.Insert(at, card);

        static int Compare(ReviewAnchor a, ReviewAnchor b) =>
            a.Line != b.Line ? a.Line.CompareTo(b.Line)
            : a.Index != b.Index ? a.Index.CompareTo(b.Index)
            : a.Start.CompareTo(b.Start);
    }

    private void Renumber()
    {
        HasReviewComments = ReviewComments.Count > 0;

        for (int i = 0; i < ReviewComments.Count; i++)
        {
            ReviewComments[i].Number = i + 1;
        }
    }

    /// <summary>Tells the shell what to draw: every card, drafts included, numbered as the sidebar numbers them.</summary>
    private async Task PushReviewAsync()
    {
        if (_host is null || _review is null)
        {
            return;
        }

        ReviewMark[] marks = [.. ReviewComments.Select(c => new ReviewMark(c.Id, c.Number, c.Anchor, c.IsDraft))];

        await _host.SetReviewAsync(_review.DocumentId, true, marks).ConfigureAwait(true);
    }

    /// <summary>
    /// The sidebar, the banner and the menu, brought up to date with the review and with which
    /// document is in front. Called on every workspace change and after every change here.
    /// </summary>
    private void RefreshReviewState()
    {
        IsReviewing = _review is not null;
        HasReviewComments = ReviewComments.Count > 0;

        ShareReviewCommand.NotifyCanExecuteChanged();
        CopyReviewMarkdownCommand.NotifyCanExecuteChanged();

        if (_review is not { } review || _workspace.Find(review.DocumentId) is not { } document)
        {
            IsReviewPanelVisible = false;
            ReviewNotice = string.Empty;
            SetReviewBarOpen(false);
            _dismissedReviewNotices.Clear();
            ReviewStatus = string.Empty;
            return;
        }

        IsReviewPanelVisible = _workspace.Active?.Id == review.DocumentId;

        string count = review.Count == 1 ? "1 comment" : $"{review.Count} comments";

        ReviewStatus = review.IsShared && review.SharedUtc is { } shared
            ? $"{count} · Shared {shared.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)}"
            : review.SharedUtc is not null
                ? $"{count} · Changed since the last share"
                : $"{count} · Not shared yet";

        string notice = HasSharedReviewFile && review.IsShared
            ? $"Review saved: {Path.GetFileName(LastReviewPath)}"
            : document.External == ExternalState.Changed
                ? $"{document.DisplayName} changed on disk. Your comments refer to the version you're reading."
                : document.External == ExternalState.Missing
                    ? $"{document.DisplayName} was deleted on disk. Your comments refer to the version you're reading."
                    : $"Commenting on {document.DisplayName}. The source is locked so your comments match what you read.";

        ReviewNotice = notice;
        SetReviewBarOpen(IsReviewPanelVisible && !_dismissedReviewNotices.Contains(notice));
    }

    private void SetReviewBarOpen(bool open)
    {
        _refreshingReviewBar = true;

        try
        {
            IsReviewBarOpen = open;
        }
        finally
        {
            _refreshingReviewBar = false;
        }
    }

    /// <summary>What a read-only refusal calls the document: a review is not the reader's mark.</summary>
    private static string ReadOnlyWord(MarkdownDocument? document) =>
        document is { IsUnderReview: true } ? "Commenting" : "Read-only";

    // ------------------------------------------------------------------- sharing

    /// <summary>The CriticMarkup copy: the reviewed source with every comment written beside its passage.</summary>
    private static string ReviewMarkdown(ReviewSession review) =>
        CriticMarkupWriter.Write(
            review.SourceText,
            [.. review.Ordered.Select(c => new CriticMarkupWriter.Note(c.Anchor.Line, c.Anchor.Start, c.Anchor.Quote, c.Note))]);

    /// <summary>
    /// Whether there is a saved comment to send. Share and Copy stand grayed until there is -
    /// offered with nothing to send, the first press could only say so.
    /// </summary>
    private bool CanShareReview() => _review is { Count: > 0 };

    [RelayCommand(CanExecute = nameof(CanShareReview))]
    private async Task ShareReviewAsync()
    {
        if (_review is not { } review || _host is null)
        {
            return;
        }

        if (_workspace.Find(review.DocumentId) is not { } document)
        {
            return;
        }

        // The page is built from the live preview, and there is only the one on screen.
        if (_workspace.Active?.Id != review.DocumentId)
        {
            _workspace.Activate(review.DocumentId);
            ShowHighlightedStatus($"Share the review from {document.DisplayName}'s tab", ReviewGlyph);
            return;
        }

        if (RefuseWhileWriting())
        {
            return;
        }

        if (review.Count == 0)
        {
            StatusText = "No comments to share yet";
            return;
        }

        string? path = await _fileDialogs.PickReviewFileAsync(ReviewPage.FileName(document.DisplayName)).ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(path))
        {
            RestoreDocumentFocusAfterChrome();
            return;
        }

        IReadOnlyList<ReviewComment> ordered = review.Ordered;
        ReviewNote[] notes = [.. ordered.Select((c, i) => new ReviewNote(c.Id, i + 1, CommentMarkup.ToHtml(c.Note)))];

        try
        {
            IsBusy = true;

            string body = await _host.GetReviewHtmlAsync(review.DocumentId, notes).ConfigureAwait(true);

            if (string.IsNullOrWhiteSpace(body))
            {
                await _dialogs.ShowMessageAsync(
                    "Could not share the review",
                    "The preview did not hand over the page. Nothing was written; try Share Review again.")
                    .ConfigureAwait(true);
                return;
            }

            string markdown = ReviewMarkdown(review);
            string reviewed = DateTimeOffset.Now.ToString("MMM d, yyyy h:mm tt", CultureInfo.GetCultureInfo("en-US"));
            string stamp = ReviewPage.Stamp(document.DisplayName, reviewed, ReviewPage.ShortHash(review.SourceText), ordered.Count);

            IReadOnlyList<string> skipped = await ReviewPageWriter.WriteAsync(
                _packager,
                path,
                $"{Path.GetFileNameWithoutExtension(document.DisplayName)} — Review",
                body,
                document.Path,
                _settings.Current.PreviewMaxWidth,
                stamp,
                ordered.Count,
                markdown).ConfigureAwait(true);

            review.MarkShared(DateTimeOffset.Now);

            LastReviewPath = path;
            HasSharedReviewFile = true;
            IsEndWarningVisible = false;
            RefreshReviewState();

            _logger.LogInformation("Shared a review of {Document} with {Count} comments to {Path}.", document.DisplayPath, ordered.Count, path);

            ShowHighlightedStatus(
                skipped.Count == 0
                    ? $"Review saved to {Path.GetFileName(path)}"
                    : $"Review saved to {Path.GetFileName(path)} — {skipped.Count} image(s) could not be included",
                ReviewGlyph);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not write the review to {Path}.", path);

            await _dialogs.ShowMessageAsync("Could not share the review", ex.Message).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
            RestoreDocumentFocusAfterChrome();
        }
    }

    /// <summary>
    /// The reviewed source with the comments written in, for pasting into an AI or a message.
    /// Counts as sharing: the reader has taken the comments somewhere, which is what End's
    /// warning is there to make sure of.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanShareReview))]
    private void CopyReviewMarkdown()
    {
        if (_review is not { } review)
        {
            return;
        }

        if (RefuseWhileWriting())
        {
            return;
        }

        if (review.Count == 0)
        {
            StatusText = "No comments to copy yet";
            return;
        }

        if (!ClipboardText.Set(ReviewMarkdown(review), _logger))
        {
            StatusText = "Could not copy — the clipboard is in use";
            return;
        }

        review.MarkShared(DateTimeOffset.Now);
        IsEndWarningVisible = false;
        RefreshReviewState();

        StatusText = review.Count == 1
            ? "Copied 1 comment as Markdown"
            : $"Copied {review.Count} comments as Markdown";
    }

    [RelayCommand]
    private void ShowReviewInFolder()
    {
        if (!string.IsNullOrEmpty(LastReviewPath))
        {
            RevealInExplorer(LastReviewPath);
        }
    }

    [RelayCommand]
    private async Task CopyReviewFileAsync()
    {
        if (string.IsNullOrEmpty(LastReviewPath))
        {
            return;
        }

        StatusText = await ClipboardFile.SetAsync(LastReviewPath, _logger).ConfigureAwait(true)
            ? $"Copied {Path.GetFileName(LastReviewPath)} — paste it into a message to attach it"
            : "Could not copy the file";
    }

    /// <summary>
    /// Puts the review back on a shell that restarted - a WebView that crashed and came back has
    /// lost every mark it drew.
    /// </summary>
    private async Task RestoreReviewAsync()
    {
        if (_review is not null)
        {
            await PushReviewAsync().ConfigureAwait(true);
        }
    }
}
