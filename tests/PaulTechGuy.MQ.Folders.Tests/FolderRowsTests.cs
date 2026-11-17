// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;
using static PaulTechGuy.MQ.Folders.Tests.FolderTreeTests;

namespace PaulTechGuy.MQ.Folders.Tests;

public sealed class FolderRowsTests
{
    private static FolderRow Row(string path, int depth = 0, bool expanded = false) =>
        new(path, Path.GetFileName(path), depth, FolderEntryKind.Markdown, expanded, false, false, 1, 1);

    /// <summary>Every diff must turn the old list into the new one exactly.</summary>
    private static IReadOnlyList<RowChange> Proven(IReadOnlyList<FolderRow> before, IReadOnlyList<FolderRow> after)
    {
        IReadOnlyList<RowChange> changes = FolderRows.Diff(before, after);
        FolderRows.ApplyTo(before, changes).ShouldBe(after);
        return changes;
    }

    [Fact]
    public void Expanding_a_folder_is_one_inserted_range_and_an_update_of_its_row()
    {
        using var temp = new TempFolder();
        temp.File("docs/a.md");
        temp.File("docs/b.md");
        temp.File("z.md");
        FolderTree tree = Opened(temp);
        IReadOnlyList<FolderRow> before = FolderRows.Build(tree, RowOptions.Default);

        Open(tree, temp.Full("docs"));
        IReadOnlyList<RowChange> changes = Proven(before, FolderRows.Build(tree, RowOptions.Default));

        changes.Count.ShouldBe(2);
        changes[0].ShouldBeOfType<RowUpdated>().Index.ShouldBe(0);
        RowsInserted inserted = changes[1].ShouldBeOfType<RowsInserted>();
        inserted.Index.ShouldBe(1);
        inserted.Rows.Select(r => r.Name).ShouldBe(["a.md", "b.md"]);
    }

    [Fact]
    public void Collapsing_is_one_removed_range()
    {
        using var temp = new TempFolder();
        temp.File("docs/a.md");
        temp.File("docs/b.md");
        temp.File("z.md");
        FolderTree tree = Opened(temp);
        Open(tree, temp.Full("docs"));
        IReadOnlyList<FolderRow> before = FolderRows.Build(tree, RowOptions.Default);

        tree.Collapse(temp.Full("docs"));
        IReadOnlyList<RowChange> changes = Proven(before, FolderRows.Build(tree, RowOptions.Default));

        changes.OfType<RowsRemoved>().ShouldHaveSingleItem().ShouldBe(new RowsRemoved(1, 2));
    }

    [Fact]
    public void Narrowing_the_filter_removes_only_the_rows_that_stopped_matching()
    {
        using var temp = new TempFolder();
        temp.File("alpha.md");
        temp.File("beta.md");
        temp.File("alphabet.md");
        FolderTree tree = Opened(temp);
        IReadOnlyList<FolderRow> before = FolderRows.Build(tree, RowOptions.Default);

        IReadOnlyList<RowChange> changes = Proven(before, FolderRows.Build(tree, new RowOptions("alpha")));

        // Nothing is inserted: the rows that still match stay put, updated only because they now
        // have one sibling fewer for Narrator to count.
        changes.OfType<RowsInserted>().ShouldBeEmpty();
        changes.OfType<RowsRemoved>().ShouldHaveSingleItem().ShouldBe(new RowsRemoved(2, 1));
        changes.OfType<RowUpdated>().Select(u => u.Row.SizeOfSet).ShouldAllBe(size => size == 2);
        FolderRows.Build(tree, new RowOptions("alpha")).Select(r => r.Name).ShouldBe(["alpha.md", "alphabet.md"]);
    }

    [Fact]
    public void A_filter_shows_matches_below_closed_folders_with_their_folders_open()
    {
        using var temp = new TempFolder();
        temp.File("docs/guide/install.md");
        temp.File("docs/other.md");
        temp.File("top.md");
        FolderTree tree = Opened(temp);
        Open(tree, temp.Full("docs"));
        Open(tree, temp.Full("docs/guide"));
        tree.Collapse(temp.Full("docs"));

        IReadOnlyList<FolderRow> rows = FolderRows.Build(tree, new RowOptions("install"));

        rows.Select(r => r.Name).ShouldBe(["docs", "guide", "install.md"]);
        rows[0].IsExpanded.ShouldBeTrue();
        rows[1].IsExpanded.ShouldBeTrue();

        // Clearing it puts the tree back as it was left.
        FolderRows.Build(tree, RowOptions.Default).Select(r => r.Name).ShouldBe(["docs", "top.md"]);
    }

    [Fact]
    public void Sorting_by_date_keeps_folders_first_by_name_then_newest_files_first()
    {
        using var temp = new TempFolder();
        temp.File("b-folder/x.md");
        temp.File("a-folder/x.md");
        string older = temp.File("older.md");
        string newer = temp.File("newer.md");
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow.AddDays(-1));
        FolderTree tree = Opened(temp);

        FolderRows.Build(tree, new RowOptions(SortByModified: true)).Select(r => r.Name)
            .ShouldBe(["a-folder", "b-folder", "newer.md", "older.md"]);
        FolderRows.Build(tree, RowOptions.Default).Select(r => r.Name)
            .ShouldBe(["a-folder", "b-folder", "newer.md", "older.md"]);

        File.SetLastWriteTimeUtc(older, DateTime.UtcNow);
        tree.Apply([new FolderChange(FolderChangeKind.Changed, older)]);

        FolderRows.Build(tree, new RowOptions(SortByModified: true)).Select(r => r.Name)
            .ShouldBe(["a-folder", "b-folder", "older.md", "newer.md"]);
    }

    [Fact]
    public void Position_and_size_count_visible_siblings_for_narrator()
    {
        using var temp = new TempFolder();
        temp.File("docs/a.md");
        temp.File("docs/b.md");
        temp.File("src/x.cs");
        temp.File("top.md");
        FolderTree tree = Opened(temp);
        Open(tree, temp.Full("docs"));

        IReadOnlyList<FolderRow> rows = FolderRows.Build(tree, RowOptions.Default);

        rows.Select(r => (r.Name, r.Depth, r.PositionInSet, r.SizeOfSet)).ShouldBe(
        [
            ("docs", 0, 1, 2),
            ("a.md", 1, 1, 2),
            ("b.md", 1, 2, 2),
            ("top.md", 0, 2, 2),
        ]);
    }

    [Fact]
    public void Images_and_ignored_rows_are_dimmed()
    {
        using var temp = new TempFolder();
        temp.Folder(".git");
        temp.File(".gitignore", "draft.md\n");
        temp.File("draft.md");
        temp.File("logo.png");
        temp.File("plain.md");
        GitIgnoreReader reader = GitIgnoreReader.For(temp.Path);
        var tree = new FolderTree(temp.Path, false, reader.IsIgnored);
        Load(tree, temp.Path);

        FolderRows.Build(tree, RowOptions.Default).Select(r => (r.Name, r.IsDimmed)).ShouldBe(
        [
            ("draft.md", true),
            ("logo.png", true),
            ("plain.md", false),
        ]);
    }

    [Fact]
    public void A_reordering_diff_is_still_exact()
    {
        FolderRow a = Row(@"C:\r\a.md");
        FolderRow b = Row(@"C:\r\b.md");
        FolderRow c = Row(@"C:\r\c.md");
        FolderRow d = Row(@"C:\r\d.md");

        Proven([a, b, c], [c, b, a]);
        Proven([a, b, c, d], [d, a, b, c]);
        Proven([a, b], [b, c, a]);
        Proven([], [a, b]);
        Proven([a, b], []);
        Proven([a, b, c], [a, b with { IsExpanded = true }, c]).ShouldBe([new RowUpdated(1, b with { IsExpanded = true })]);
    }

    [Fact]
    public void An_unchanged_list_has_no_changes()
    {
        FolderRow a = Row(@"C:\r\a.md");

        FolderRows.Diff([a], [a]).ShouldBeEmpty();
    }
}
