// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.RegularExpressions;

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// Composites translucent CSS colors onto white, for markup going somewhere that cannot work
/// them out for itself.
///
/// Word and Outlook read a pasted style block and apply what they understand, which is roughly
/// the CSS of twenty years ago: a hex color is honored, <c>rgba()</c> and <c>color-mix()</c>
/// are not recognized as colors at all and the whole declaration is discarded. That is not a
/// graceful fall back to an opaque shade - it is the property going missing.
///
/// The symptom that found this was the table header. <c>thead th</c> has exactly one
/// background, the accent tint, so losing it left the head indistinguishable from the body in
/// every pasted table. The callouts hide the same failure better: <c>.markdown-alert</c> paints
/// a solid gray first and each type overrides it with a tint, so when the override is thrown
/// away the gray still shows and the box merely wears the wrong color.
///
/// White is not a parameter. The fragment this serves is always a light document - see
/// <c>RenderedHtmlPackager</c>, whose output is light whichever theme the app is in - and a
/// backdrop that could be anything else would be a backdrop this cannot flatten against.
///
/// Only the clipboard goes through here. An exported HTML file is opened in a browser, which
/// has implemented both functions for years and blends them against whatever is actually
/// behind them; flattening there would replace a correct color with an approximation of it.
/// </summary>
public static partial class CssAlphaFlattening
{
    /// <summary>
    /// Rewrites every <c>rgba()</c> and <c>color-mix(in srgb, X n%, transparent)</c> in the
    /// stylesheet as the opaque hex it resolves to on a white page.
    ///
    /// Run this after custom properties have been resolved, not before. Both functions are
    /// written in terms of <c>var()</c> references in <c>app.css</c>, and there is nothing to
    /// composite until those have been substituted for their values.
    ///
    /// Anything that does not parse is left exactly as it was written. A color this does not
    /// recognize is one that was already going to be ignored, and passing it through unchanged
    /// keeps the failure where it started rather than inventing a shade for it.
    /// </summary>
    public static string OverWhite(string css)
    {
        ArgumentNullException.ThrowIfNull(css);

        if (css.Length == 0)
        {
            return css;
        }

        css = RgbaColor().Replace(css, match =>
        {
            if (!TryByte(match.Groups["r"].Value, out int r)
                || !TryByte(match.Groups["g"].Value, out int g)
                || !TryByte(match.Groups["b"].Value, out int b)
                || !TryAlpha(match.Groups["a"].Value, match.Groups["pct"].Success, out double alpha))
            {
                return match.Value;
            }

            return Composite(r, g, b, alpha);
        });

        return ColorMixOverTransparent().Replace(css, match =>
        {
            if (!TryHex(match.Groups["color"].Value, out int r, out int g, out int b)
                || !TryAlpha(match.Groups["pct"].Value, percentage: true, out double alpha))
            {
                return match.Value;
            }

            return Composite(r, g, b, alpha);
        });
    }

    /// <summary>
    /// The source-over blend, and the one case that must not go through it.
    ///
    /// Zero alpha stays the keyword rather than becoming <c>#ffffff</c>. The two are the same
    /// picture on a white page and completely different further down the cascade: a rule that
    /// said "nothing here" would start painting a white box over whatever it was letting
    /// through, which is how a flattening pass turns one missing background into a new one.
    /// </summary>
    private static string Composite(int r, int g, int b, double alpha)
    {
        if (alpha <= 0)
        {
            return "transparent";
        }

        if (alpha >= 1)
        {
            return $"#{r:x2}{g:x2}{b:x2}";
        }

        return $"#{Blend(r):x2}{Blend(g):x2}{Blend(b):x2}";

        int Blend(int channel)
        {
            double value = (channel * alpha) + (255 * (1 - alpha));

            return Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
        }
    }

    private static bool TryByte(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value <= 255;

    /// <summary>
    /// An alpha as CSS writes it: 0 to 1, or 0% to 100% where a percentage is allowed. Both
    /// come back as a fraction, and anything outside the range is refused rather than clamped,
    /// because a value that far out means the pattern has matched something it should not have.
    /// </summary>
    private static bool TryAlpha(string text, bool percentage, out double alpha)
    {
        alpha = 0;

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            return false;
        }

        alpha = percentage ? parsed / 100 : parsed;

        return alpha is >= 0 and <= 1;
    }

    /// <summary>Three-digit and six-digit hex, which is everything <c>app.css</c> writes.</summary>
    private static bool TryHex(string text, out int r, out int g, out int b)
    {
        r = g = b = 0;

        ReadOnlySpan<char> digits = text.AsSpan(1);

        if (digits.Length == 3)
        {
            return TryPair(digits[..1], digits[..1], out r)
                && TryPair(digits.Slice(1, 1), digits.Slice(1, 1), out g)
                && TryPair(digits.Slice(2, 1), digits.Slice(2, 1), out b);
        }

        if (digits.Length == 6)
        {
            return TryPair(digits[..1], digits.Slice(1, 1), out r)
                && TryPair(digits.Slice(2, 1), digits.Slice(3, 1), out g)
                && TryPair(digits.Slice(4, 1), digits.Slice(5, 1), out b);
        }

        return false;

        static bool TryPair(ReadOnlySpan<char> high, ReadOnlySpan<char> low, out int value)
        {
            value = 0;

            if (!int.TryParse(high, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int h)
                || !int.TryParse(low, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int l))
            {
                return false;
            }

            value = (h * 16) + l;

            return true;
        }
    }

    /// <summary>
    /// The legacy comma form, which is the only one <c>app.css</c> writes. The modern
    /// <c>rgb(r g b / a)</c> spelling is deliberately not matched: nothing in the tree uses it,
    /// and a pattern kept for a case that never arrives is a pattern nothing tests.
    /// </summary>
    [GeneratedRegex(
        @"rgba\(\s*(?<r>\d{1,3})\s*,\s*(?<g>\d{1,3})\s*,\s*(?<b>\d{1,3})\s*,\s*(?<a>\d*\.?\d+)\s*(?<pct>%)?\s*\)",
        RegexOptions.IgnoreCase)]
    private static partial Regex RgbaColor();

    /// <summary>
    /// A tint of one color and nothing else. Every <c>color-mix</c> in <c>app.css</c> has this
    /// shape - a color, a percentage, and <c>transparent</c> - because that is how a translucent
    /// shade of a token is written without restating the token. A mix of two real colors is not
    /// matched, since compositing it onto white is not what it asked for.
    /// </summary>
    [GeneratedRegex(
        @"color-mix\(\s*in\s+srgb\s*,\s*(?<color>#[0-9A-Fa-f]{3}(?:[0-9A-Fa-f]{3})?)\s+(?<pct>\d*\.?\d+)%\s*,\s*transparent\s*\)",
        RegexOptions.IgnoreCase)]
    private static partial Regex ColorMixOverTransparent();
}
