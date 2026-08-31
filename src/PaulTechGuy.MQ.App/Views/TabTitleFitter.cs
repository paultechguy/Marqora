// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using Windows.UI.Text;
using Microsoft.UI.Xaml.Controls;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// Shortens a tab title from the middle so it fits the tab, keeping both ends.
///
/// WinUI trims text from the end and nowhere else, which is the wrong end for a filename:
/// "GRADE-PREDICTION-ARCHITECTURE.md" cut to "GRADE-PREDICT..." loses the extension and,
/// worse, looks identical to every other file that starts the same way. Taking the middle
/// out instead gives "GRADE-PRED...TURE.md", which still says what kind of file it is and
/// still tells two similar names apart.
///
/// Only worth doing with equal-width tabs. There the tab's width does not depend on its
/// title, so replacing the text cannot change the width that decided the truncation — with
/// size-to-content tabs the two would chase each other.
///
/// Every title is measured at one weight, always, whatever weight it is currently drawn at.
/// WinUI draws a selected tab's title heavier, and the same string measures about three per
/// cent wider that way: measured as drawn, a name was fitted to one width while inactive and
/// a wider one while active, so clicking a tab moved every tab after it, and a name sitting
/// near the limit gained an ellipsis on the way in and lost it on the way out.
///
/// The weight is a constant here rather than read off the selected tab, which was the first
/// attempt and is a race. WinUI applies the selected state some time after the selection
/// changes, so a pass that ran in between read the old weight off the new tab and fitted the
/// whole strip to it, and the pass after that fitted the whole strip again to the new one.
/// Two re-fits, both of them moving tabs. A constant cannot lag.
/// </summary>
internal static class TabTitleFitter
{
    private const string Ellipsis = "…";

    /// <summary>The shortest title worth showing; below this the tab shows nothing useful anyway.</summary>
    private const int MinimumKept = 6;

    /// <summary>
    /// Breathing room left at the end of every fit.
    ///
    /// The measuring is done by a TextBlock of our own rather than the one on screen, and in
    /// a proportional font the two do not agree to the pixel — hinting and subpixel
    /// positioning move the last glyph either way. Without a little slack a title that
    /// measured as fitting can render a hair too wide and get clipped after all, which is
    /// the exact thing this class exists to avoid. It also stops the text sitting flush
    /// against the tab's edge.
    /// </summary>
    private const double Slack = 6;

    /// <summary>
    /// Measured off-tree. One instance is reused because creating a TextBlock per candidate
    /// would cost more than the measuring does.
    /// </summary>
    private static readonly TextBlock Ruler = new() { TextWrapping = TextWrapping.NoWrap };

    /// <summary>
    /// Puts the fitted form of <paramref name="full"/> into <paramref name="block"/>, or the
    /// whole thing when it already fits.
    /// </summary>
    /// <param name="prefix">
    /// Put in front of the fitted name and never shortened — the state marker. Left out of
    /// both the fitting and the returned width: the caller books room for it separately, and
    /// books it whether or not one is there, which is what stops a tab resizing when a
    /// document goes dirty.
    /// </param>
    /// <returns>
    /// How wide the name ended up, without the prefix, for working out how many tabs will fit.
    /// </returns>
    public static double Fit(TextBlock block, string full, double available, string prefix)
    {
        if (block is null || string.IsNullOrEmpty(full))
        {
            return 0;
        }

        // Before anything is measured, including the paths that never reach Shorten: the
        // Ruler is shared, and would otherwise still be carrying the last caller's font.
        ApplyFont(block);

        double room = available - Slack;
        string fitted = room <= 0 ? full : Shorten(full, room);
        string text = prefix + fitted;

        // Only assign when it actually differs: setting Text invalidates layout, and this
        // runs from a layout callback.
        if (!string.Equals(block.Text, text, StringComparison.Ordinal))
        {
            block.Text = text;
        }

        return Width(fitted);
    }

    /// <summary>
    /// Room to book on every tab for the state marker, in the font the titles are measured in.
    ///
    /// The widest of them rather than the one currently showing, so that a tab does not resize
    /// when its marker appears, goes, or is replaced by a different one — an exclamation mark
    /// and a refresh arrow are not the same width.
    /// </summary>
    public static double Reserve(TextBlock block, IReadOnlyList<string> markers)
    {
        if (block is null || markers is null)
        {
            return 0;
        }

        ApplyFont(block);

        double widest = 0;

        foreach (string marker in markers)
        {
            widest = Math.Max(widest, Width(marker));
        }

        return widest;
    }

    /// <summary>
    /// The weight every title is measured at: what WinUI draws the selected tab's title in,
    /// read off the running strip - 600 selected against 400 everywhere else, at the same
    /// family and size in both.
    ///
    /// The heavier of the two on purpose. A title measured at the weight it will have when
    /// its tab is active fits in both states; measured at the lighter one it would fit only
    /// until it was clicked. Being wrong here is worth a pixel or two of slack on an inactive
    /// tab, which is invisible, rather than a clipped name on the active one, which is not.
    /// </summary>
    private static readonly FontWeight MeasuringWeight = FontWeights.SemiBold;

    /// <summary>
    /// Points the ruler at a block's font, then overrides the weight with the constant above.
    /// Everything else that decides how wide a glyph comes out is taken from the block, since
    /// the two states agree on all of it.
    /// </summary>
    private static void ApplyFont(TextBlock style)
    {
        Ruler.FontFamily = style.FontFamily;
        Ruler.FontSize = style.FontSize;
        Ruler.FontStyle = style.FontStyle;
        Ruler.FontStretch = style.FontStretch;
        Ruler.CharacterSpacing = style.CharacterSpacing;
        Ruler.FontWeight = MeasuringWeight;
    }

    private static string Shorten(string full, double available)
    {
        if (Width(full) <= available)
        {
            return full;
        }

        // Binary search on how many characters survive. Each candidate is measured rather
        // than estimated, because a proportional font makes any character-count guess wrong
        // by a wide margin on names that are mostly capitals or mostly dots.
        int low = MinimumKept;
        int high = full.Length - 1;
        string best = Build(full, MinimumKept);

        while (low <= high)
        {
            int mid = (low + high) / 2;
            string candidate = Build(full, mid);

            if (Width(candidate) <= available)
            {
                best = candidate;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return best;
    }

    /// <summary>
    /// Keeps <paramref name="kept"/> characters, split either side of the ellipsis. The tail
    /// gets the smaller half so the extension survives without crowding out the start.
    /// </summary>
    private static string Build(string full, int kept)
    {
        if (kept >= full.Length)
        {
            return full;
        }

        int tail = Math.Max(1, kept / 2);
        int head = Math.Max(1, kept - tail);

        return string.Concat(full.AsSpan(0, head), Ellipsis, full.AsSpan(full.Length - tail));
    }

    private static double Width(string text)
    {
        Ruler.Text = text;
        Ruler.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        return Ruler.DesiredSize.Width;
    }
}
