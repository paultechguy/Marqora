// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Docx.Tests;

/// <summary>
/// The paper spec is the one place type and space on paper are stated
/// (docs/Export-Alignment-Plan.md, D1 and §8). Word's styles are written from it, the print
/// stylesheets read it, and fonts are named nowhere else. These tests are what keeps "one
/// place" true after the first change: a size typed into DocxStyles, a font written into the
/// print block, an element the print side forgot - each fails here rather than drifting.
///
/// docs/Paper-Design.md's table is generated from the record too; run normally the test fails
/// when the document has fallen behind, and with MARQORA_WRITE_PAPER_DOCS=1 it rewrites the
/// table, which is what build/Update-PaperDesign.ps1 does.
/// </summary>
public sealed partial class PaperSpecTests
{
    private const string WriteVariable = "MARQORA_WRITE_PAPER_DOCS";

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>The Word style each paper element is written into.</summary>
    private static readonly (string StyleId, PaperElement Element)[] WordStyles =
    [
        ("Heading1", PaperSpec.Heading1),
        ("Heading2", PaperSpec.Heading2),
        ("Heading3", PaperSpec.Heading3),
        ("Heading4", PaperSpec.Heading4),
        ("Heading5", PaperSpec.Heading5),
        ("Heading6", PaperSpec.Heading6),
        ("MarqoraCode", PaperSpec.CodeBlock),
        ("Quote", PaperSpec.Quote),
        ("Caption", PaperSpec.Caption),
        ("MarqoraTable", PaperSpec.Table),
        ("FootnoteText", PaperSpec.Footnote),
        ("Header", PaperSpec.RunningHeader),
        ("Footer", PaperSpec.RunningFooter),
        ("TOC1", PaperSpec.ContentsEntry),
        ("Title", PaperSpec.CoverTitle),
        ("Subtitle", PaperSpec.CoverSubtitle),
    ];

    [Fact]
    public async Task Word_styles_are_written_from_the_paper_spec()
    {
        using var exported = await ExportedDocument.FromAsync("# A\n\nText.\n");

        XElement styles = XElement.Parse(exported.StylesXml());

        // Body text is the document defaults, which every style starts from.
        XElement defaults = styles.Element(W + "docDefaults")!;

        HalfPoints(defaults.Descendants(W + "sz").First()).ShouldBe(PaperSpec.Body.SizePoints);
        Spacing(defaults.Descendants(W + "spacing").First()).ShouldBe(Expected(PaperSpec.Body));

        foreach ((string styleId, PaperElement element) in WordStyles)
        {
            XElement style = styles.Elements(W + "style")
                .SingleOrDefault(s => (string?)s.Attribute(W + "styleId") == styleId)
                ?? throw new ShouldAssertException($"styles.xml defines no {styleId}.");

            XElement? size = style.Element(W + "rPr")?.Element(W + "sz");

            size.ShouldNotBeNull($"{styleId} states no size; the spec gives {element.Name} one.");
            HalfPoints(size).ShouldBe(element.SizePoints, $"{styleId} is not the size of {element.Name}.");

            XElement? spacing = style.Element(W + "pPr")?.Element(W + "spacing");

            spacing.ShouldNotBeNull($"{styleId} states no spacing; the spec gives {element.Name} some.");
            Spacing(spacing).ShouldBe(Expected(element), $"{styleId} is not spaced as {element.Name}.");
        }

        // Inline code takes the spec's size, and is bold only when the spec says so.
        XElement inline = styles.Elements(W + "style").Single(s => (string?)s.Attribute(W + "styleId") == "MarqoraCodeChar");

        HalfPoints(inline.Element(W + "rPr")!.Element(W + "sz")!).ShouldBe(PaperSpec.CodeInline.SizePoints);
        (inline.Element(W + "rPr")!.Element(W + "b") is not null).ShouldBe(PaperSpec.CodeInline.Bold);
    }

    [Fact]
    public async Task Word_faces_are_the_paper_faces()
    {
        using var exported = await ExportedDocument.FromAsync("A `code` span.\n");

        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XElement theme = XElement.Parse(exported.ThemeXml());

        string? Latin(string slot) =>
            (string?)theme.Descendants(a + slot).Single().Element(a + "latin")!.Attribute("typeface");

        Latin("majorFont").ShouldBe(PaperFaces.Display.WordFamily);
        Latin("minorFont").ShouldBe(PaperFaces.Text.WordFamily);
        exported.StylesXml().ShouldContain($"w:ascii=\"{PaperFaces.Code.WordFamily}\"");
    }

    /// <summary>
    /// Every element's size is read by a print rule - in the print block of app.css by its
    /// property, or in the paged print page, for the furniture only it draws, through set() or
    /// band(), which build the property names from the element's. An element the print side
    /// forgot would silently take the screen's size on paper while Word took the spec's.
    /// </summary>
    [Fact]
    public void The_print_side_reads_every_element()
    {
        string block = PrintBlock();
        string page = StripComments(File.ReadAllText(Repository.Webshell("print.js")));

        foreach (PaperElement element in PaperSpec.All)
        {
            bool read = block.Contains(element.CssProperty("size"), StringComparison.Ordinal)
                || page.Contains($"set('{element.Name}')", StringComparison.Ordinal)
                || page.Contains($"band('{element.Name}')", StringComparison.Ordinal);

            read.ShouldBeTrue($"No print rule reads {element.Name}'s size from the paper spec.");
        }
    }

    /// <summary>
    /// Fonts are named in PaperFaces and nowhere else in the export code: not in Word's styles
    /// or theme, not in the print block, not in the paged print page. Comments may name them -
    /// they explain why - so they are taken out before looking.
    /// </summary>
    [Fact]
    public void Fonts_are_named_only_in_PaperFaces()
    {
        string[] names =
        [
            .. PaperFaces.All
                .SelectMany(face => face.CssFamilies.Split(',').Append(face.WordFamily ?? string.Empty))
                .Select(name => name.Trim().Trim('"'))
                .Where(name => name.Length > 0 && name is not ("serif" or "sans-serif" or "monospace" or "system-ui"))
                .Append("Aptos")
                .Distinct(StringComparer.Ordinal),
        ];

        var places = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DocxStyles.cs"] = StripComments(File.ReadAllText(Repository.Source("PaulTechGuy.MQ.Docx", "DocxStyles.cs"))),
            ["DocxTheme.cs"] = StripComments(File.ReadAllText(Repository.Source("PaulTechGuy.MQ.Docx", "DocxTheme.cs"))),
            ["the print block of app.css"] = PrintBlock(),
            ["print.js"] = StripComments(File.ReadAllText(Repository.Webshell("print.js"))),
        };

        foreach ((string place, string text) in places)
        {
            foreach (string name in names)
            {
                text.ShouldNotContain(name, customMessage: $"{place} names the font \"{name}\"; name it in PaperFaces and read it from there.");
            }
        }
    }

    [Fact]
    public void Every_size_is_one_Word_can_store_and_every_element_is_declared()
    {
        string css = PaperSpec.Css();

        foreach (PaperElement element in PaperSpec.All)
        {
            (element.SizePoints * 2 % 1).ShouldBe(0, $"{element.Name}: Word stores sizes in half-points.");
            element.LineHeight.ShouldBeGreaterThan(0);

            foreach (string value in new[] { "family", "size", "weight", "style", "transform", "line", "before", "after" })
            {
                css.ShouldContain(element.CssProperty(value) + ":");
            }
        }

        foreach (PaperFace face in PaperFaces.All)
        {
            css.ShouldContain(PaperFaces.CssProperty(face.Role) + ": " + face.CssFamilies + ";");
        }
    }

    [Fact]
    public void The_paper_design_table_matches_the_spec()
    {
        string path = Path.Combine(Repository.Root(), "docs", "Paper-Design.md");
        string text = File.ReadAllText(path).ReplaceLineEndings("\n");
        const string Start = "<!-- paper-table:start -->\n";
        const string End = "<!-- paper-table:end -->";

        int from = text.IndexOf(Start, StringComparison.Ordinal);
        int to = text.IndexOf(End, StringComparison.Ordinal);

        Assert.True(from >= 0 && to > from, "docs/Paper-Design.md has no paper-table block.");

        string current = text[(from + Start.Length)..to];
        string expected = Table();

        if (current == expected)
        {
            return;
        }

        if (Environment.GetEnvironmentVariable(WriteVariable) == "1")
        {
            File.WriteAllText(path, string.Concat(text[..(from + Start.Length)], expected, text[to..]), new UTF8Encoding(false));
            return;
        }

        current.ShouldBe(expected, "docs/Paper-Design.md is out of date. Run: pwsh ./build/Update-PaperDesign.ps1");
    }

    private static string Table()
    {
        var table = new StringBuilder();

        table.Append("| Role | Word | CSS | CSS weight |\n|---|---|---|---|\n");

        foreach (PaperFace face in PaperFaces.All)
        {
            table.Append(CultureInfo.InvariantCulture, $"| {face.Role} | {face.WordFamily ?? "—"} | `{face.CssFamilies}` | {face.CssWeight} |\n");
        }

        table.Append("\n| Element | What it is | Face | Size | Line | Before | After | Set |\n|---|---|---|---|---|---|---|---|\n");

        foreach (PaperElement e in PaperSpec.All)
        {
            string set = string.Join(", ", new[]
            {
                e.Bold ? "bold" : null,
                e.Italic ? "italic" : null,
                e.Uppercase ? "capitals" : null,
            }.Where(s => s is not null));

            table.Append(CultureInfo.InvariantCulture, $"| `{e.Name}` | {e.Description} | {e.Face} | {Points(e.SizePoints)} | {e.LineHeight:0.###} | {Points(e.BeforePoints)} | {Points(e.AfterPoints)} | {(set.Length == 0 ? "—" : set)} |\n");
        }

        return table.ToString();
    }

    private static string Points(double value) => value.ToString("0.###", CultureInfo.InvariantCulture) + " pt";

    /// <summary>The @media print block of app.css, comments taken out.</summary>
    private static string PrintBlock()
    {
        string css = File.ReadAllText(Repository.Webshell("app.css"));
        int start = css.IndexOf("@media print {", StringComparison.Ordinal);

        start.ShouldBeGreaterThanOrEqualTo(0, "app.css has no print block.");

        int depth = 0;
        int end = start;

        for (int i = css.IndexOf('{', start); i < css.Length; i++)
        {
            depth += css[i] switch { '{' => 1, '}' => -1, _ => 0 };

            if (depth == 0)
            {
                end = i;
                break;
            }
        }

        return StripComments(css[start..end]);
    }

    private static string StripComments(string text) => LineComment().Replace(BlockComment().Replace(text, string.Empty), string.Empty);

    private static double HalfPoints(XElement size) =>
        int.Parse((string)size.Attribute(W + "val")!, CultureInfo.InvariantCulture) / 2.0;

    private static (int Before, int After, int Line, string? Rule) Spacing(XElement spacing) =>
    (
        int.Parse((string?)spacing.Attribute(W + "before") ?? "0", CultureInfo.InvariantCulture),
        int.Parse((string?)spacing.Attribute(W + "after") ?? "0", CultureInfo.InvariantCulture),
        int.Parse((string?)spacing.Attribute(W + "line") ?? "0", CultureInfo.InvariantCulture),
        (string?)spacing.Attribute(W + "lineRule")
    );

    // The line as a distance - the size times the spec's multiple, in twips - at least that
    // tall. A multiple of 240 with lineRule auto is Word's "multiple" spacing, which scales
    // the face's natural line height rather than its size and set body text a third taller
    // than the PDF.
    private static (int Before, int After, int Line, string? Rule) Expected(PaperElement element) =>
    (
        (int)Math.Round(element.BeforePoints * 20),
        (int)Math.Round(element.AfterPoints * 20),
        (int)Math.Round(element.SizePoints * element.LineHeight * 20),
        "atLeast"
    );

    [GeneratedRegex(@"/\*[\s\S]*?\*/")]
    private static partial Regex BlockComment();

    [GeneratedRegex(@"(?m)^\s*//.*$")]
    private static partial Regex LineComment();
}
