// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

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
    /// level-one heading carries none at all - which is the whole point of the preference, and
    /// is expressed by which style each level names.
    /// </summary>
    [Fact]
    public async Task Numbering_from_a_deeper_level_leaves_the_ones_above_it_alone()
    {
        using var exported = await ExportedDocument.FromAsync(
            "# Part\n\n## First\n\n### Detail\n",
            headingNumbering: HeadingNumbering.FromHeading2);

        string numbering = exported.NumberingXml();

        numbering.ShouldContain("w:val=\"Heading2\"");
        numbering.ShouldContain("w:val=\"Heading3\"");
        numbering.ShouldNotContain("w:val=\"Heading1\"");

        exported.ValidationErrors().ShouldBeEmpty();
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
