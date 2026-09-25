// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;

namespace PaulTechGuy.MQ.Formatting;

/// <summary>
/// How many columns a string occupies in a monospaced editor.
///
/// <see cref="string.Length"/> counts UTF-16 code units, which is the wrong ruler for padding:
/// ✅ is one code unit but is drawn two columns wide, 👍 is two code units and also two
/// columns, and a family emoji is eight code units drawn as a single two-column glyph. A
/// table padded by code units comes out with every emoji row one column proud of the header.
///
/// The measure is per grapheme cluster, so a joined sequence or a flag counts once. A cluster
/// is two columns when it starts with a wide or emoji-presentation character or carries the
/// emoji variation selector, nothing when it is only a mark or a format character, and one
/// otherwise. Ambiguous-width characters (☐, →, —) are one, which is how a Western monospace
/// font draws them.
/// </summary>
internal static class DisplayWidth
{
    private const int EmojiVariationSelector = 0xFE0F;

    // East Asian Wide and Fullwidth, plus the emoji that default to emoji presentation.
    // Sorted, non-overlapping, inclusive.
    private static readonly (int First, int Last)[] Wide =
    [
        (0x1100, 0x115F), (0x231A, 0x231B), (0x2329, 0x232A), (0x23E9, 0x23EC),
        (0x23F0, 0x23F0), (0x23F3, 0x23F3), (0x25FD, 0x25FE), (0x2614, 0x2615),
        (0x2648, 0x2653), (0x267F, 0x267F), (0x2693, 0x2693), (0x26A1, 0x26A1),
        (0x26AA, 0x26AB), (0x26BD, 0x26BE), (0x26C4, 0x26C5), (0x26CE, 0x26CE),
        (0x26D4, 0x26D4), (0x26EA, 0x26EA), (0x26F2, 0x26F3), (0x26F5, 0x26F5),
        (0x26FA, 0x26FA), (0x26FD, 0x26FD), (0x2705, 0x2705), (0x270A, 0x270B),
        (0x2728, 0x2728), (0x274C, 0x274C), (0x274E, 0x274E), (0x2753, 0x2755),
        (0x2757, 0x2757), (0x2795, 0x2797), (0x27B0, 0x27B0), (0x27BF, 0x27BF),
        (0x2B1B, 0x2B1C), (0x2B50, 0x2B50), (0x2B55, 0x2B55), (0x2E80, 0x303E),
        (0x3041, 0x33FF), (0x3400, 0x4DBF), (0x4E00, 0x9FFF), (0xA000, 0xA4CF),
        (0xA960, 0xA97F), (0xAC00, 0xD7A3), (0xF900, 0xFAFF), (0xFE10, 0xFE19),
        (0xFE30, 0xFE6F), (0xFF00, 0xFF60), (0xFFE0, 0xFFE6), (0x16FE0, 0x16FE4),
        (0x17000, 0x18CFF), (0x1B000, 0x1B2FF), (0x1F004, 0x1F004), (0x1F0CF, 0x1F0CF),
        (0x1F18E, 0x1F18E), (0x1F191, 0x1F19A), (0x1F1E6, 0x1F1FF), (0x1F200, 0x1F202),
        (0x1F210, 0x1F23B), (0x1F240, 0x1F248), (0x1F250, 0x1F251), (0x1F260, 0x1F265),
        (0x1F300, 0x1F64F), (0x1F680, 0x1F6FF), (0x1F7E0, 0x1F7EB), (0x1F7F0, 0x1F7F0),
        (0x1F90C, 0x1F9FF), (0x1FA70, 0x1FAFF), (0x20000, 0x2FFFD), (0x30000, 0x3FFFD),
    ];

    public static int Of(string text)
    {
        int width = 0;
        TextElementEnumerator clusters = StringInfo.GetTextElementEnumerator(text);

        while (clusters.MoveNext())
        {
            width += OfCluster(clusters.GetTextElement());
        }

        return width;
    }

    private static int OfCluster(string cluster)
    {
        Rune first = Rune.GetRuneAt(cluster, 0);

        if (IsWide(first.Value) || cluster.Contains((char)EmojiVariationSelector))
        {
            return 2;
        }

        return Rune.GetUnicodeCategory(first) switch
        {
            UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format => 0,
            _ => 1,
        };
    }

    private static bool IsWide(int codePoint)
    {
        int lo = 0;
        int hi = Wide.Length - 1;

        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;

            if (codePoint < Wide[mid].First)
            {
                hi = mid - 1;
            }
            else if (codePoint > Wide[mid].Last)
            {
                lo = mid + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }
}
