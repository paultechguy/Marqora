// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Services.Tests;

/// <summary>
/// What the workspace does with a pin: restores one when a document is opened, refuses one for a
/// document that has no path, and carries one across a Save As.
///
/// That last is the whole reason the pin lives on the document rather than on its tab, and it is
/// the one behavior here that differs from the read-only mark — so it gets tested from both ends,
/// the document's flag and the store's answer.
///
/// Real files, because Save As writes one. Only the pin store is stood in for; what it does with
/// a path of its own is <see cref="DocumentPinsTests"/>.
/// </summary>
public sealed class PinnedDocumentTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeFileWatcherFactory _watchers = new();
    private readonly FakeDocumentPins _pins = new();
    private readonly DocumentWorkspace _workspace;

    public PinnedDocumentTests() =>
        _workspace = new DocumentWorkspace(
            _watchers,
            new FakeSettingsService(),
            new FakeDocumentLocks(),
            _pins,
            NullLogger<DocumentWorkspace>.Instance);

    public void Dispose()
    {
        _workspace.Dispose();
        _folder.Dispose();
    }

    private async Task<MarkdownDocument> OpenAsync(string name = "notes.md", string text = "# Notes\n")
    {
        string path = _folder.Write(name, text);

        return await _workspace.OpenAsync(path, TestContext.Current.CancellationToken);
    }

    // ----------------------------------------------------------------- restoring

    [Fact]
    public async Task A_document_opens_unpinned_when_nothing_pinned_it()
    {
        MarkdownDocument document = await OpenAsync();

        document.IsPinned.ShouldBeFalse();
    }

    [Fact]
    public async Task A_document_whose_path_is_pinned_opens_pinned()
    {
        string path = _folder.Write("spec.md", "# Spec\n");

        await _pins.SetAsync(path, true, TestContext.Current.CancellationToken);

        MarkdownDocument document = await _workspace.OpenAsync(path, TestContext.Current.CancellationToken);

        document.IsPinned.ShouldBeTrue();
    }

    // ------------------------------------------------------------------ setting

    [Fact]
    public async Task Pinning_a_document_sets_the_flag_and_the_store()
    {
        MarkdownDocument document = await OpenAsync();

        bool pinned = await _workspace.SetPinnedAsync(document.Id, true, TestContext.Current.CancellationToken);

        pinned.ShouldBeTrue();
        _workspace.Documents.Single().IsPinned.ShouldBeTrue();
        _pins.IsPinned(document.Path).ShouldBeTrue();
    }

    [Fact]
    public async Task Unpinning_takes_it_off_both()
    {
        MarkdownDocument document = await OpenAsync();

        await _workspace.SetPinnedAsync(document.Id, true, TestContext.Current.CancellationToken);
        await _workspace.SetPinnedAsync(document.Id, false, TestContext.Current.CancellationToken);

        _workspace.Documents.Single().IsPinned.ShouldBeFalse();
        _pins.IsPinned(document.Path).ShouldBeFalse();
    }

    /// <summary>A pin is remembered by path, and an untitled document has not got one yet.</summary>
    [Fact]
    public async Task An_untitled_document_cannot_be_pinned()
    {
        MarkdownDocument untitled = _workspace.CreateUntitled();

        bool pinned = await _workspace.SetPinnedAsync(untitled.Id, true, TestContext.Current.CancellationToken);

        pinned.ShouldBeFalse();
        _workspace.Documents.Single().IsPinned.ShouldBeFalse();
    }

    [Fact]
    public async Task Pinning_raises_a_change_of_its_own()
    {
        MarkdownDocument document = await OpenAsync();
        List<WorkspaceChangedEventArgs> changes = [];

        _workspace.Changed += (_, e) => changes.Add(e);

        await _workspace.SetPinnedAsync(document.Id, true, TestContext.Current.CancellationToken);

        changes.ShouldContain(e => e.Change == WorkspaceChange.PinChanged && e.DocumentId == document.Id);
    }

    /// <summary>Nothing moved, so nothing is written and nobody is told.</summary>
    [Fact]
    public async Task Pinning_something_already_pinned_raises_nothing()
    {
        MarkdownDocument document = await OpenAsync();

        await _workspace.SetPinnedAsync(document.Id, true, TestContext.Current.CancellationToken);

        List<WorkspaceChangedEventArgs> changes = [];
        _workspace.Changed += (_, e) => changes.Add(e);

        bool pinned = await _workspace.SetPinnedAsync(document.Id, true, TestContext.Current.CancellationToken);

        pinned.ShouldBeTrue();
        changes.ShouldNotContain(e => e.Change == WorkspaceChange.PinChanged);
    }

    // ------------------------------------------------------------------ save as

    /// <summary>
    /// The divergence from the read-only mark, and the reason the pin lives on the document. The
    /// file is being renamed rather than replaced, and the tab has not moved - so the pin goes
    /// with it, and the tab stays where it was in the strip.
    /// </summary>
    [Fact]
    public async Task A_pin_follows_the_document_through_save_as()
    {
        MarkdownDocument document = await OpenAsync("spec.md", "# Spec\n");
        string destination = Path.Combine(_folder.Path, "spec-v2.md");

        await _workspace.SetPinnedAsync(document.Id, true, TestContext.Current.CancellationToken);

        bool written = await _workspace.SaveAsAsync(
            document.Id,
            destination,
            TestContext.Current.CancellationToken);

        written.ShouldBeTrue();
        _workspace.Documents.Single().IsPinned.ShouldBeTrue();
        _pins.IsPinned(destination).ShouldBeTrue();
    }

    /// <summary>
    /// The other half of following: the path left behind stops being pinned, so reopening the
    /// original later does not bring back a pin the user never put on it.
    /// </summary>
    [Fact]
    public async Task The_path_left_behind_by_save_as_is_no_longer_pinned()
    {
        MarkdownDocument document = await OpenAsync("spec.md", "# Spec\n");
        string original = document.Path!;
        string destination = Path.Combine(_folder.Path, "spec-v2.md");

        await _workspace.SetPinnedAsync(document.Id, true, TestContext.Current.CancellationToken);
        await _workspace.SaveAsAsync(document.Id, destination, TestContext.Current.CancellationToken);

        _pins.IsPinned(original).ShouldBeFalse();
    }

    [Fact]
    public async Task An_unpinned_document_saved_as_stays_unpinned()
    {
        MarkdownDocument document = await OpenAsync("spec.md", "# Spec\n");
        string destination = Path.Combine(_folder.Path, "spec-v2.md");

        await _workspace.SaveAsAsync(document.Id, destination, TestContext.Current.CancellationToken);

        _workspace.Documents.Single().IsPinned.ShouldBeFalse();
        _pins.IsPinned(destination).ShouldBeFalse();
    }

    /// <summary>An untitled document has no pin to carry, and saving it does not invent one.</summary>
    [Fact]
    public async Task Saving_an_untitled_document_leaves_it_unpinned()
    {
        MarkdownDocument untitled = _workspace.CreateUntitled();
        string destination = Path.Combine(_folder.Path, "fresh.md");

        await _workspace.SaveAsAsync(untitled.Id, destination, TestContext.Current.CancellationToken);

        _workspace.Documents.Single().IsPinned.ShouldBeFalse();
        _pins.IsPinned(destination).ShouldBeFalse();
    }
}
