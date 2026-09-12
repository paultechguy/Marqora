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
/// Word's styles do not hold colors and fonts directly; they hold references into a theme -
/// "the major font", "accent 1" - and the theme resolves them. Writing a theme is therefore
/// the difference between a document whose headings are a teal somebody typed in, and one
/// whose headings follow a scheme the reader can swap wholesale. The second is what a Word
/// user expects of a Word file.
///
/// The XML is an embedded resource rather than built here. Most of it is the format scheme,
/// which Word requires in full and which nothing in a markdown document ever uses; building
/// that in code would be a hundred lines that say nothing. What does vary - the accent and
/// the two faces - is patched on the way out, so Marqora's teal stays written down once, in
/// <see cref="DocumentAccent"/>.
/// </summary>
internal static class DocxTheme
{
    private const string ResourceName = "PaulTechGuy.MQ.Docx.Resources.theme1.xml";

    private static readonly XNamespace A =
        "http://schemas.openxmlformats.org/drawingml/2006/main";

    /// <summary>
    /// The body face, and the fallback behind it.
    ///
    /// Aptos is what Word itself has used for new documents since 2024, so it is the face
    /// that makes an exported file look native rather than like something a converter
    /// produced. It ships with Microsoft 365 and not with Windows, which is why the styles
    /// reference the theme rather than naming a face: on a machine without Aptos, Word
    /// substitutes rather than falling back to Times New Roman, and the document still reads
    /// as intended.
    /// </summary>
    public const string MinorFont = "Aptos";

    /// <summary>The heading face. Aptos Display is Aptos cut for large sizes.</summary>
    public const string MajorFont = "Aptos Display";

    public static void Write(ThemePart part)
    {
        ArgumentNullException.ThrowIfNull(part);

        XDocument theme = Load();

        SetSchemeColor(theme, "accent1", DocumentAccent.LightRgb);
        SetSchemeColor(theme, "hlink", DocumentAccent.LightRgb);
        SetFont(theme, "majorFont", MajorFont);
        SetFont(theme, "minorFont", MinorFont);

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
