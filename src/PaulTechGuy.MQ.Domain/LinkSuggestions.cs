// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// What the author probably meant, for a link that leads nowhere.
///
/// Pure: the candidates are handed in. Finding them costs a folder listing for a file and a
/// walk over the headings for an anchor, and neither belongs in a check that runs on a
/// debounce - the caller gathers them when the menu opens.
///
/// The ordering is the whole design. A wrong suggestion at the top of a menu is worse than an
/// empty menu, because it gets clicked, so the tiers below run strongest first and stop as soon
/// as one of them fills the list.
/// </summary>
public static class LinkSuggestions
{
    /// <summary>
    /// How far apart two names can be and still be offered.
    ///
    /// Three is enough for a transposition, a doubled letter and a dropped one at once, and
    /// short of the point where unrelated short names start matching each other - "a.png" and
    /// "b.png" are two apart and must never be offered for one another, which is what the
    /// length guard below is for rather than this number.
    /// </summary>
    private const int MaximumDistance = 3;

    /// <summary>
    /// The best few candidates for <paramref name="target"/>, strongest first, or none.
    ///
    /// Comparison is on the last segment - the file name - because that is what is usually
    /// mistyped, but what comes back is the whole candidate, so clicking it writes a path that
    /// actually resolves.
    /// </summary>
    public static IReadOnlyList<string> For(string target, IReadOnlyList<string> candidates, int limit)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        if (string.IsNullOrWhiteSpace(target) || candidates.Count == 0 || limit <= 0)
        {
            return [];
        }

        string wanted = LastSegment(target);

        if (wanted.Length == 0)
        {
            return [];
        }

        string wantedStem = StemOf(wanted);

        List<string> exact = [];
        List<string> sameStem = [];
        List<(string Candidate, int Distance)> near = [];

        foreach (string candidate in candidates)
        {
            string name = LastSegment(candidate);

            if (name.Length == 0)
            {
                continue;
            }

            // The commonest real miss on Windows, and the one the preview will not serve: the
            // file is there, spelled the same, in a different case. NTFS finds it; the virtual
            // host's ordinal comparison does not, so the image is broken in the preview while
            // Explorer shows it sitting right there.
            if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase))
            {
                exact.Add(candidate);
                continue;
            }

            if (wantedStem.Length > 0
                && string.Equals(StemOf(name), wantedStem, StringComparison.OrdinalIgnoreCase))
            {
                sameStem.Add(candidate);
                continue;
            }

            // Two names of very different lengths are not a typo of one another, whatever the
            // edit distance says. Without this "a.png" offers "index.png" on a three-letter
            // budget and the menu fills with noise.
            if (Math.Abs(name.Length - wanted.Length) > MaximumDistance)
            {
                continue;
            }

            int distance = Distance(name, wanted, MaximumDistance);

            if (distance <= MaximumDistance)
            {
                near.Add((candidate, distance));
            }
        }

        near.Sort((a, b) => a.Distance != b.Distance
            ? a.Distance.CompareTo(b.Distance)
            : string.Compare(a.Candidate, b.Candidate, StringComparison.OrdinalIgnoreCase));

        return
        [
            .. exact
                .Concat(sameStem)
                .Concat(near.Select(n => n.Candidate))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(limit),
        ];
    }

    /// <summary>
    /// The part after the last separator. Anchors have none, so "#install" comes back whole.
    /// </summary>
    private static string LastSegment(string value)
    {
        int cut = value.LastIndexOfAny(['/', '\\']);

        return cut < 0 ? value : value[(cut + 1)..];
    }

    /// <summary>
    /// The name without its extension, which is what catches "logo.png" written for "logo.svg".
    /// Empty when there is no extension, so extension-less names do not all match each other.
    /// </summary>
    private static string StemOf(string name)
    {
        int dot = name.LastIndexOf('.');

        return dot <= 0 ? string.Empty : name[..dot];
    }

    /// <summary>
    /// Levenshtein distance, case-insensitive, abandoned once it cannot come in under
    /// <paramref name="ceiling"/>.
    ///
    /// Two rows rather than a full matrix: the candidate list can be several hundred names and
    /// this runs against every one of them while a menu is opening.
    /// </summary>
    private static int Distance(string a, string b, int ceiling)
    {
        int width = b.Length + 1;

        Span<int> previous = stackalloc int[width];
        Span<int> current = stackalloc int[width];

        for (int i = 0; i < width; i++)
        {
            previous[i] = i;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            int best = current[0];

            for (int j = 1; j < width; j++)
            {
                int cost = char.ToUpperInvariant(a[i - 1]) == char.ToUpperInvariant(b[j - 1]) ? 0 : 1;

                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);

                best = Math.Min(best, current[j]);
            }

            // Nothing in this row is close enough, and later rows only ever grow.
            if (best > ceiling)
            {
                return ceiling + 1;
            }

            current.CopyTo(previous);
        }

        return previous[b.Length];
    }
}
