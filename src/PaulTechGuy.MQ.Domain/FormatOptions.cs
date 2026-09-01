// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>Bullet character the formatter settles on when it normalises list markers.</summary>
public enum BulletMarker
{
    Dash,
    Asterisk,
    Plus,
}

/// <summary>Which characters the formatter settles on for emphasis.</summary>
public enum EmphasisStyle
{
    /// <summary>*italic* and **bold**.</summary>
    Asterisk,

    /// <summary>_italic_ and __bold__.</summary>
    Underscore,
}

/// <summary>Line ending the formatter writes.</summary>
public enum LineEndingStyle
{
    /// <summary>Whatever the document already uses, deciding by majority.</summary>
    Detect,

    Crlf,
    Lf,
}

/// <summary>
/// Which tidy-up rules the markdown formatter applies.
///
/// Every rule is independent and can be turned off, because "tidy" is a matter of taste and
/// a formatter that insists on its own is one people stop using. The defaults match what
/// most markdown linters agree on; the two that rewrite prose rather than punctuation —
/// <see cref="NormalizeMarkers"/> and <see cref="ReflowParagraphs"/> — start off.
/// </summary>
public sealed record FormatOptions
{
    /// <summary>A space after the hashes: <c>#Heading</c> becomes <c># Heading</c>.</summary>
    public bool HeadingSpace { get; set; } = true;

    /// <summary>
    /// Strips spaces and tabs from the end of a line. A deliberate two-space hard line break
    /// is preserved: it is meaningful markdown, not stray whitespace.
    /// </summary>
    public bool TrailingWhitespace { get; set; } = true;

    /// <summary>
    /// Rewrites every bullet to the same character. Off by default: it touches lines the
    /// author chose deliberately, and mixed markers are legal markdown.
    /// </summary>
    public bool NormalizeMarkers { get; set; }

    /// <summary>Makes every line ending in the file the same.</summary>
    public bool LineEndings { get; set; } = true;

    /// <summary>A blank line before and after headings, fences, tables and lists.</summary>
    public bool BlankLines { get; set; } = true;

    /// <summary>A space after a list marker: <c>-item</c> becomes <c>- item</c>.</summary>
    public bool ListMarkerSpace { get; set; } = true;

    /// <summary>Closes the gap in <c>[text] (url)</c>, which most renderers do not treat as a link.</summary>
    public bool LinkSyntax { get; set; } = true;

    /// <summary>Exactly one newline at the end of the file.</summary>
    public bool EofNewline { get; set; } = true;

    /// <summary>Collapses runs of blank lines down to a single one.</summary>
    public bool CollapseBlanks { get; set; } = true;

    /// <summary>Renumbers ordered lists so their numbering is consistent.</summary>
    public bool OrderedNumbering { get; set; } = true;

    /// <summary>A space after the angle bracket: <c>&gt;quote</c> becomes <c>&gt; quote</c>.</summary>
    public bool BlockquoteSpace { get; set; } = true;

    /// <summary>Pads table cells so the pipes line up, preserving alignment colons.</summary>
    public bool FormatTables { get; set; } = true;

    /// <summary>Unifies fence characters and guarantees a blank line either side.</summary>
    public bool TidyCodeFences { get; set; } = true;

    /// <summary>Converts underlined headings to the <c>#</c> form.</summary>
    public bool SetextToAtx { get; set; } = true;

    /// <summary>Settles on one pair of characters for italic and bold.</summary>
    public bool UnifyEmphasis { get; set; } = true;

    /// <summary>
    /// Re-wraps paragraphs to <see cref="WrapColumn"/>.
    ///
    /// Off by default, and deliberately so. Re-wrapping rewrites every line of every
    /// paragraph, which destroys one-sentence-per-line writing and turns a one-word edit
    /// into a diff covering the whole file. It is genuinely useful, but only when asked for.
    /// </summary>
    public bool ReflowParagraphs { get; set; }

    /// <summary>Column to wrap at when <see cref="ReflowParagraphs"/> is on.</summary>
    public int WrapColumn { get; set; } = 80;

    public BulletMarker Bullet { get; set; } = BulletMarker.Dash;

    public EmphasisStyle Emphasis { get; set; } = EmphasisStyle.Asterisk;

    public LineEndingStyle LineEndingStyle { get; set; } = LineEndingStyle.Detect;

    /// <summary>Run the formatter automatically before every save. Off by default.</summary>
    public bool FormatOnSave { get; set; }

    public static FormatOptions Default => new();
}
