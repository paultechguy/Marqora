// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Folders.Tests;

/// <summary>
/// WP-0's first observation, kept in the suite: does .NET rename a file or a folder when only the
/// case of its name changes? Windows treats <c>notes.md</c> and <c>Notes.md</c> as one name, and
/// older runtimes refused such a move as "source and destination are the same". If either test
/// fails after a runtime upgrade, the workspace's rename has to go through a temporary name.
/// </summary>
public sealed class CaseOnlyMoveTests
{
    [Fact]
    public void A_file_can_be_renamed_by_case_alone()
    {
        using var temp = new TempFolder();
        string from = temp.File("notes.md", "text");
        string to = temp.Full("Notes.md");

        File.Move(from, to);

        Directory.GetFiles(temp.Path).Select(Path.GetFileName).ShouldBe(["Notes.md"]);
        File.ReadAllText(to).ShouldBe("text");
    }

    [Fact]
    public void A_folder_can_be_renamed_by_case_alone()
    {
        using var temp = new TempFolder();
        temp.File("guide/intro.md", "text");

        Directory.Move(temp.Full("guide"), temp.Full("Guide"));

        Directory.GetDirectories(temp.Path).Select(Path.GetFileName).ShouldBe(["Guide"]);
        File.Exists(temp.Full("Guide/intro.md")).ShouldBeTrue();
    }
}
