// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Rendering;

namespace PaulTechGuy.MQ.Docx;

/// <summary>
/// The parts of a document that are not the document: the running header and footer, the
/// table of contents, and the title page.
///
/// All three are off unless asked for, except the header and footer - a Word file is something
/// people print and hand round, and one with no page numbers is the surprising outcome rather
/// than the safe one.
/// </summary>
internal static class DocxFurniture
{
    /// <summary>
    /// A header carrying the document's title, and a footer carrying the page number.
    ///
    /// Returns the relationship ids the section properties need, or nulls when there is
    /// nowhere to put them: with no margin at all there is no band above the text to print a
    /// header into, and one would overlap the first line.
    /// </summary>
    public static (string? Header, string? Footer) WriteHeaderAndFooter(
        MainDocumentPart main,
        string title,
        DocxExportSetup setup,
        ExportLayout layout)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(layout);

        if (!layout.IncludeHeaderAndFooter || setup.VerticalMarginInches <= 0)
        {
            return (null, null);
        }

        HeaderPart headerPart = main.AddNewPart<HeaderPart>();

        var titleRun = new Run(new RunProperties(new Color { Val = "767676" }));

        RunText.AppendTo(titleRun, title);

        headerPart.Header = new Header(
            new Paragraph(
                new ParagraphProperties(
                    new ParagraphStyleId { Val = StyleIds.Header },
                    new Justification { Val = JustificationValues.Left }),
                titleRun));

        FooterPart footerPart = main.AddNewPart<FooterPart>();

        // Right-aligned, against the outer margin, which is where a reader's thumb looks for a
        // page number and where Word's own page-number gallery puts one.
        var footer = new Paragraph(
            new ParagraphProperties(
                new ParagraphStyleId { Val = StyleIds.Footer },
                new Justification { Val = JustificationValues.Right }));

        // The number on its own, and nothing else. The section it sits in decides how it is
        // written - i, ii, iii through the contents, 1, 2, 3 through the body - and Word
        // answers PAGE relative to whichever section the page is in, so one footer part
        // serves both of them.
        //
        // A field rather than a number, so Word fills it in as it lays the document out and
        // it stays right when somebody adds a paragraph. This read "Page x of y" for a while,
        // on a SECTIONPAGES total; a total that has to be explained - it counts the section,
        // not the file - earns less than the bare number costs nothing to read.
        footer.AppendChild(Field(" PAGE "));

        footerPart.Footer = new Footer(footer);

        return (main.GetIdOfPart(headerPart), main.GetIdOfPart(footerPart));
    }

    /// <summary>
    /// Two blank lines at the body's line height, which is the gap the titles sit above.
    /// </summary>
    private const int TwoLines = 560;

    /// <summary>
    /// A title page, read from the front matter.
    ///
    /// Off by default. It puts content into the document that the markdown did not contain,
    /// and only the author knows whether what they have written is a report or a note.
    ///
    /// A line the front matter did not answer is left out, as on the PDF's cover
    /// (docs/Export-Alignment-Plan.md, P4). It was a template once - every line present, the
    /// unanswered ones written as the word itself in gray, so the author would learn there was
    /// somewhere to put a version number. But the two exports now draw one cover, and a gray
    /// "Author" is the one thing on it that reaches paper looking like a mistake: whoever is
    /// handed the printout cannot fill it in.
    ///
    /// The block sits a third of the way down the text column, measured against the column
    /// rather than the paper so that it lands in the same place whatever the margins are, and
    /// is flush left - where a report cover puts it, and what Word's own Title style, which is
    /// left-aligned, is built for.
    /// </summary>
    public static void WriteCoverPage(
        Body body,
        string title,
        FrontMatter front,
        int usableHeightTwips)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(front);

        // The title always has an answer - the document's own name, when the front matter
        // offers nothing.
        body.AppendChild(Line(StyleIds.Title, front.Title ?? title, before: usableHeightTwips / 3));

        if (front.Subject is { Length: > 0 } subject)
        {
            body.AppendChild(Line(StyleIds.Subtitle, subject));
        }

        // The three facts are one block: the air above them is what separates them from the
        // titles, so they must not also be spaced from each other. The date always has an
        // answer, so the air always lands on a line that is there.
        body.AppendChild(Line(null, front.CoverDate(), before: TwoLines, tight: true));

        foreach (string? fact in new[] { front.Version, front.Author })
        {
            if (fact is { Length: > 0 })
            {
                body.AppendChild(Line(null, fact, tight: true));
            }
        }
    }

    /// <summary>One line of the title page.</summary>
    private static Paragraph Line(
        string? styleId,
        string value,
        int before = 0,
        bool tight = false)
    {
        var properties = new ParagraphProperties();

        if (styleId is not null)
        {
            properties.AppendChild(new ParagraphStyleId { Val = styleId });
        }

        if (before > 0 || tight)
        {
            var spacing = new SpacingBetweenLines
            {
                Before = before.ToString(CultureInfo.InvariantCulture),
            };

            if (tight)
            {
                spacing.After = "0";
            }

            properties.AppendChild(spacing);
        }

        return new Paragraph(properties, default(RunFormat).ToRun(value));
    }

    /// <summary>
    /// A table of contents Word will build when it is asked to.
    ///
    /// The field is written as the five runs the format requires - begin, the instruction,
    /// separate, a placeholder, end - rather than as a simple field, because a complex one is
    /// what Word itself writes and what it reliably treats as needing an update.
    ///
    /// The placeholder is a sentence telling the reader what to do, and that is deliberate.
    /// Word normally offers to update the fields when the file opens, but a managed
    /// configuration can suppress that prompt - and then the placeholder is all they get. The
    /// words "Table of contents" would leave them looking at a heading with nothing under it.
    /// </summary>
    public static void WriteTableOfContents(
        Body body,
        int usableWidthTwips,
        HeadingNumbering numbering)
    {
        ArgumentNullException.ThrowIfNull(body);

        body.AppendChild(new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = StyleIds.TocHeading }),
            default(RunFormat).ToRun(ContentsListing.Title)));

        var field = new Paragraph(
            new ParagraphProperties(
                new Tabs(new TabStop
                {
                    Val = TabStopValues.Right,
                    Leader = TabStopLeaderCharValues.Dot,
                    Position = usableWidthTwips,
                })));

        field.AppendChild(new Run(new FieldChar
        {
            FieldCharType = FieldCharValues.Begin,
            Dirty = true,
        }));

        // Which levels to list, whether the entries are links, and that the level comes from
        // each paragraph's own outline level - which is why the heading styles set one.
        field.AppendChild(new Run(new FieldCode(@" TOC \o """ + Levels(numbering) + @""" \h \z \u ")
        {
            Space = SpaceProcessingModeValues.Preserve,
        }));

        field.AppendChild(new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }));

        field.AppendChild(new Run(
            new RunProperties(new Italic(), new Color { Val = "8A8A8A" }),
            new Text("Right-click here and choose Update Field to build the table of contents.")
            {
                Space = SpaceProcessingModeValues.Preserve,
            }));

        field.AppendChild(new Run(new FieldChar { FieldCharType = FieldCharValues.End }));

        body.AppendChild(field);
    }

    /// <summary>
    /// The paragraph that closes a section.
    ///
    /// A section break is not an element of its own: it is the section's properties carried
    /// in the paragraph mark of its last paragraph, and the last section of all puts its
    /// properties on the body instead. A next-page break starts a new page by itself, which
    /// is why the explicit page breaks that used to end the title page and the contents are
    /// gone - two of them would have left a blank sheet behind.
    /// </summary>
    public static Paragraph SectionBreak(SectionProperties section) =>
        new(new ParagraphProperties(section));

    /// <summary>
    /// Which heading levels the contents lists, as the field's own range. The rule is
    /// <see cref="ContentsListing.Levels"/>, shared with the PDF's contents page.
    /// </summary>
    private static string Levels(HeadingNumbering numbering)
    {
        (int first, int last) = ContentsListing.Levels(numbering);

        return FormattableString.Invariant($"{first}-{last}");
    }

    /// <summary>
    /// A one-run field. Simple fields are enough for a page number: there is nothing to show
    /// until Word calculates it, and it always does.
    /// </summary>
    private static SimpleField Field(string instruction) => new(
        new Run(new Text("1") { Space = SpaceProcessingModeValues.Preserve }))
    {
        Instruction = instruction,
    };
}
