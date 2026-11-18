// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Folders.Tests;

public sealed class FolderChangeCoalescerTests
{
    private const string Root = @"C:\repo\docs";
    private static readonly DateTimeOffset T0 = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    private static FolderChange Created(string relative) => new(FolderChangeKind.Created, Path.Combine(Root, relative));

    private static FolderChange Changed(string relative) => new(FolderChangeKind.Changed, Path.Combine(Root, relative));

    [Fact]
    public void Nothing_is_handed_over_until_the_folder_has_been_quiet()
    {
        var coalescer = new FolderChangeCoalescer(Root);
        coalescer.Add(Created("a.md"), T0);
        coalescer.Add(Created("b.md"), T0.AddMilliseconds(200));

        coalescer.TakeIfQuiet(T0.AddMilliseconds(400)).ShouldBeNull();
        coalescer.TakeIfQuiet(T0.AddMilliseconds(500)).ShouldBe([Created("a.md"), Created("b.md")]);
        coalescer.HasPending.ShouldBeFalse();
    }

    [Fact]
    public void Changes_inside_hidden_folders_are_dropped_before_they_count()
    {
        var coalescer = new FolderChangeCoalescer(Root);
        coalescer.Add(Created(@".git\objects\ab\cdef"), T0);
        coalescer.Add(Created(@"site\node_modules\pkg\README.md"), T0);

        coalescer.HasPending.ShouldBeFalse();
    }

    [Fact]
    public void A_hidden_folder_itself_and_a_dot_file_still_count()
    {
        var coalescer = new FolderChangeCoalescer(Root);
        coalescer.Add(Created(".gitignore"), T0);
        coalescer.Add(Created("bin"), T0);

        coalescer.TakeIfQuiet(T0.AddSeconds(1))!.Count.ShouldBe(2);
    }

    [Fact]
    public void Repeated_writes_to_one_file_are_one_change()
    {
        var coalescer = new FolderChangeCoalescer(Root);
        coalescer.Add(Changed("a.md"), T0);
        coalescer.Add(Changed("a.md"), T0);
        coalescer.Add(Changed("a.md"), T0);

        coalescer.TakeIfQuiet(T0.AddSeconds(1)).ShouldBe([Changed("a.md")]);
    }

    [Fact]
    public void A_rescan_replaces_everything_waiting_and_comes_at_most_every_five_seconds()
    {
        var coalescer = new FolderChangeCoalescer(Root);
        var rescan = new FolderChange(FolderChangeKind.Rescan, Root);

        coalescer.Add(Created("a.md"), T0);
        coalescer.Add(rescan, T0);
        coalescer.TakeIfQuiet(T0.AddSeconds(1)).ShouldBe([rescan]);

        coalescer.Add(rescan, T0.AddSeconds(2));
        coalescer.TakeIfQuiet(T0.AddSeconds(3)).ShouldBeNull();
        coalescer.DueIn(T0.AddSeconds(3)).ShouldBe(TimeSpan.FromSeconds(3));
        coalescer.TakeIfQuiet(T0.AddSeconds(6)).ShouldBe([rescan]);
    }
}
