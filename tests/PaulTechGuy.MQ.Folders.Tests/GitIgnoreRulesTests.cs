// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Folders.Tests;

public sealed class GitIgnoreRulesTests
{
    private static GitIgnoreRules Top(string text) => GitIgnoreRules.Empty.With("", text);

    [Fact]
    public void Comments_and_blank_lines_ignore_nothing()
    {
        GitIgnoreRules rules = Top("# a comment\n\n   \n");

        rules.IsEmpty.ShouldBeTrue();
        rules.IsIgnored("a.md", false).ShouldBeFalse();
    }

    [Fact]
    public void A_name_without_a_slash_matches_at_any_depth()
    {
        GitIgnoreRules rules = Top("draft.md");

        rules.IsIgnored("draft.md", false).ShouldBeTrue();
        rules.IsIgnored("docs/guide/draft.md", false).ShouldBeTrue();
        rules.IsIgnored("docs/final.md", false).ShouldBeFalse();
    }

    [Fact]
    public void A_leading_slash_anchors_to_the_folder_of_the_file()
    {
        GitIgnoreRules rules = Top("/draft.md");

        rules.IsIgnored("draft.md", false).ShouldBeTrue();
        rules.IsIgnored("docs/draft.md", false).ShouldBeFalse();
    }

    [Fact]
    public void A_slash_in_the_middle_anchors_too()
    {
        GitIgnoreRules rules = Top("docs/*.tmp.md");

        rules.IsIgnored("docs/a.tmp.md", false).ShouldBeTrue();
        rules.IsIgnored("other/docs/a.tmp.md", false).ShouldBeFalse();
    }

    [Fact]
    public void Star_and_question_mark_stay_inside_one_name()
    {
        GitIgnoreRules rules = Top("/*.md\n/note?.txt");

        rules.IsIgnored("a.md", false).ShouldBeTrue();
        rules.IsIgnored("sub/a.md", false).ShouldBeFalse();
        rules.IsIgnored("note1.txt", false).ShouldBeTrue();
        rules.IsIgnored("note12.txt", false).ShouldBeFalse();
    }

    [Fact]
    public void Double_star_matches_any_number_of_folders()
    {
        GitIgnoreRules leading = Top("**/build/out.md");
        leading.IsIgnored("build/out.md", false).ShouldBeTrue();
        leading.IsIgnored("a/b/build/out.md", false).ShouldBeTrue();

        GitIgnoreRules middle = Top("docs/**/draft.md");
        middle.IsIgnored("docs/draft.md", false).ShouldBeTrue();
        middle.IsIgnored("docs/a/b/draft.md", false).ShouldBeTrue();
        middle.IsIgnored("other/draft.md", false).ShouldBeFalse();

        GitIgnoreRules trailing = Top("generated/**");
        trailing.IsIgnored("generated/a.md", false).ShouldBeTrue();
        trailing.IsIgnored("generated/x/y.md", false).ShouldBeTrue();
    }

    [Fact]
    public void A_trailing_slash_matches_folders_only()
    {
        GitIgnoreRules rules = Top("out/");

        rules.IsIgnored("out", isDirectory: true).ShouldBeTrue();
        rules.IsIgnored("out", isDirectory: false).ShouldBeFalse();
        rules.IsIgnored("out/a.md", false).ShouldBeTrue();
    }

    [Fact]
    public void The_last_matching_rule_wins_and_negation_brings_a_file_back()
    {
        GitIgnoreRules rules = Top("*.md\n!keep.md");

        rules.IsIgnored("drop.md", false).ShouldBeTrue();
        rules.IsIgnored("keep.md", false).ShouldBeFalse();

        GitIgnoreRules reversed = Top("!keep.md\n*.md");
        reversed.IsIgnored("keep.md", false).ShouldBeTrue();
    }

    [Fact]
    public void Nothing_comes_back_out_of_an_ignored_folder()
    {
        GitIgnoreRules rules = Top("drafts/\n!drafts/keep.md");

        rules.IsIgnored("drafts/keep.md", false).ShouldBeTrue();
    }

    [Fact]
    public void A_deeper_file_comes_after_and_is_relative_to_its_own_folder()
    {
        GitIgnoreRules rules = Top("*.md").With("docs", "!/keep.md");

        rules.IsIgnored("docs/keep.md", false).ShouldBeFalse();
        rules.IsIgnored("docs/sub/keep.md", false).ShouldBeTrue();
        rules.IsIgnored("keep.md", false).ShouldBeTrue();
    }

    [Fact]
    public void Matching_ignores_case_as_git_does_on_windows() =>
        Top("Draft.MD").IsIgnored("docs/draft.md", false).ShouldBeTrue();

    [Fact]
    public void Character_classes_are_not_read_and_fail_toward_not_ignored() =>
        Top("draft[0-9].md").IsIgnored("draft1.md", false).ShouldBeFalse();

    [Fact]
    public void Escapes_for_a_leading_hash_or_bang_and_a_trailing_space()
    {
        Top(@"\#notes.md").IsIgnored("#notes.md", false).ShouldBeTrue();
        Top(@"\!important.md").IsIgnored("!important.md", false).ShouldBeTrue();
        Top("trailing.md   ").IsIgnored("trailing.md", false).ShouldBeTrue();
    }

    [Fact]
    public void Windows_line_endings_are_read() =>
        Top("a.md\r\nb.md\r\n").IsIgnored("b.md", false).ShouldBeTrue();

    [Fact]
    public void The_reader_finds_the_repository_above_the_root_and_reads_top_down()
    {
        using var temp = new TempFolder();
        temp.Folder(".git");
        temp.File(".gitignore", "*.draft.md\n");
        temp.File("docs/.gitignore", "!keep.draft.md\nprivate/\n");
        string drop = temp.File("docs/a.draft.md");
        string keep = temp.File("docs/keep.draft.md");
        string privateFolder = temp.Folder("docs/private");
        string plain = temp.File("docs/plain.md");

        GitIgnoreReader reader = GitIgnoreReader.For(temp.Full("docs"));

        reader.RepositoryTop.ShouldBe(temp.Path);
        reader.IsIgnored(drop, false).ShouldBeTrue();
        reader.IsIgnored(keep, false).ShouldBeFalse();
        reader.IsIgnored(privateFolder, true).ShouldBeTrue();
        reader.IsIgnored(plain, false).ShouldBeFalse();
    }

    [Fact]
    public void Outside_a_repository_nothing_is_ignored()
    {
        using var temp = new TempFolder();
        temp.File(".gitignore", "*.md\n");
        string file = temp.File("a.md");

        GitIgnoreReader reader = GitIgnoreReader.For(temp.Path);

        // The temp folder itself is not inside a repository, unless the machine's temp folder is.
        if (reader.RepositoryTop is null)
        {
            reader.IsIgnored(file, false).ShouldBeFalse();
        }
    }

    [Fact]
    public void Forget_reads_a_changed_gitignore_again()
    {
        using var temp = new TempFolder();
        temp.Folder(".git");
        temp.File(".gitignore", "");
        string file = temp.File("a.md");

        GitIgnoreReader reader = GitIgnoreReader.For(temp.Path);
        reader.IsIgnored(file, false).ShouldBeFalse();

        File.WriteAllText(temp.Full(".gitignore"), "a.md\n");
        reader.IsIgnored(file, false).ShouldBeFalse();

        reader.Forget(temp.Path);
        reader.IsIgnored(file, false).ShouldBeTrue();
    }
}
