// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PaulTechGuy.MQ.Domain;

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
        DocxExportSetup setup)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(setup);

        if (!setup.IncludeHeaderAndFooter || setup.VerticalMarginInches <= 0)
        {
            return (null, null);
        }

        HeaderPart headerPart = main.AddNewPart<HeaderPart>();

        headerPart.Header = new Header(
            new Paragraph(
                new ParagraphProperties(
                    new ParagraphStyleId { Val = StyleIds.Header },
                    new Justification { Val = JustificationValues.Left }),
                new Run(
                    new RunProperties(new Color { Val = "767676" }),
                    new Text(XmlSafeText.Clean(title))
                    {
                        Space = SpaceProcessingModeValues.Preserve,
                    })));

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

    /// <summary>Gray, so an unanswered line reads as a blank to fill rather than as text.</summary>
    private const string PlaceholderInk = "8A8A8A";

    /// <summary>
    /// Two blank lines at the body's line height, which is the gap the titles sit above.
    /// </summary>
    private const int TwoLines = 560;

    /// <summary>
    /// A title page, written as a template rather than as a statement.
    ///
    /// Off by default. It puts content into the document that the markdown did not contain,
    /// and only the author knows whether what they have written is a report or a note.
    ///
    /// Every line is present whether or not the front matter had anything to say about it.
    /// That is the point: the page is something to fill in, and one that quietly drops the
    /// lines it has no value for is not a template but a shrinking list - the author never
    /// learns that a version number was somewhere they could have put one. A line the front
    /// matter answered is written as the answer; a line it did not is written as the word
    /// itself, in gray.
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

        // The title is the one line that always has an answer - the document's own name, when
        // the front matter offers nothing - so it is never a placeholder.
        body.AppendChild(Line(
            StyleIds.Title,
            front.Title ?? title,
            placeholder: null,
            before: usableHeightTwips / 3));

        body.AppendChild(Line(StyleIds.Subtitle, front.Subject, "Sub-Title"));

        // The three facts are one block: the air above them is what separates them from the
        // titles, so they must not also be spaced from each other.
        body.AppendChild(Line(null, DateOrToday(front), null, before: TwoLines, tight: true));
        body.AppendChild(Line(null, front.Version, "Version", tight: true));
        body.AppendChild(Line(null, front.Author, "Author", tight: true));
    }

    /// <summary>
    /// The date the front matter gave, or today's.
    ///
    /// Written out rather than left as a placeholder because a cover page almost always wants
    /// a date and this one is true at the moment of export. Not a Word DATE field, which would
    /// keep updating: a report that is filed and read a year later should say when it was
    /// written, not claim to be current.
    /// </summary>
    private static string DateOrToday(FrontMatter front) =>
        front.Date is { Length: > 0 } stated
            ? stated
            : DateTime.Now.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// One line of the title page: what the front matter said, or the word to replace.
    /// </summary>
    private static Paragraph Line(
        string? styleId,
        string? value,
        string? placeholder,
        int before = 0,
        bool tight = false)
    {
        bool answered = value is { Length: > 0 };
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

        RunFormat format = answered ? default : default(RunFormat).WithColor(PlaceholderInk);

        return new Paragraph(
            properties,
            format.ToRun(answered ? value! : placeholder ?? string.Empty));
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
            default(RunFormat).ToRun("Contents")));

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
    /// A one-run field. Simple fields are enough for a page number: there is nothing to show
    /// until Word calculates it, and it always does.
    /// </summary>
    /// <summary>
    /// Which heading levels the contents lists, as the field's own range.
    ///
    /// Tied to the heading numbering rather than fixed at one to three, so the contents begin
    /// at the first numbered section. A document numbering from Heading 2 opens with an H1
    /// title, and that title listing itself as the first line of its own contents - above
    /// section 1, and the only entry with no number beside it - reads as a mistake rather
    /// than as a title.
    ///
    /// Numbering switched off is treated as starting at Heading 2 too, because a markdown
    /// file that opens with a single H1 title is the common shape whether or not anything in
    /// it is numbered.
    ///
    /// Three levels deep from wherever it starts, which is what Word's own contents does.
    /// </summary>
    private static string Levels(HeadingNumbering numbering)
    {
        int first = numbering switch
        {
            HeadingNumbering.FromHeading1 => 1,
            HeadingNumbering.FromHeading3 => 3,
            _ => 2,
        };

        return FormattableString.Invariant($"{first}-{first + 2}");
    }

    private static SimpleField Field(string instruction) => new(
        new Run(new Text("1") { Space = SpaceProcessingModeValues.Preserve }))
    {
        Instruction = instruction,
    };
}
