// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

/// <summary>A window's first size and place, on displays of different scales and sizes.</summary>
public sealed class WindowSizingTests
{
    [Fact]
    public void At_100_percent_the_design_size_stands()
    {
        WindowSizing.FirstOpen(940, 660, 720, 460, 1.0, 1920, 1040).ShouldBe((940, 660));
    }

    [Fact]
    public void At_150_percent_the_content_keeps_the_room_it_was_designed_for()
    {
        WindowSizing.FirstOpen(940, 660, 720, 460, 1.5, 2240, 1330).ShouldBe((1410, 990));
    }

    [Fact]
    public void A_small_display_holds_the_window_to_its_work_area()
    {
        WindowSizing.FirstOpen(940, 660, 720, 460, 1.5, 1366, 720).ShouldBe((1366, 720));
    }

    [Fact]
    public void The_minimum_is_a_floor()
    {
        WindowSizing.FirstOpen(500, 300, 720, 460, 1.0, 1920, 1040).ShouldBe((720, 460));
    }

    [Fact]
    public void The_display_beats_the_minimum()
    {
        WindowSizing.FirstOpen(940, 660, 720, 460, 1.0, 640, 400).ShouldBe((640, 400));
    }

    [Theory]
    [InlineData(100, 200, 0, 1000, 100)] // Already inside: left alone.
    [InlineData(900, 200, 0, 1000, 800)] // Runs off the far edge: moved back just enough.
    [InlineData(-50, 200, 0, 1000, 0)] // Before the area: moved in.
    [InlineData(300, 1200, 0, 1000, 0)] // Larger than the area: the start stays on screen.
    [InlineData(2900, 200, 1920, 1000, 2720)] // A second monitor has its own origin.
    public void Keep_inside_moves_no_further_than_it_has_to(int position, int size, int start, int area, int expected)
    {
        WindowSizing.KeepInside(position, size, start, area).ShouldBe(expected);
    }
}
