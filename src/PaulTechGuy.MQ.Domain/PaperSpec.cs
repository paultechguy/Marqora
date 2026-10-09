// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// One kind of thing on paper - body text, a level-two heading, a code block - and how it is
/// set: its face role, size, weight, slant, case, line height and the space around it.
/// </summary>
/// <param name="Name">The element's name in the stylesheet: <c>--mq-paper-&lt;name&gt;-size</c>.</param>
/// <param name="Description">What it is, for docs/Paper-Design.md.</param>
/// <param name="SizePoints">Type size. Word stores half-points, so a size is a whole or half point.</param>
/// <param name="LineHeight">Line height as a multiple of the size, as CSS means it. Word's "multiple" spacing means something else (a multiple of the face's natural line height), so Word is given the same distance in points.</param>
/// <param name="BeforePoints">Space above, in points.</param>
/// <param name="AfterPoints">Space below, in points.</param>
public sealed record PaperElement(
    string Name,
    string Description,
    PaperFaceRole Face,
    double SizePoints,
    double LineHeight,
    double BeforePoints,
    double AfterPoints,
    bool Bold = false,
    bool Italic = false,
    bool Uppercase = false)
{
    /// <summary>The CSS weight: bold, or the face's own (semibold for display).</summary>
    public int CssWeight => Bold ? 700 : PaperFaces.For(Face).CssWeight;

    /// <summary>The custom property a stylesheet reads one of this element's values from.</summary>
    public string CssProperty(string value) => $"--mq-paper-{Name}-{value}";
}

/// <summary>
/// How a document is set on paper, stated once, for both exports
/// (docs/Export-Alignment-Plan.md, D1 and §8; the human-readable table is docs/Paper-Design.md,
/// generated from this).
///
/// Colors already work this way - one theme file, the stylesheet declares none, Word reads the
/// palette through DocxColors - and this does the same for type and space. Word's styles read
/// these values (DocxStyles); the print stylesheet reads them as custom properties the host
/// pushes (<see cref="Css"/>), and declares none of them itself beyond a fallback for pages the
/// host never reaches.
///
/// The screen is not affected. The preview keeps its own sizing and the reader's chosen font;
/// this governs paper only.
///
/// The values were settled on 2026-10-06: body 11 pt at a line height of 1.4 with 8 pt after a
/// paragraph, the preview's heading scale in semibold, code at 9.5 pt, quotes upright. Faces
/// are in <see cref="PaperFaces"/>.
/// </summary>
public static class PaperSpec
{
    public static PaperElement Body { get; } = new(
        "body", "Paragraphs, list items and anything else not listed below.",
        PaperFaceRole.Text, 11, 1.4, 0, 8);

    public static PaperElement Heading1 { get; } = new(
        "h1", "Heading 1.", PaperFaceRole.Display, 22, 1.2, 18, 6);

    public static PaperElement Heading2 { get; } = new(
        "h2", "Heading 2, with the theme's rule beneath it.", PaperFaceRole.Display, 17, 1.2, 16, 6);

    public static PaperElement Heading3 { get; } = new(
        "h3", "Heading 3.", PaperFaceRole.Display, 14, 1.25, 14, 4);

    public static PaperElement Heading4 { get; } = new(
        "h4", "Heading 4.", PaperFaceRole.Display, 12, 1.3, 12, 4);

    public static PaperElement Heading5 { get; } = new(
        "h5", "Heading 5.", PaperFaceRole.Display, 11, 1.3, 10, 4);

    public static PaperElement Heading6 { get; } = new(
        "h6", "Heading 6, in capitals.", PaperFaceRole.Display, 10, 1.3, 10, 4, Uppercase: true);

    public static PaperElement CodeBlock { get; } = new(
        "code-block", "A fenced or indented code block.", PaperFaceRole.Code, 9.5, 1.35, 8, 8);

    public static PaperElement CodeInline { get; } = new(
        "code-inline", "Inline code, inside a sentence.", PaperFaceRole.Code, 9.5, 1.4, 0, 0);

    public static PaperElement Quote { get; } = new(
        "quote", "A blockquote, upright.", PaperFaceRole.Text, 11, 1.4, 8, 8);

    public static PaperElement Table { get; } = new(
        "table", "Table cells, header row included.", PaperFaceRole.Text, 10.5, 1.3, 2, 2);

    public static PaperElement Footnote { get; } = new(
        "footnote", "A footnote at the foot of the page.", PaperFaceRole.Text, 9.5, 1.3, 0, 0);

    public static PaperElement Caption { get; } = new(
        "caption", "A figure's caption.", PaperFaceRole.Text, 10, 1.3, 0, 10, Italic: true);

    public static PaperElement CalloutTitle { get; } = new(
        "callout-title", "A callout's label: Note, Warning.", PaperFaceRole.Text, 11, 1.4, 0, 0, Bold: true);

    public static PaperElement RunningHeader { get; } = new(
        "header", "The running header: the document's title.", PaperFaceRole.Text, 9, 1.0, 0, 0);

    public static PaperElement RunningFooter { get; } = new(
        "footer", "The running footer: the page number.", PaperFaceRole.Text, 9, 1.0, 0, 0);

    public static PaperElement ContentsEntry { get; } = new(
        "contents-entry", "One line of the contents.", PaperFaceRole.Text, 11, 1.4, 0, 0);

    public static PaperElement CoverTitle { get; } = new(
        "cover-title", "The cover page's title.", PaperFaceRole.Display, 28, 1.15, 0, 4);

    public static PaperElement CoverSubtitle { get; } = new(
        "cover-subtitle", "The cover page's subtitle.", PaperFaceRole.Text, 14, 1.3, 0, 8);

    public static IReadOnlyList<PaperElement> All { get; } =
    [
        Body, Heading1, Heading2, Heading3, Heading4, Heading5, Heading6,
        CodeBlock, CodeInline, Quote, Table, Footnote, Caption, CalloutTitle,
        RunningHeader, RunningFooter, ContentsEntry, CoverTitle, CoverSubtitle,
    ];

    /// <summary>A heading level's element, one to six.</summary>
    public static PaperElement Heading(int level) => level switch
    {
        1 => Heading1,
        2 => Heading2,
        3 => Heading3,
        4 => Heading4,
        5 => Heading5,
        6 => Heading6,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Headings run from 1 to 6."),
    };

    /// <summary>
    /// The spec as CSS custom properties on :root: one family chain per face role, and per
    /// element its family, size, weight, slant, case, line height and spacing. The host pushes
    /// this to the shell and to the paged print page; the print rules read it and nothing else.
    /// </summary>
    public static string Css()
    {
        var css = new StringBuilder(":root {\n");

        foreach (PaperFace face in PaperFaces.All)
        {
            Declare(css, PaperFaces.CssProperty(face.Role), face.CssFamilies);
        }

        foreach (PaperElement element in All)
        {
            Declare(css, element.CssProperty("family"), $"var({PaperFaces.CssProperty(element.Face)})");
            Declare(css, element.CssProperty("size"), Points(element.SizePoints));
            Declare(css, element.CssProperty("weight"), element.CssWeight.ToString(CultureInfo.InvariantCulture));
            Declare(css, element.CssProperty("style"), element.Italic ? "italic" : "normal");
            Declare(css, element.CssProperty("transform"), element.Uppercase ? "uppercase" : "none");
            Declare(css, element.CssProperty("line"), element.LineHeight.ToString("0.###", CultureInfo.InvariantCulture));
            Declare(css, element.CssProperty("before"), Points(element.BeforePoints));
            Declare(css, element.CssProperty("after"), Points(element.AfterPoints));
        }

        return css.Append("}\n").ToString();
    }

    private static void Declare(StringBuilder css, string property, string value) =>
        css.Append("  ").Append(property).Append(": ").Append(value).Append(";\n");

    private static string Points(double points) =>
        points.ToString("0.###", CultureInfo.InvariantCulture) + "pt";
}
