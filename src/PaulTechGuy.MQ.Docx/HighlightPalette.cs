// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;

namespace PaulTechGuy.MQ.Docx;

/// <summary>How one token is drawn: a color, and whether it is bold or italic.</summary>
internal readonly record struct TokenStyle(string Color, bool Bold = false, bool Italic = false);

/// <summary>
/// What each highlight.js token class looks like, in the GitHub light theme.
///
/// The same theme the preview uses on a light page and the same one an exported PDF prints,
/// so a fence looks the same in all three. The values come from
/// <c>webshell/vendor/highlight/github.min.css</c>, which is a vendored file that changes only
/// when the library is upgraded - unlike the document colors, which are Marqora's own and are
/// checked against app.css by a script.
///
/// A class that is not here is drawn in the body ink, which is what the theme does with it
/// too: highlight.js emits more classes than any one theme colors.
/// </summary>
internal static class HighlightPalette
{
    private const string Red = "D73A49";
    private const string Purple = "6F42C1";
    private const string Blue = "005CC5";
    private const string Navy = "032F62";
    private const string Orange = "E36209";
    private const string Grey = "6A737D";
    private const string Green = "22863A";
    private const string Ink = "24292E";

    private static readonly FrozenDictionary<string, TokenStyle> Styles =
        new Dictionary<string, TokenStyle>(StringComparer.Ordinal)
        {
            ["hljs-doctag"] = new(Red),
            ["hljs-keyword"] = new(Red),
            ["hljs-meta .hljs-keyword"] = new(Red),
            ["hljs-template-tag"] = new(Red),
            ["hljs-template-variable"] = new(Red),
            ["hljs-type"] = new(Red),
            ["hljs-variable.language_"] = new(Red),

            ["hljs-title"] = new(Purple),
            ["hljs-title.class_"] = new(Purple),
            ["hljs-title.function_"] = new(Purple),

            ["hljs-attr"] = new(Blue),
            ["hljs-attribute"] = new(Blue),
            ["hljs-literal"] = new(Blue),
            ["hljs-meta"] = new(Blue),
            ["hljs-number"] = new(Blue),
            ["hljs-operator"] = new(Blue),
            ["hljs-variable"] = new(Blue),
            ["hljs-selector-attr"] = new(Blue),
            ["hljs-selector-class"] = new(Blue),
            ["hljs-selector-id"] = new(Blue),

            ["hljs-regexp"] = new(Navy),
            ["hljs-string"] = new(Navy),

            ["hljs-built_in"] = new(Orange),
            ["hljs-symbol"] = new(Orange),

            ["hljs-code"] = new(Grey),
            ["hljs-comment"] = new(Grey),
            ["hljs-formula"] = new(Grey),

            ["hljs-name"] = new(Green),
            ["hljs-quote"] = new(Green),
            ["hljs-selector-pseudo"] = new(Green),
            ["hljs-selector-tag"] = new(Green),

            ["hljs-subst"] = new(Ink),
            ["hljs-section"] = new(Blue, Bold: true),
            ["hljs-bullet"] = new("735C0F"),
            ["hljs-emphasis"] = new(Ink, Italic: true),
            ["hljs-strong"] = new(Ink, Bold: true),
            ["hljs-addition"] = new(Green),
            ["hljs-deletion"] = new("B31D28"),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// The style for a token's class attribute, or null when nothing colors it.
    ///
    /// The attribute can carry several classes - highlight.js writes
    /// <c>hljs-title function_</c> - so each is tried, most specific first, which is how the
    /// stylesheet's own selectors resolve.
    /// </summary>
    public static TokenStyle? For(string? tokenClass)
    {
        if (string.IsNullOrWhiteSpace(tokenClass))
        {
            return null;
        }

        if (Styles.TryGetValue(tokenClass, out TokenStyle exact))
        {
            return exact;
        }

        // "hljs-title function_" is the title style; the second word only narrows it.
        string[] parts = tokenClass.Split(
            ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string part in parts)
        {
            if (Styles.TryGetValue(part, out TokenStyle style))
            {
                return style;
            }
        }

        return null;
    }
}
