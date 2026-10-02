// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// The part of a sheet a print can use, as a shape rather than a size.
///
/// The print stylesheets hold a diagram to one page, and they are told the page as a ratio
/// because a size would be wrong. Chromium lays a printed page out wider than the paper and
/// scales the result down to fit - by at least a third, and further when something in the
/// document is wider still - so an inch in print layout is not an inch on paper. A limit of
/// nine inches printed under six. The shape of the page area is the one thing the scaling
/// leaves alone, so the stylesheet multiplies its own laid-out width by this.
/// </summary>
public static class PrintArea
{
    /// <summary>Letter with one-inch margins: what the stylesheets assume when told nothing.</summary>
    public const double DefaultRatio = 9.0 / 6.5;

    /// <summary>
    /// Printable height over printable width, between the margins. The default when the
    /// margins leave nothing to print on, so a nonsense setup cannot produce a nonsense limit.
    /// </summary>
    public static double Ratio(
        double widthInches,
        double heightInches,
        double horizontalMarginInches,
        double verticalMarginInches)
    {
        double width = widthInches - (2 * horizontalMarginInches);
        double height = heightInches - (2 * verticalMarginInches);

        return width > 0 && height > 0 ? height / width : DefaultRatio;
    }
}
