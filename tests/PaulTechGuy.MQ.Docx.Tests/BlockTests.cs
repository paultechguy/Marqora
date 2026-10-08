// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Docx.Tests;

/// <summary>
/// What each kind of markdown block becomes in the document.
///
/// The three tests about inheritance are the ones worth keeping. Markdig models a GitHub
/// callout as a kind of block quote, a display equation as a kind of fenced code block, and
/// front matter as a kind of code block - so a walker that asks about the base type first
/// quietly gets all three wrong, and the document it produces opens without complaint and
/// reads almost right. Only the front-matter case fails to compile; the other two need a test
/// to notice.
/// </summary>
public class BlockTests
{
    [Fact]
    public async Task Heading_levels_take_Words_own_heading_styles()
    {
        using var exported = await ExportedDocument.FromAsync(
            "# One\n\n## Two\n\n### Three\n\n#### Four\n\n##### Five\n\n###### Six\n");

        string xml = exported.DocumentXml();

        for (int level = 1; level <= 6; level++)
        {
            xml.ShouldContain($"w:val=\"Heading{level}\"");
        }
    }

    [Fact]
    public async Task A_heading_carries_a_bookmark_so_links_and_the_navigation_pane_can_find_it()
    {
        using var exported = await ExportedDocument.FromAsync("## Why it exists\n");

        string xml = exported.DocumentXml();

        xml.ShouldContain("bookmarkStart");
        xml.ShouldContain("bookmarkEnd");

        // The slug is "why-it-exists", which Word will not accept as a bookmark name: hyphens
        // are illegal and a name must start with a letter or underscore.
        xml.ShouldNotContain("w:name=\"why-it-exists\"");
    }

    [Fact]
    public async Task Front_matter_becomes_properties_rather_than_the_first_thing_on_the_page()
    {
        using var exported = await ExportedDocument.FromAsync(
            "---\ntitle: Quarterly report\ntags: [finance]\n---\n\nThe body.\n");

        string xml = exported.DocumentXml();

        xml.ShouldContain("The body.");
        xml.ShouldNotContain("Quarterly report");
        xml.ShouldNotContain("finance");
    }

    [Fact]
    public async Task Display_math_is_not_written_as_a_code_block()
    {
        using var exported = await ExportedDocument.FromAsync("$$\nE = mc^2\n$$\n");

        string xml = exported.DocumentXml();

        xml.ShouldContain("E = mc^2");

        // MathBlock derives from FencedCodeBlock. If the dispatch asked about the base type
        // first, this equation would be sitting in a shaded code paragraph.
        xml.ShouldNotContain("w:val=\"MarqoraCode\"");
    }

    [Fact]
    public async Task A_fenced_block_is_one_paragraph_per_line_so_the_border_collapses_into_a_box()
    {
        using var exported = await ExportedDocument.FromAsync(
            "```csharp\nvar a = 1;\nvar b = 2;\nvar c = 3;\n```\n");

        string xml = exported.DocumentXml();

        CountOf(xml, "w:val=\"MarqoraCode\"").ShouldBe(3);
    }

    [Fact]
    public async Task An_indented_code_block_is_code_too()
    {
        using var exported = await ExportedDocument.FromAsync("Text.\n\n    indented();\n");

        exported.DocumentXml().ShouldContain("w:val=\"MarqoraCode\"");
    }

    [Fact]
    public async Task A_block_quote_takes_the_Quote_style()
    {
        using var exported = await ExportedDocument.FromAsync("> Something said.\n");

        exported.DocumentXml().ShouldContain("w:val=\"Quote\"");
    }

    /// <summary>
    /// Two quotes in a row are two panels. Word draws consecutive paragraphs with the same
    /// borders as one box, so the fixture's three quotes in a row read as a single quote; a
    /// hairline paragraph between them ends each box. One between each pair, none inside a
    /// quote, and none before a quote that follows a callout, whose title already separates it.
    /// </summary>
    [Fact]
    public async Task Quotes_in_a_row_stay_separate_panels()
    {
        using var exported = await ExportedDocument.FromAsync(
            "> One.\n\n> Two.\n>\n> Still two.\n\n> Three.\n\n> [!NOTE]\n> A note.\n\n> Four.\n");

        string xml = exported.DocumentXml();

        Regex.Count(xml, "<w:spacing w:before=\"0\" w:after=\"0\" w:line=\"20\" w:lineRule=\"exact\" />")
            .ShouldBe(2);
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// Each level of a nested quote stands in one more step, as the preview indents it. The
    /// style's bar is drawn once whatever the depth, so without the step three levels read as
    /// one.
    /// </summary>
    [Fact]
    public async Task A_nested_quote_stands_in_one_step_per_level()
    {
        using var exported = await ExportedDocument.FromAsync(
            "> Level one.\n>\n> > Level two.\n> >\n> > > Level three.\n");

        string xml = exported.DocumentXml();

        xml.ShouldContain("<w:ind w:left=\"864\" w:right=\"432\" />");
        xml.ShouldContain("<w:ind w:left=\"1296\" w:right=\"432\" />");
        Regex.Count(xml, "w:val=\"Quote\"").ShouldBe(3);
        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_thematic_break_is_a_bordered_paragraph_rather_than_nothing()
    {
        using var exported = await ExportedDocument.FromAsync("Above.\n\n---\n\nBelow.\n");

        string xml = exported.DocumentXml();

        xml.ShouldContain("pBdr");
        xml.ShouldContain("Above.");
        xml.ShouldContain("Below.");
    }

    /// <summary>
    /// Section numbers are Word's, not ours.
    ///
    /// Writing them as text - which an earlier version did, because that is what the preview
    /// does - produces numbers that are right when the file is written and wrong from the first
    /// edit: insert a section in Word two days later and everything after it still reads as it
    /// did before. Linking the numbering to the heading styles is what makes Word maintain it,
    /// and it is the same thing Word's own "Multilevel List, link to Heading styles" builds.
    /// </summary>
    [Fact]
    public async Task Heading_numbers_are_Words_own_so_an_edit_renumbers_the_document()
    {
        using var exported = await ExportedDocument.FromAsync(
            "# First\n\n## Under first\n\n# Second\n",
            headingNumbering: HeadingNumbering.FromHeading1);

        string numbering = exported.NumberingXml();

        // Each level names the heading style it belongs to. That link is the whole mechanism.
        numbering.ShouldContain("w:val=\"Heading1\"");
        numbering.ShouldContain("w:val=\"Heading2\"");
        numbering.ShouldContain("%1.%2");

        // And the styles point back at the numbering instance.
        exported.StylesXml().ShouldContain("numId");

        // A space after the number, and no indent. Word's default for a numbered paragraph is
        // to hang the text off a tab stop, which is right for a list and wrong for a heading:
        // it would step every heading in and leave the deeper ones adrift from the margin.
        numbering.ShouldContain("w:val=\"space\"");
        numbering.ShouldContain("w:left=\"0\"");

        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task Nothing_is_numbered_when_the_reader_has_not_asked_for_it()
    {
        using var exported = await ExportedDocument.FromAsync("# First\n\n## Second\n");

        exported.NumberingXml().ShouldNotContain("Heading1");
        exported.StylesXml().ShouldNotContain("numPr");
    }

    /// <summary>
    /// Numbering from heading two means a level-two heading is the outermost number and a
    /// level-one heading shows none at all - which is the whole point of the preference.
    ///
    /// But the level-one heading is still in the list, unnumbered, because it begins a new
    /// section and the count beneath it starts again from one: the documented rule on
    /// HeadingNumbering, and what the preview and the PDF do. This test used to assert that
    /// Heading 1 was absent from the list, which is exactly what let Word number a
    /// three-chapter document's sections straight through.
    /// </summary>
    [Fact]
    public async Task Numbering_from_a_deeper_level_hides_the_levels_above_it_but_restarts_on_them()
    {
        using var exported = await ExportedDocument.FromAsync(
            "# Part\n\n## First\n\n### Detail\n",
            headingNumbering: HeadingNumbering.FromHeading2);

        string numbering = exported.NumberingXml();

        // Heading 1 is level zero, prints nothing, and leaves no space behind.
        LevelOf(numbering, "Heading1").ShouldContain("<w:numFmt w:val=\"none\" />");
        LevelOf(numbering, "Heading1").ShouldContain("<w:lvlText w:val=\"\" />");
        LevelOf(numbering, "Heading1").ShouldContain("<w:suff w:val=\"nothing\" />");

        // The numbered levels name themselves and the levels between, never the hidden one.
        LevelOf(numbering, "Heading2").ShouldContain("<w:lvlText w:val=\"%2\" />");
        LevelOf(numbering, "Heading3").ShouldContain("<w:lvlText w:val=\"%2.%3\" />");

        // And every heading style is linked, Heading 1 included, at its own level.
        string styles = exported.StylesXml();

        styles.ShouldContain("<w:ilvl w:val=\"0\" />");
        styles.ShouldContain("<w:ilvl w:val=\"1\" />");

        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// A document that skips a level reads differently in Word, and this pins how. The preview
    /// drops a missing level's zero (HeadingNumbers), so "# A" then "### B" numbers B as "1";
    /// Word's pattern always names the level between, so B is "0.1". Documented in
    /// docs/Export-Alignment-Plan.md as a difference kept on purpose.
    /// </summary>
    [Fact]
    public async Task A_skipped_level_keeps_its_place_in_the_word_pattern()
    {
        using var exported = await ExportedDocument.FromAsync(
            "# A\n\n### B\n",
            headingNumbering: HeadingNumbering.FromHeading2);

        LevelOf(exported.NumberingXml(), "Heading3").ShouldContain("<w:lvlText w:val=\"%2.%3\" />");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>The w:lvl that names a heading style, as XML.</summary>
    private static string LevelOf(string numbering, string headingStyle)
    {
        int style = numbering.IndexOf($"<w:pStyle w:val=\"{headingStyle}\" />", StringComparison.Ordinal);

        style.ShouldBeGreaterThanOrEqualTo(0, $"{headingStyle} is not in the heading list");

        int start = numbering.LastIndexOf("<w:lvl ", style, StringComparison.Ordinal);
        int end = numbering.IndexOf("</w:lvl>", style, StringComparison.Ordinal);

        return numbering[start..end];
    }

    /// <summary>
    /// A heading keeps its style wherever it sits, so Word counts it wherever it sits. The
    /// numbering is attached to the style rather than applied by the walker, which is what
    /// makes that true without the walker having to think about it.
    /// </summary>
    [Fact]
    public async Task A_heading_inside_a_quote_is_numbered_like_any_other()
    {
        using var exported = await ExportedDocument.FromAsync(
            "# First\n\n> ## Quoted heading\n\n# Second\n",
            headingNumbering: HeadingNumbering.FromHeading1);

        // The quoted heading is a Heading 2, so it takes the second level of the numbering.
        exported.DocumentXml().ShouldContain("w:val=\"Heading2\"");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task Documents_from_the_shipped_corpus_validate()
    {
        // Every construct Marqora supports, in the two documents that ship with it.
        string cheatsheet = await File.ReadAllTextAsync(
            Path.Combine(RepoRoot(), "webshell", "cheatsheet.md"),
            TestContext.Current.CancellationToken);

        using var exported = await ExportedDocument.FromAsync(cheatsheet);

        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task The_welcome_document_validates()
    {
        string welcome = await File.ReadAllTextAsync(
            Path.Combine(RepoRoot(), "src", "PaulTechGuy.MQ.App", "Assets", "Welcome to Marqora.md"),
            TestContext.Current.CancellationToken);

        using var exported = await ExportedDocument.FromAsync(welcome);

        exported.ValidationErrors().ShouldBeEmpty();
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

    /// <summary>
    /// The repository root, found by walking up from the test binary until the solution file
    /// turns up. Tests run from bin/Debug/net10.0, and the depth from there is not something
    /// worth hard-coding.
    /// </summary>
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PaulTechGuy.MQ.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root could not be found.");
    }
}
