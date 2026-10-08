// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// What a Word export should produce, chosen in the export dialog.
///
/// Separate from <see cref="PdfPageSetup"/> rather than folded into it, though the first three
/// members are the same three questions. Two reasons. A Word document and a print-ready PDF
/// genuinely want different margins, and a reader who sets one should not find the other
/// changed underneath them. The cover, contents and header boxes were here too while only Word
/// could draw them; they are <see cref="ExportLayout"/> now, shared by both exports, and only
/// the keys an older settings file wrote remain, to be read once.
///
/// The paper, orientation and margin enums <em>are</em> shared, because Letter is Letter and
/// portrait is portrait in both worlds, and a second set of names for them would be a second
/// set to keep right. The margin was a second set for a while - a PageMargin sharing four
/// member names with the PDF's own and agreeing with it on not one measurement - and the cost
/// showed up twice: a setup carried from one dialog to the other kept the word and changed the
/// page, and the same five numbers had to be written down in two places.
///
/// Properties are settable rather than init-only for the reason set out at the top of
/// <see cref="AppSettings"/>: the JSON source generator turns init-only properties into
/// constructor parameters and then assigns every one of them, so a key absent from an older
/// settings file would arrive as default and silently wipe the initializer here.
/// </summary>
public sealed record DocxExportSetup
{
    public PaperSize Paper { get; set; } = PaperSize.Letter;

    public PageOrientation Orientation { get; set; } = PageOrientation.Portrait;

    public PageMargin Margin { get; set; } = PageMargin.Normal;

    // ------------------------------------------------------------ before ExportLayout

    /// <summary>
    /// The header-and-footer box as a settings file from before <see cref="ExportLayout"/>
    /// recorded it, under the key it was written with. Read, never written: null on every setup
    /// this build makes, and a null key is left out of the file. See <see cref="LegacyLayout"/>.
    /// </summary>
    [JsonPropertyName("includeHeaderAndFooter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyIncludeHeaderAndFooter { get; set; }

    /// <summary>The contents box, from before <see cref="ExportLayout"/>. See <see cref="LegacyIncludeHeaderAndFooter"/>.</summary>
    [JsonPropertyName("includeTableOfContents")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyIncludeTableOfContents { get; set; }

    /// <summary>The cover box, from before <see cref="ExportLayout"/>. See <see cref="LegacyIncludeHeaderAndFooter"/>.</summary>
    [JsonPropertyName("includeCoverPage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyIncludeCoverPage { get; set; }

    /// <summary>
    /// The layout a settings file from before <see cref="ExportLayout"/> chose, or null when this
    /// setup carries none of the old keys. A key that is missing takes the default it had then,
    /// which is the default it has now.
    /// </summary>
    public ExportLayout? LegacyLayout() =>
        LegacyIncludeHeaderAndFooter is null && LegacyIncludeTableOfContents is null && LegacyIncludeCoverPage is null
            ? null
            : new ExportLayout
            {
                IncludeHeaderAndFooter = LegacyIncludeHeaderAndFooter ?? true,
                IncludeTableOfContents = LegacyIncludeTableOfContents ?? false,
                IncludeCoverPage = LegacyIncludeCoverPage ?? false,
            };

    /// <summary>This setup with the old keys dropped, once their answer is held by an <see cref="ExportLayout"/>.</summary>
    public DocxExportSetup WithoutLegacyLayout() => this with
    {
        LegacyIncludeHeaderAndFooter = null,
        LegacyIncludeTableOfContents = null,
        LegacyIncludeCoverPage = null,
    };

    public static DocxExportSetup Default => new();

    /// <summary>
    /// A setup that opens on the same page choices the reader last made for a PDF.
    ///
    /// Used the first time a document is exported to Word and never again: after that the
    /// answer is remembered on its own. Someone who has already told Marqora they work in A4
    /// should not have to say so a second time to reach the same paper.
    ///
    /// The margin is still not carried over, though the two dialogs now mean the same thing by
    /// every preset and copying it would no longer change the page under the reader. The
    /// reason is now the one in the summary above rather than a mismatch of units: a document
    /// meant to be edited and one meant to be printed want different margins often enough that
    /// inheriting the answer is a worse guess than starting from Word's own default.
    /// </summary>
    public static DocxExportSetup SeededFrom(PdfPageSetup pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        return new DocxExportSetup
        {
            Paper = pdf.Paper,
            Orientation = pdf.Orientation,
        };
    }

    /// <summary>Top and bottom margin, in inches.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double VerticalMarginInches => PageMargins.VerticalInches(Margin);

    /// <summary>
    /// Left and right margin, in inches. Not always the same as the vertical one: Moderate and
    /// Wide are Word's way of changing the measure without changing the page.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double HorizontalMarginInches => PageMargins.HorizontalInches(Margin);
}
