// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Abstractions.Repositories;
using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Services.Tests;

/// <summary>
/// WP-2: the read-only marks, the pins and the recent list follow a rename made in Marqora.
///
/// The files are real, because the hard case is about the disk: a key stored under a junction's
/// spelling, which can only be matched while the junction is still where it was.
/// </summary>
public sealed class PathKeyMoveTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeDocumentLocksRepository _locksRepository = new();
    private readonly FakeDocumentPinsRepository _pinsRepository = new();
    private readonly DocumentLocksService _locks;
    private readonly DocumentPinsService _pins;

    public PathKeyMoveTests()
    {
        _locks = new DocumentLocksService(_locksRepository, NullLogger<DocumentLocksService>.Instance);
        _pins = new DocumentPinsService(_pinsRepository, NullLogger<DocumentPinsService>.Instance);
    }

    public void Dispose() => _folder.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string File(string relative)
    {
        string full = Path.Combine(_folder.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, "# Notes\n");
        return full;
    }

    private string Full(string relative) => Path.Combine(_folder.Path, relative);

    [Fact]
    public async Task A_marked_and_pinned_file_keeps_both_through_a_rename()
    {
        string from = File("setup.md");
        string to = Full("install.md");
        await _locks.SetAsync(from, true, Token);
        await _pins.SetAsync(from, true, Token);

        var marks = _locks.PrepareMove(from);
        var pins = _pins.PrepareMove(from);
        System.IO.File.Move(from, to);
        await _locks.CompleteMoveAsync(marks, to, Token);
        await _pins.CompleteMoveAsync(pins, to, Token);

        _locks.IsLocked(to).ShouldBeTrue();
        _locks.IsLocked(from).ShouldBeFalse();
        _pins.IsPinned(to).ShouldBeTrue();
        _pins.IsPinned(from).ShouldBeFalse();
        _locksRepository.Saved.ShouldBe([to], Case.Insensitive);
        _pinsRepository.Saved.ShouldBe([to], Case.Insensitive);
    }

    [Fact]
    public async Task A_folder_rename_carries_marks_on_files_nobody_has_open()
    {
        string a = File(@"guide\a.md");
        string b = File(@"guide\deep\b.md");
        string other = File("other.md");
        await _locks.SetAsync(a, true, Token);
        await _locks.SetAsync(b, true, Token);
        await _locks.SetAsync(other, true, Token);

        var prepared = _locks.PrepareMove(Full("guide"));
        Directory.Move(Full("guide"), Full("manual"));
        await _locks.CompleteMoveAsync(prepared, Full("manual"), Token);

        _locks.IsLocked(Full(@"manual\a.md")).ShouldBeTrue();
        _locks.IsLocked(Full(@"manual\deep\b.md")).ShouldBeTrue();
        _locks.IsLocked(other).ShouldBeTrue();
        _locks.IsLocked(a).ShouldBeFalse();
        _locksRepository.Saved.Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_folder_named_like_the_start_of_another_does_not_take_its_marks()
    {
        File(@"notes\a.md");
        string old = File(@"notes-old\a.md");
        await _locks.SetAsync(old, true, Token);

        var prepared = _locks.PrepareMove(Full("notes"));
        prepared.IsEmpty.ShouldBeTrue();

        Directory.Move(Full("notes"), Full("notebook"));
        await _locks.CompleteMoveAsync(prepared, Full("notebook"), Token);

        _locks.IsLocked(old).ShouldBeTrue();
        _locks.AnyUnder(Full("notebook")).ShouldBeFalse();
        _locks.AnyUnder(Full("notes-old")).ShouldBeTrue();
    }

    [Fact]
    public async Task A_case_only_rename_moves_the_stored_spelling()
    {
        string from = File("readme.md");
        string to = Full("README.md");
        await _pins.SetAsync(from, true, Token);

        var prepared = _pins.PrepareMove(from);
        System.IO.File.Move(from, to);
        await _pins.CompleteMoveAsync(prepared, to, Token);

        _pins.IsPinned(to).ShouldBeTrue();
        _pinsRepository.Saved.ShouldHaveSingleItem().ShouldEndWith("README.md");
    }

    [Fact]
    public async Task Renaming_a_junction_moves_marks_under_its_spelling_and_leaves_those_under_the_target()
    {
        string real = Full("real");
        File(@"real\a.md");
        string viaTarget = File(@"real\b.md");
        string junction = Full("link");

        if (!MakeJunction(junction, real))
        {
            Assert.Skip("Could not create a junction on this machine.");
        }

        string viaJunction = Path.Combine(junction, "a.md");
        await _locks.SetAsync(viaJunction, true, Token);
        await _locks.SetAsync(viaTarget, true, Token);

        var prepared = _locks.PrepareMove(junction);
        Directory.Move(junction, Full("renamed-link"));
        await _locks.CompleteMoveAsync(prepared, Full("renamed-link"), Token);

        _locks.IsLocked(Full(@"renamed-link\a.md")).ShouldBeTrue();
        _locks.IsLocked(viaTarget).ShouldBeTrue();
        _locks.IsLocked(viaJunction).ShouldBeFalse();
    }

    [Fact]
    public async Task A_failed_write_after_the_move_still_moves_the_mark_in_memory()
    {
        var failing = new FailingLocksRepository();
        var locks = new DocumentLocksService(failing, NullLogger<DocumentLocksService>.Instance);
        string from = File("a.md");
        string to = Full("b.md");
        await locks.SetAsync(from, true, Token);

        var prepared = locks.PrepareMove(from);
        System.IO.File.Move(from, to);
        failing.Fail = true;

        await locks.CompleteMoveAsync(prepared, to, Token);

        locks.IsLocked(to).ShouldBeTrue();

        // The next write that does work carries it.
        failing.Fail = false;
        await locks.SetAsync(File("c.md"), true, Token);
        failing.Saved.ShouldContain(p => p.EndsWith("b.md", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_delete_unpins_everything_under_the_path()
    {
        string a = File(@"old\a.md");
        string b = File(@"old\sub\b.md");
        string keep = File("keep.md");
        await _pins.SetAsync(a, true, Token);
        await _pins.SetAsync(b, true, Token);
        await _pins.SetAsync(keep, true, Token);

        await _pins.RemoveUnderAsync(Full("old"), Token);

        _pins.IsPinned(a).ShouldBeFalse();
        _pins.IsPinned(b).ShouldBeFalse();
        _pins.IsPinned(keep).ShouldBeTrue();
    }

    [Fact]
    public void A_folder_is_gone_only_when_its_volume_answers_and_it_is_not_there()
    {
        DocumentPathKeys.FolderHasBeenDeleted(_folder.Path).ShouldBeFalse();
        DocumentPathKeys.FolderHasBeenDeleted(Full("missing")).ShouldBeTrue();
        DocumentPathKeys.FolderHasBeenDeleted(@"Q:\not\attached").ShouldBeFalse();

        // The file test would have called a present folder deleted (C24).
        DocumentPathKeys.HasBeenDeleted(_folder.Path).ShouldBeTrue();
    }

    [Fact]
    public async Task Recent_entries_follow_a_rename_and_keep_their_pin_and_time()
    {
        var repository = new FakeRecentFilesRepository();
        var recent = new RecentFilesService(repository, new FakeSettingsService(), NullLogger<RecentFilesService>.Instance);
        string a = Full(@"guide\a.md");
        string b = Full(@"guide\b.md");
        string other = Full(@"guide-old\c.md");
        await recent.AddAsync(a, Token);
        await recent.AddAsync(b, Token);
        await recent.AddAsync(other, Token);
        await recent.TogglePinAsync(a, Token);
        DateTimeOffset when = recent.Items.Single(i => i.Path == a).LastOpenedUtc;

        await recent.RenameAsync(Full("guide"), Full("manual"), Token);

        RecentFile moved = recent.Items.Single(i => i.Path == Full(@"manual\a.md"));
        moved.IsPinned.ShouldBeTrue();
        moved.LastOpenedUtc.ShouldBe(when);
        recent.Items.ShouldContain(i => i.Path == Full(@"manual\b.md"));
        recent.Items.ShouldContain(i => i.Path == other);
        recent.Items.ShouldNotContain(i => i.Path.StartsWith(Full(@"guide\"), StringComparison.OrdinalIgnoreCase));

        await recent.RemoveUnderAsync(Full("manual"), Token);

        recent.Items.Select(i => i.Path).ShouldBe([other]);
    }

    [Fact]
    public async Task A_rename_that_touches_no_recent_entry_writes_nothing()
    {
        var repository = new FakeRecentFilesRepository();
        var recent = new RecentFilesService(repository, new FakeSettingsService(), NullLogger<RecentFilesService>.Instance);
        await recent.AddAsync(Full("a.md"), Token);
        int writes = repository.SaveCount;

        await recent.RenameAsync(Full("elsewhere"), Full("moved"), Token);
        await recent.RemoveUnderAsync(Full("elsewhere"), Token);

        repository.SaveCount.ShouldBe(writes);
    }

    /// <summary>
    /// <c>mklink /J</c>, which makes a junction without administrator rights or developer mode,
    /// unlike a symbolic link.
    /// </summary>
    private static bool MakeJunction(string link, string target)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            process?.WaitForExit(10_000);
            return Directory.Exists(link);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}

/// <summary>A marks file that can be made to refuse writes, as a full disk or a locked file would.</summary>
internal sealed class FailingLocksRepository : IDocumentLocksRepository
{
    public bool Fail { get; set; }

    public List<string> Saved { get; private set; } = [];

    public Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task SaveAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        if (Fail)
        {
            throw new IOException("The disk is full.");
        }

        Saved = [.. paths];
        return Task.CompletedTask;
    }
}

/// <summary>The recent list held in memory, standing in for the JSON file.</summary>
internal sealed class FakeRecentFilesRepository : IRecentFilesRepository
{
    public int SaveCount { get; private set; }

    public Task<IReadOnlyList<RecentFile>> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RecentFile>>([]);

    public Task SaveAsync(IReadOnlyList<RecentFile> entries, CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}
