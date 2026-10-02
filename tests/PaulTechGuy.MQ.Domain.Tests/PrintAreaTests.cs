// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

public class PrintAreaTests
{
    [Fact]
    public void Letter_with_one_inch_margins_is_nine_over_six_and_a_half()
    {
        PrintArea.Ratio(8.5, 11, 1, 1).ShouldBe(9.0 / 6.5, 1e-9);
    }

    [Fact]
    public void The_stylesheet_fallback_is_the_same_letter_page()
    {
        // app.css and diagram.css spell this out as 1.3846 for a print the host did not prepare.
        PrintArea.DefaultRatio.ShouldBe(1.3846, 1e-4);
    }

    [Fact]
    public void Landscape_is_wider_than_it_is_tall()
    {
        PrintArea.Ratio(11, 8.5, 1, 1).ShouldBe(6.5 / 9.0, 1e-9);
    }

    [Fact]
    public void Unequal_margins_change_the_shape()
    {
        // Moderate and Wide change the measure without changing the page.
        PrintArea.Ratio(8.5, 11, 0.75, 1).ShouldBe(9.0 / 7.0, 1e-9);
    }

    [Theory]
    [InlineData(8.5, 11, 4.25, 1)]
    [InlineData(8.5, 11, 1, 5.5)]
    [InlineData(0, 0, 0, 0)]
    public void Margins_that_leave_nothing_fall_back_to_the_default(
        double width, double height, double horizontal, double vertical)
    {
        PrintArea.Ratio(width, height, horizontal, vertical).ShouldBe(PrintArea.DefaultRatio);
    }
}
