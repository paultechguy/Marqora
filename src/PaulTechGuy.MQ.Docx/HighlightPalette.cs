// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;

namespace PaulTechGuy.MQ.Docx;

/// <summary>
/// How one token is drawn: the color theme's syntax slot it takes, whether it is bold or
/// italic, and for a diff line the slot it is shaded with.
/// </summary>
internal readonly record struct TokenStyle(string Slot, bool Bold = false, bool Italic = false, string? FillSlot = null);

/// <summary>
/// Which syntax slot each highlight.js token class takes - the same mapping, in the same groups,
/// as <c>webshell/syntax.css</c>, so a fence in Word wears the colors it wears in the preview
/// and in a PDF.
///
/// Slots rather than colors: the color comes from the theme the document is exported in, by way
/// of <see cref="DocxColors.Syntax"/>. The groups are GitHub's, which is what highlight.js was
/// written against; Default's syntax slots are GitHub's colors, darkened where they fell short
/// of AA on the gray a code block sits on.
///
/// A class that is not here is drawn in the code block's own ink, which is what the theme does
/// with it too: highlight.js emits more classes than any one theme colors.
/// </summary>
internal static class HighlightPalette
{
    private const string Keyword = "syntax-keyword";
    private const string Type = "syntax-type";
    private const string Function = "syntax-function";
    private const string Variable = "syntax-variable";
    private const string Number = "syntax-number";
    private const string Str = "syntax-string";
    private const string Builtin = "syntax-builtin";
    private const string Comment = "syntax-comment";
    private const string Tag = "syntax-tag";
    private const string Punctuation = "syntax-punctuation";
    private const string Ink = "code-block-text";

    private static readonly FrozenDictionary<string, TokenStyle> Styles =
        new Dictionary<string, TokenStyle>(StringComparer.Ordinal)
        {
            ["hljs-doctag"] = new(Keyword),
            ["hljs-keyword"] = new(Keyword),
            ["hljs-meta .hljs-keyword"] = new(Keyword),
            ["hljs-template-tag"] = new(Keyword),
            ["hljs-template-variable"] = new(Keyword),

            // this, self: written "hljs-variable language_", and a keyword in the stylesheet,
            // whose two-class selector outranks the plain variable rule.
            ["hljs-variable.language_"] = new(Keyword),
            ["hljs-variable language_"] = new(Keyword),

            ["hljs-type"] = new(Type),

            ["hljs-title"] = new(Function),
            ["hljs-title.class_"] = new(Function),
            ["hljs-title.function_"] = new(Function),

            ["hljs-attr"] = new(Number),
            ["hljs-attribute"] = new(Number),
            ["hljs-literal"] = new(Number),
            ["hljs-meta"] = new(Number),
            ["hljs-number"] = new(Number),
            ["hljs-operator"] = new(Number),
            ["hljs-selector-attr"] = new(Number),
            ["hljs-selector-class"] = new(Number),
            ["hljs-selector-id"] = new(Number),

            ["hljs-variable"] = new(Variable),

            ["hljs-regexp"] = new(Str),
            ["hljs-string"] = new(Str),

            ["hljs-built_in"] = new(Builtin),
            ["hljs-symbol"] = new(Builtin),
            ["hljs-bullet"] = new(Builtin),

            ["hljs-code"] = new(Comment),
            ["hljs-comment"] = new(Comment),
            ["hljs-formula"] = new(Comment),

            ["hljs-name"] = new(Tag),
            ["hljs-quote"] = new(Tag),
            ["hljs-selector-pseudo"] = new(Tag),
            ["hljs-selector-tag"] = new(Tag),

            ["hljs-punctuation"] = new(Punctuation),

            ["hljs-subst"] = new(Ink),
            ["hljs-section"] = new(Number, Bold: true),
            ["hljs-emphasis"] = new(Ink, Italic: true),
            ["hljs-strong"] = new(Ink, Bold: true),
            ["hljs-addition"] = new("syntax-addition", FillSlot: "syntax-addition-fill"),
            ["hljs-deletion"] = new("syntax-deletion", FillSlot: "syntax-deletion-fill"),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// The style for a token's class attribute, or null when nothing colors it.
    ///
    /// The attribute can carry several classes - highlight.js writes
    /// <c>hljs-title function_</c> - so the whole attribute is tried first and then each
    /// class, most specific first, which is how the stylesheet's own selectors resolve.
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
