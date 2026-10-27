// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Cli.Outputs;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Verbs;

/// <summary>
/// Amendment A5c: --overwrite replaces a folder's contents, never one of the folders that could not
/// be put back, and never in a way that leaves nothing.
/// </summary>
public sealed class FolderReplacementTests
{
    [Fact]
    public void A_drive_root_is_never_emptied()
    {
        string root = Path.GetPathRoot(Path.GetTempPath())!;

        FolderReplacement.Refusal(root, [], Path.GetTempPath()).ShouldNotBeNull().ShouldContain("drive root");
    }

    [Fact]
    public void The_profile_folder_is_never_emptied()
    {
        string profile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);

        FolderReplacement.Refusal(profile, [], Path.GetTempPath()).ShouldNotBeNull().ShouldContain("profile folder");
    }

    [Fact]
    public void A_folder_whose_name_only_begins_like_an_input_s_folder_may_be_emptied()
    {
        using var workspace = new Workspace();

        FolderReplacement.Refusal(workspace.At("doc"), [workspace.At("docs/guide.md")], workspace.Root).ShouldBeNull();
    }

    [Fact]
    public async Task A_fill_that_fails_leaves_the_folder_as_it_was_and_nothing_beside_it()
    {
        using var workspace = new Workspace();
        workspace.Text("Set/mine.md", "mine\n");

        await Should.ThrowAsync<IOException>(() => FolderReplacement.ReplaceAsync(workspace.At("Set"), overwrite: true, pending =>
        {
            Directory.CreateDirectory(pending);
            File.WriteAllText(Path.Combine(pending, "half.md"), "half");

            throw new IOException("disk full");
        }));

        workspace.Read("Set/mine.md").ShouldBe("mine\n");
        Directory.GetFileSystemEntries(workspace.Root).Select(Path.GetFileName).ShouldBe(["Set"]);
    }

    [Fact]
    public async Task Without_overwrite_a_folder_that_filled_up_meanwhile_is_not_taken_away()
    {
        using var workspace = new Workspace();
        Directory.CreateDirectory(workspace.At("Set"));

        await Should.ThrowAsync<IOException>(() => FolderReplacement.ReplaceAsync(workspace.At("Set"), overwrite: false, pending =>
        {
            // Something arrives in the folder after it was checked and found empty.
            File.WriteAllText(workspace.At("Set/arrived.md"), "arrived");
            Directory.CreateDirectory(pending);

            return Task.CompletedTask;
        }));

        File.Exists(workspace.At("Set/arrived.md")).ShouldBeTrue();
    }
}
