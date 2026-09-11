// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Windows.UI;

namespace PaulTechGuy.MQ.App;

/// <summary>
/// Turns the hex a color is written in into the color itself.
///
/// The app states its shared colors as hex strings rather than as WinUI colors, because the
/// other half of every one of them is a stylesheet: <see cref="MatchColors"/> and
/// <see cref="AccentColors"/> are both read by C# and posted to the webshell, and hex is the
/// one notation both sides speak. This is the single reader, so the two of them cannot drift
/// into disagreeing about what a malformed color should do.
/// </summary>
internal static class HexColor
{
    /// <summary>
    /// #rrggbb or #rrggbbaa - the two forms Monaco accepts, so one constant can serve both
    /// sides. Anything else is a typo in a constant, and says so, naming the class it was
    /// read from, rather than quietly painting something nobody chose.
    /// </summary>
    public static Color Parse(string hex, string owner)
    {
        ReadOnlySpan<char> digits = hex.AsSpan().TrimStart('#');

        if (digits.Length is not (6 or 8)
            || !TryByte(digits[..2], out byte r)
            || !TryByte(digits.Slice(2, 2), out byte g)
            || !TryByte(digits.Slice(4, 2), out byte b))
        {
            throw new FormatException($"{owner}: '{hex}' is not a #rrggbb or #rrggbbaa color.");
        }

        byte alpha = 0xFF;

        if (digits.Length == 8 && !TryByte(digits.Slice(6, 2), out alpha))
        {
            throw new FormatException($"{owner}: '{hex}' does not end in a two-digit alpha.");
        }

        return Color.FromArgb(alpha, r, g, b);
    }

    private static bool TryByte(ReadOnlySpan<char> pair, out byte value) =>
        byte.TryParse(pair, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
}
