// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// The questions the outline panel asks of a document's headings.
///
/// Here rather than in the panel because none of it is about XAML: which heading a line
/// falls under is a fact about the document, and a fact worth having a test on. The panel
/// itself only turns the answers into rows.
/// </summary>
public static class OutlineNavigation
{
    /// <summary>Passed as the depth limit to mean "every level".</summary>
    public const int UnlimitedDepth = 0;

    /// <summary>
    /// Which heading a source line sits under: the last one at or above it, or -1 when the
    /// line is above the first heading and no heading owns it yet.
    ///
    /// A document does not have to open with a heading - a title block, a front-matter
    /// fence, or a paragraph of preamble are all ordinary - so "no heading yet" is a real
    /// answer rather than an edge case to round away. The panel shows it as no selection.
    ///
    /// Binary search over <see cref="OutlineHeading.SourceLine"/>, which is safe because the
    /// outline is built by walking the parsed document in order and a heading occupies a
    /// line by itself: the lines are strictly increasing. The loop below does not depend on
    /// that being strict, only on the list being sorted.
    /// </summary>
    public static int IndexOfHeadingAt(IReadOnlyList<OutlineHeading> outline, int line)
    {
        ArgumentNullException.ThrowIfNull(outline);

        if (outline.Count == 0 || line < outline[0].SourceLine)
        {
            return -1;
        }

        int low = 0;
        int high = outline.Count - 1;

        // Narrows to the last index whose line is <= the one asked about. The invariant is
        // that outline[low] always qualifies and outline[high + 1] never does.
        while (low < high)
        {
            int middle = (low + high + 1) / 2;

            if (outline[middle].SourceLine <= line)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    /// <summary>
    /// The headings the panel should show: those within the depth limit whose text matches
    /// the filter.
    ///
    /// Non-matching headings are dropped rather than dimmed, and their children are not kept
    /// for context. The list is flat and has no expanders, so a parent retained only to
    /// explain a child would be indistinguishable from a parent that matched - and clicking
    /// it would jump somewhere the user did not search for.
    ///
    /// Returns the outline itself when nothing is being filtered out, so the common case
    /// allocates nothing.
    /// </summary>
    public static IReadOnlyList<OutlineHeading> Filter(
        IReadOnlyList<OutlineHeading> outline,
        string? term,
        int maxDepth)
    {
        ArgumentNullException.ThrowIfNull(outline);

        bool limited = maxDepth > UnlimitedDepth;
        string? needle = string.IsNullOrWhiteSpace(term) ? null : term.Trim();

        if (!limited && needle is null)
        {
            return outline;
        }

        List<OutlineHeading> kept = [];

        foreach (OutlineHeading heading in outline)
        {
            if (limited && heading.Level > maxDepth)
            {
                continue;
            }

            if (needle is not null && !Matches(heading, needle))
            {
                continue;
            }

            kept.Add(heading);
        }

        return kept;
    }

    /// <summary>
    /// Whether a heading answers to a filter term.
    ///
    /// Ordinal rather than culture-aware. A filter box is judged on being predictable while
    /// the letters are still arriving, and culture-aware comparison brings ignorable
    /// characters with it - under which an apparently non-empty term can match every row.
    /// </summary>
    public static bool Matches(OutlineHeading heading, string term)
    {
        ArgumentNullException.ThrowIfNull(heading);

        return string.IsNullOrEmpty(term)
            || heading.Text.Contains(term, StringComparison.OrdinalIgnoreCase);
    }
}
