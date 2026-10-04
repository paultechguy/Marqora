// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace PaulTechGuy.MQ.Themes;

/// <summary>
/// WCAG 2 contrast: the ratio between two colors' relative luminance, and the three bars a
/// slot is held to.
///
/// Every theme meets these in both modes, Default included. Default reached them by darkening
/// the handful of today's colors that did not - the link, two callout titles, the faintest
/// heading and list marker, and four of GitHub's syntax colors, which were chosen for a white
/// page and sit on a gray one here - each kept to its hue and darkened only as far as it had
/// to go.
/// </summary>
public static class Contrast
{
    /// <summary>Body-size text: AA's 4.5:1.</summary>
    public const double Text = 4.5;

    /// <summary>Large text - H1 to H3 - which AA lets off at 3:1.</summary>
    public const double LargeText = 3.0;

    /// <summary>A control rather than text, such as a task checkbox: AA's 3:1 for non-text.</summary>
    public const double NonText = 3.0;

    /// <summary>The contrast ratio of two <c>#rrggbb</c> colors, from 1 to 21, in either order.</summary>
    public static double Ratio(string first, string second)
    {
        double a = Luminance(first);
        double b = Luminance(second);

        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(string hex) =>
        (0.2126 * Linear(hex, 1)) + (0.7152 * Linear(hex, 3)) + (0.0722 * Linear(hex, 5));

    private static double Linear(string hex, int at)
    {
        double channel = int.Parse(hex.AsSpan(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;

        return channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }
}
