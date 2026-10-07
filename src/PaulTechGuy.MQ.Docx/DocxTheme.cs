// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Docx;

/// <summary>
/// The document theme, which is what lets Word restyle the whole file from the Design tab.
///
/// Word's styles can hold references into a theme - "the major font", "accent 1" - for the
/// theme to resolve, and the faces here are used that way, so the Design tab can swap them.
///
/// The XML is an embedded resource rather than built here. Most of it is the format scheme,
/// which Word requires in full and which nothing in a markdown document ever uses; building
/// that in code would be a hundred lines that say nothing. What does vary - the scheme colors
/// and the two faces - is patched on the way out.
///
/// The scheme colors are the color theme's: accent 1 and the hyperlink are its link color,
/// accent 2 is the note callout's bar, and accents 3 to 6 are the tip, important, warning and
/// caution bars. The styles themselves carry plain colors (see DocxStyles), so this is what
/// Word's own galleries - a new table, a chart, SmartArt - start from. Accent 1 is the link
/// rather than a heading color because several themes, Default among them, have near-black
/// headings, and a black accent 1 would turn every one of those galleries black.
/// </summary>
internal static class DocxTheme
{
    private const string ResourceName = "PaulTechGuy.MQ.Docx.Resources.theme1.xml";

    private static readonly XNamespace A =
        "http://schemas.openxmlformats.org/drawingml/2006/main";

    /// <summary>
    /// Writes the theme part: the color theme's link and callout colors into the scheme, and
    /// the paper spec's two faces as the theme's fonts - the body face as minor, the display
    /// face as major. The styles reference those two by role rather than naming a face, which
    /// is what lets Word's Design tab restyle the document, and <see cref="PaperFaces"/> is the
    /// only place either is named.
    ///
    /// The faces were Aptos and Aptos Display until the paper spec, which is what Word itself
    /// uses for new documents. Aptos is an Office cloud font WebView2 cannot see, so the PDF of
    /// the same document came out in Segoe UI; both exports now use the faces both can draw.
    /// </summary>
    public static void Write(ThemePart part, DocxColors colors)
    {
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(colors);

        XDocument theme = Load();

        SetSchemeColor(theme, "accent1", colors.Link);
        SetSchemeColor(theme, "accent2", colors.CalloutBar(CalloutKind.Note));
        SetSchemeColor(theme, "accent3", colors.CalloutBar(CalloutKind.Tip));
        SetSchemeColor(theme, "accent4", colors.CalloutBar(CalloutKind.Important));
        SetSchemeColor(theme, "accent5", colors.CalloutBar(CalloutKind.Warning));
        SetSchemeColor(theme, "accent6", colors.CalloutBar(CalloutKind.Caution));
        SetSchemeColor(theme, "hlink", colors.Link);
        SetFont(theme, "majorFont", PaperFaces.Display.WordFamily!);
        SetFont(theme, "minorFont", PaperFaces.Text.WordFamily!);

        using Stream stream = part.GetStream(FileMode.Create, FileAccess.Write);

        theme.Save(stream);
    }

    private static XDocument Load()
    {
        using Stream? stream = typeof(DocxTheme).Assembly
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded theme {ResourceName} is missing from the assembly.");

        return XDocument.Load(stream);
    }

    private static void SetSchemeColor(XDocument theme, string slot, string rgb)
    {
        XElement? target = theme.Descendants(A + slot).FirstOrDefault()?.Element(A + "srgbClr");

        target?.SetAttributeValue("val", rgb);
    }

    private static void SetFont(XDocument theme, string slot, string typeface)
    {
        XElement? target = theme.Descendants(A + slot).FirstOrDefault()?.Element(A + "latin");

        target?.SetAttributeValue("typeface", typeface);
    }
}
