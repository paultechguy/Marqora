// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using PaulTechGuy.MQ.Domain;
using MarkdigDocument = Markdig.Syntax.MarkdownDocument;

namespace PaulTechGuy.MQ.Rendering;

/// <summary>
/// Writes the section numbers into a parsed document, before it is rendered.
///
/// Here rather than in the shell's stylesheet or its script, so that one pass produces both
/// the number the preview shows and the number the outline panel shows, from one rule. The
/// numbers are real text by the time the HTML leaves this assembly, which is what carries
/// them into the HTML export, the PDF, the printed page and the rich-text clipboard without
/// any of those needing to know the feature exists.
///
/// The markdown source is never touched: this runs on the parsed copy on its way to HTML.
/// </summary>
internal static class HeadingNumberPass
{
    /// <summary>
    /// The class on the span that carries a number. The preview's stylesheet styles it -
    /// tabular figures, and the trailing space preserved - and a re-render replaces the
    /// whole fragment, so nothing has to find these again to remove them.
    /// </summary>
    public const string NumberClass = "mq-heading-number";

    /// <summary>
    /// Numbers every heading in the document and returns what each one was given, so the
    /// outline can be built with the same numbers rather than a second opinion.
    ///
    /// Run after parsing and before rendering. That order matters more than it looks:
    /// UseAutoIdentifiers assigns each heading its anchor id while the document is being
    /// parsed, so by the time this adds anything the ids are settled and a numbered heading
    /// still answers to the same "#my-heading" link it always did.
    /// </summary>
    public static IReadOnlyDictionary<HeadingBlock, string> Apply(
        MarkdigDocument document,
        HeadingNumbering numbering)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (numbering == HeadingNumbering.Off)
        {
            return new Dictionary<HeadingBlock, string>();
        }

        List<HeadingBlock> headings = [.. document.Descendants<HeadingBlock>()];

        if (headings.Count == 0)
        {
            return new Dictionary<HeadingBlock, string>();
        }

        // Every heading counts, including one with no text of its own. An empty heading is
        // still a section as far as the document is concerned, and skipping it here would
        // renumber everything after it.
        int[] levels = new int[headings.Count];

        for (int i = 0; i < headings.Count; i++)
        {
            levels[i] = headings[i].Level;
        }

        IReadOnlyList<string> numbers = HeadingNumbers.Compute(levels, numbering);
        Dictionary<HeadingBlock, string> assigned = [];

        for (int i = 0; i < headings.Count; i++)
        {
            if (numbers[i].Length == 0)
            {
                continue;
            }

            assigned[headings[i]] = numbers[i];
            Insert(headings[i], numbers[i]);
        }

        return assigned;
    }

    /// <summary>
    /// Puts the number at the front of the heading's own content, as raw HTML.
    ///
    /// An <see cref="HtmlInline"/> rather than a literal: the renderer writes its tag
    /// through untouched, so the span survives, and everything that walks a heading's
    /// inlines for its text - the outline reader, and the slug it falls back on - steps over
    /// it without having to know what it is.
    /// </summary>
    private static void Insert(HeadingBlock heading, string number)
    {
        if (heading.Inline is null)
        {
            return;
        }

        // The trailing spaces belong to the label rather than to a margin, so they come
        // along with it into a copy, an export and a print. The stylesheet keeps them from
        // collapsing.
        var label = new HtmlInline($"<span class=\"{NumberClass}\">{number}  </span>");

        if (heading.Inline.FirstChild is { } first)
        {
            first.InsertBefore(label);
        }
        else
        {
            heading.Inline.AppendChild(label);
        }
    }
}
