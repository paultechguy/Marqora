// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

/// <summary>
/// The one answer to "is this path inside that folder", now that six places ask it.
///
/// Nothing here touches the disk: the question is about how a path resolves, not about what is
/// on the filesystem, and every caller checks existence separately afterwards.
/// </summary>
public sealed class PathContainmentTests
{
    [Theory]
    [InlineData(@"C:\docs", @"C:\docs\logo.png")]
    [InlineData(@"C:\docs", @"C:\docs\images\logo.png")]
    [InlineData(@"C:\docs\", @"C:\docs\logo.png")]
    public void A_path_beneath_the_folder_is_contained(string folder, string candidate) =>
        PathContainment.Contains(folder, candidate).ShouldBeTrue();

    /// <summary>What a reference written as "." resolves to, so it has to count as inside.</summary>
    [Fact]
    public void The_folder_is_contained_by_itself() =>
        PathContainment.Contains(@"C:\docs", @"C:\docs").ShouldBeTrue();

    /// <summary>
    /// The trap the trailing separator exists for. Without it "C:\docs2" starts with "C:\docs"
    /// and a sibling folder reads as being inside.
    /// </summary>
    [Theory]
    [InlineData(@"C:\docs", @"C:\docs2\logo.png")]
    [InlineData(@"C:\docs", @"C:\docsmore\logo.png")]
    public void A_sibling_whose_name_begins_the_same_is_not_contained(string folder, string candidate) =>
        PathContainment.Contains(folder, candidate).ShouldBeFalse();

    [Theory]
    [InlineData(@"C:\docs", @"C:\secrets.txt")]
    [InlineData(@"C:\docs", @"C:\")]
    public void Something_outside_is_not_contained(string folder, string candidate) =>
        PathContainment.Contains(folder, candidate).ShouldBeFalse();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Nothing_is_contained_by_nothing(string? folder) =>
        PathContainment.Contains(folder!, @"C:\docs\logo.png").ShouldBeFalse();

    [Fact]
    public void A_relative_reference_resolves_inside_the_folder() =>
        PathContainment.ResolveWithin(@"C:\docs", "images/logo.png")
            .ShouldBe(@"C:\docs\images\logo.png");

    /// <summary>
    /// Forward slashes, because a markdown reference is written with them whatever the platform
    /// underneath.
    /// </summary>
    [Fact]
    public void Forward_slashes_are_understood() =>
        PathContainment.ResolveWithin(@"C:\docs", "a/b/c.png").ShouldBe(@"C:\docs\a\b\c.png");

    /// <summary>
    /// The case the Folio unpacker leans on: a reference that climbs out resolves to null rather
    /// than to a file somewhere it was never meant to reach.
    /// </summary>
    [Theory]
    [InlineData("../secrets.txt")]
    [InlineData(@"..\secrets.txt")]
    [InlineData("a/../../secrets.txt")]
    public void A_reference_that_climbs_out_resolves_to_nothing(string relative) =>
        PathContainment.ResolveWithin(@"C:\docs", relative).ShouldBeNull();

    /// <summary>
    /// An absolute reference wins over the folder when the two are combined, so it lands outside
    /// and is refused - which is what makes this safe to hand an untrusted entry name.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Windows\System32\drivers\etc\hosts")]
    [InlineData(@"\\server\share\file.txt")]
    public void An_absolute_reference_is_refused(string relative) =>
        PathContainment.ResolveWithin(@"C:\docs", relative).ShouldBeNull();

    [Fact]
    public void A_reference_going_nowhere_resolves_to_nothing()
    {
        PathContainment.ResolveWithin(@"C:\docs", string.Empty).ShouldBeNull();
        PathContainment.ResolveWithin(string.Empty, "logo.png").ShouldBeNull();
    }

    /// <summary>A path this filesystem cannot express is not contained by anything.</summary>
    [Fact]
    public void An_unexpressable_path_is_answered_rather_than_thrown()
    {
        PathContainment.Contains(@"C:\docs", "a\0b").ShouldBeFalse();
        PathContainment.ResolveWithin(@"C:\docs", "a\0b").ShouldBeNull();
    }
}
