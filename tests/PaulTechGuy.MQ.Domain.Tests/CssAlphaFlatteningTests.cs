// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

/// <summary>
/// Translucent colors composited onto white, for a clipboard fragment going to an application
/// that would otherwise throw the declaration away.
///
/// The values below are the ones actually in <c>app.css</c> rather than round numbers, so this
/// fails if a token is retinted without the paste being looked at again.
/// </summary>
public sealed class CssAlphaFlatteningTests
{
    /// <summary>
    /// The accent tint, which is the whole reason this exists: the only background on
    /// <c>thead th</c>, and so the difference between a pasted table having a header and not.
    /// </summary>
    [Fact]
    public void The_accent_tint_becomes_the_shade_it_paints_on_white() =>
        CssAlphaFlattening.OverWhite("background: rgba(63, 143, 152, 0.14);")
            .ShouldBe("background: #e4eff1;");

    /// <summary>
    /// The four callout tints. These were failing too; the solid gray on .markdown-alert
    /// underneath them is what stopped anyone noticing.
    /// </summary>
    [Theory]
    [InlineData("rgba(179, 38, 30, 0.08)", "#f9eeed")]   // --mq-danger-soft
    [InlineData("rgba(26, 127, 55, 0.08)", "#edf5ef")]   // --mq-tip-soft
    [InlineData("rgba(130, 80, 223, 0.08)", "#f5f1fc")]  // --mq-important-soft
    [InlineData("rgba(154, 103, 0, 0.10)", "#f5f0e6")]   // --mq-warning-soft
    public void Each_callout_tint_becomes_an_opaque_shade(string rgba, string expected) =>
        CssAlphaFlattening.OverWhite(rgba).ShouldBe(expected);

    /// <summary>
    /// The form a tint takes before AccentDeclarations restates it, and the one the zebra
    /// stripe and the outline rule still use directly.
    /// </summary>
    [Theory]
    [InlineData("color-mix(in srgb, #3f8f98 14%, transparent)", "#e4eff1")]
    [InlineData("color-mix(in srgb, #f6f6f6 55%, transparent)", "#fafafa")]
    [InlineData("color-mix(in srgb, #3f8f98 35%, transparent)", "#bcd8db")]
    public void A_tint_of_one_color_becomes_that_shade(string mix, string expected) =>
        CssAlphaFlattening.OverWhite(mix).ShouldBe(expected);

    [Fact]
    public void Shorthand_hex_is_expanded_before_blending() =>
        CssAlphaFlattening.OverWhite("color-mix(in srgb, #abc 50%, transparent)")
            .ShouldBe(CssAlphaFlattening.OverWhite("color-mix(in srgb, #aabbcc 50%, transparent)"));

    /// <summary>
    /// The trap. Fully transparent and white look identical on this page and behave nothing
    /// alike: turning one into the other paints a white box over whatever the rule was letting
    /// through, which would trade the missing header for a new bug somewhere else.
    /// </summary>
    [Theory]
    [InlineData("rgba(0, 0, 0, 0)")]
    [InlineData("color-mix(in srgb, #3f8f98 0%, transparent)")]
    public void Nothing_at_all_stays_nothing_rather_than_becoming_white(string css) =>
        CssAlphaFlattening.OverWhite(css).ShouldBe("transparent");

    [Fact]
    public void A_fully_opaque_rgba_keeps_its_own_color() =>
        CssAlphaFlattening.OverWhite("rgba(63, 143, 152, 1)").ShouldBe("#3f8f98");

    /// <summary>
    /// A mix of two real colors is a different question, and compositing it onto white is not
    /// the answer to it. Left alone, it reaches Word as the unreadable value it already was.
    /// </summary>
    [Fact]
    public void A_mix_of_two_colors_is_left_alone()
    {
        const string css = "color-mix(in srgb, #3f8f98 40%, #b3261e)";

        CssAlphaFlattening.OverWhite(css).ShouldBe(css);
    }

    /// <summary>
    /// Anything unparseable is passed through rather than guessed at. An alpha above 1 is the
    /// give-away that the pattern has caught something that is not a color.
    /// </summary>
    [Theory]
    [InlineData("rgba(300, 0, 0, 0.5)")]
    [InlineData("rgba(0, 0, 0, 4.2)")]
    [InlineData("color-mix(in oklab, #3f8f98 14%, transparent)")]
    public void Something_this_does_not_understand_is_passed_through(string css) =>
        CssAlphaFlattening.OverWhite(css).ShouldBe(css);

    /// <summary>Hex, keywords and everything else in the stylesheet are none of its business.</summary>
    [Fact]
    public void Opaque_colors_are_untouched()
    {
        const string css = ".mq-preview pre { background: #f0f0f0; border: 1px solid #d8d8d8; }";

        CssAlphaFlattening.OverWhite(css).ShouldBe(css);
    }

    /// <summary>
    /// A stylesheet, not a single value: every occurrence is rewritten in place and the rules
    /// around them are left exactly as they were.
    /// </summary>
    [Fact]
    public void Every_occurrence_in_a_stylesheet_is_rewritten()
    {
        const string css = """
            .mq-preview thead th {
              background: rgba(63, 143, 152, 0.14);
              border-bottom: 2px solid #3f8f98;
            }
            .mq-preview tbody tr:nth-child(even) { background: color-mix(in srgb, #f6f6f6 55%, transparent); }
            """;

        string flattened = CssAlphaFlattening.OverWhite(css);

        flattened.ShouldContain("background: #e4eff1;");
        flattened.ShouldContain("background: #fafafa;");
        flattened.ShouldContain("border-bottom: 2px solid #3f8f98;");
        flattened.ShouldNotContain("rgba(");
        flattened.ShouldNotContain("color-mix(");
    }

    /// <summary>
    /// A box-shadow carries a color in the middle of three lengths. Nothing this is aimed at
    /// draws one, but the value has to stay a valid box-shadow for the parsers that do.
    /// </summary>
    [Fact]
    public void A_color_inside_a_longer_value_is_replaced_in_place() =>
        CssAlphaFlattening.OverWhite("--mq-shadow: 0 2px 12px rgba(0, 0, 0, 0.10);")
            .ShouldBe("--mq-shadow: 0 2px 12px #e6e6e6;");

    [Fact]
    public void An_empty_stylesheet_is_returned_as_it_came() =>
        CssAlphaFlattening.OverWhite(string.Empty).ShouldBe(string.Empty);
}
