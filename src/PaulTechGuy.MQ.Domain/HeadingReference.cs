// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// The text of a link that points at a heading in the same document - what Insert Reference
/// writes into the source.
///
/// The link itself is ordinary markdown, <c>[text](#slug)</c>, rather than a syntax of
/// Marqora's own. It is clickable in the preview, reads the same on GitHub, and the dead-anchor
/// check already flags it once the heading it names is renamed or removed - which is the only
/// warning a reference can give, since nothing rewrites it when the heading moves.
///
/// That is also why a number written this way is a snapshot. The section numbers are not in the
/// source at all; the renderer works them out each time, so "2.3" inserted today still says
/// "2.3" after a section has been added above it. The anchor keeps pointing at the right
/// heading; only the words go stale.
/// </summary>
public static class HeadingReference
{
    /// <summary>
    /// A heading's words or number made safe to stand between the brackets of a link.
    ///
    /// The heading's text arrives as plain text - its own markup already read away - so anything
    /// markdown would act on has to be escaped back, or a heading that was written as
    /// <c>The \*real\* answer</c> comes back as an italic. Brackets are the ones that matter most:
    /// an unescaped one ends the link text early and the reference stops being a link.
    ///
    /// An underscore is escaped only where it could open or close emphasis. Inside a word it is
    /// inert to CommonMark, and <c>snake_case</c> written as <c>snake\_case</c> is correct and
    /// ugly in exactly the place someone will read it.
    /// </summary>
    public static string EscapeLabel(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var builder = new StringBuilder(text.Length + 8);

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            bool escape = c switch
            {
                '\\' or '[' or ']' or '*' or '`' or '<' => true,
                '_' => !IsWordChar(text, i - 1) || !IsWordChar(text, i + 1),
                _ => false,
            };

            if (escape)
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>The whole link: <c>[label](#slug)</c>, with the label escaped.</summary>
    public static string Link(string label, string slug)
    {
        ArgumentNullException.ThrowIfNull(slug);

        return $"[{EscapeLabel(label)}](#{slug})";
    }

    private static bool IsWordChar(string text, int index) =>
        index >= 0 && index < text.Length && char.IsLetterOrDigit(text[index]);
}
