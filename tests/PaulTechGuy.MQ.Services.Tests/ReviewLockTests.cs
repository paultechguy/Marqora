// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Services.Tests;

/// <summary>
/// A document under review holds still. Its comments are anchored in the text as it stood when
/// the review began, so nothing may move that text until the review ends - not an edit, not a
/// save, and not a reload, which is the one path that bypasses the read-only backstop.
/// </summary>
public sealed class ReviewLockTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeFileWatcherFactory _watchers = new();
    private readonly FakeSettingsService _settings = new();
    private readonly FakeDocumentLocks _locks = new();
    private readonly DocumentWorkspace _workspace;
    private readonly List<WorkspaceChangedEventArgs> _changes = [];

    public ReviewLockTests()
    {
        _workspace = new DocumentWorkspace(
            _watchers,
            _settings,
            _locks,
            new FakeDocumentPins(),
            NullLogger<DocumentWorkspace>.Instance);
        _workspace.Changed += (_, e) => _changes.Add(e);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _folder.Dispose();
    }

    private async Task<(MarkdownDocument Document, string Path, FakeFileWatcher Watcher)> OpenAsync(string text = "# Notes\n")
    {
        string path = _folder.Write("notes.md", text);
        MarkdownDocument document = await _workspace.OpenAsync(path, TestContext.Current.CancellationToken);

        return (document, path, _watchers.For(path)!);
    }

    private MarkdownDocument Current(Guid id) => _workspace.Find(id)!;

    private IReadOnlyList<WorkspaceChange> ChangesFor(Guid id) =>
        [.. _changes.Where(c => c.DocumentId == id).Select(c => c.Change)];

    private static async Task WaitForAsync(Func<bool> condition, string what)
    {
        for (int attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        condition().ShouldBeTrue($"Timed out waiting for {what}.");
    }

    [Fact]
    public async Task Starting_a_review_makes_the_document_read_only_and_says_so()
    {
        (MarkdownDocument document, _, _) = await OpenAsync();
        _changes.Clear();

        _workspace.SetUnderReview(document.Id, true).ShouldBeTrue();

        Current(document.Id).IsUnderReview.ShouldBeTrue();
        Current(document.Id).IsReadOnly.ShouldBeTrue();
        ChangesFor(document.Id).ShouldBe([WorkspaceChange.LockChanged]);
    }

    /// <summary>
    /// A review resumed from its page opens as a snapshot: named for the review, clean because
    /// the page still holds the text, and the untitled counter left where it was.
    /// </summary>
    [Fact]
    public void A_snapshot_is_clean_named_and_can_be_reviewed()
    {
        MarkdownDocument snapshot = _workspace.CreateSnapshot("# Plan\n", "notes.md (review)");
        MarkdownDocument untitled = _workspace.CreateUntitled();

        snapshot.IsUntitled.ShouldBeTrue();
        snapshot.DisplayName.ShouldBe("notes.md (review)");
        snapshot.Text.ShouldBe("# Plan\n");
        snapshot.IsDirty.ShouldBeFalse();
        untitled.DisplayName.ShouldBe("Untitled 1");

        _workspace.SetUnderReview(snapshot.Id, true).ShouldBeTrue();
        Current(snapshot.Id).IsReadOnly.ShouldBeTrue();
    }

    [Fact]
    public async Task An_edit_is_refused_during_a_review()
    {
        (MarkdownDocument document, _, _) = await OpenAsync();
        _workspace.SetUnderReview(document.Id, true);

        _workspace.ApplyEdit(document.Id, "# Changed\n").ShouldBeFalse();

        Current(document.Id).Text.ShouldBe("# Notes\n");
    }

    /// <summary>The review is Marqora's own business in memory; the persisted mark is untouched.</summary>
    [Fact]
    public async Task A_review_does_not_mark_the_file()
    {
        (MarkdownDocument document, string path, _) = await OpenAsync();

        _workspace.SetUnderReview(document.Id, true);

        Current(document.Id).IsLocked.ShouldBeFalse();
        _locks.IsLocked(path).ShouldBeFalse();
    }

    /// <summary>A reload would move the text under the comments, so it is refused and says so.</summary>
    [Fact]
    public async Task A_reload_is_refused_during_a_review()
    {
        (MarkdownDocument document, string path, _) = await OpenAsync();
        _workspace.SetUnderReview(document.Id, true);

        File.WriteAllText(path, "# From elsewhere\n");

        (await _workspace.ReloadAsync(document.Id, TestContext.Current.CancellationToken)).ShouldBeFalse();

        Current(document.Id).Text.ShouldBe("# Notes\n");
    }

    [Fact]
    public async Task A_reload_answers_true_when_it_happens()
    {
        (MarkdownDocument document, string path, _) = await OpenAsync();

        File.WriteAllText(path, "# From elsewhere\n");

        (await _workspace.ReloadAsync(document.Id, TestContext.Current.CancellationToken)).ShouldBeTrue();
        Current(document.Id).Text.ShouldBe("# From elsewhere\n");
    }

    /// <summary>
    /// The automatic reload a clean document would normally take is held back, and the change is
    /// recorded instead, so the reviewer can be told the file moved.
    /// </summary>
    [Fact]
    public async Task An_external_change_is_recorded_rather_than_taken_during_a_review()
    {
        (MarkdownDocument document, string path, FakeFileWatcher watcher) = await OpenAsync();
        _workspace.SetUnderReview(document.Id, true);

        TempFolder.Rewrite(path, "# From elsewhere\n");
        watcher.RaiseChanged();

        MarkdownDocument current = Current(document.Id);

        current.Text.ShouldBe("# Notes\n");
        current.External.ShouldBe(ExternalState.Changed);
    }

    /// <summary>Ending the review settles the held change the way the watcher would have.</summary>
    [Fact]
    public async Task Ending_the_review_takes_a_held_change_when_automatic_reload_is_on()
    {
        (MarkdownDocument document, string path, FakeFileWatcher watcher) = await OpenAsync();
        _workspace.SetUnderReview(document.Id, true);

        TempFolder.Rewrite(path, "# From elsewhere\n");
        watcher.RaiseChanged();

        _workspace.SetUnderReview(document.Id, false);

        await WaitForAsync(() => Current(document.Id).Text == "# From elsewhere\n", "the deferred reload");

        Current(document.Id).External.ShouldBe(ExternalState.InSync);
        Current(document.Id).IsReadOnly.ShouldBeFalse();
    }

    /// <summary>
    /// Ending a review and closing its tab together - what the close prompt does - starts the
    /// deferred reload and then takes the document away while the file is being read. The
    /// reload must not land on whichever document took its place in the list.
    /// </summary>
    [Fact]
    public async Task A_deferred_reload_never_lands_on_another_document()
    {
        (MarkdownDocument reviewed, string path, FakeFileWatcher watcher) = await OpenAsync("# Reviewed\n");

        string otherPath = _folder.Write("other.md", "# Other\n");
        MarkdownDocument other = await _workspace.OpenAsync(otherPath, TestContext.Current.CancellationToken);
        _workspace.ApplyEdit(other.Id, "# Other\nunsaved work\n");

        _workspace.SetUnderReview(reviewed.Id, true);

        // Large, so the read is still going when the tab closes. A small file is read before
        // the close runs and the race never opens - which is also why it went unnoticed.
        TempFolder.Rewrite(path, "# Reviewed, changed elsewhere\n" + new string('x', 8_000_000));
        watcher.RaiseChanged();
        Current(reviewed.Id).External.ShouldBe(ExternalState.Changed);

        _workspace.SetUnderReview(reviewed.Id, false);
        _workspace.Close(reviewed.Id);

        // Long enough for the read to finish; the assertion is that nothing happened.
        await Task.Delay(1500, TestContext.Current.CancellationToken);

        MarkdownDocument survivor = Current(other.Id);

        survivor.Text.ShouldBe("# Other\nunsaved work\n");
        survivor.IsDirty.ShouldBeTrue();
    }

    [Fact]
    public async Task Ending_the_review_leaves_a_held_change_standing_when_automatic_reload_is_off()
    {
        _settings.Update(s => s with { ReloadOnExternalChange = false });

        (MarkdownDocument document, string path, FakeFileWatcher watcher) = await OpenAsync();
        _workspace.SetUnderReview(document.Id, true);

        TempFolder.Rewrite(path, "# From elsewhere\n");
        watcher.RaiseChanged();

        _workspace.SetUnderReview(document.Id, false);

        Current(document.Id).Text.ShouldBe("# Notes\n");
        Current(document.Id).External.ShouldBe(ExternalState.Changed);
    }

    [Fact]
    public async Task Ending_the_review_hands_editing_back()
    {
        (MarkdownDocument document, _, _) = await OpenAsync();
        _workspace.SetUnderReview(document.Id, true);

        _workspace.SetUnderReview(document.Id, false).ShouldBeTrue();

        _workspace.ApplyEdit(document.Id, "# Changed\n").ShouldBeTrue();
        Current(document.Id).IsReadOnly.ShouldBeFalse();
    }

    /// <summary>An untitled document - pasted AI output, most often - can be reviewed too.</summary>
    [Fact]
    public void An_untitled_document_can_be_put_under_review()
    {
        MarkdownDocument untitled = _workspace.CreateUntitled("# Pasted\n");

        _workspace.SetUnderReview(untitled.Id, true).ShouldBeTrue();

        _workspace.ApplyEdit(untitled.Id, "# Other\n").ShouldBeFalse();
    }

    /// <summary>A review holds even over a deleted file, where a mark would stand down.</summary>
    [Fact]
    public async Task A_review_holds_over_a_missing_file()
    {
        (MarkdownDocument document, _, FakeFileWatcher watcher) = await OpenAsync();
        _workspace.SetUnderReview(document.Id, true);

        watcher.RaiseRemoved();

        Current(document.Id).External.ShouldBe(ExternalState.Missing);
        Current(document.Id).IsReadOnly.ShouldBeTrue();
    }

    [Fact]
    public void An_unknown_document_cannot_be_put_under_review() =>
        _workspace.SetUnderReview(Guid.NewGuid(), true).ShouldBeFalse();
}
