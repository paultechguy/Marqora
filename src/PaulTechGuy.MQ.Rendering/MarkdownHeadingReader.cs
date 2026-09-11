// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using PaulTechGuy.MQ.Domain;
using MarkdigDocument = Markdig.Syntax.MarkdownDocument;

namespace PaulTechGuy.MQ.Rendering;

/// <summary>Extracts a plain-text outline from a parsed document for the outline flyout.</summary>
internal static class MarkdownHeadingReader
{
    /// <param name="numbers">
    /// What <see cref="HeadingNumberPass"/> gave each heading, when the document has been
    /// numbered. Read from here rather than from the heading's own inlines: the span the
    /// pass inserted is raw HTML, and parsing a number back out of it would be inventing a
    /// second source for something already known.
    /// </param>
    public static IReadOnlyList<OutlineHeading> ReadOutline(
        MarkdigDocument document,
        IReadOnlyDictionary<HeadingBlock, string>? numbers = null)
    {
        List<OutlineHeading> headings = [];

        foreach (HeadingBlock heading in document.Descendants<HeadingBlock>())
        {
            // The number span is an HtmlInline, which the walk below ignores, so this is
            // the heading's own words whether the document has been numbered or not.
            string text = ToPlainText(heading.Inline);

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            headings.Add(new OutlineHeading
            {
                Level = heading.Level,
                Text = text,
                // UseAutoIdentifiers populates Id; fall back to a slug so anchors always work.
                Slug = heading.GetAttributes().Id ?? Slugify(text),
                SourceLine = heading.Line,
                Number = numbers is not null && numbers.TryGetValue(heading, out string? number)
                    ? number
                    : string.Empty,
            });
        }

        return headings;
    }

    private static string ToPlainText(ContainerInline? container)
    {
        if (container is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        Append(container, builder);
        return builder.ToString().Trim();
    }

    private static void Append(ContainerInline container, StringBuilder builder)
    {
        foreach (Inline inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    builder.Append(literal.Content.AsSpan());
                    break;
                case CodeInline code:
                    builder.Append(code.Content);
                    break;
                case LineBreakInline:
                    builder.Append(' ');
                    break;
                case ContainerInline nested:
                    Append(nested, builder);
                    break;
            }
        }
    }

    private static string Slugify(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (char c in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (c is ' ' or '-' or '_' && builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }
}
