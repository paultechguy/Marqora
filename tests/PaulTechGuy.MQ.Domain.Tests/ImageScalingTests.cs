// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

/// <summary>
/// The size and format decisions behind a pasted image.
///
/// Two shipped bugs came from this arithmetic, and both are pinned below: a screenshot written
/// as an uncompressed .bmp, and a thumbnail squashed out of shape. The imaging calls themselves
/// need WinRT and a real clipboard and are not testable here - which is exactly why the thinking
/// was moved out of them.
/// </summary>
public class ImageScalingTests
{
    private const int Cap = 1920;

    // ------------------------------------------------------- the .bmp bug

    [Fact]
    public void A_bitmap_narrower_than_the_cap_is_still_re_encoded()
    {
        // The regression. What Windows hands back for a clipboard bitmap is a BMP, so treating
        // "no resizing needed" as "nothing to do" wrote 1080p screenshots to disk uncompressed
        // and called them .bmp - and most screenshots are narrower than the cap, so it was most
        // of them.
        ImageEncodeStep step = ImageScaling.Plan(1920, 1080, Cap, mustReencode: true);

        step.Encode.ShouldBeTrue();
        step.Width.ShouldBe(1920u);
        step.Height.ShouldBe(1080u);
    }

    [Fact]
    public void A_bitmap_is_re_encoded_even_with_no_cap_at_all()
    {
        // A BMP has to become something before it can be a file, cap or no cap.
        ImageScaling.Plan(800, 600, maxWidth: null, mustReencode: true).Encode.ShouldBeTrue();
    }

    [Fact]
    public void A_png_narrower_than_the_cap_keeps_every_byte_it_arrived_with()
    {
        // The other half of the same rule: these bytes already are what they should be, and
        // re-encoding one that needs no resizing throws away the original for nothing.
        ImageScaling.Plan(1200, 800, Cap, mustReencode: false).ShouldBe(ImageEncodeStep.KeepOriginal);
    }

    [Fact]
    public void A_picked_file_with_no_cap_is_left_alone()
    {
        ImageScaling.Plan(4000, 3000, maxWidth: null, mustReencode: false)
            .ShouldBe(ImageEncodeStep.KeepOriginal);
    }

    // ------------------------------------------------- the aspect ratio

    [Fact]
    public void Scaling_down_keeps_the_shape()
    {
        // 3840x2160 is 16:9 and must still be 16:9 at 1920.
        ImageEncodeStep step = ImageScaling.Plan(3840, 2160, Cap, mustReencode: true);

        step.Width.ShouldBe(1920u);
        step.Height.ShouldBe(1080u);
    }

    [Fact]
    public void A_portrait_image_stays_portrait()
    {
        ImageEncodeStep step = ImageScaling.Plan(2400, 3600, 1200, mustReencode: false);

        step.Width.ShouldBe(1200u);
        step.Height.ShouldBe(1800u);
    }

    [Fact]
    public void The_height_is_rounded_rather_than_truncated()
    {
        // 1999 -> 1000 halves the height of 999 to 499.5. Truncating loses a row every time.
        ImageScaling.Plan(1999, 999, 1000, mustReencode: false).Height.ShouldBe(500u);
    }

    [Fact]
    public void An_extremely_wide_image_still_gets_at_least_one_row()
    {
        // A 10000x3 rule or divider scaled to 320 wide computes to less than a pixel high, and
        // no encoder will take a zero-height bitmap.
        ImageScaling.Plan(10000, 3, 320, mustReencode: false).Height.ShouldBe(1u);
    }

    // --------------------------------------------------------- the edges

    [Fact]
    public void An_image_exactly_at_the_cap_is_not_scaled()
    {
        ImageScaling.Plan(1920, 1080, Cap, mustReencode: false).ShouldBe(ImageEncodeStep.KeepOriginal);
    }

    [Fact]
    public void One_pixel_over_the_cap_is_scaled()
    {
        ImageScaling.Plan(1921, 1080, Cap, mustReencode: false).Encode.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0u, 100u)]
    [InlineData(100u, 0u)]
    [InlineData(0u, 0u)]
    public void A_decoder_that_reports_no_size_is_passed_through_rather_than_divided_by(
        uint width,
        uint height)
    {
        // Scaling by zero is not an answer, and neither is throwing on the way to writing a file.
        ImageScaling.Plan(width, height, Cap, mustReencode: true).ShouldBe(ImageEncodeStep.KeepOriginal);
    }

    [Fact]
    public void A_nonsense_cap_is_ignored_rather_than_producing_a_zero_wide_image()
    {
        // Only reachable through a hand-edited settings file, which is exactly why it is here.
        ImageScaling.Plan(1000, 500, maxWidth: 0, mustReencode: false)
            .ShouldBe(ImageEncodeStep.KeepOriginal);
    }
}
