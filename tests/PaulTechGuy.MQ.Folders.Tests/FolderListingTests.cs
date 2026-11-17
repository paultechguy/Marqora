// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Folders.Tests;

public sealed class FolderListingTests
{
    [Theory]
    [InlineData(".git")]
    [InlineData(".vs")]
    [InlineData("node_modules")]
    [InlineData("BIN")]
    [InlineData("obj")]
    public void Build_and_dot_folders_are_always_hidden(string name) =>
        FolderListing.IsAlwaysHiddenFolder(name).ShouldBeTrue();

    [Theory]
    [InlineData("docs")]
    [InlineData("binaries")]
    [InlineData("objects")]
    public void Ordinary_folders_are_not(string name) =>
        FolderListing.IsAlwaysHiddenFolder(name).ShouldBeFalse();

    [Theory]
    [InlineData("a.md", FolderEntryKind.Markdown)]
    [InlineData("a.MARKDOWN", FolderEntryKind.Markdown)]
    [InlineData("a.mdx", FolderEntryKind.Markdown)]
    [InlineData("a.png", FolderEntryKind.Image)]
    [InlineData("a.svg", FolderEntryKind.Image)]
    [InlineData("a.txt", FolderEntryKind.Other)]
    [InlineData("a.cs", FolderEntryKind.Other)]
    public void Files_are_classified_by_extension(string name, FolderEntryKind kind) =>
        FolderListing.KindOfFile(name).ShouldBe(kind);

    [Fact]
    public void Without_show_all_only_markdown_and_images_are_listed_and_never_dot_files()
    {
        FolderListing.IsListedFile("a.md", false).ShouldBeTrue();
        FolderListing.IsListedFile("a.jpg", false).ShouldBeTrue();
        FolderListing.IsListedFile("a.txt", false).ShouldBeFalse();
        FolderListing.IsListedFile(".notes.md", false).ShouldBeFalse();
    }

    [Fact]
    public void Show_all_lists_every_file_dot_files_included()
    {
        FolderListing.IsListedFile("a.txt", true).ShouldBeTrue();
        FolderListing.IsListedFile(".gitignore", true).ShouldBeTrue();
    }

    [Fact]
    public void Read_lists_one_level_and_leaves_out_hidden_folders_and_unlisted_files()
    {
        using var temp = new TempFolder();
        temp.File("a.md");
        temp.File("b.png");
        temp.File("c.txt");
        temp.File(".gitignore");
        temp.File("docs/deep.md");
        temp.File(".git/HEAD");
        temp.File("node_modules/x.md");

        IReadOnlyList<FolderEntry> entries = FolderListing.Read(temp.Path, showAllFiles: false, TestContext.Current.CancellationToken);

        entries.Select(e => e.Name).Order().ShouldBe(["a.md", "b.png", "docs"]);
        entries.Single(e => e.Name == "docs").IsFolder.ShouldBeTrue();
    }

    [Fact]
    public void Read_with_show_all_adds_other_files_but_still_not_hidden_folders()
    {
        using var temp = new TempFolder();
        temp.File("a.md");
        temp.File("c.txt");
        temp.File(".gitignore");
        temp.File(".git/HEAD");

        FolderListing.Read(temp.Path, showAllFiles: true, TestContext.Current.CancellationToken).Select(e => e.Name).Order()
            .ShouldBe([".gitignore", "a.md", "c.txt"]);
    }

    [Fact]
    public void Read_of_a_missing_folder_is_empty_rather_than_an_error()
    {
        using var temp = new TempFolder();

        FolderListing.Read(temp.Full("gone"), showAllFiles: false, TestContext.Current.CancellationToken).ShouldBeEmpty();
    }
}
