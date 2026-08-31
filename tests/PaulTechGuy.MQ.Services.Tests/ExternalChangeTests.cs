// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Services.Tests;

/// <summary>
/// The decision matrix for a file that changes underneath an open document.
///
/// The workspace takes its watcher through a factory, so every branch here is reachable
/// without waiting on a real FileSystemWatcher: the files are real, the notifications are not.
/// </summary>
public sealed class ExternalChangeTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeFileWatcherFactory _watchers = new();
    private readonly FakeSettingsService _settings;
    private readonly DocumentWorkspace _workspace;

    private readonly List<WorkspaceChangedEventArgs> _changes = [];

    public ExternalChangeTests()
    {
        _settings = new FakeSettingsService();
        _workspace = new DocumentWorkspace(_watchers, _settings, NullLogger<DocumentWorkspace>.Instance);
        _workspace.Changed += (_, e) => _changes.Add(e);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _folder.Dispose();
    }

    // ------------------------------------------------------------------ helpers

    private async Task<(MarkdownDocument Document, string Path, FakeFileWatcher Watcher)> OpenAsync(
        string name = "notes.md",
        string text = "# Notes\n")
    {
        string path = _folder.Write(name, text);
        MarkdownDocument document = await _workspace.OpenAsync(path, TestContext.Current.CancellationToken);

        return (document, path, _watchers.For(path)!);
    }

    private MarkdownDocument Current(Guid id) => _workspace.Find(id)!;

    private IReadOnlyList<WorkspaceChange> ChangesFor(Guid id) =>
        [.. _changes.Where(c => c.DocumentId == id).Select(c => c.Change)];

    /// <summary>
    /// Waits for an automatic reload to land.
    ///
    /// The workspace starts one and does not wait for it - a watcher notification is not
    /// something anybody awaits - so a test that asserts immediately is racing it. Asserting
    /// straight after the notification passed on an idle machine and failed under load, which
    /// is the worst way for this to be wrong.
    /// </summary>
    private static async Task WaitForAsync(Func<bool> condition, string what)
    {
        for (int attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        condition().ShouldBeTrue($"Timed out waiting for {what}.");
    }

    // ------------------------------------------------------------------- quiet

    [Fact]
    public async Task A_rewrite_with_identical_content_says_nothing()
    {
        (MarkdownDocument document, string path, FakeFileWatcher watcher) = await OpenAsync();
        _changes.Clear();

        // What a save from another editor that changed nothing looks like: the timestamp
        // moves, the bytes do not.
        TempFolder.Rewrite(path, "# Notes\n");
        watcher.RaiseChanged();

        Current(document.Id).External.ShouldBe(ExternalState.InSync);
        ChangesFor(document.Id).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_touch_that_moves_only_the_timestamp_says_nothing()
    {
        (MarkdownDocument document, string path, FakeFileWatcher watcher) = await OpenAsync();
        _changes.Clear();

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(30));
        watcher.RaiseChanged();

        Current(document.Id).External.ShouldBe(ExternalState.InSync);
        ChangesFor(document.Id).ShouldBeEmpty();
    }

    [Fact]
    public async Task Marqoras_own_save_is_not_reported_back()
    {
        (MarkdownDocument document, _, FakeFileWatcher watcher) = await OpenAsync();

        _workspace.ApplyEdit(document.Id, "# Notes\nmine\n");
        await _workspace.SaveAsync(document.Id, TestContext.Current.CancellationToken);
        _changes.Clear();

        watcher.RaiseChanged();

        Current(document.Id).External.ShouldBe(ExternalState.InSync);
        ChangesFor(document.Id).ShouldBeEmpty();
    }

    // ----------------------------------------------------------------- reload

    [Fact]
    public async Task A_clean_document_takes_the_new_content_by_itself()
    {
        (MarkdownDocument document, string path, FakeFileWatcher watcher) = await OpenAsync();
        _changes.Clear();

        TempFolder.Rewrite(path, "# Notes\nfrom elsewhere\n");
        watcher.RaiseChanged();

        await WaitForAsync(
            () => ChangesFor(document.Id).Contains(WorkspaceChange.ReloadedFromDisk),
            "the automatic reload");

        MarkdownDocument current = Current(document.Id);

        current.Text.ShouldBe("# Notes\nfrom elsewhere\n");
        current.External.ShouldBe(ExternalState.InSync);
        current.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public async Task A_clean_document_waits_to_be_asked_when_automatic_reload_is_off()
    {
        _settings.Update(s => s with { ReloadOnExternalChange = false });

        (MarkdownDocument document, string path, FakeFileWatcher watcher) = await OpenAsync();
        _changes.Clear();

        TempFolder.Rewrite(path, "# Notes\nfrom elsewhere\n");
        watcher.RaiseChanged();

        MarkdownDocument current = Current(document.Id);

        current.Text.ShouldBe("# Notes\n");
        current.External.ShouldBe(ExternalState.Changed);
        ChangesFor(document.Id).ShouldContain(WorkspaceChange.ExternalStateChanged);
    }

    // ------------------------------------------------------------------ dirty

    [Fact]
    public async Task Unsaved_edits_are_never_overwritten_without_asking()
    {
        (MarkdownDocument document, string path, FakeFileWatcher watcher) = await OpenAsync();

        _workspace.ApplyEdit(document.Id, "# Notes\nmine\n");
        _changes.Clear();

        TempFolder.Rewrite(path, "# Notes\ntheirs\n");
        watcher.RaiseChanged();

        MarkdownDocument current = Current(document.Id);

        current.Text.ShouldBe("# Notes\nmine\n");
        current.External.ShouldBe(ExternalState.Changed);
    }

    [Fact]
    public async Task Keeping_mine_clears_the_marker_and_leaves_the_text_alone()
    {
        (MarkdownDocument document, string path, FakeFileWatcher watcher) = await OpenAsync();

        _workspace.ApplyEdit(document.Id, "# Notes\nmine\n");
        TempFolder.Rewrite(path, "# Notes\ntheirs\n");
        watcher.RaiseChanged();

        _workspace.ResolveExternalChange(document.Id);

        MarkdownDocument current = Current(document.Id);

        current.External.ShouldBe(ExternalState.InSync);
        current.Text.ShouldBe("# Notes\nmine\n");
        current.IsDirty.ShouldBeTrue();
    }

    [Fact]
    public async Task Reloading_takes_the_new_content_and_clears_the_marker()
    {
        (MarkdownDocument document, string path, FakeFileWatcher watcher) = await OpenAsync();

        _workspace.ApplyEdit(document.Id, "# Notes\nmine\n");
        TempFolder.Rewrite(path, "# Notes\ntheirs\n");
        watcher.RaiseChanged();

        await _workspace.ReloadAsync(document.Id, TestContext.Current.CancellationToken);

        MarkdownDocument current = Current(document.Id);

        current.Text.ShouldBe("# Notes\ntheirs\n");
        current.External.ShouldBe(ExternalState.InSync);
        current.IsDirty.ShouldBeFalse();
    }

    // ---------------------------------------------------------------- deleted

    [Fact]
    public async Task A_deleted_file_keeps_its_tab_and_turns_the_buffer_unsaved()
    {
        (MarkdownDocument document, string path, FakeFileWatcher watcher) = await OpenAsync();

        File.Delete(path);
        watcher.RaiseRemoved();

        MarkdownDocument current = Current(document.Id);

        _workspace.Documents.ShouldContain(d => d.Id == document.Id);
        current.External.ShouldBe(ExternalState.Missing);
        current.Text.ShouldBe("# Notes\n");

        // The whole point: this is what puts Ctrl+S back within reach.
        current.IsDirty.ShouldBeTrue();
    }

    [Fact]
    public async Task Saving_a_deleted_file_writes_it_back_and_clears_the_marker()
    {
        (MarkdownDocument document, string path, FakeFileWatcher watcher) = await OpenAsync();

        File.Delete(path);
        watcher.RaiseRemoved();

        await _workspace.SaveAsync(document.Id, TestContext.Current.CancellationToken);

        MarkdownDocument current = Current(document.Id);

        File.Exists(path).ShouldBeTrue();
        File.ReadAllText(path).ShouldBe("# Notes\n");
        current.External.ShouldBe(ExternalState.InSync);
        current.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public async Task A_file_that_comes_back_different_asks_rather_than_staying_missing()
    {
        (MarkdownDocument document, string path, FakeFileWatcher watcher) = await OpenAsync();

        File.Delete(path);
        watcher.RaiseRemoved();

        // A branch switch back, or a stash pop.
        TempFolder.Rewrite(path, "# Notes\nrestored differently\n");
        watcher.RaiseChanged();

        Current(document.Id).External.ShouldBe(ExternalState.Changed);
    }

    [Fact]
    public async Task A_file_that_comes_back_unchanged_takes_its_marker_down()
    {
        (MarkdownDocument document, string path, FakeFileWatcher watcher) = await OpenAsync();

        File.Delete(path);
        watcher.RaiseRemoved();

        TempFolder.Rewrite(path, "# Notes\n");
        watcher.RaiseChanged();

        MarkdownDocument current = Current(document.Id);

        current.External.ShouldBe(ExternalState.InSync);
        current.IsDirty.ShouldBeFalse();
    }

    // ------------------------------------------------------------- housekeeping

    [Fact]
    public async Task An_untitled_document_is_watched_only_once_it_has_a_file()
    {
        MarkdownDocument document = _workspace.CreateUntitled("draft");

        _watchers.Created.ShouldBeEmpty();

        string path = Path.Combine(_folder.Path, "draft.md");
        await _workspace.SaveAsAsync(document.Id, path, TestContext.Current.CancellationToken);

        _watchers.For(path).ShouldNotBeNull();
        Current(document.Id).Stamp.ShouldNotBeNull();
    }

    [Fact]
    public async Task Closing_a_tab_disposes_its_watcher()
    {
        (MarkdownDocument document, _, FakeFileWatcher watcher) = await OpenAsync();

        _workspace.Close(document.Id);

        watcher.IsDisposed.ShouldBeTrue();
    }

    [Fact]
    public async Task Each_open_document_gets_its_own_watcher()
    {
        (MarkdownDocument first, string firstPath, _) = await OpenAsync("one.md", "one\n");
        (MarkdownDocument second, string secondPath, FakeFileWatcher secondWatcher) =
            await OpenAsync("two.md", "two\n");

        _watchers.Created.Count.ShouldBe(2);

        TempFolder.Rewrite(secondPath, "two, changed\n");
        secondWatcher.RaiseChanged();

        await WaitForAsync(
            () => Current(second.Id).Text == "two, changed\n",
            "the second document to reload");

        // The one that did not change is left entirely alone.
        Current(first.Id).Text.ShouldBe("one\n");

        firstPath.ShouldNotBe(secondPath);
    }

    // ------------------------------------------------- worth mentioning later

    /*
        A reload nobody asked for is the only kind the user can be surprised by, so it is the
        only kind the document remembers. These pin down which paths set that stamp and which
        clear it - the UI decides how to say it, but it can only say it about what is stamped
        here.
    */

    [Fact]
    public async Task An_automatic_reload_records_when_it_happened()
    {
        (MarkdownDocument document, string path, FakeFileWatcher watcher) = await OpenAsync();

        Current(document.Id).AutoReloadedUtc.ShouldBeNull();

        DateTimeOffset before = DateTimeOffset.UtcNow;

        TempFolder.Rewrite(path, "# Notes\nfrom elsewhere\n");
        watcher.RaiseChanged();

        await WaitForAsync(
            () => Current(document.Id).AutoReloadedUtc is not null,
            "the reload to be stamped");

        Current(document.Id).AutoReloadedUtc!.Value.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Fact]
    public async Task A_reload_the_user_asked_for_is_not_worth_mentioning()
    {
        (MarkdownDocument document, string path, _) = await OpenAsync();

        TempFolder.Rewrite(path, "# Notes\nfrom elsewhere\n");
        await _workspace.ReloadAsync(document.Id, TestContext.Current.CancellationToken);

        Current(document.Id).Text.ShouldBe("# Notes\nfrom elsewhere\n");
        Current(document.Id).AutoReloadedUtc.ShouldBeNull();
    }

    [Fact]
    public async Task Reloading_by_hand_clears_a_stamp_the_watcher_left()
    {
        (MarkdownDocument document, string path, FakeFileWatcher watcher) = await OpenAsync();

        TempFolder.Rewrite(path, "# Notes\nfrom elsewhere\n");
        watcher.RaiseChanged();

        await WaitForAsync(
            () => Current(document.Id).AutoReloadedUtc is not null,
            "the reload to be stamped");

        TempFolder.Rewrite(path, "# Notes\nlater still\n");
        await _workspace.ReloadAsync(document.Id, TestContext.Current.CancellationToken);

        Current(document.Id).AutoReloadedUtc.ShouldBeNull();
    }

    [Fact]
    public async Task Saving_clears_a_stamp_the_watcher_left()
    {
        (MarkdownDocument document, string path, FakeFileWatcher watcher) = await OpenAsync();

        TempFolder.Rewrite(path, "# Notes\nfrom elsewhere\n");
        watcher.RaiseChanged();

        await WaitForAsync(
            () => Current(document.Id).AutoReloadedUtc is not null,
            "the reload to be stamped");

        _workspace.ApplyEdit(document.Id, "# Notes\nmine now\n");
        await _workspace.SaveAsync(document.Id, TestContext.Current.CancellationToken);

        Current(document.Id).AutoReloadedUtc.ShouldBeNull();
    }

    [Fact]
    public async Task A_touch_that_changes_nothing_leaves_no_stamp()
    {
        (MarkdownDocument document, string path, FakeFileWatcher watcher) = await OpenAsync();

        // The same bytes with a newer timestamp: a scanner, or a tool restamping the file.
        // Announcing that would train the user to ignore the notice.
        TempFolder.Rewrite(path, "# Notes\n");
        watcher.RaiseChanged();

        Current(document.Id).AutoReloadedUtc.ShouldBeNull();
        ChangesFor(document.Id).ShouldNotContain(WorkspaceChange.ReloadedFromDisk);
    }
}
