// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

public class ClipboardImageTierTests
{
    [Fact]
    public void A_copied_file_wins_over_everything_else()
    {
        // Explorer puts the file and a preview bitmap on together. Taking the file is the only
        // path that re-encodes nothing at all.
        ClipboardImageTiers.Choose(hasFiles: true, hasPng: true, hasBitmap: true)
            .ShouldBe(ClipboardImageTier.Files);
    }

    [Fact]
    public void A_raw_png_wins_over_the_bitmap()
    {
        // What a browser copy carries. The PNG keeps whatever transparency it had; the bitmap
        // beside it does not.
        ClipboardImageTiers.Choose(hasFiles: false, hasPng: true, hasBitmap: true)
            .ShouldBe(ClipboardImageTier.Png);
    }

    [Fact]
    public void The_bitmap_is_the_fallback_every_app_can_produce()
    {
        ClipboardImageTiers.Choose(hasFiles: false, hasPng: false, hasBitmap: true)
            .ShouldBe(ClipboardImageTier.Bitmap);
    }

    [Fact]
    public void A_clipboard_with_no_picture_on_it_offers_nothing()
    {
        // The caller falls through to a text paste on this, rather than treating it as a failure.
        ClipboardImageTiers.Choose(hasFiles: false, hasPng: false, hasBitmap: false)
            .ShouldBe(ClipboardImageTier.None);
    }

    [Theory]
    [InlineData(ClipboardImageTier.Files, false)]
    [InlineData(ClipboardImageTier.Png, false)]
    [InlineData(ClipboardImageTier.None, false)]
    [InlineData(ClipboardImageTier.Bitmap, true)]
    public void Only_the_bitmap_has_to_be_written_out_again(ClipboardImageTier tier, bool expected)
    {
        // Pinned against the two decisions drifting apart: the bitmap is a BMP and cannot be
        // stored as one, and nothing else needs touching. Getting this pair out of step is what
        // produced .bmp screenshots.
        ClipboardImageTiers.MustReencode(tier).ShouldBe(expected);
    }
}
