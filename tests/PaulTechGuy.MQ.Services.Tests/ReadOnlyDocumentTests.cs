// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Services.Tests;

/// <summary>
/// What a document marked read-only refuses, and the narrower set of things it still takes.
///
/// The files are real, because the promise being tested is about a file on disk staying as it
/// was. Only the marks are stood in for: which paths carry one is the store's question, and
/// the workspace's question is what to do about it.
/// </summary>
public sealed class ReadOnlyDocumentTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeFileWatcherFactory _watchers = new();
    private readonly FakeDocumentLocks _locks = new();
    private readonly DocumentWorkspace _workspace;

    public ReadOnlyDocumentTests() =>
        _workspace = new DocumentWorkspace(
            _watchers,
            new FakeSettingsService(),
            _locks,
            new FakeDocumentPins(),
            NullLogger<DocumentWorkspace>.Instance);

    public void Dispose()
    {
        _workspace.Dispose();
        _folder.Dispose();
    }

    private async Task<(MarkdownDocument Document, string Path)> OpenAsync(
        string name = "notes.md",
        string text = "# Notes\n",
        bool marked = false)
    {
        string path = _folder.Write(name, text);

        if (marked)
        {
            await _locks.SetAsync(path, true, TestContext.Current.CancellationToken);
        }

        MarkdownDocument document = await _workspace.OpenAsync(path, TestContext.Current.CancellationToken);

        return (document, path);
    }

    private MarkdownDocument Current(Guid id) => _workspace.Find(id)!;

    // ------------------------------------------------------------------ opening

    [Fact]
    public async Task A_document_whose_path_carries_a_mark_opens_marked()
    {
        (MarkdownDocument document, _) = await OpenAsync(marked: true);

        document.IsLocked.ShouldBeTrue();
        document.IsReadOnly.ShouldBeTrue();
    }

    [Fact]
    public async Task An_ordinary_document_opens_writable()
    {
        (MarkdownDocument document, _) = await OpenAsync();

        document.IsLocked.ShouldBeFalse();
        document.IsReadOnly.ShouldBeFalse();
    }

    /// <summary>The mark is keyed by path, so the same file opened again is still marked.</summary>
    [Fact]
    public async Task A_mark_survives_the_document_being_closed_and_opened_again()
    {
        (MarkdownDocument first, string path) = await OpenAsync(marked: true);

        _workspace.Close(first.Id);

        MarkdownDocument again = await _workspace.OpenAsync(path, TestContext.Current.CancellationToken);

        again.Id.ShouldNotBe(first.Id);
        again.IsLocked.ShouldBeTrue();
    }

    // ------------------------------------------------------------------- saving

    /// <summary>
    /// The whole promise, stated against the bytes on disk rather than a flag.
    ///
    /// Edited and then marked, which is both the realistic order and the only one that tests
    /// anything: a marked document that is clean has no unsaved text for a save to write, so a
    /// refusal there would pass whether the guard existed or not.
    /// </summary>
    [Fact]
    public async Task A_marked_document_is_not_written()
    {
        (MarkdownDocument document, string path) = await OpenAsync(text: "# Original\n");

        _workspace.ApplyEdit(document.Id, "# Rewritten\n").ShouldBeTrue();
        await _workspace.SetLockedAsync(document.Id, true, TestContext.Current.CancellationToken);

        Current(document.Id).IsDirty.ShouldBeTrue();

        bool written = await _workspace.SaveAsync(document.Id, TestContext.Current.CancellationToken);

        written.ShouldBeFalse();
        File.ReadAllText(path).ShouldBe("# Original\n");
    }

    /// <summary>
    /// The caller has to be able to tell a refusal from a save, because it announces one the
    /// moment this returns.
    /// </summary>
    [Fact]
    public async Task An_ordinary_document_reports_that_it_was_written()
    {
        (MarkdownDocument document, string path) = await OpenAsync(text: "# Original\n");

        _workspace.ApplyEdit(document.Id, "# Rewritten\n");

        bool written = await _workspace.SaveAsync(document.Id, TestContext.Current.CancellationToken);

        written.ShouldBeTrue();
        File.ReadAllText(path).ShouldBe("# Rewritten\n");
    }

    /// <summary>Save As is the way out, so the copy has to arrive without the mark.</summary>
    [Fact]
    public async Task Save_As_writes_elsewhere_and_the_copy_is_not_marked()
    {
        (MarkdownDocument document, string original) = await OpenAsync(text: "# Original\n");

        // Edited first, then marked. The other order cannot be tested: a marked clean document
        // turns the edit away, so there would be nothing to carry out.
        _workspace.ApplyEdit(document.Id, "# Rewritten\n").ShouldBeTrue();
        await _workspace.SetLockedAsync(document.Id, true, TestContext.Current.CancellationToken);

        string elsewhere = Path.Combine(_folder.Path, "copy.md");

        bool written = await _workspace.SaveAsAsync(
            document.Id, elsewhere, TestContext.Current.CancellationToken);

        written.ShouldBeTrue();
        File.ReadAllText(elsewhere).ShouldBe("# Rewritten\n");

        // The file the mark was protecting is untouched.
        File.ReadAllText(original).ShouldBe("# Original\n");

        MarkdownDocument moved = Current(document.Id);

        moved.Path.ShouldBe(elsewhere);
        moved.IsLocked.ShouldBeFalse();

        // And the way out stays open: the next ordinary save works.
        _workspace.ApplyEdit(moved.Id, "# Again\n");

        (await _workspace.SaveAsync(moved.Id, TestContext.Current.CancellationToken)).ShouldBeTrue();
        File.ReadAllText(elsewhere).ShouldBe("# Again\n");
    }

    /// <summary>Whichever command is asking, writing over a marked file is still that.</summary>
    [Fact]
    public async Task Save_As_onto_a_marked_file_is_refused()
    {
        string target = _folder.Write("guarded.md", "# Guarded\n");
        await _locks.SetAsync(target, true, TestContext.Current.CancellationToken);

        (MarkdownDocument document, _) = await OpenAsync("scratch.md", "# Scratch\n");

        bool written = await _workspace.SaveAsAsync(
            document.Id, target, TestContext.Current.CancellationToken);

        written.ShouldBeFalse();
        File.ReadAllText(target).ShouldBe("# Guarded\n");
    }

    // ------------------------------------------------------------------ editing

    /// <summary>
    /// A marked, clean document turns text away - and answers so, because the caller pushes the
    /// same text into the editor and the two must not end up holding different documents.
    /// </summary>
    [Fact]
    public async Task A_marked_clean_document_refuses_an_edit()
    {
        (MarkdownDocument document, _) = await OpenAsync(text: "# Notes\n", marked: true);

        _workspace.ApplyEdit(document.Id, "# Changed\n").ShouldBeFalse();

        Current(document.Id).Text.ShouldBe("# Notes\n");
        Current(document.Id).IsDirty.ShouldBeFalse();
    }

    /// <summary>
    /// The mark applies whether or not there are unsaved edits already in the buffer. Those
    /// edits are held exactly where they are - not written, not undoable - until the mark comes
    /// off or they are saved somewhere else.
    ///
    /// An earlier version stood the mark down over unsaved edits, so that Monaco's readOnly
    /// would not switch off undo along with typing. That was the more dangerous of the two:
    /// undoing until the buffer matched the file made the document clean, the editor locked on
    /// that transition, and redo went with it - so the edits could not be brought back at all.
    /// </summary>
    [Fact]
    public async Task Marking_a_document_that_is_already_dirty_holds_its_edits_where_they_are()
    {
        (MarkdownDocument document, string path) = await OpenAsync(text: "# Notes\n");

        // Dirty first, then marked - the order a user marking the tab they are working in
        // would produce.
        _workspace.ApplyEdit(document.Id, "# Half a thought\n").ShouldBeTrue();
        (await _workspace.SetLockedAsync(document.Id, true, TestContext.Current.CancellationToken))
            .ShouldBeTrue();

        Current(document.Id).IsDirty.ShouldBeTrue();
        Current(document.Id).IsReadOnly.ShouldBeTrue();

        // The edits are still there, and nothing further can be added to them.
        _workspace.ApplyEdit(document.Id, "# A whole thought\n").ShouldBeFalse();
        Current(document.Id).Text.ShouldBe("# Half a thought\n");

        // And nothing reaches the file.
        (await _workspace.SaveAsync(document.Id, TestContext.Current.CancellationToken)).ShouldBeFalse();
        File.ReadAllText(path).ShouldBe("# Notes\n");

        // Taking the mark off hands the document back, edits and all.
        await _workspace.SetLockedAsync(document.Id, false, TestContext.Current.CancellationToken);

        Current(document.Id).Text.ShouldBe("# Half a thought\n");
        _workspace.ApplyEdit(document.Id, "# A whole thought\n").ShouldBeTrue();
    }

    /// <summary>
    /// The editor is told this and the buffer enforces it, so there is one answer rather than
    /// two that could disagree about a keystroke.
    /// </summary>
    [Fact]
    public async Task Refusing_edits_follows_the_mark_alone()
    {
        (MarkdownDocument document, _) = await OpenAsync(marked: true);

        Current(document.Id).IsReadOnly.ShouldBeTrue();

        await _workspace.SetLockedAsync(document.Id, false, TestContext.Current.CancellationToken);
        Current(document.Id).IsReadOnly.ShouldBeFalse();

        // Dirty makes no difference to the answer.
        _workspace.ApplyEdit(document.Id, "# Dirty now\n").ShouldBeTrue();
        await _workspace.SetLockedAsync(document.Id, true, TestContext.Current.CancellationToken);

        Current(document.Id).IsDirty.ShouldBeTrue();
        Current(document.Id).IsReadOnly.ShouldBeTrue();
    }

    // ----------------------------------------------------------------- the mark

    [Fact]
    public async Task Marking_and_unmarking_is_remembered_for_the_path()
    {
        (MarkdownDocument document, string path) = await OpenAsync();

        await _workspace.SetLockedAsync(document.Id, true, TestContext.Current.CancellationToken);

        _locks.IsLocked(path).ShouldBeTrue();
        Current(document.Id).IsLocked.ShouldBeTrue();

        await _workspace.SetLockedAsync(document.Id, false, TestContext.Current.CancellationToken);

        _locks.IsLocked(path).ShouldBeFalse();
        Current(document.Id).IsLocked.ShouldBeFalse();
    }

    [Fact]
    public async Task Marking_raises_its_own_change_rather_than_an_edit()
    {
        (MarkdownDocument document, _) = await OpenAsync();

        var changes = new List<WorkspaceChange>();
        _workspace.Changed += (_, e) => changes.Add(e.Change);

        await _workspace.SetLockedAsync(document.Id, true, TestContext.Current.CancellationToken);

        changes.ShouldBe([WorkspaceChange.LockChanged]);
    }

    /// <summary>An untitled document has no file for a mark to protect.</summary>
    [Fact]
    public async Task An_untitled_document_cannot_be_marked()
    {
        MarkdownDocument untitled = _workspace.CreateUntitled();

        (await _workspace.SetLockedAsync(untitled.Id, true, TestContext.Current.CancellationToken))
            .ShouldBeFalse();

        Current(untitled.Id).IsLocked.ShouldBeFalse();
    }

    // --------------------------------------------------------------- what stays

    /// <summary>
    /// Reload reads rather than writes, and a marked document tracking its file is the point of
    /// marking it. It also does not go through ApplyEdit, which is what makes this safe.
    /// </summary>
    [Fact]
    public async Task A_marked_document_still_reloads_from_disk()
    {
        (MarkdownDocument document, string path) = await OpenAsync(text: "# First\n", marked: true);

        File.WriteAllText(path, "# Second\n");

        await _workspace.ReloadAsync(document.Id, TestContext.Current.CancellationToken);

        MarkdownDocument reloaded = Current(document.Id);

        reloaded.Text.ShouldBe("# Second\n");
        reloaded.IsDirty.ShouldBeFalse();

        // And it is still marked afterwards.
        reloaded.IsLocked.ShouldBeTrue();
    }

    /// <summary>
    /// A file that has been deleted is the one case where rewriting an unedited buffer is
    /// exactly the point, so the mark stands down rather than taking away the way back.
    /// </summary>
    [Fact]
    public async Task A_marked_document_whose_file_is_gone_can_be_written_back()
    {
        (MarkdownDocument document, string path) = await OpenAsync(text: "# Notes\n", marked: true);

        File.Delete(path);
        _watchers.For(path)!.RaiseRemoved();

        MarkdownDocument missing = Current(document.Id);

        missing.IsDirty.ShouldBeTrue();
        missing.IsLocked.ShouldBeTrue();
        missing.IsReadOnly.ShouldBeFalse();

        (await _workspace.SaveAsync(document.Id, TestContext.Current.CancellationToken)).ShouldBeTrue();
        File.ReadAllText(path).ShouldBe("# Notes\n");
    }
}
