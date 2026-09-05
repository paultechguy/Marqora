// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Windows.UI;

namespace PaulTechGuy.MQ.App;

/// <summary>
/// The two colors a match is drawn in, and the only place either one is written down.
///
/// Marqora points at a match in three places - the source pane's selection, a result picked
/// in the Find All window, and the source pane's own Find - and two of those live in a
/// WebView while the third is WinUI. Rather than the same color written twice in two
/// notations, it is written once here, as hex, and each side converts: WinUI through
/// <see cref="Background"/> and <see cref="Foreground"/>, the web side by
/// <c>WebViewPreviewHost.SetThemeAsync</c>, which posts these strings to the shell. app.css
/// does not name them at all; app.js puts what it is given into --mq-selection and
/// --mq-selection-text, so stylesheet rules can use them too.
///
/// Dark only. Light mode's tint is a translucent accent that has never needed lifting and is
/// used in one place, where it is written.
///
/// To change the colors, change the two constants below and nothing else.
/// </summary>
internal static class MatchColors
{
    /// <summary>The color behind a match. #rrggbb, or #rrggbbaa to let the text show through.</summary>
    public const string BackgroundHex = "#75b1ff";

    /// <summary>
    /// The text on top of it. An opaque background needs one: the editor and the results
    /// list both draw their ordinary near-white, which on a light tint is barely there.
    /// </summary>
    public const string ForegroundHex = "#000000";

    public static Color Background => Parse(BackgroundHex);

    public static Color Foreground => Parse(ForegroundHex);

    /// <summary>
    /// #rrggbb or #rrggbbaa - the two forms Monaco accepts, so one constant can serve both
    /// sides. Anything else is a typo in a constant above, and says so rather than quietly
    /// painting something nobody chose.
    /// </summary>
    private static Color Parse(string hex)
    {
        ReadOnlySpan<char> digits = hex.AsSpan().TrimStart('#');

        if (digits.Length is not (6 or 8)
            || !TryByte(digits[..2], out byte r)
            || !TryByte(digits.Slice(2, 2), out byte g)
            || !TryByte(digits.Slice(4, 2), out byte b))
        {
            throw new FormatException($"MatchColors: '{hex}' is not a #rrggbb or #rrggbbaa color.");
        }

        byte alpha = 0xFF;

        if (digits.Length == 8 && !TryByte(digits.Slice(6, 2), out alpha))
        {
            throw new FormatException($"MatchColors: '{hex}' does not end in a two-digit alpha.");
        }

        return Color.FromArgb(alpha, r, g, b);
    }

    private static bool TryByte(ReadOnlySpan<char> pair, out byte value) =>
        byte.TryParse(pair, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
}
