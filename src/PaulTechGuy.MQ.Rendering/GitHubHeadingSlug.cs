// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Markdig.Renderers.Html;
using Markdig.Syntax;
using PaulTechGuy.MQ.Markdown;
using MarkdigDocument = Markdig.Syntax.MarkdownDocument;

namespace PaulTechGuy.MQ.Rendering;

/// <summary>
/// The pass that gives every heading its anchor id, once <see cref="MarqoraMarkdownPipeline"/>
/// has removed Markdig's own auto-identifier extension.
///
/// The rule itself is <see cref="GitHubSlug"/>, one layer down, because the heading rewriter
/// needs the same answer and cannot reach into the renderer for it. Why it is not Markdig's rule
/// is explained there.
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

            string text = InlinePlainText.OfHeading(heading);

            if (text.Length == 0)
            {
                continue;
            }

            heading.GetAttributes().Id = GitHubSlug.Uniquify(GitHubSlug.Slugify(text), seen);
        }
    }
}
