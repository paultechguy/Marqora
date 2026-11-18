// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Folders.Tests;

public sealed class RecyclePolicyTests
{
    private const long Megabyte = 1024L * 1024L;

    [Theory]
    [InlineData(DriveType.Fixed, null, null, 10, true)]
    [InlineData(DriveType.Fixed, false, 100L, 10, true)]
    [InlineData(DriveType.Fixed, true, 100L, 10, false)]
    [InlineData(DriveType.Fixed, false, 5L, 10, false)]
    [InlineData(DriveType.Fixed, false, 10L, 10, true)]
    [InlineData(DriveType.Removable, null, null, 10, false)]
    [InlineData(DriveType.Network, false, 100L, 10, false)]
    [InlineData(DriveType.Unknown, null, null, 10, false)]
    public void Only_a_fixed_drive_whose_bin_will_take_it_recycles(
        DriveType drive, bool? nuke, long? capacityMegabytes, long sizeMegabytes, bool recycles) =>
        RecyclePolicy.WillRecycle(new VolumeFacts(drive, nuke, capacityMegabytes), sizeMegabytes * Megabyte)
            .ShouldBe(recycles);
}

public sealed class LaunchArgumentsTests
{
    private const string Here = @"C:\Users\me\notes";

    [Fact]
    public void Relative_paths_are_made_absolute_where_they_were_typed() =>
        LaunchArguments.Qualify(["notes.md", ".", @"..\other\a.md"], Here)
            .ShouldBe([@"C:\Users\me\notes\notes.md", @"C:\Users\me\notes", @"C:\Users\me\other\a.md"]);

    [Fact]
    public void Switches_and_absolute_paths_are_left_alone() =>
        LaunchArguments.Qualify(["--remote-debugging-port=9222", "/safe", @"D:\docs\a.md"], Here)
            .ShouldBe(["--remote-debugging-port=9222", "/safe", @"D:\docs\a.md"]);

    [Fact]
    public void Any_relative_finds_only_paths_that_need_it()
    {
        LaunchArguments.AnyRelative([@"C:\a.md", "--flag"]).ShouldBeFalse();
        LaunchArguments.AnyRelative([@"C:\a.md", "b.md"]).ShouldBeTrue();
        LaunchArguments.AnyRelative([@"\rooted-but-no-drive.md"]).ShouldBeTrue();
    }

    [Theory]
    [InlineData(@"C:\docs\a.md", true)]
    [InlineData(@"\\server\share\a.md", true)]
    [InlineData("a.md", false)]
    [InlineData(".", false)]
    [InlineData(@"C:relative.md", false)]
    [InlineData("--flag", false)]
    [InlineData("", false)]
    public void The_receiver_accepts_only_fully_qualified_paths(string arg, bool accepted) =>
        LaunchArguments.IsAcceptable(arg).ShouldBe(accepted);
}

public sealed class FolderPathsTests
{
    [Fact]
    public void A_folder_is_a_prefix_only_at_a_separator()
    {
        FolderPaths.IsSameOrUnder(@"C:\notes\a.md", @"C:\notes").ShouldBeTrue();
        FolderPaths.IsSameOrUnder(@"C:\notes-old\a.md", @"C:\notes").ShouldBeFalse();
        FolderPaths.IsSameOrUnder(@"C:\NOTES", @"c:\notes\").ShouldBeTrue();
        FolderPaths.IsUnder(@"C:\notes", @"C:\notes").ShouldBeFalse();
        FolderPaths.IsUnder(@"C:\a.md", @"C:\").ShouldBeTrue();
    }

    [Fact]
    public void Rebase_swaps_the_prefix()
    {
        FolderPaths.Rebase(@"C:\docs\guide\a.md", @"C:\docs\guide", @"C:\docs\manual").ShouldBe(@"C:\docs\manual\a.md");
        FolderPaths.Rebase(@"C:\docs\guide", @"C:\docs\guide", @"C:\docs\manual").ShouldBe(@"C:\docs\manual");
        Should.Throw<ArgumentException>(() => FolderPaths.Rebase(@"C:\other\a.md", @"C:\docs", @"C:\x"));
    }

    [Fact]
    public void A_drive_root_keeps_its_separator() => FolderPaths.TrimEnd(@"C:\").ShouldBe(@"C:\");
}
