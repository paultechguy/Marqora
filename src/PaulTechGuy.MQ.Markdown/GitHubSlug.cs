// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace PaulTechGuy.MQ.Markdown;

/// <summary>
/// The anchor real GitHub gives a heading.
///
/// Lives here, at the bottom, because two very different callers need the same answer and
/// neither can be the home of it. The renderer assigns these ids while parsing, so a hand-written
/// "#some-heading" link resolves; the heading rewriter needs to know which of those ids its own
/// edit is about to change, so it can move the links that point at them. One rule, or the app
/// renames an anchor in one place and looks for the old one in the other.
///
/// Markdig's own auto-identifier extension is close but not exact, which is why
/// <c>MarqoraMarkdownPipeline</c> removes it: GitHub strips punctuation first and only then turns
/// every remaining space into a hyphen, one for one, so "Foo &amp; Bar" becomes <c>foo--bar</c>
/// rather than <c>foo-bar</c>. A hand-written table of contents is written against the real
/// thing.
/// </summary>
public static class GitHubSlug
{
    /// <summary>
    /// Lowercase; keep letters, digits, hyphens and underscores; turn a space into a hyphen and
    /// drop anything else outright. No collapsing — a run of two spaces left behind by one
    /// removed character becomes two hyphens, because that is what the real algorithm does.
    ///
    /// Worth naming, because the heading rewriter leans on it: every character is decided on its
    /// own, with no look-around and nothing collapsed, so slugifying two pieces of text and
    /// joining the results gives the same answer as joining the text and slugifying once. That
    /// is what lets a number added to the front of a heading be reasoned about as a change to
    /// the front of its anchor.
    /// </summary>
    public static string Slugify(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var builder = new StringBuilder(text.Length);

        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else if (c is '-' or '_')
            {
                builder.Append(c);
            }
            else if (char.IsWhiteSpace(c))
            {
                builder.Append('-');
            }
        }

        return builder.ToString();
    }

    /// <summary>The first heading to want a slug keeps it bare; every repeat counts up from 1.</summary>
    /// <param name="seen">
    /// Carried across the whole document by the caller, because the answer depends on what came
    /// before. Two passes over one document must walk it in the same order or they will disagree
    /// about which heading owns the bare slug.
    /// </param>
    public static string Uniquify(string slug, Dictionary<string, int> seen)
    {
        ArgumentNullException.ThrowIfNull(seen);

        if (!seen.TryGetValue(slug, out int count))
        {
            seen[slug] = 1;

            return slug;
        }

        seen[slug] = count + 1;

        return $"{slug}-{count}";
    }
}
