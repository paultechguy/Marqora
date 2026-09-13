// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using MarkdigDocument = Markdig.Syntax.MarkdownDocument;

namespace PaulTechGuy.MQ.Rendering;

/// <summary>
/// The anchor real GitHub gives a heading, and the pass that assigns it once
/// <see cref="MarqoraMarkdownPipeline"/> has removed Markdig's own auto-identifier extension.
///
/// That extension's algorithm is close, not exact. GitHub's actual algorithm removes punctuation
/// first and only then turns every remaining space into a hyphen, one for one - so "Foo &amp;
/// Bar", with the "&amp;" gone and the two spaces that sat around it left standing, becomes
/// <c>foo--bar</c>. Markdig's version collapses that run of separators into a single hyphen
/// instead, which reads as the more defensible rule and is not what GitHub does:
/// airbnb/javascript's own hand-written table of contents links "Comparison Operators &amp;
/// Equality" to <c>#comparison-operators--equality</c>, and that link works on github.com today.
/// A hand-written "#some-heading" anchor - the shape every document with its own table of
/// contents uses, this one included - is written against the real thing, so Marqora's anchors
/// have to match it rather than Markdig's approximation.
/// </summary>
internal static class GitHubHeadingSlug
{
    /// <summary>
    /// Assigns every heading without an id one, in document order.
    ///
    /// A heading can already have an id here - <c>{#short}</c>, the generic-attributes syntax,
    /// is a separate extension from the auto-identifier one <see cref="MarqoraMarkdownPipeline"/>
    /// removes, and keeps working unaffected. Those are left exactly as the author wrote them:
    /// an explicit id is a promise made to whatever already links against it, and correcting it
    /// into something the author never asked for would break every one of those links instead of
    /// fixing them. It still claims its slot in <c>seen</c>, so a later heading whose computed
    /// slug would collide with it is renumbered instead of silently landing on the same id.
    ///
    /// Registered on <see cref="Markdig.MarkdownPipelineBuilder.DocumentProcessed"/> in
    /// <see cref="MarqoraMarkdownPipeline"/> rather than run as a later, separate pass, so the
    /// document order this walks and the order duplicates get numbered in are the same order
    /// - one counter, not two that could disagree about which heading came first.
    /// </summary>
    public static void FixIdentifiers(MarkdigDocument document)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (HeadingBlock heading in document.Descendants<HeadingBlock>())
        {
            string? explicitId = heading.GetAttributes().Id;

            if (explicitId is { Length: > 0 })
            {
                seen[explicitId] = seen.TryGetValue(explicitId, out int count) ? count + 1 : 1;
                continue;
            }

            string text = MarkdownHeadingReader.ToPlainText(heading.Inline);

            if (text.Length == 0)
            {
                continue;
            }

            heading.GetAttributes().Id = Uniquify(Slugify(text), seen);
        }
    }

    /// <summary>
    /// Lowercase; keep letters, digits, hyphens and underscores; turn a space into a hyphen and
    /// drop anything else outright. No collapsing - a run of two spaces left behind by one
    /// removed character becomes two hyphens, because that is what the real algorithm does.
    /// </summary>
    internal static string Slugify(string text)
    {
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
    private static string Uniquify(string slug, Dictionary<string, int> seen)
    {
        if (!seen.TryGetValue(slug, out int count))
        {
            seen[slug] = 1;
            return slug;
        }

        seen[slug] = count + 1;
        return $"{slug}-{count}";
    }
}
