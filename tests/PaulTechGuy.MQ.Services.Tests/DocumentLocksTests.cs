// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Abstractions.Repositories;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Services.Tests;

/// <summary>
/// How the set of marks is keyed, and what it does with an entry whose file it cannot find.
///
/// The files are real, because both questions are about the disk: which spellings of a path
/// mean the same file, and whether a file is missing or merely out of reach. Only the JSON is
/// stood in for.
/// </summary>
public sealed class DocumentLocksTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeDocumentLocksRepository _repository = new();
    private readonly DocumentLocksService _locks;

    public DocumentLocksTests() =>
        _locks = new DocumentLocksService(_repository, NullLogger<DocumentLocksService>.Instance);

    public void Dispose() => _folder.Dispose();

    private string Write(string name) => _folder.Write(name, "# Notes\n");

    // ------------------------------------------------------------------- keying

    [Fact]
    public async Task A_path_that_was_never_marked_is_not_marked()
    {
        await _locks.LoadAsync(TestContext.Current.CancellationToken);

        _locks.IsLocked(Write("notes.md")).ShouldBeFalse();
    }

    [Fact]
    public async Task Marking_a_path_and_asking_about_it_agree()
    {
        string path = Write("notes.md");

        await _locks.SetAsync(path, true, TestContext.Current.CancellationToken);

        _locks.IsLocked(path).ShouldBeTrue();

        await _locks.SetAsync(path, false, TestContext.Current.CancellationToken);

        _locks.IsLocked(path).ShouldBeFalse();
    }

    /// <summary>Windows paths are not case-sensitive, and neither is the key.</summary>
    [Fact]
    public async Task The_same_file_spelled_in_another_case_is_the_same_mark()
    {
        string path = Write("Notes.md");

        await _locks.SetAsync(path, true, TestContext.Current.CancellationToken);

        _locks.IsLocked(path.ToUpperInvariant()).ShouldBeTrue();
        _locks.IsLocked(path.ToLowerInvariant()).ShouldBeTrue();
    }

    /// <summary>A relative path reaches the same file, so it has to find the same mark.</summary>
    [Fact]
    public async Task A_path_written_the_long_way_round_is_the_same_mark()
    {
        string path = Write("notes.md");
        string roundabout = Path.Combine(_folder.Path, "sub", "..", "notes.md");

        await _locks.SetAsync(path, true, TestContext.Current.CancellationToken);

        _locks.IsLocked(roundabout).ShouldBeTrue();
    }

    [Fact]
    public void Nothing_is_marked_by_a_path_that_is_not_one()
    {
        _locks.IsLocked(null).ShouldBeFalse();
        _locks.IsLocked(string.Empty).ShouldBeFalse();
        _locks.IsLocked("   ").ShouldBeFalse();
    }

    // ------------------------------------------------------------------ writing

    [Fact]
    public async Task A_mark_is_written_out_as_it_is_made()
    {
        string path = Write("notes.md");

        await _locks.SetAsync(path, true, TestContext.Current.CancellationToken);

        _repository.Saved.ShouldHaveSingleItem();
        _repository.Saved[0].ShouldBe(Path.GetFullPath(path), StringCompareShould.IgnoreCase);
    }

    /// <summary>Records compare by value, and a set that did not change is not worth a write.</summary>
    [Fact]
    public async Task Marking_something_already_marked_writes_nothing()
    {
        string path = Write("notes.md");

        await _locks.SetAsync(path, true, TestContext.Current.CancellationToken);

        int writes = _repository.SaveCount;

        await _locks.SetAsync(path, true, TestContext.Current.CancellationToken);

        _repository.SaveCount.ShouldBe(writes);
    }

    // ------------------------------------------------------------------ pruning

    [Fact]
    public async Task A_stored_mark_whose_file_is_there_is_kept()
    {
        string path = Write("notes.md");
        _repository.Stored = [path];

        await _locks.LoadAsync(TestContext.Current.CancellationToken);

        _locks.IsLocked(path).ShouldBeTrue();
        _locks.DroppedOnLoad.ShouldBe(0);
    }

    [Fact]
    public async Task A_stored_mark_whose_file_has_been_deleted_is_dropped()
    {
        string path = Path.Combine(_folder.Path, "gone.md");
        _repository.Stored = [path];

        await _locks.LoadAsync(TestContext.Current.CancellationToken);

        _locks.IsLocked(path).ShouldBeFalse();
        _locks.DroppedOnLoad.ShouldBe(1);

        // Rewritten, so the same entry is not weighed again on every launch.
        _repository.Saved.ShouldBeEmpty();
    }

    /// <summary>
    /// The case the obvious implementation gets wrong. A document on a drive that is not
    /// attached, or a share the machine is not currently on, does not exist either - and
    /// dropping its mark would unlock a whole shareful of reference documents the first time
    /// Marqora was opened away from the network.
    /// </summary>
    [Fact]
    public async Task A_stored_mark_on_a_volume_that_is_not_there_is_kept()
    {
        const string Unattached = @"Q:\reference\spec.md";
        _repository.Stored = [Unattached];

        await _locks.LoadAsync(TestContext.Current.CancellationToken);

        _locks.DroppedOnLoad.ShouldBe(0);
        _locks.IsLocked(Unattached).ShouldBeTrue();
    }

    [Fact]
    public async Task Loading_replaces_whatever_was_held_before()
    {
        string first = Write("first.md");
        string second = Write("second.md");

        await _locks.SetAsync(first, true, TestContext.Current.CancellationToken);

        _repository.Stored = [second];
        await _locks.LoadAsync(TestContext.Current.CancellationToken);

        _locks.IsLocked(first).ShouldBeFalse();
        _locks.IsLocked(second).ShouldBeTrue();
    }
}

/// <summary>Marks held in a list, standing in for the JSON file.</summary>
internal sealed class FakeDocumentLocksRepository : IDocumentLocksRepository
{
    public List<string> Stored { get; set; } = [];

    /// <summary>What the last write was given.</summary>
    public List<string> Saved { get; private set; } = [];

    public int SaveCount { get; private set; }

    public Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(Stored);

    public Task SaveAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        Saved = [.. paths];
        SaveCount++;

        return Task.CompletedTask;
    }
}
