// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Docx.Tests;

/// <summary>
/// The parts of the file that are not its content: the running header and footer, the table of
/// contents, the title page, and what the file says about itself in Explorer and in Word's
/// Info pane.
///
/// Two of the four are off unless asked for, and the tests say so. A contents field and a
/// cover page both put content into the document that the markdown did not contain, and a new
/// switch should default to leaving the author's document alone.
/// </summary>
public class FurnitureTests
{
    [Fact]
    public async Task A_header_and_footer_are_written_by_default()
    {
        using var exported = await ExportedDocument.FromAsync("# Report\n\nText.\n");

        HeaderText(exported.Path).ShouldContain("Test document");
        FooterXml(exported.Path).ShouldContain("PAGE");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task The_page_number_is_a_field_rather_than_a_number()
    {
        using var exported = await ExportedDocument.FromAsync("Text.\n");

        string footer = FooterXml(exported.Path);

        // Written as numbers they would all say the same thing, and would be wrong the
        // moment anybody edited the document.
        footer.ShouldContain("PAGE");

        // The number alone. A total would have to count the section rather than the file -
        // the sections each begin again - and saying so on every page earns less than it
        // costs to read.
        footer.ShouldNotContain("SECTIONPAGES");
        footer.ShouldNotContain("NUMPAGES");
        footer.ShouldNotContain(" of ");
    }

    [Fact]
    public async Task They_can_be_turned_off()
    {
        using var exported = await ExportedDocument.FromAsync(
            "Text.\n",
            new DocxExportSetup { IncludeHeaderAndFooter = false });

        HasHeader(exported.Path).ShouldBeFalse();
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// With no margin there is no band above the text to print into, and a header would be
    /// laid over the first line of the document.
    /// </summary>
    [Fact]
    public async Task A_document_with_no_margin_gets_no_header()
    {
        using var exported = await ExportedDocument.FromAsync(
            "Text.\n",
            new DocxExportSetup { Margin = PageMargin.None });

        HasHeader(exported.Path).ShouldBeFalse();
        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task The_contents_field_is_off_unless_it_is_asked_for()
    {
        using var exported = await ExportedDocument.FromAsync("# One\n\n## Two\n");

        exported.DocumentXml().ShouldNotContain("TOC");
    }

    [Fact]
    public async Task A_contents_field_is_one_Word_will_offer_to_build()
    {
        using var exported = await ExportedDocument.FromAsync(
            "# One\n\n## Two\n",
            new DocxExportSetup { IncludeTableOfContents = true });

        string xml = exported.DocumentXml();

        // Levels two to four, not one to three: the contents begin where the numbering does,
        // and a document numbering from Heading 2 opens with an H1 title that has no business
        // being the first line of its own contents.
        xml.ShouldContain(@"TOC \o ""2-4"" \h \z \u");
        xml.ShouldContain("Contents");

        // The heading over it is out of the outline, so the contents cannot list themselves.
        xml.ShouldContain("TOCHeading");
        exported.StylesXml().ShouldContain("outlineLvl");

        // The prompt on open comes from the settings part, not from the field.
        SettingsXml(exported.Path).ShouldContain("updateFields");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// A managed Word configuration can suppress the update prompt, and then the placeholder
    /// is the only thing the reader sees. It has to tell them what to do.
    /// </summary>
    [Fact]
    public async Task The_contents_placeholder_says_how_to_build_it()
    {
        using var exported = await ExportedDocument.FromAsync(
            "# One\n",
            new DocxExportSetup { IncludeTableOfContents = true });

        exported.PlainText().ShouldContain("Update Field");
    }

    [Fact]
    public async Task A_cover_page_is_off_unless_it_is_asked_for()
    {
        using var exported = await ExportedDocument.FromAsync(
            "---\ntitle: Quarterly Report\nauthor: A Person\n---\n\nText.\n");

        exported.PlainText().ShouldNotContain("A Person");
    }

    [Fact]
    public async Task A_cover_page_is_built_from_the_front_matter()
    {
        using var exported = await ExportedDocument.FromAsync(
            "---\ntitle: Quarterly Report\nauthor: A Person\ndate: 2026-09-11\n---\n\nText.\n",
            new DocxExportSetup { IncludeCoverPage = true });

        string text = exported.PlainText();

        text.ShouldContain("Quarterly Report");
        text.ShouldContain("A Person");
        text.ShouldContain("2026-09-11");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// <summary>
    /// The page is a template, so every line is on it whether or not the front matter had
    /// anything to say. A page that drops the lines it has no value for is a shrinking list,
    /// and the author never learns that a version number was somewhere they could have put
    /// one.
    /// </summary>
    [Fact]
    public async Task A_title_page_carries_every_line_even_with_nothing_to_fill_them()
    {
        using var exported = await ExportedDocument.FromAsync(
            "Just a document.\n",
            new DocxExportSetup { IncludeCoverPage = true });

        string text = exported.PlainText();

        text.ShouldContain("Sub-Title");
        text.ShouldContain("Version");
        text.ShouldContain("Author");

        // The date is the one line that fills itself in.
        text.ShouldContain(DateTime.Now.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture));
        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_line_the_front_matter_answered_carries_the_answer_and_not_the_word()
    {
        using var exported = await ExportedDocument.FromAsync(
            "---\ntitle: Quarterly Report\nsubject: Fourth Quarter\nversion: 2.1\n"
            + "author: A Person\n---\n\nText.\n",
            new DocxExportSetup { IncludeCoverPage = true });

        string text = exported.PlainText();

        text.ShouldContain("Fourth Quarter");
        text.ShouldContain("2.1");
        text.ShouldContain("A Person");

        text.ShouldNotContain("Sub-Title");
        text.ShouldNotContain("Version");
        text.ShouldNotContain("Author");
    }

    /// <summary>
    /// A placeholder has to read as a blank to fill rather than as text somebody wrote, or it
    /// goes out in the document.
    /// </summary>
    [Fact]
    public async Task An_unanswered_line_is_written_in_gray()
    {
        using var exported = await ExportedDocument.FromAsync(
            "Just a document.\n",
            new DocxExportSetup { IncludeCoverPage = true });

        exported.DocumentXml().ShouldContain("8A8A8A");
    }

    /// <summary>
    /// A third of the way down the text column, not the paper - so the block lands in the same
    /// place whatever the margins are set to.
    /// </summary>
    [Fact]
    public async Task The_title_block_starts_a_third_of_the_way_down_the_column()
    {
        using var exported = await ExportedDocument.FromAsync(
            "Text.\n",
            new DocxExportSetup { IncludeCoverPage = true });

        // Letter is 15840 twips tall; an inch of margin top and bottom leaves 12960.
        exported.DocumentXml().ShouldContain("w:before=\"4320\"");
    }

    /// <summary>
    /// The title page, the contents and the body are three sections, not one.
    ///
    /// It was one, and the cover got out of carrying a header by setting titlePg - the flag
    /// that suppresses the header on the first page of a section. That works only while the
    /// cover is the first page of the only section, and it says nothing at all about
    /// numbering. A section of its own lets the cover reference no header and no footer, and
    /// lets the two sections after it count their pages differently.
    /// </summary>
    [Fact]
    public async Task A_title_page_and_a_contents_each_get_a_section_of_their_own()
    {
        using var exported = await ExportedDocument.FromAsync(
            "---\ntitle: A Report\n---\n\n# One\n\nText.\n",
            new DocxExportSetup { IncludeCoverPage = true, IncludeTableOfContents = true });

        string xml = exported.DocumentXml();

        CountOf(xml, "<w:sectPr").ShouldBe(3);

        // The flag the cover used to lean on is gone.
        xml.ShouldNotContain("titlePg");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// The contents number themselves i, ii, iii and the body begins again at 1.
    /// </summary>
    [Fact]
    public async Task The_contents_are_roman_and_the_body_starts_again_at_one()
    {
        using var exported = await ExportedDocument.FromAsync(
            "# One\n\nText.\n",
            new DocxExportSetup { IncludeCoverPage = true, IncludeTableOfContents = true });

        string xml = exported.DocumentXml();

        xml.ShouldContain("w:fmt=\"lowerRoman\"");
        xml.ShouldContain("w:fmt=\"decimal\"");

        // Both restart, which is what makes the first body page 1 rather than 3.
        CountOf(xml, "w:start=\"1\"").ShouldBe(2);
    }

    /// <summary>
    /// Nothing to put in front of the document means nothing to cut it into.
    /// </summary>
    [Fact]
    public async Task Without_a_title_page_or_contents_there_is_one_section()
    {
        using var exported = await ExportedDocument.FromAsync("Text.\n");

        CountOf(exported.DocumentXml(), "<w:sectPr").ShouldBe(1);
    }

    /// <summary>
    /// The contents begin where the numbering begins.
    ///
    /// Fixed at levels one to three, the contents of a document numbering from Heading 2 opened
    /// with its own H1 title - above section 1, and the only line on the page without a number
    /// beside it. Off is treated as Heading 2 as well, because a markdown file that opens with
    /// a single title is the common shape whether or not anything in it is numbered.
    /// </summary>
    [Theory]
    [InlineData(HeadingNumbering.Off, "2-4")]
    [InlineData(HeadingNumbering.FromHeading1, "1-3")]
    [InlineData(HeadingNumbering.FromHeading2, "2-4")]
    [InlineData(HeadingNumbering.FromHeading3, "3-5")]
    public async Task The_contents_list_three_levels_from_wherever_the_numbering_starts(
        HeadingNumbering numbering,
        string range)
    {
        using var exported = await ExportedDocument.FromAsync(
            "# One\n\n## Two\n\n### Three\n",
            new DocxExportSetup { IncludeTableOfContents = true },
            numbering);

        exported.DocumentXml().ShouldContain(@"TOC \o """ + range + @"""");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// The contents heading takes no section number of its own.
    ///
    /// It is based on Heading 1 so that it looks like one, and Heading 1 carries a numbering
    /// instance when the reader has asked to number from there - so without saying otherwise
    /// the word "Contents" arrived as section 1 and pushed the real first section to 2.
    /// </summary>
    [Fact]
    public async Task The_contents_heading_is_not_given_a_section_number()
    {
        using var exported = await ExportedDocument.FromAsync(
            "# One\n\n## Two\n",
            new DocxExportSetup { IncludeTableOfContents = true },
            HeadingNumbering.FromHeading1);

        string style = StyleOf(exported.StylesXml(), "TOCHeading");

        // Instance zero is Word's way of saying none.
        style.ShouldContain("<w:numId w:val=\"0\" />");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    private static string StyleOf(string styles, string styleId)
    {
        int at = styles.IndexOf($"w:styleId=\"{styleId}\"", StringComparison.Ordinal);

        at.ShouldBeGreaterThan(-1, $"the styles part should define {styleId}");

        int end = styles.IndexOf("</w:style>", at, StringComparison.Ordinal);

        return end < 0 ? styles[at..] : styles[at..end];
    }

    private static int CountOf(string haystack, string needle)
    {
        int count = 0;
        int at = 0;

        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }

    [Fact]
    public async Task Front_matter_becomes_what_the_file_says_about_itself()
    {
        using var exported = await ExportedDocument.FromAsync(
            "---\ntitle: Quarterly Report\nauthor: A Person\n"
            + "description: How the quarter went\nkeywords: finance, review\n---\n\nText.\n");

        (string? title, string? creator, string? subject, string? keywords) =
            Properties(exported.Path);

        title.ShouldBe("Quarterly Report");
        creator.ShouldBe("A Person");
        subject.ShouldBe("How the quarter went");
        keywords.ShouldBe("finance, review");
    }

    [Fact]
    public async Task A_document_without_front_matter_still_says_who_wrote_it()
    {
        using var exported = await ExportedDocument.FromAsync("Text.\n");

        (string? title, string? creator, _, _) = Properties(exported.Path);

        title.ShouldBe("Test document");
        creator.ShouldBe("Marqora");
    }

    /// <summary>
    /// The reader is a few lines of key-and-value rather than a YAML parser, so anything
    /// structured is passed over instead of guessed at. A list would otherwise arrive as the
    /// literal text of its brackets.
    /// </summary>
    [Fact]
    public async Task Structured_front_matter_is_passed_over_rather_than_guessed_at()
    {
        using var exported = await ExportedDocument.FromAsync(
            "---\ntitle: Fine\ntags: [a, b]\nnested:\n  key: value\n---\n\nText.\n");

        (string? title, _, _, string? keywords) = Properties(exported.Path);

        title.ShouldBe("Fine");
        keywords.ShouldBeNull();
        exported.PlainText().ShouldNotContain("value");
    }

    [Fact]
    public async Task Everything_at_once_validates()
    {
        using var exported = await ExportedDocument.FromAsync(
            "---\ntitle: The Whole Thing\nauthor: A Person\n---\n\n"
            + "# One\n\nText.[^1]\n\n## Two\n\n- a\n- b\n\n[^1]: A note.\n",
            new DocxExportSetup
            {
                IncludeCoverPage = true,
                IncludeTableOfContents = true,
                IncludeHeaderAndFooter = true,
            },
            HeadingNumbering.FromHeading1);

        exported.ValidationErrors().ShouldBeEmpty();
    }

    private static bool HasHeader(string path)
    {
        using WordprocessingDocument file = WordprocessingDocument.Open(path, false);

        return file.MainDocumentPart!.HeaderParts.Any();
    }

    private static string HeaderText(string path)
    {
        using WordprocessingDocument file = WordprocessingDocument.Open(path, false);

        return file.MainDocumentPart!.HeaderParts.FirstOrDefault()?.Header?.InnerText
            ?? string.Empty;
    }

    private static string FooterText(string path)
    {
        using WordprocessingDocument file = WordprocessingDocument.Open(path, false);

        return file.MainDocumentPart!.FooterParts.FirstOrDefault()?.Footer?.InnerText
            ?? string.Empty;
    }

    private static string FooterXml(string path)
    {
        using WordprocessingDocument file = WordprocessingDocument.Open(path, false);

        return file.MainDocumentPart!.FooterParts.FirstOrDefault()?.Footer?.OuterXml
            ?? string.Empty;
    }

    private static string SettingsXml(string path)
    {
        using WordprocessingDocument file = WordprocessingDocument.Open(path, false);

        return file.MainDocumentPart!.DocumentSettingsPart?.Settings?.OuterXml ?? string.Empty;
    }

    private static (string? Title, string? Creator, string? Subject, string? Keywords) Properties(
        string path)
    {
        using WordprocessingDocument file = WordprocessingDocument.Open(path, false);

        return (
            file.PackageProperties.Title,
            file.PackageProperties.Creator,
            file.PackageProperties.Subject,
            file.PackageProperties.Keywords);
    }
}
