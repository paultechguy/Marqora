// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace PaulTechGuy.MQ.Folders;

/// <summary>
/// Orders names the way File Explorer does: a run of digits compares as a number, so
/// "Chapter 2" comes before "Chapter 10". The rest compares in the current culture, ignoring case.
///
/// The order is total. Two names that tie (they differ only in case, or in leading zeros) are
/// then ordered ordinally, so a sort never depends on which name happened to come first, and a
/// row diff never sees two rows swap for no reason.
/// </summary>
public sealed class NaturalNameComparer : IComparer<string>
{
    public static NaturalNameComparer Instance { get; } = new();

    private static CompareInfo Culture => CultureInfo.CurrentCulture.CompareInfo;

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        int natural = CompareNatural(x, y);
        return natural != 0 ? natural : string.CompareOrdinal(x, y);
    }

    private static int CompareNatural(string x, string y)
    {
        int i = 0;
        int j = 0;

        while (i < x.Length && j < y.Length)
        {
            bool xDigit = char.IsAsciiDigit(x[i]);
            bool yDigit = char.IsAsciiDigit(y[j]);

            int xEnd = RunEnd(x, i, xDigit);
            int yEnd = RunEnd(y, j, yDigit);

            int result = xDigit && yDigit
                ? CompareNumbers(x.AsSpan(i, xEnd - i), y.AsSpan(j, yEnd - j))
                : Culture.Compare(x, i, xEnd - i, y, j, yEnd - j, CompareOptions.IgnoreCase);

            if (result != 0)
            {
                return result;
            }

            i = xEnd;
            j = yEnd;
        }

        // The shorter name, when it is a prefix of the other, comes first.
        return (x.Length - i).CompareTo(y.Length - j);
    }

    private static int RunEnd(string text, int start, bool digits)
    {
        int end = start;

        while (end < text.Length && char.IsAsciiDigit(text[end]) == digits)
        {
            end++;
        }

        return end;
    }

    /// <summary>Compares digit runs of any length by value, without parsing into a number that could overflow.</summary>
    private static int CompareNumbers(ReadOnlySpan<char> x, ReadOnlySpan<char> y)
    {
        x = x.TrimStart('0');
        y = y.TrimStart('0');

        if (x.Length != y.Length)
        {
            return x.Length.CompareTo(y.Length);
        }

        return x.SequenceCompareTo(y);
    }
}
