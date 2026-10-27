// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Cli;

/// <summary>
/// "Did you mean": the closest of a set of names to one that was not found, when it is close
/// enough to be a typo rather than a different word. Shared by verbs, options and themes, so a
/// misspelling is caught the same way wherever it is made (CLI-CMD-04, CLI-TH-03).
/// </summary>
internal static class Suggest
{
    /// <summary>
    /// The candidate nearest <paramref name="typed"/>, ignoring case, or null when none is near.
    /// Near is an edit distance of at most a third of the longer word, and never more than three:
    /// "fromat" finds "format", "Oceanic" finds "ocean", and "pdf" finds nothing in a list of verbs.
    /// </summary>
    public static string? Closest(string typed, IEnumerable<string> candidates)
    {
        ArgumentNullException.ThrowIfNull(typed);
        ArgumentNullException.ThrowIfNull(candidates);

        string? best = null;
        int bestDistance = int.MaxValue;
        string lowered = typed.ToLowerInvariant();

        foreach (string candidate in candidates)
        {
            int distance = Distance(lowered, candidate.ToLowerInvariant());
            int allowed = Math.Min(3, Math.Max(1, Math.Max(typed.Length, candidate.Length) / 3));

            // A candidate that starts with what was typed is near however long it runs on:
            // "oce" for "ocean", "vintage" for "vintage-press".
            bool prefix = lowered.Length >= 3 && candidate.StartsWith(lowered, StringComparison.OrdinalIgnoreCase);

            if ((distance <= allowed || prefix) && distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>Levenshtein distance, with a transposition counted as one edit.</summary>
    private static int Distance(string a, string b)
    {
        int[,] d = new int[a.Length + 1, b.Length + 1];

        for (int i = 0; i <= a.Length; i++)
        {
            d[i, 0] = i;
        }

        for (int j = 0; j <= b.Length; j++)
        {
            d[0, j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;

                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);

                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }
            }
        }

        return d[a.Length, b.Length];
    }
}
