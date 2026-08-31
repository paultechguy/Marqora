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
    public static IReadOnlyList<OutlineHeading> ReadOutline(MarkdigDocument document)
    {
        List<OutlineHeading> headings = [];

        foreach (HeadingBlock heading in document.Descendants<HeadingBlock>())
        {
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
