// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Folio;

/// <summary>
/// Turning many documents into one page, as far as their anchors and links are concerned.
///
/// Pure string work, and here rather than beside the writer that uses it for one reason: it is
/// the part of the reading copy that can be got wrong invisibly. A heading that answers to the
/// wrong anchor, or a contents entry that lands on the document above the one it names, looks
/// exactly like a working page until somebody clicks.
/// </summary>
public static class FolioLinks
{
    /// <summary>The id a document answers to inside the page. One-based, because it is read.</summary>
    public static string Anchor(int index) => $"mq-doc-{index + 1}";

    /// <summary>
    /// Prefixes this document's heading anchors, so that twelve documents in one page do not
    /// contain twelve elements called "introduction".
    ///
    /// Only the slugs Markdig minted are touched, and each is matched as a whole attribute so
    /// that "intro" cannot rename "introduction". Every other id in the markup is left exactly
    /// as it is, and mermaid's are the reason: a rendered diagram refers to its own markers with
    /// url(#...) from inside the SVG, so renaming those without renaming the references would
    /// strip the arrowheads off every flowchart in the Folio.
    /// </summary>
    public static string Namespace(string html, IReadOnlyList<string> slugs, string prefix)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(slugs);

        foreach (string slug in slugs.Distinct(StringComparer.Ordinal))
        {
            if (slug.Length > 0)
            {
                html = html.Replace($"id=\"{slug}\"", $"id=\"{prefix}-{slug}\"", StringComparison.Ordinal);
            }
        }

        return html;
    }

    /// <summary>
    /// Points every link that stays inside the Folio at the right place in the page.
    ///
    /// Three kinds, and the order they are done in matters. A link within this document keeps
    /// its target and gains this document's prefix, matching what <see cref="Namespace"/> did to
    /// the headings - done first, because it is the only one whose reference ends at the quote.
    /// A link to another document becomes that document's anchor. A link to a heading in another
    /// document takes <em>that</em> document's prefix rather than this one's, which is the case
    /// most easily got backwards.
    ///
    /// Anything else - an http link, a file that never travelled - is left alone. The preflight
    /// already named those.
    /// </summary>
    /// <param name="anchors">Entry name inside the Folio, to the anchor its document answers to.</param>
    public static string Relink(
        string html,
        IReadOnlyDictionary<string, string> anchors,
        string prefix,
        IReadOnlyList<string> ownSlugs)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(anchors);
        ArgumentNullException.ThrowIfNull(ownSlugs);

        foreach (string slug in ownSlugs.Distinct(StringComparer.Ordinal))
        {
            if (slug.Length > 0)
            {
                html = html.Replace($"href=\"#{slug}\"", $"href=\"#{prefix}-{slug}\"", StringComparison.Ordinal);
            }
        }

        foreach ((string entry, string anchor) in anchors)
        {
            // A destination with a space in it reaches the markup percent-encoded, so both
            // spellings are looked for rather than only the one the plan holds.
            string encoded = Uri.EscapeDataString(entry).Replace("%2F", "/", StringComparison.Ordinal);

            foreach (string written in new[] { entry, encoded }.Distinct(StringComparer.Ordinal))
            {
                html = html.Replace($"href=\"{written}\"", $"href=\"#{anchor}\"", StringComparison.Ordinal);
                html = html.Replace($"href=\"{written}#", $"href=\"#{anchor}-", StringComparison.Ordinal);
            }
        }

        return html;
    }
}
