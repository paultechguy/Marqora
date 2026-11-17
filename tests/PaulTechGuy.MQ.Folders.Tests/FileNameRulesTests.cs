// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Folders.Tests;

public sealed class FileNameRulesTests
{
    private static NameCheck Check(string folder, string oldName, string newName, bool isFolder = false, bool showAll = false) =>
        FileNameRules.Check(folder, oldName, newName, isFolder, showAll);

    [Fact]
    public void An_ordinary_new_name_is_fine()
    {
        using var temp = new TempFolder();
        temp.File("setup.md");

        NameCheck check = Check(temp.Path, "setup.md", "install.md");

        check.IsAcceptable.ShouldBeTrue();
        check.Message.ShouldBeNull();
        check.IsCaseOnly.ShouldBeFalse();
        check.LeavesTree.ShouldBeFalse();
    }

    [Fact]
    public void The_same_name_is_unchanged_not_an_error()
    {
        using var temp = new TempFolder();

        Check(temp.Path, "a.md", "a.md").Problem.ShouldBe(NameProblem.Unchanged);
    }

    [Theory]
    [InlineData("", NameProblem.Empty)]
    [InlineData("   ", NameProblem.Empty)]
    [InlineData("a:b.md", NameProblem.IllegalCharacter)]
    [InlineData("a?.md", NameProblem.IllegalCharacter)]
    [InlineData("a/b.md", NameProblem.IllegalCharacter)]
    [InlineData("a\tb.md", NameProblem.IllegalCharacter)]
    [InlineData("notes.", NameProblem.TrailingDotOrSpace)]
    [InlineData("notes.md ", NameProblem.TrailingDotOrSpace)]
    [InlineData("CON", NameProblem.ReservedName)]
    [InlineData("con.md", NameProblem.ReservedName)]
    [InlineData("Com1.tar.md", NameProblem.ReservedName)]
    [InlineData("LPT9", NameProblem.ReservedName)]
    [InlineData("NUL .md", NameProblem.ReservedName)]
    public void Names_windows_refuses_are_refused_with_a_reason(string name, NameProblem problem)
    {
        using var temp = new TempFolder();

        NameCheck check = Check(temp.Path, "a.md", name);

        check.Problem.ShouldBe(problem);
        check.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("console.md")]
    [InlineData("COM10.md")]
    [InlineData("auxiliary.md")]
    public void Names_that_only_start_like_a_reserved_one_are_fine(string name)
    {
        using var temp = new TempFolder();

        Check(temp.Path, "a.md", name).IsAcceptable.ShouldBeTrue();
    }

    [Fact]
    public void A_name_over_255_characters_is_too_long()
    {
        using var temp = new TempFolder();

        Check(temp.Path, "a.md", new string('x', 253) + ".md").Problem.ShouldBe(NameProblem.TooLong);
    }

    [Fact]
    public void A_name_already_taken_by_a_file_or_a_folder_is_refused()
    {
        using var temp = new TempFolder();
        temp.File("a.md");
        temp.File("b.md");
        temp.Folder("docs");

        Check(temp.Path, "a.md", "B.md").Problem.ShouldBe(NameProblem.AlreadyTaken);
        Check(temp.Path, "a.md", "docs").Problem.ShouldBe(NameProblem.AlreadyTaken);
    }

    [Fact]
    public void A_case_only_rename_is_allowed_and_said_to_be_one()
    {
        using var temp = new TempFolder();
        temp.File("readme.md");

        NameCheck check = Check(temp.Path, "readme.md", "README.md");

        check.IsAcceptable.ShouldBeTrue();
        check.IsCaseOnly.ShouldBeTrue();
    }

    [Fact]
    public void Giving_markdown_an_unlisted_extension_says_it_will_leave_the_tree()
    {
        using var temp = new TempFolder();
        temp.File("a.md");

        Check(temp.Path, "a.md", "a.txt").LeavesTree.ShouldBeTrue();
        Check(temp.Path, "a.md", "a.txt", showAll: true).LeavesTree.ShouldBeFalse();
        Check(temp.Path, "a.md", "a.markdown").LeavesTree.ShouldBeFalse();
    }

    [Fact]
    public void A_folder_never_leaves_the_tree_by_its_name()
    {
        using var temp = new TempFolder();
        temp.Folder("docs.md");

        Check(temp.Path, "docs.md", "docs.txt", isFolder: true).LeavesTree.ShouldBeFalse();
    }
}
