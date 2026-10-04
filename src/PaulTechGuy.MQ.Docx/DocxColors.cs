// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Themes;

namespace PaulTechGuy.MQ.Docx;

/// <summary>
/// The color theme's light palette, in the shape Word writes a color: six hex digits, no hash,
/// upper case.
///
/// A Word document is output, and output is light whatever the window is wearing, so only the
/// light palette is ever read here. Every value is already opaque - the theme files are held to
/// that because Word's shading has no alpha - so nothing is composited on the way.
///
/// Each member names the slot it reads, which is the whole of the mapping from the preview to
/// Word: a slot added to a theme reaches Word when a member here reads it.
/// </summary>
internal sealed class DocxColors(ColorTheme theme)
{
    private readonly ThemePalette _palette = (theme ?? throw new ArgumentNullException(nameof(theme))).Light;

    /// <summary>The page Word draws on, which a fill equal to it does not need to paint.</summary>
    private const string White = "FFFFFF";

    public string Heading(int level) => Rgb($"heading-{Math.Clamp(level, 1, 6)}");

    public string HeadingRule => Rgb("heading-rule");

    public string Rule => Rgb("rule");

    public string Link => Rgb("link");

    public string Strong => Rgb("strong");

    public string Emphasis => Rgb("emphasis");

    public string ListMarker => Rgb("list-marker");

    public string TaskCheck => Rgb("task-check");

    public string FootnoteReference => Rgb("footnote-ref");

    public string QuoteBar => Rgb("quote-bar");

    public string QuoteText => Rgb("quote-text");

    /// <summary>Behind a quote, or null when that is the page itself and there is nothing to paint.</summary>
    public string? QuoteFill => Painted(Rgb("quote-fill"));

    public string CalloutBar(CalloutKind kind) => Rgb($"callout-{Slot(kind)}-bar");

    public string CalloutFill(CalloutKind kind) => Rgb($"callout-{Slot(kind)}-fill");

    public string CalloutTitle(CalloutKind kind) => Rgb($"callout-{Slot(kind)}-title");

    public string TableHeaderFill => Rgb("table-header-fill");

    public string TableHeaderText => Rgb("table-header-text");

    public string TableHeaderRule => Rgb("table-header-rule");

    public string TableBorder => Rgb("table-border");

    /// <summary>Behind every other body row, or null when that is the page and banding would show nothing.</summary>
    public string? TableStripe => Painted(Rgb("table-stripe"));

    public string CodeInlineText => Rgb("code-inline-text");

    public string CodeInlineFill => Rgb("code-inline-fill");

    public string CodeBlockText => Rgb("code-block-text");

    public string CodeBlockFill => Rgb("code-block-fill");

    public string CodeBlockBorder => Rgb("code-block-border");

    public string MarkFill => Rgb("mark-fill");

    /// <summary>A syntax slot by its id, for <see cref="HighlightPalette"/>.</summary>
    public string Syntax(string slot) => Rgb(slot);

    private string Rgb(string slot) => _palette[slot].TrimStart('#').ToUpperInvariant();

    private static string? Painted(string rgb) =>
        string.Equals(rgb, White, StringComparison.Ordinal) ? null : rgb;

    private static string Slot(CalloutKind kind) => kind switch
    {
        CalloutKind.Tip => "tip",
        CalloutKind.Important => "important",
        CalloutKind.Warning => "warning",
        CalloutKind.Caution => "caution",
        _ => "note",
    };
}
