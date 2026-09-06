// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// What to do with an image on its way to disk: leave the bytes alone, or re-encode them, and at
/// what size.
/// </summary>
/// <param name="Encode">
/// False means hand the original bytes back untouched. True means decode and write them out
/// again at <see cref="Width"/> by <see cref="Height"/>.
/// </param>
public readonly record struct ImageEncodeStep(bool Encode, uint Width, uint Height)
{
    /// <summary>Nothing to do: the bytes are already what they should be.</summary>
    public static ImageEncodeStep KeepOriginal { get; }
}

/// <summary>
/// The size decision for a pasted image, kept apart from the code that decodes one.
///
/// This is arithmetic and a couple of rules, and it is where both of the bugs in this feature
/// actually lived - so it lives here, in a project a test can reach, rather than inside the WinRT
/// shim where nothing can look at it. What is left up there is decoding and encoding, which need
/// a real imaging stack and a real clipboard.
/// </summary>
public static class ImageScaling
{
    /// <param name="maxWidth">The width cap, or null for none.</param>
    /// <param name="mustReencode">
    /// True when the source bytes are not a format worth keeping and have to be written out again
    /// whatever their size.
    ///
    /// This is the flag that matters. What Windows hands back for a clipboard bitmap is a BMP, so
    /// treating "no resizing needed" as "nothing to do" wrote screenshots to disk uncompressed
    /// and named .bmp - and because most screenshots are narrower than the default cap, that was
    /// most of them. A PNG or a file the user picked passes false: those bytes already are what
    /// they should be, and re-encoding one that needs no resizing throws away the original for
    /// nothing.
    /// </param>
    public static ImageEncodeStep Plan(uint sourceWidth, uint sourceHeight, int? maxWidth, bool mustReencode)
    {
        // A decoder that reports no size has told us nothing to act on. Passing the bytes
        // through is the only safe answer; scaling by zero is not.
        if (sourceWidth == 0 || sourceHeight == 0)
        {
            return ImageEncodeStep.KeepOriginal;
        }

        bool tooWide = maxWidth is { } cap && cap > 0 && sourceWidth > (uint)cap;

        if (!tooWide && !mustReencode)
        {
            return ImageEncodeStep.KeepOriginal;
        }

        uint width = tooWide ? (uint)maxWidth!.Value : sourceWidth;

        return new ImageEncodeStep(true, width, HeightFor(sourceWidth, sourceHeight, width));
    }

    /// <summary>
    /// The height that keeps the picture's shape at a given width.
    ///
    /// Rounded rather than truncated, so a 1999-pixel image does not lose a row, and floored at
    /// one, because a zero-height bitmap is not something an encoder will take. Getting this
    /// wrong does not produce a smaller picture, it produces a squashed one.
    /// </summary>
    private static uint HeightFor(uint sourceWidth, uint sourceHeight, uint width) =>
        (uint)Math.Max(1, Math.Round(sourceHeight * (double)width / sourceWidth));
}
