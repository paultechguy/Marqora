// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Markdown;

namespace PaulTechGuy.MQ.Formatting;

/// <summary>
/// Works out which heading anchors a rewrite is about to rename, so the links pointing at them
/// can be moved in the same edit.
///
/// A heading's anchor is its text, slugified — so putting "1.2" on the front of "Scope" turns
/// <c>#scope</c> into <c>#12--scope</c>, and every <c>[see](#scope)</c> in the document stops
/// resolving. Nothing about that is visible: a dead anchor still looks like a link and still
/// looks clickable. That is the whole reason this exists.
///
/// The slugs are worked out from the source text with <see cref="GitHubSlug"/>, the same rule
/// the renderer assigns ids with, run twice — once over the document as it stands and once over
/// it as it will stand — with one duplicate counter each, in document order, so two headings
/// that slugify the same are numbered the same way the renderer numbers them.
/// </summary>
internal static class Anchors
{
    /// <summary>
    /// The anchors this rewrite renames: old name to new one, for every heading whose anchor can
    /// be trusted.
    /// </summary>
    /// <param name="rewritten">
    /// Index-for-index with <paramref name="headings"/>: the heading's new text, or null where it
    /// is left alone.
    /// </param>
    /// <param name="uncertain">
    /// How many headings were changed but had to be skipped because their anchor could not be
    /// worked out from the source alone. Links pointing at those are left exactly as they are and
    /// the caller says so — see <see cref="IsDerivable"/>.
    /// </param>
    public static Dictionary<string, string> Renamed(
        IReadOnlyList<HeadingScanner.ScannedHeading> headings,
        string?[] rewritten,
        out int uncertain)
    {
        // Matched without case, which is what the dead-anchor check in Analysis does: a
        // hand-written "#Scope" reaches a heading slugged "scope", so a rewrite has to move it
        // too or it is left pointing at a name nothing has any more.
        var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var before = new Dictionary<string, int>(StringComparer.Ordinal);
        var after = new Dictionary<string, int>(StringComparer.Ordinal);

        uncertain = 0;

        for (int i = 0; i < headings.Count; i++)
        {
            string old = headings[i].Text;

            // A heading with no words is given no id at all by the renderer, so it claims no
            // slot in the duplicate count either. Counting one here would push every later
            // duplicate along by one and rename anchors nothing asked about.
            if (old.Length == 0)
            {
                continue;
            }

            string now = rewritten[i] ?? old;

            // Both counters are advanced for every heading, including the ones skipped below,
            // because a duplicate's number depends on how many came before it rather than on
            // whether this particular heading is one anybody is asking about.
            string oldSlug = GitHubSlug.Uniquify(GitHubSlug.Slugify(old), before);
            string newSlug = GitHubSlug.Uniquify(GitHubSlug.Slugify(now), after);

            if (!IsDerivable(headings[i].Title))
            {
                // Only worth mentioning when this rewrite actually moved the heading. An
                // untouched one keeps whatever anchor it had, however hard that is to work out.
                if (!string.Equals(old, now, StringComparison.Ordinal))
                {
                    uncertain++;
                }

                continue;
            }

            if (!string.Equals(oldSlug, newSlug, StringComparison.Ordinal))
            {
                renamed[oldSlug] = newSlug;
            }
        }

        return renamed;
    }

    /// <summary>
    /// Whether a heading's anchor can be worked out from its source text.
    ///
    /// The renderer slugifies the heading's *rendered* words, and this only has the markdown
    /// that produced them. Usually those agree, because the slug rule drops every character that
    /// is not a letter, digit, hyphen, underscore or space — so the asterisks of <c>**bold**</c>,
    /// the tildes of a strikethrough and the backticks of a code span all fall out on both sides
    /// and the answer is the same either way.
    ///
    /// Four things break that agreement, and each one is a character this refuses:
    ///
    /// <list type="bullet">
    ///   <item><c>[</c> and <c>]</c> — a link or an image. The rendered heading keeps the link
    ///   text; the source also carries the destination, which would slugify into the anchor.</item>
    ///   <item><c>&lt;</c> and <c>&gt;</c> — raw HTML. The tag name is not part of the rendered
    ///   words but is part of the source.</item>
    ///   <item><c>&amp;</c> — an entity. <c>&amp;amp;</c> renders as one ampersand, which the
    ///   slug drops, while the source spells out three letters the slug would keep. A literal
    ///   ampersand is harmless, but the two are not worth telling apart for what it buys.</item>
    ///   <item>An underscore that could open emphasis. The slug rule keeps underscores, so
    ///   <c>_Scope_</c> slugifies to <c>_scope_</c> from the source and to <c>scope</c> from the
    ///   rendered words. Intraword underscores are not emphasis in CommonMark, so
    ///   <c>snake_case</c> is left alone rather than being refused with the rest.</item>
    /// </list>
    ///
    /// Refusing is not a failure to be fixed later by guessing harder. A wrong guess repoints a
    /// working link at a name nothing has, which is worse than leaving a link alone: the first
    /// is silent and the second is at least still pointing where the author put it. If this ever
    /// has to be exact, the answer is not a better guess but the real ids — the outline the
    /// renderer already produces carries them, keyed by source line.
    /// </summary>
    public static bool IsDerivable(string title)
    {
        for (int i = 0; i < title.Length; i++)
        {
            switch (title[i])
            {
                case '[' or ']' or '<' or '>' or '&':
                    return false;

                case '_' when i == 0 || !char.IsLetterOrDigit(title[i - 1]):
                    return false;

                default:
                    continue;
            }
        }

        return true;
    }
}
