// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace PaulTechGuy.MQ.Docx;

/// <summary>
/// Document-wide settings.
///
/// Small, and two of the three entries matter more than their size suggests. The color-scheme
/// mapping is what connects a style saying "accent 1" to the theme that defines it; without
/// it Word still opens the document but the Design tab has nothing coherent to re-map. And
/// the compatibility mode tells Word to lay the document out by its current rules rather than
/// emulating Word 2007, which changes line breaking and table sizing.
/// </summary>
internal static class DocxSettings
{
    /// <summary>
    /// Word 2013 and later. The highest value Word understands; anything lower puts the
    /// layout engine into an emulation mode that no document written today wants.
    /// </summary>
    private const int CurrentCompatibilityMode = 15;

    public static void Write(DocumentSettingsPart part, bool updateFieldsOnOpen, bool hasFootnotes)
    {
        ArgumentNullException.ThrowIfNull(part);

        var settings = new Settings(
            new Zoom { Percent = "100" },
            new DefaultTabStop { Val = 720 },
            new CharacterSpacingControl { Val = CharacterSpacingValues.DoNotCompress });

        if (updateFieldsOnOpen)
        {
            // What makes Word offer to build the table of contents when the file is opened.
            // Until someone accepts that offer - or presses F9 - the field shows the
            // placeholder text the exporter wrote into it, which is why that placeholder is a
            // sentence telling them so rather than the words "Table of contents".
            //
            // Before the footnote properties, not after: the settings are a schema sequence
            // too, and this is one of the places where the order reads backwards.
            settings.AppendChild(new UpdateFieldsOnOpen { Val = true });
        }

        if (hasFootnotes)
        {
            // Names the two separator notes so Word knows which of them draws the rule above
            // the footnotes and which continues one onto the next page.
            settings.AppendChild(new FootnoteDocumentWideProperties(
                new FootnoteSpecialReference { Id = -1 },
                new FootnoteSpecialReference { Id = 0 }));
        }

        settings.AppendChild(new Compatibility(
            new CompatibilitySetting
            {
                Name = CompatSettingNameValues.CompatibilityMode,
                Uri = "http://schemas.microsoft.com/office/word",
                Val = CurrentCompatibilityMode.ToString(System.Globalization.CultureInfo.InvariantCulture),
            }));

        settings.AppendChild(new ThemeFontLanguages { Val = "en-US" });

        settings.AppendChild(new ColorSchemeMapping
        {
            Background1 = ColorSchemeIndexValues.Light1,
            Text1 = ColorSchemeIndexValues.Dark1,
            Background2 = ColorSchemeIndexValues.Light2,
            Text2 = ColorSchemeIndexValues.Dark2,
            Accent1 = ColorSchemeIndexValues.Accent1,
            Accent2 = ColorSchemeIndexValues.Accent2,
            Accent3 = ColorSchemeIndexValues.Accent3,
            Accent4 = ColorSchemeIndexValues.Accent4,
            Accent5 = ColorSchemeIndexValues.Accent5,
            Accent6 = ColorSchemeIndexValues.Accent6,
            Hyperlink = ColorSchemeIndexValues.Hyperlink,
            FollowedHyperlink = ColorSchemeIndexValues.FollowedHyperlink,
        });

        part.Settings = settings;
    }
}
