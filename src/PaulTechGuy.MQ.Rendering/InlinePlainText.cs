// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Markdig.Extensions.Abbreviations;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace PaulTechGuy.MQ.Rendering;

/// <summary>
/// The words a run of inlines reads as, with the markup taken away: a heading's text for the
/// outline and its anchor, an image's alt text, a link's label.
///
/// One walk for all of them, because each used to carry its own and each had the same hole. A
/// reader that only collects <see cref="LiteralInline"/> drops every entity - Markdig decodes
/// <c>&amp;amp;</c> and <c>&amp;nbsp;</c> into an <see cref="HtmlEntityInline"/>, not a literal -
/// so "Q&amp;amp;A" came out as "QA" in the outline and as "[QA]" in place of a missing picture.
/// </summary>
public static class InlinePlainText
{
    /// <summary>
    /// Exactly what was written, spaces included. Not trimmed, because a label's spaces can be
    /// the whole of what it says: <c>![ ](divider.png)</c> is how an author marks a picture as
    /// decorative, and trimming it would read as alt text nobody wrote.
    /// </summary>
    public static string Of(ContainerInline? container)
    {
        if (container is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        Append(container, builder);
        return builder.ToString();
    }

    /// <summary>
    /// A heading's words, trimmed. One method for the outline and for the anchor, since the two
    /// have to agree about which headings have any words at all.
    /// </summary>
    public static string OfHeading(HeadingBlock heading)
    {
        ArgumentNullException.ThrowIfNull(heading);

        return Of(heading.Inline).Trim();
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
                // The decoded character, which is what the preview shows. A non-breaking space
                // stays one: the anchor rule drops it where GitHub does, and anywhere else it
                // is the author's choice to keep two words together.
                case HtmlEntityInline entity:
                    builder.Append(entity.Transcoded.AsSpan());
                    break;
                // A leaf, not a container: *[HTML]: ... turns every later "HTML" into one of
                // these, and its own children are always empty. What was actually written is
                // the short form on the abbreviation itself, not its title-attribute expansion.
                case AbbreviationInline abbreviation:
                    builder.Append(abbreviation.Abbreviation?.Label);
                    break;
                case ContainerInline nested:
                    Append(nested, builder);
                    break;
            }
        }
    }
}
