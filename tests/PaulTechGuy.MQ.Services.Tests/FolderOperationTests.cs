// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Services.Tests;

/// <summary>
/// WP-3: renaming and deleting from the folder panel, with documents open.
///
/// Built like the disk-watch tests: real files in a scratch folder, and watchers driven by hand,
/// so a callback can be made to land at exactly the moment that used to go wrong (C4).
/// </summary>
public sealed class FolderOperationTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeFileWatcherFactory _watchers = new();
    private readonly FakeDocumentLocks _locks = new();
    private readonly FakeDocumentPins _pins = new();
    private readonly FakeRecycleBin _bin = new();
    private readonly DocumentWorkspace _workspace;
    private readonly List<WorkspaceChangedEventArgs> _changes = [];

    public FolderOperationTests()
    {
        _workspace = new DocumentWorkspace(
            _watchers,
            new FakeSettingsService(),
            _locks,
            _pins,
            _bin,
            NullLogger<DocumentWorkspace>.Instance);

        _workspace.Changed += (_, e) => _changes.Add(e);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _folder.Dispose();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Full(string relative) => Path.Combine(_folder.Path, relative);

    private string Write(string relative, string text = "# Notes\n")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Full(relative))!);
        return _folder.Write(relative, text);
    }

    private async Task<MarkdownDocument> OpenAsync(string relative, string text = "# Notes\n") =>
        await _workspace.OpenAsync(Write(relative, text), Token);

    private MarkdownDocument Current(Guid id) => _workspace.Find(id)!;

    // --------------------------------------------------------------- renaming

    [Fact]
    public async Task A_clean_open_file_follows_its_rename()
    {
        MarkdownDocument document = await OpenAsync("setup.md");
        _changes.Clear();

        FileOperationResult result = await _workspace.RenameAsync(document.Path!, Full("install.md"), Token);

        result.Succeeded.ShouldBeTrue();
        MarkdownDocument renamed = Current(document.Id);
        renamed.Path.ShouldBe(Full("install.md"));
        renamed.External.ShouldBe(ExternalState.InSync);
        renamed.IsDirty.ShouldBeFalse();
        File.Exists(Full("install.md")).ShouldBeTrue();
        File.Exists(Full("setup.md")).ShouldBeFalse();
        _changes.ShouldHaveSingleItem().Change.ShouldBe(WorkspaceChange.Renamed);
        _watchers.For(Full("install.md")).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_dirty_file_keeps_its_edits_and_saves_to_the_new_name()
    {
        MarkdownDocument document = await OpenAsync("setup.md");
        _workspace.ApplyEdit(document.Id, "# Edited\n");

        await _workspace.RenameAsync(document.Path!, Full("install.md"), Token);

        Current(document.Id).Text.ShouldBe("# Edited\n");
        Current(document.Id).IsDirty.ShouldBeTrue();

        (await _workspace.SaveAsync(document.Id, Token)).ShouldBeTrue();
        File.ReadAllText(Full("install.md")).ShouldBe("# Edited\n");
        File.Exists(Full("setup.md")).ShouldBeFalse();
    }

    [Fact]
    public async Task A_marked_file_is_still_marked_and_the_store_holds_only_the_new_name()
    {
        MarkdownDocument document = await OpenAsync("spec.md");
        await _workspace.SetLockedAsync(document.Id, true, Token);

        await _workspace.RenameAsync(document.Path!, Full("spec-v2.md"), Token);

        Current(document.Id).IsLocked.ShouldBeTrue();
        _locks.Stored.ShouldBe([Full("spec-v2.md")], ignoreOrder: true);
    }

    [Fact]
    public async Task A_pinned_file_is_still_pinned_under_its_new_name()
    {
        MarkdownDocument document = await OpenAsync("todo.md");
        await _workspace.SetPinnedAsync(document.Id, true, Token);

        await _workspace.RenameAsync(document.Path!, Full("tasks.md"), Token);

        Current(document.Id).IsPinned.ShouldBeTrue();
        _pins.Stored.ShouldBe([Full("tasks.md")], ignoreOrder: true);
    }

    [Fact]
    public async Task A_watcher_report_at_any_point_of_the_rename_leaves_the_document_in_sync()
    {
        MarkdownDocument document = await OpenAsync("setup.md");
        string from = document.Path!;
        string to = Full("install.md");
        FakeFileWatcher before = _watchers.For(from)!;

        // Before the move: the file is there, so a "gone" report is stale.
        before.RaiseRemoved(from);
        Current(document.Id).External.ShouldBe(ExternalState.InSync);

        await _workspace.RenameAsync(from, to, Token);

        // From the retired watcher, as a callback that read the old path before the stop and
        // lands after the move, or after the re-point: dropped.
        before.RaiseRemoved(from);
        before.RaiseChanged(from);
        Current(document.Id).External.ShouldBe(ExternalState.InSync);

        // From the new watcher, about the new path, which is there.
        _watchers.For(to)!.RaiseRemoved(to);
        Current(document.Id).External.ShouldBe(ExternalState.InSync);
        Current(document.Id).Path.ShouldBe(to);
    }

    [Fact]
    public async Task A_folder_rename_carries_every_open_document_and_a_mark_on_an_unopened_file()
    {
        MarkdownDocument a = await OpenAsync(@"guide\a.md");
        MarkdownDocument b = await OpenAsync(@"guide\deep\b.md");
        string unopened = Write(@"guide\c.md");
        await _locks.SetAsync(unopened, true, Token);
        MarkdownDocument outside = await OpenAsync("other.md");

        FileOperationResult result = await _workspace.RenameAsync(Full("guide"), Full("manual"), Token);

        result.Succeeded.ShouldBeTrue();
        Current(a.Id).Path.ShouldBe(Full(@"manual\a.md"));
        Current(b.Id).Path.ShouldBe(Full(@"manual\deep\b.md"));
        Current(outside.Id).Path.ShouldBe(Full("other.md"));
        _locks.IsLocked(Full(@"manual\c.md")).ShouldBeTrue();
        _locks.IsLocked(unopened).ShouldBeFalse();
        _changes.Count(c => c.Change == WorkspaceChange.Renamed).ShouldBe(2);
    }

    [Fact]
    public async Task An_untitled_documents_save_folder_follows_a_folder_rename()
    {
        Directory.CreateDirectory(Full(@"guide\deep"));
        MarkdownDocument untitled = _workspace.CreateUntitled(saveFolder: Full(@"guide\deep"));

        await _workspace.RenameAsync(Full("guide"), Full("manual"), Token);

        Current(untitled.Id).SaveFolder.ShouldBe(Full(@"manual\deep"));
    }

    [Fact]
    public async Task A_name_already_taken_is_refused_and_nothing_moves()
    {
        MarkdownDocument document = await OpenAsync("a.md");
        Write("b.md", "other");

        FileOperationResult result = await _workspace.RenameAsync(document.Path!, Full("b.md"), Token);

        result.Succeeded.ShouldBeFalse();
        result.Reason.ShouldNotBeNullOrWhiteSpace();
        Current(document.Id).Path.ShouldBe(Full("a.md"));
        File.ReadAllText(Full("b.md")).ShouldBe("other");
        _watchers.For(Full("a.md"))!.IsDisposed.ShouldBeFalse();
    }

    [Fact]
    public async Task A_rename_windows_refuses_leaves_everything_and_watches_again()
    {
        MarkdownDocument document = await OpenAsync("held.md");

        FileOperationResult result;

        using (new FileStream(document.Path!, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = await _workspace.RenameAsync(document.Path!, Full("moved.md"), Token);
        }

        result.Succeeded.ShouldBeFalse();
        result.Reason.ShouldNotBeNullOrWhiteSpace();
        Current(document.Id).Path.ShouldBe(Full("held.md"));
        File.Exists(Full("held.md")).ShouldBeTrue();

        FakeFileWatcher? watching = _watchers.For(Full("held.md"));
        watching.ShouldNotBeNull();
        watching.IsDisposed.ShouldBeFalse();
    }

    [Fact]
    public async Task A_case_only_rename_works_for_a_file_and_a_folder()
    {
        MarkdownDocument file = await OpenAsync(@"guide\readme.md");

        (await _workspace.RenameAsync(file.Path!, Full(@"guide\README.md"), Token)).Succeeded.ShouldBeTrue();
        Current(file.Id).Path.ShouldBe(Full(@"guide\README.md"));

        (await _workspace.RenameAsync(Full("guide"), Full("Guide"), Token)).Succeeded.ShouldBeTrue();
        Current(file.Id).Path.ShouldBe(Full(@"Guide\README.md"));
        Directory.GetDirectories(_folder.Path).Select(Path.GetFileName).ShouldContain("Guide");
    }

    [Fact]
    public async Task Renaming_back_puts_the_tab_mark_and_pin_where_they_started()
    {
        MarkdownDocument document = await OpenAsync("a.md");
        await _workspace.SetLockedAsync(document.Id, true, Token);
        await _workspace.SetPinnedAsync(document.Id, true, Token);

        await _workspace.RenameAsync(Full("a.md"), Full("b.md"), Token);
        await _workspace.RenameAsync(Full("b.md"), Full("a.md"), Token);

        MarkdownDocument back = Current(document.Id);
        back.Path.ShouldBe(Full("a.md"));
        back.IsLocked.ShouldBeTrue();
        back.IsPinned.ShouldBeTrue();
        _locks.Stored.ShouldBe([Full("a.md")], ignoreOrder: true);
        _pins.Stored.ShouldBe([Full("a.md")], ignoreOrder: true);
    }

    [Fact]
    public async Task A_document_whose_file_is_missing_cannot_be_renamed()
    {
        MarkdownDocument document = await OpenAsync("gone.md");
        File.Delete(document.Path!);
        _watchers.For(document.Path!)!.RaiseRemoved();

        FileOperationResult result = await _workspace.RenameAsync(document.Path!, Full("back.md"), Token);

        result.Succeeded.ShouldBeFalse();
        Current(document.Id).External.ShouldBe(ExternalState.Missing);
    }

    // ---------------------------------------------------------------- deleting

    [Fact]
    public async Task Deleting_an_open_dirty_file_closes_its_tab_without_asking()
    {
        MarkdownDocument document = await OpenAsync("draft.md");
        _workspace.ApplyEdit(document.Id, "# Unsaved\n");
        await _workspace.SetPinnedAsync(document.Id, true, Token);
        _changes.Clear();

        FileOperationResult result = await _workspace.DeleteAsync(document.Path!, Token);

        result.Succeeded.ShouldBeTrue();
        result.Permanent.ShouldBeFalse();
        _workspace.Find(document.Id).ShouldBeNull();
        File.Exists(Full("draft.md")).ShouldBeFalse();
        _changes.ShouldContain(c => c.Change == WorkspaceChange.Closed && c.DocumentId == document.Id);
        _pins.Stored.ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleting_a_folder_closes_every_document_in_it()
    {
        MarkdownDocument a = await OpenAsync(@"old\a.md");
        MarkdownDocument b = await OpenAsync(@"old\sub\b.md");
        MarkdownDocument keep = await OpenAsync("keep.md");

        (await _workspace.DeleteAsync(Full("old"), Token)).Succeeded.ShouldBeTrue();

        _workspace.Find(a.Id).ShouldBeNull();
        _workspace.Find(b.Id).ShouldBeNull();
        _workspace.Find(keep.Id).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_failed_delete_keeps_the_tab_and_its_watch()
    {
        MarkdownDocument document = await OpenAsync("stays.md");
        _bin.FailWith = "The Recycle Bin refused.";

        FileOperationResult result = await _workspace.DeleteAsync(document.Path!, Token);

        result.Succeeded.ShouldBeFalse();
        result.Reason.ShouldBe("The Recycle Bin refused.");
        _workspace.Find(document.Id).ShouldNotBeNull();
        _watchers.For(document.Path!)!.IsDisposed.ShouldBeFalse();
    }

    [Fact]
    public async Task A_marked_document_or_a_folder_holding_one_is_not_deleted()
    {
        MarkdownDocument marked = await OpenAsync(@"ref\spec.md");
        await _workspace.SetLockedAsync(marked.Id, true, Token);
        string unopened = Write(@"other\guard.md");
        await _locks.SetAsync(unopened, true, Token);

        (await _workspace.DeleteAsync(marked.Path!, Token)).Succeeded.ShouldBeFalse();
        (await _workspace.DeleteAsync(Full("ref"), Token)).Succeeded.ShouldBeFalse();
        (await _workspace.DeleteAsync(Full("other"), Token)).Succeeded.ShouldBeFalse();

        File.Exists(marked.Path!).ShouldBeTrue();
        File.Exists(unopened).ShouldBeTrue();
        _bin.Recycled.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_document_under_review_is_not_deleted()
    {
        MarkdownDocument document = await OpenAsync("reviewed.md");
        _workspace.SetUnderReview(document.Id, true);

        FileOperationResult result = await _workspace.DeleteAsync(document.Path!, Token);

        result.Succeeded.ShouldBeFalse();
        _workspace.Find(document.Id).ShouldNotBeNull();
    }

    [Fact]
    public async Task An_autosave_arriving_during_a_delete_writes_nothing()
    {
        MarkdownDocument document = await OpenAsync("draft.md");
        _workspace.ApplyEdit(document.Id, "# Unsaved\n");
        _bin.PauseNext();

        Task<FileOperationResult> delete = _workspace.DeleteAsync(document.Path!, Token);
        await _bin.Entered.Task.WaitAsync(Token);

        (await _workspace.SaveAsync(document.Id, Token)).ShouldBeFalse();

        _bin.Release();
        (await delete).Succeeded.ShouldBeTrue();
        File.Exists(Full("draft.md")).ShouldBeFalse();
    }

    [Fact]
    public async Task A_delete_arriving_during_a_save_waits_for_it()
    {
        MarkdownDocument document = await OpenAsync("draft.md");
        _workspace.ApplyEdit(document.Id, new string('x', 2_000_000));

        Task<bool> save = _workspace.SaveAsync(document.Id, Token);
        Task<FileOperationResult> delete = _workspace.DeleteAsync(document.Path!, Token);

        (await save).ShouldBeTrue();
        (await delete).Succeeded.ShouldBeTrue();

        // Had the delete not waited, the save's write could have landed after it and put the
        // file back.
        File.Exists(Full("draft.md")).ShouldBeFalse();
    }
}
