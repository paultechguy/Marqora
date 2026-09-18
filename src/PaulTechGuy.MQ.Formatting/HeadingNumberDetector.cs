// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Markdown;

namespace PaulTechGuy.MQ.Formatting;

/// <summary>
/// Decides whether a document writes its own section numbers into its heading text, and which
/// level it counts from.
///
/// The question cannot be answered a heading at a time. "1.2 Scope" and "2026 Budget" are the
/// same shape — digits, a separator, words — and no pattern tells them apart. What tells them
/// apart is the rest of the document: a number is the author's numbering when it is the number
/// this document's own counter would have put there.
///
/// So the test is a comparison rather than a match. Run <see cref="HeadingNumbers.Compute"/>
/// over the heading levels for each level the count could start from, and see which one the
/// author's numbers agree with. That has three properties worth having:
///
/// <list type="bullet">
///   <item>A run must begin at 1, because the counter does. Three headings reading "2026", "2027"
///   and "2028" are a flawless increment and still not numbering, and this is what rejects
///   them.</item>
///   <item>Which level the document counts from falls out of the same pass, rather than being a
///   second guess — it is the candidate that agreed.</item>
///   <item>A heading is judged one at a time against that answer, so "2026 Budget" sitting inside
///   a properly numbered document keeps its year while its neighbours lose their numbers.</item>
/// </list>
///
/// Strictly a reading of the source. Nothing here rewrites anything; <see cref="Scan"/> is what
/// a rewriter, a report and the open-time check all act on, so the three cannot disagree about
/// which headings carry numbers.
/// </summary>
public static class HeadingNumberDetector
{
    /// <summary>
    /// Two agreeing headings before a document counts as numbered.
    ///
    /// One is not evidence. A lone "# 1. Overview" at the top of an otherwise unnumbered
    /// document agrees with the counter perfectly, and reading that as a numbering scheme would
    /// stand Marqora's numbers down across a document that has none.
    /// </summary>
    private const int MinimumMatches = 2;

    /// <summary>
    /// The levels a count can start from — the same three the preference offers, because a
    /// document recognized as counting from a level Marqora cannot render is a document nothing
    /// can be done about.
    /// </summary>
    private static readonly HeadingNumbering[] Candidates =
    [
        HeadingNumbering.FromHeading1,
        HeadingNumbering.FromHeading2,
        HeadingNumbering.FromHeading3,
    ];

    /// <summary>
    /// How the counter is run against the document when looking for agreement. Both readings are
    /// tried for every start level and the one that agrees more wins, because a real document is
    /// usually one or the other and nothing says in advance which.
    /// </summary>
    private enum Reading
    {
        /// <summary>
        /// Count every heading, numbered or not. What a document numbered end to end looks like,
        /// and the only reading that recognizes a skipped level: a "###" under a "#" is "1.0.1"
        /// to the counter, which is also what Word and a CSS counter chain write.
        /// </summary>
        EveryHeading,

        /// <summary>
        /// Count only the headings that carry a number, so an unnumbered one between two numbered
        /// ones is stepped over rather than consuming a number.
        ///
        /// This is the reading that survives an edit. Insert a section into a numbered document
        /// and every number below it is now one out, so counting every heading agrees with almost
        /// nothing and the document reads as unnumbered — which would make Number Sections
        /// prepend a second number to each heading instead of renumbering it. Renumbering after
        /// an insert is the main reason the command exists, so the detector has to survive
        /// exactly the state the document is in when someone reaches for it.
        /// </summary>
        NumberedOnly,
    }

    /// <summary>What a document turned out to be doing with its headings.</summary>
    /// <param name="StartLevel">
    /// The level the author counts from, or <see cref="HeadingNumbering.Off"/> when the document
    /// does not number itself. This is the level a renumber should default to and the answer the
    /// open-time check needs.
    /// </param>
    /// <param name="Headings">Every heading found, numbered or not, in document order.</param>
    /// <param name="Numbered">
    /// Index-for-index with <paramref name="Headings"/>: true where that heading's leading number
    /// is the one the counter would have given it, and so is the author's numbering rather than
    /// a year, a version or a quantity. The parallel list matches
    /// <see cref="HeadingNumbers.Compute"/>'s own convention, and is what keeps a caller from
    /// having to re-derive the judgment from a filtered set.
    /// </param>
    public sealed record Scan(
        HeadingNumbering StartLevel,
        IReadOnlyList<HeadingScanner.ScannedHeading> Headings,
        IReadOnlyList<bool> Numbered)
    {
        /// <summary>Whether the document writes its own numbers.</summary>
        public bool IsNumbered => StartLevel != HeadingNumbering.Off;

        /// <summary>How many headings carry a number this judged to be real.</summary>
        public int NumberedCount => Numbered.Count(numbered => numbered);
    }

    /// <summary>Reads <paramref name="lines"/> and judges what it finds.</summary>
    public static Scan Read(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        return Read(HeadingScanner.Find(lines));
    }

    /// <summary>
    /// Judges headings already scanned, for a caller that has one in hand and should not pay for
    /// a second pass over the document.
    /// </summary>
    public static Scan Read(IReadOnlyList<HeadingScanner.ScannedHeading> headings)
    {
        ArgumentNullException.ThrowIfNull(headings);

        if (headings.Count == 0)
        {
            return new Scan(HeadingNumbering.Off, headings, []);
        }

        int[] levels = new int[headings.Count];

        for (int i = 0; i < headings.Count; i++)
        {
            levels[i] = headings[i].Level;
        }

        // Every heading wearing a number, whether or not it turns out to be one. This is the
        // denominator rather than "every heading the counter would number", so a document that
        // numbers its chapters and sections but leaves the sub-sections alone is still
        // recognized - which is how most numbered documents are actually written.
        int wearingNumbers = headings.Count(heading => heading.Prefix is not null);

        var best = HeadingNumbering.Off;
        bool[] bestAgreements = new bool[headings.Count];
        int bestMatched = 0;

        // Every start level is tried straight before any of them is tried with headings stepped
        // over, and a later reading has to beat the best strictly. The order is the rule: a
        // document that reads correctly as written is never re-read as one with gaps in it.
        //
        // It also settles a real ambiguity. A document with an unnumbered "#" title over "##"
        // sections counts from "##", but drop the title as unnumbered and the sections look like
        // a top-level count instead - the two are indistinguishable once a heading is stepped
        // over, because a leading zero is dropped. Reading straight first gives the right answer.
        foreach (Reading reading in (Reading[])[Reading.EveryHeading, Reading.NumberedOnly])
        {
            foreach (HeadingNumbering start in Candidates)
            {
                bool[] agreements = Agreements(headings, levels, start, reading, out int matched);

                if (matched >= MinimumMatches && matched * 2 >= wearingNumbers && matched > bestMatched)
                {
                    best = start;
                    bestMatched = matched;
                    bestAgreements = agreements;
                }
            }
        }

        return new Scan(best, headings, bestAgreements);
    }

    /// <summary>
    /// Which headings wrote the number this start level and reading would have given them.
    ///
    /// A document that skips a level needs no special handling here, which is worth knowing
    /// before anyone adds some: under <see cref="Reading.EveryHeading"/> a "###" sitting directly
    /// under a "#" already counts as "1.0.1" rather than "1.1" — only a *leading* zero is dropped
    /// — so the form a CSS counter chain and Word write is the form this compares against anyway.
    /// See <c>A_level_skipped_mid_chain_keeps_its_place</c> in the numbering tests.
    /// </summary>
    private static bool[] Agreements(
        IReadOnlyList<HeadingScanner.ScannedHeading> headings,
        int[] levels,
        HeadingNumbering start,
        Reading reading,
        out int matched)
    {
        bool[] agreements = new bool[headings.Count];
        matched = 0;

        // Which headings the counter is run over, and in which order. Counting only the numbered
        // ones means running it over their levels alone and mapping the answers back, so the
        // positions it returns line up with the subset rather than with the document.
        int[] counted = reading == Reading.EveryHeading
            ? [.. Enumerable.Range(0, headings.Count)]
            : [.. Enumerable.Range(0, headings.Count).Where(i => headings[i].Prefix is not null)];

        if (counted.Length == 0)
        {
            return agreements;
        }

        int[] countedLevels = new int[counted.Length];
        int shallowest = int.MaxValue;

        for (int k = 0; k < counted.Length; k++)
        {
            countedLevels[k] = levels[counted[k]];
            shallowest = Math.Min(shallowest, countedLevels[k]);
        }

        // A count cannot start above the shallowest heading that carries a number, and saying so
        // is what keeps this reading from reporting a level nobody could have meant.
        //
        // The trouble it fixes: once the unnumbered headings are stepped over, a document whose
        // "##" sections are numbered under unnumbered "#" parts looks exactly like one counting
        // from "#", because a leading zero is dropped and both readings produce 1, 2, 3. The
        // shallowest numbered heading is a "##", so the count starts there - and a reader who
        // renumbers gets the levels their preview is already showing rather than everything
        // pushed down a level.
        //
        // Reading the document straight has no such trouble and is not guarded: there the
        // unnumbered headings are still present to say where the count begins.
        if (reading == Reading.NumberedOnly && (int)start < shallowest)
        {
            return agreements;
        }

        IReadOnlyList<string> ours = HeadingNumbers.Compute(countedLevels, start);

        for (int k = 0; k < counted.Length; k++)
        {
            // Above the level the count starts at, so the counter has no opinion and neither
            // has this. A number up there is left alone rather than read as a disagreement.
            if (ours[k].Length == 0)
            {
                continue;
            }

            int i = counted[k];

            if (headings[i].Prefix is not { } prefix)
            {
                continue;
            }

            if (prefix.Written == ours[k])
            {
                agreements[i] = true;
                matched++;
            }
        }

        return agreements;
    }
}
