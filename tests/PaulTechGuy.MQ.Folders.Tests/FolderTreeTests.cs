// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Folders.Tests;

public sealed class FolderTreeTests
{
    /// <summary>Does what the panel's view model will: read each folder the tree asks for, and answer each look.</summary>
    internal static void Settle(FolderTree tree, FolderWork work)
    {
        var probes = new Queue<string>(work.FoldersToProbe);

        foreach (string folder in work.FoldersToReload)
        {
            Load(tree, folder, probes);
        }

        while (probes.TryDequeue(out string? folder))
        {
            tree.SetProbe(folder, FolderProbe.Look(folder, 1000, CancellationToken.None));
        }
    }

    internal static void Load(FolderTree tree, string folder, Queue<string>? probes = null)
    {
        FolderWork work = tree.SetChildren(folder, FolderListing.Read(folder, tree.ShowAllFiles));

        if (probes is not null)
        {
            foreach (string p in work.FoldersToProbe)
            {
                probes.Enqueue(p);
            }
        }
        else
        {
            Settle(tree, work);
        }
    }

    internal static void Open(FolderTree tree, string folder)
    {
        if (tree.Expand(folder))
        {
            Load(tree, folder);
        }
    }

    internal static FolderTree Opened(TempFolder temp, bool showAll = false)
    {
        var tree = new FolderTree(temp.Path, showAll);
        Load(tree, temp.Path);
        return tree;
    }

    private static List<string> Names(FolderTree tree) =>
        [.. FolderRows.Build(tree, RowOptions.Default).Select(r => new string(' ', r.Depth * 2) + r.Name)];

    [Fact]
    public void Folders_that_lead_nowhere_are_not_shown()
    {
        using var temp = new TempFolder();
        temp.File("docs/a.md");
        temp.File("src/Program.cs");
        temp.File("README.md");

        Names(Opened(temp)).ShouldBe(["docs", "README.md"]);
    }

    [Fact]
    public void With_show_all_every_folder_but_the_hidden_ones_is_shown()
    {
        using var temp = new TempFolder();
        temp.File("docs/a.md");
        temp.File("src/Program.cs");
        temp.File(".git/HEAD");

        Names(Opened(temp, showAll: true)).ShouldBe(["docs", "src"]);
    }

    [Fact]
    public void A_folder_is_read_when_first_opened_and_keeps_its_state_after()
    {
        using var temp = new TempFolder();
        temp.File("docs/guide/intro.md");
        temp.File("docs/a.md");
        FolderTree tree = Opened(temp);

        tree.Find(temp.Full("docs"))!.IsLoaded.ShouldBeFalse();

        Open(tree, temp.Full("docs"));
        Open(tree, temp.Full("docs/guide"));

        Names(tree).ShouldBe(["docs", "  guide", "    intro.md", "  a.md"]);

        tree.Collapse(temp.Full("docs"));
        Names(tree).ShouldBe(["docs"]);

        // Opening again shows guide still open, without reading anything.
        tree.Expand(temp.Full("docs")).ShouldBeFalse();
        Names(tree).ShouldBe(["docs", "  guide", "    intro.md", "  a.md"]);
    }

    [Fact]
    public void Reveal_asks_for_each_unread_folder_in_turn_then_opens_the_way()
    {
        using var temp = new TempFolder();
        temp.File("a/b/c/deep.md");
        FolderTree tree = Opened(temp);
        string target = temp.Full("a/b/c/deep.md");

        var asked = new List<string>();

        while (tree.Reveal(target) is { } next)
        {
            asked.Add(next);
            Load(tree, next);
        }

        asked.ShouldBe([temp.Full("a"), temp.Full("a/b"), temp.Full("a/b/c")]);
        Names(tree).ShouldBe(["a", "  b", "    c", "      deep.md"]);
    }

    [Fact]
    public void Reveal_of_a_path_outside_the_root_does_nothing()
    {
        using var temp = new TempFolder();
        FolderTree tree = Opened(temp);

        tree.Reveal(@"Z:\elsewhere\a.md").ShouldBeNull();
    }

    [Fact]
    public void A_created_file_appears_and_applying_it_twice_changes_nothing()
    {
        using var temp = new TempFolder();
        temp.File("a.md");
        FolderTree tree = Opened(temp);

        string created = temp.File("b.md");
        var change = new FolderChange(FolderChangeKind.Created, created);

        tree.Apply([change]);
        IReadOnlyList<FolderRow> once = FolderRows.Build(tree, RowOptions.Default);
        tree.Apply([change]);

        FolderRows.Build(tree, RowOptions.Default).ShouldBe(once);
        Names(tree).ShouldBe(["a.md", "b.md"]);
    }

    [Fact]
    public void The_panels_own_rename_then_the_watchers_report_of_it_lands_once()
    {
        using var temp = new TempFolder();
        temp.File("docs/setup.md");
        FolderTree tree = Opened(temp);
        Open(tree, temp.Full("docs"));

        File.Move(temp.Full("docs/setup.md"), temp.Full("docs/install.md"));
        var rename = new FolderChange(FolderChangeKind.Renamed, temp.Full("docs/install.md"), temp.Full("docs/setup.md"));

        Settle(tree, tree.Apply([rename]));
        Names(tree).ShouldBe(["docs", "  install.md"]);

        Settle(tree, tree.Apply([rename]));
        Names(tree).ShouldBe(["docs", "  install.md"]);
    }

    [Fact]
    public void A_renamed_folder_keeps_its_expansion_and_everything_below_moves_with_it()
    {
        using var temp = new TempFolder();
        temp.File("guide/part/one.md");
        FolderTree tree = Opened(temp);
        Open(tree, temp.Full("guide"));
        Open(tree, temp.Full("guide/part"));

        Directory.Move(temp.Full("guide"), temp.Full("manual"));
        Settle(tree, tree.Apply([new FolderChange(FolderChangeKind.Renamed, temp.Full("manual"), temp.Full("guide"))]));

        Names(tree).ShouldBe(["manual", "  part", "    one.md"]);
        tree.Find(temp.Full("manual/part/one.md")).ShouldNotBeNull();
        tree.Find(temp.Full("guide/part/one.md")).ShouldBeNull();
    }

    [Fact]
    public void Renaming_markdown_to_an_unlisted_extension_takes_it_out_of_the_tree()
    {
        using var temp = new TempFolder();
        temp.File("a.md");
        temp.File("b.md");
        FolderTree tree = Opened(temp);

        File.Move(temp.Full("a.md"), temp.Full("a.txt"));
        Settle(tree, tree.Apply([new FolderChange(FolderChangeKind.Renamed, temp.Full("a.txt"), temp.Full("a.md"))]));

        Names(tree).ShouldBe(["b.md"]);
    }

    [Fact]
    public void Deleting_a_folders_last_document_hides_the_folder()
    {
        using var temp = new TempFolder();
        temp.File("docs/only.md");
        temp.File("docs/code.cs");
        temp.File("top.md");
        FolderTree tree = Opened(temp);
        Open(tree, temp.Full("docs"));

        File.Delete(temp.Full("docs/only.md"));
        Settle(tree, tree.Apply([new FolderChange(FolderChangeKind.Deleted, temp.Full("docs/only.md"))]));

        Names(tree).ShouldBe(["top.md"]);
    }

    [Fact]
    public void Markdown_appearing_below_a_hidden_unread_folder_shows_the_folder()
    {
        using var temp = new TempFolder();
        temp.File("src/deep/Program.cs");
        temp.File("top.md");
        FolderTree tree = Opened(temp);
        Names(tree).ShouldBe(["top.md"]);

        string added = temp.File("src/deep/notes.md");
        Settle(tree, tree.Apply([new FolderChange(FolderChangeKind.Created, added)]));

        Names(tree).ShouldBe(["src", "top.md"]);
    }

    [Fact]
    public void A_new_subfolder_is_shown_once_its_look_finds_something()
    {
        using var temp = new TempFolder();
        temp.File("top.md");
        FolderTree tree = Opened(temp);

        temp.File("fresh/a.md");
        FolderWork work = tree.Apply([new FolderChange(FolderChangeKind.Created, temp.Full("fresh"))]);

        work.FoldersToProbe.ShouldBe([temp.Full("fresh")]);
        Names(tree).ShouldBe(["top.md"]);

        Settle(tree, work);
        Names(tree).ShouldBe(["fresh", "top.md"]);
    }

    [Fact]
    public void While_held_changes_wait_and_are_applied_on_release()
    {
        using var temp = new TempFolder();
        temp.File("a.md");
        FolderTree tree = Opened(temp);

        tree.Hold();
        string created = temp.File("b.md");
        tree.Apply([new FolderChange(FolderChangeKind.Created, created)]).IsEmpty.ShouldBeTrue();
        Names(tree).ShouldBe(["a.md"]);

        Settle(tree, tree.Release());
        Names(tree).ShouldBe(["a.md", "b.md"]);
    }

    [Fact]
    public void Rescan_asks_for_every_loaded_folder_to_be_read_again_without_collapsing()
    {
        using var temp = new TempFolder();
        temp.File("docs/a.md");
        FolderTree tree = Opened(temp);
        Open(tree, temp.Full("docs"));

        temp.File("docs/b.md");
        FolderWork work = tree.Apply([new FolderChange(FolderChangeKind.Rescan, temp.Path)]);

        work.FoldersToReload.ShouldBe([temp.Path, temp.Full("docs")], ignoreOrder: true);
        Settle(tree, work);

        Names(tree).ShouldBe(["docs", "  a.md", "  b.md"]);
    }

    [Fact]
    public void A_changed_gitignore_regrays_what_is_known()
    {
        using var temp = new TempFolder();
        temp.Folder(".git");
        temp.File(".gitignore", "");
        temp.File("draft.md");
        GitIgnoreReader reader = GitIgnoreReader.For(temp.Path);
        var tree = new FolderTree(temp.Path, false, (path, dir) => reader.IsIgnored(path, dir));
        Load(tree, temp.Path);

        tree.Find(temp.Full("draft.md"))!.IsIgnored.ShouldBeFalse();

        File.WriteAllText(temp.Full(".gitignore"), "draft.md\n");
        reader.Forget(temp.Path);
        tree.Apply([new FolderChange(FolderChangeKind.Changed, temp.Full(".gitignore"))]).IgnoreRulesChanged.ShouldBeTrue();

        tree.Find(temp.Full("draft.md"))!.IsIgnored.ShouldBeTrue();
    }
}
