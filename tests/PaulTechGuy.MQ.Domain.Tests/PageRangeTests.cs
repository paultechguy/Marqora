// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

public class PageRangeTests
{
    // ------------------------------------------------------------ accepted

    [Theory]
    [InlineData("3", "3")]
    [InlineData("2-5", "2-5")]
    [InlineData("1,4-6", "1,4-6")]
    [InlineData("  2 - 5  ", "2-5")]
    [InlineData("1, 4-6, 9", "1,4-6,9")]
    [InlineData("7-7", "7-7")]
    public void A_range_the_printer_can_read_comes_back_normalised(string typed, string expected)
    {
        PageRange.TryParse(typed, out string normalised, out string error).ShouldBeTrue();

        normalised.ShouldBe(expected);
        error.ShouldBeEmpty();
    }

    /// <summary>
    /// "5-" is the print API's own way of saying "from five to the end". No page count is
    /// known while the dialog is open, so it is passed on as typed rather than resolved.
    /// </summary>
    [Fact]
    public void An_open_ended_range_is_kept()
    {
        PageRange.TryParse("5-", out string normalised, out _).ShouldBeTrue();

        normalised.ShouldBe("5-");
    }

    [Fact]
    public void Empty_parts_between_commas_are_dropped()
    {
        PageRange.TryParse("1,,3", out string normalised, out _).ShouldBeTrue();

        normalised.ShouldBe("1,3");
    }

    // ------------------------------------------------------------ rejected

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",")]
    public void Nothing_typed_is_not_a_range(string? typed)
    {
        PageRange.TryParse(typed, out _, out string error).ShouldBeFalse();

        error.ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("2-abc")]
    [InlineData("1-2-3")]
    [InlineData("-4")]
    [InlineData("2.5")]
    [InlineData("+3")]
    public void Anything_that_is_not_pages_is_refused(string typed) =>
        PageRange.TryParse(typed, out _, out _).ShouldBeFalse();

    /// <summary>
    /// Pages count from one. Zero is refused rather than nudged to one: the user meant
    /// something by typing it, and printing a different range instead is a guess.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("0-5")]
    public void Page_zero_does_not_exist(string typed) =>
        PageRange.TryParse(typed, out _, out _).ShouldBeFalse();

    [Fact]
    public void A_range_that_ends_before_it_starts_is_refused()
    {
        PageRange.TryParse("9-2", out _, out string error).ShouldBeFalse();

        error.ShouldContain("ends before it starts");
    }

    [Fact]
    public void One_bad_part_refuses_the_whole_range()
    {
        PageRange.TryParse("1-3,nonsense,7", out string normalised, out _).ShouldBeFalse();

        normalised.ShouldBeEmpty();
    }

    // ------------------------------------------------------- what we tell the user

    /// <summary>
    /// The print dialog shows <see cref="PageRange.Examples"/> in a tooltip beside the Pages
    /// box, as the answer to "what may I type here". Nothing else checks that the answer is
    /// true, and an example the parser refuses is worse than no example at all: it sends the
    /// reader to type something that disables the Print button.
    ///
    /// Each is asserted to come back unchanged as well as accepted, so an example written in
    /// a form the parser silently rewrites - "1, 4-6" rather than "1,4-6" - is caught too.
    /// What is shown should be what goes to the printer.
    /// </summary>
    [Fact]
    public void Every_documented_example_is_one_the_parser_accepts()
    {
        PageRange.Examples.ShouldNotBeEmpty();

        foreach ((string syntax, string meaning) in PageRange.Examples)
        {
            PageRange.TryParse(syntax, out string normalised, out string error)
                .ShouldBeTrue($"\"{syntax}\" is offered as an example but the parser refuses it: {error}");

            normalised.ShouldBe(syntax);
            meaning.ShouldNotBeNullOrWhiteSpace();
        }
    }
}
