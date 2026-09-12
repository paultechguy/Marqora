// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// What a Word export should produce, chosen in the export dialog.
///
/// Separate from <see cref="PdfPageSetup"/> rather than folded into it, though the first three
/// members are the same three questions. Two reasons. A Word document and a print-ready PDF
/// genuinely want different margins, and a reader who sets one should not find the other
/// changed underneath them. And the four members below the margin are Word-only - there is no
/// sensible place for a cover page or a table-of-contents field on the PDF dialog, and putting
/// them on the shared record would mean a dialog that has to know which fields to ignore.
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

    /// <summary>
    /// A header carrying the document title and a footer carrying the page number.
    ///
    /// On by default, which is the one default here that adds something the markdown did not
    /// ask for. A Word file is a thing people print and hand round, and one without page
    /// numbers is the surprising outcome rather than the safe one.
    /// </summary>
    public bool IncludeHeaderAndFooter { get; set; } = true;

    /// <summary>
    /// A Word table-of-contents field at the top of the document.
    ///
    /// Off by default: it puts visible content into the document that the markdown did not
    /// contain, and Word shows a prompt on open asking to update it. Heading bookmarks are
    /// written whether this is on or off - they cost nothing, are invisible, and are what
    /// makes Word's navigation pane work.
    /// </summary>
    public bool IncludeTableOfContents { get; set; }

    /// <summary>
    /// A title page, followed by a section break.
    ///
    /// Off by default for the same reason as the contents field. Pleasant for a report,
    /// wrong for a two-paragraph note, and only the author knows which they have written.
    /// </summary>
    public bool IncludeCoverPage { get; set; }

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
