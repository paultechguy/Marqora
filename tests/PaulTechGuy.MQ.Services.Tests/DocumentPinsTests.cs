// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Abstractions.Repositories;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Services.Tests;

/// <summary>
/// How the set of pins is keyed, and what it does with an entry whose file it cannot find.
///
/// The same questions <see cref="DocumentLocksTests"/> asks, and deliberately the same answers:
/// both services key through <c>DocumentPathKeys</c>, so these tests are what says the shared
/// helper is reached from this side too. Real files, because both questions are about the disk;
/// only the JSON is stood in for.
///
/// What is *not* tested here is the one thing that differs — a dropped pin is logged rather than
/// announced. That is a decision about the status bar, and it lives in the view model.
/// </summary>
public sealed class DocumentPinsTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeDocumentPinsRepository _repository = new();
    private readonly DocumentPinsService _pins;

    public DocumentPinsTests() =>
        _pins = new DocumentPinsService(_repository, NullLogger<DocumentPinsService>.Instance);

    public void Dispose() => _folder.Dispose();

    private string Write(string name) => _folder.Write(name, "# Notes\n");

    // ------------------------------------------------------------------- keying

    [Fact]
    public async Task A_path_that_was_never_pinned_is_not_pinned()
    {
        await _pins.LoadAsync(TestContext.Current.CancellationToken);

        _pins.IsPinned(Write("notes.md")).ShouldBeFalse();
    }

    [Fact]
    public async Task Pinning_a_path_and_asking_about_it_agree()
    {
        string path = Write("notes.md");

        await _pins.SetAsync(path, true, TestContext.Current.CancellationToken);

        _pins.IsPinned(path).ShouldBeTrue();

        await _pins.SetAsync(path, false, TestContext.Current.CancellationToken);

        _pins.IsPinned(path).ShouldBeFalse();
    }

    /// <summary>Windows paths are not case-sensitive, and neither is the key.</summary>
    [Fact]
    public async Task The_same_file_spelled_in_another_case_is_the_same_pin()
    {
        string path = Write("Notes.md");

        await _pins.SetAsync(path, true, TestContext.Current.CancellationToken);

        _pins.IsPinned(path.ToUpperInvariant()).ShouldBeTrue();
        _pins.IsPinned(path.ToLowerInvariant()).ShouldBeTrue();
    }

    /// <summary>A relative path reaches the same file, so it has to find the same pin.</summary>
    [Fact]
    public async Task A_path_written_the_long_way_round_is_the_same_pin()
    {
        string path = Write("notes.md");
        string roundabout = Path.Combine(_folder.Path, "sub", "..", "notes.md");

        await _pins.SetAsync(path, true, TestContext.Current.CancellationToken);

        _pins.IsPinned(roundabout).ShouldBeTrue();
    }

    /// <summary>An untitled document has no path, and cannot be pinned by one.</summary>
    [Fact]
    public void Nothing_is_pinned_by_a_path_that_is_not_one()
    {
        _pins.IsPinned(null).ShouldBeFalse();
        _pins.IsPinned(string.Empty).ShouldBeFalse();
        _pins.IsPinned("   ").ShouldBeFalse();
    }

    // ------------------------------------------------------------------ writing

    [Fact]
    public async Task A_pin_is_written_out_as_it_is_made()
    {
        string path = Write("notes.md");

        await _pins.SetAsync(path, true, TestContext.Current.CancellationToken);

        _repository.Saved.ShouldHaveSingleItem();
        _repository.Saved[0].ShouldBe(Path.GetFullPath(path), StringCompareShould.IgnoreCase);
    }

    [Fact]
    public async Task Pinning_something_already_pinned_writes_nothing()
    {
        string path = Write("notes.md");

        await _pins.SetAsync(path, true, TestContext.Current.CancellationToken);

        int writes = _repository.SaveCount;

        await _pins.SetAsync(path, true, TestContext.Current.CancellationToken);

        _repository.SaveCount.ShouldBe(writes);
    }

    /// <summary>Unpinning something that was never pinned is not a change either.</summary>
    [Fact]
    public async Task Unpinning_something_that_was_not_pinned_writes_nothing()
    {
        string path = Write("notes.md");

        await _pins.LoadAsync(TestContext.Current.CancellationToken);
        await _pins.SetAsync(path, false, TestContext.Current.CancellationToken);

        _repository.SaveCount.ShouldBe(0);
    }

    // ------------------------------------------------------------------ pruning

    [Fact]
    public async Task A_stored_pin_whose_file_is_there_is_kept()
    {
        string path = Write("notes.md");
        _repository.Stored = [path];

        await _pins.LoadAsync(TestContext.Current.CancellationToken);

        _pins.IsPinned(path).ShouldBeTrue();
        _pins.DroppedOnLoad.ShouldBe(0);
    }

    [Fact]
    public async Task A_stored_pin_whose_file_has_been_deleted_is_dropped()
    {
        string path = Path.Combine(_folder.Path, "gone.md");
        _repository.Stored = [path];

        await _pins.LoadAsync(TestContext.Current.CancellationToken);

        _pins.IsPinned(path).ShouldBeFalse();
        _pins.DroppedOnLoad.ShouldBe(1);

        // Rewritten, so the same entry is not weighed again on every launch.
        _repository.Saved.ShouldBeEmpty();
    }

    /// <summary>
    /// The case the obvious implementation gets wrong, and the reason the keying is shared with
    /// the read-only marks rather than written twice. A document on a drive that is not attached
    /// does not exist either, and dropping its pin would empty the front of the strip the first
    /// time Marqora was opened away from the network.
    /// </summary>
    [Fact]
    public async Task A_stored_pin_on_a_volume_that_is_not_there_is_kept()
    {
        const string Unattached = @"Q:\reference\spec.md";
        _repository.Stored = [Unattached];

        await _pins.LoadAsync(TestContext.Current.CancellationToken);

        _pins.DroppedOnLoad.ShouldBe(0);
        _pins.IsPinned(Unattached).ShouldBeTrue();
    }

    [Fact]
    public async Task Loading_replaces_whatever_was_held_before()
    {
        string first = Write("first.md");
        string second = Write("second.md");

        await _pins.SetAsync(first, true, TestContext.Current.CancellationToken);

        _repository.Stored = [second];
        await _pins.LoadAsync(TestContext.Current.CancellationToken);

        _pins.IsPinned(first).ShouldBeFalse();
        _pins.IsPinned(second).ShouldBeTrue();
    }
}

/// <summary>Pins held in a list, standing in for the JSON file.</summary>
internal sealed class FakeDocumentPinsRepository : IDocumentPinsRepository
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
