// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace PaulTechGuy.MQ.Docx;

/// <summary>One inline HTML tag, read far enough to know what it asks for.</summary>
internal readonly record struct HtmlTag(string Name, bool IsClosing, bool IsSelfClosing, string? Style);

/// <summary>
/// The inline HTML a markdown document is allowed to contain, and what Word can do about it.
///
/// Markdig hands these across as they are written: an opening tag, the text between, then a
/// closing tag, each its own node. Nothing had been done with them, so <c>&lt;kbd&gt;Ctrl&lt;/kbd&gt;</c>
/// arrived in Word as the word Ctrl in body text - the formatting silently gone while the
/// content stayed, which is the worst of the two ways to lose something.
///
/// What is handled here is everything Word has a form of. That is a real limit rather than a
/// chosen one: a run in Word can be bold, italic, underlined, struck through, raised or
/// lowered, colored, highlighted, and set in another face or size - and that is the list. A
/// tag asking for anything else has nowhere to land whatever effort is spent reading it, so
/// unknown tags are passed over and their content kept.
/// </summary>
internal static class InlineHtml
{
    /// <summary>
    /// Reads a tag, or returns null for anything that is not one - a comment, a doctype, or
    /// markup too malformed to act on.
    /// </summary>
    public static HtmlTag? Parse(string? tag)
    {
        if (string.IsNullOrEmpty(tag) || tag[0] != '<' || tag[^1] != '>')
        {
            return null;
        }

        ReadOnlySpan<char> inner = tag.AsSpan(1, tag.Length - 2).Trim();

        if (inner.Length == 0 || inner[0] == '!' || inner[0] == '?')
        {
            return null;
        }

        bool closing = inner[0] == '/';

        if (closing)
        {
            inner = inner[1..].Trim();
        }

        bool selfClosing = inner.Length > 0 && inner[^1] == '/';

        if (selfClosing)
        {
            inner = inner[..^1].Trim();
        }

        int nameEnd = inner.IndexOfAny(' ', '\t', '\n');
        ReadOnlySpan<char> name = nameEnd < 0 ? inner : inner[..nameEnd];

        if (name.Length == 0)
        {
            return null;
        }

        string? style = nameEnd < 0 ? null : Attribute(inner[nameEnd..], "style");

        return new HtmlTag(name.ToString().ToLowerInvariant(), closing, selfClosing, style);
    }

    /// <summary>
    /// What an opening tag does to the run formatting, or null when Word has no equivalent
    /// and the tag should simply be stepped over.
    /// </summary>
    public static RunFormat? Apply(HtmlTag tag, RunFormat format)
    {
        RunFormat result = tag.Name switch
        {
            "b" or "strong" => format.WithBold(),
            "i" or "em" or "cite" or "var" or "dfn" => format.WithItalic(),
            "u" or "ins" => format.WithUnderline(),
            "s" or "del" or "strike" => format.WithStrike(),
            "sub" => format.WithSubscript(),
            "sup" => format.WithSuperscript(),

            // A key and a snippet of code are the same thing to Word: monospaced, shaded, and
            // set apart from the prose around them.
            "kbd" or "code" or "samp" or "tt" => format.WithCharacterStyle(StyleIds.CodeChar),
            "mark" => format.WithCharacterStyle(StyleIds.Mark),

            // A span carries nothing on its own; whether it means anything is in its style.
            "span" or "small" or "big" or "abbr" or "q" => format,

            _ => format,
        };

        if (tag.Style is { Length: > 0 } style)
        {
            result = WithStyle(result, style);
        }

        return result == format && tag.Name is not ("span" or "small" or "big" or "abbr" or "q")
            && !IsKnown(tag.Name)
            ? null
            : result;
    }

    /// <summary>
    /// Whether a tag is one the walker understands, so an unknown one can be stepped over
    /// without its closing partner unbalancing the stack.
    /// </summary>
    public static bool IsKnown(string name) => name is
        "b" or "strong" or "i" or "em" or "cite" or "var" or "dfn"
        or "u" or "ins" or "s" or "del" or "strike"
        or "sub" or "sup" or "kbd" or "code" or "samp" or "tt" or "mark"
        or "span" or "small" or "big" or "abbr" or "q";

    /// <summary>
    /// The colors out of an inline style declaration.
    ///
    /// Only the two Word can hold. A style attribute can ask for a great deal that a run has
    /// no property for - a margin, a border, a float - and reading those to no purpose would
    /// be effort spent producing nothing.
    /// </summary>
    private static RunFormat WithStyle(RunFormat format, string style)
    {
        RunFormat result = format;

        if (Declaration(style, "color") is { } color && Rgb(color) is { } ink)
        {
            result = result.WithColor(ink);
        }

        if (Declaration(style, "background-color") is { } fill && Rgb(fill) is { } shade)
        {
            result = result.WithShading(shade);
        }

        if (Declaration(style, "font-weight") is "bold" or "600" or "700" or "800" or "900")
        {
            result = result.WithBold();
        }

        if (Declaration(style, "font-style") is "italic" or "oblique")
        {
            result = result.WithItalic();
        }

        return result;
    }

    private static string? Declaration(string style, string property)
    {
        foreach (string part in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = part.IndexOf(':', StringComparison.Ordinal);

            if (colon > 0
                && part.AsSpan(0, colon).Trim().Equals(property, StringComparison.OrdinalIgnoreCase))
            {
                return part[(colon + 1)..].Trim().ToLowerInvariant();
            }
        }

        return null;
    }

    /// <summary>
    /// A CSS color as the six hex digits Word writes, or null for one it cannot express -
    /// a named color, or a function like rgb() or color-mix().
    /// </summary>
    private static string? Rgb(string value)
    {
        if (!value.StartsWith('#'))
        {
            return null;
        }

        ReadOnlySpan<char> digits = value.AsSpan(1).Trim();

        // The short form doubles each digit: #abc is #aabbcc.
        if (digits.Length == 3)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{digits[0]}{digits[0]}{digits[1]}{digits[1]}{digits[2]}{digits[2]}")
                .ToUpperInvariant();
        }

        // Eight digits is a color with an alpha, which a run has no room for; the color is
        // still the first six.
        return digits.Length is 6 or 8 ? digits[..6].ToString().ToUpperInvariant() : null;
    }

    private static string? Attribute(ReadOnlySpan<char> attributes, string name)
    {
        int at = attributes.IndexOf($"{name}=", StringComparison.OrdinalIgnoreCase);

        if (at < 0)
        {
            return null;
        }

        ReadOnlySpan<char> rest = attributes[(at + name.Length + 1)..].TrimStart();

        if (rest.Length == 0)
        {
            return null;
        }

        char quote = rest[0];

        if (quote is not ('"' or '\''))
        {
            int space = rest.IndexOfAny(' ', '\t');
            return (space < 0 ? rest : rest[..space]).ToString();
        }

        ReadOnlySpan<char> value = rest[1..];
        int end = value.IndexOf(quote);

        return end < 0 ? null : value[..end].ToString();
    }
}
