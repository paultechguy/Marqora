// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Docx.Tests;

/// <summary>
/// Every display equation in the app's own torture document, against the MathML a browser
/// really produced for it.
///
/// The rest of <c>MathTests</c> is written by hand, which is right for asserting one shape at
/// a time and wrong as a safety net: hand-written MathML is what the author expected KaTeX to
/// emit. The gap between that and what KaTeX emits is where a whole feature can sit broken -
/// every matrix, aligned system and set of cases in the app converted to nothing but their
/// TeX source for the life of the exporter, with a green test suite, because no fixture ever
/// carried a real <c>mtable</c>.
///
/// So the fixture is generated rather than typed. <c>build/Update-MathCorpus.js</c> renders
/// the equations with <c>webshell/vendor/katex</c> - the same copy the preview loads - and
/// writes the markdown and the preview HTML side by side. Re-run it after changing that
/// document's math or taking a new KaTeX.
///
/// One assertion matters more than the count: <see cref="ExportedDocument.Skipped"/> empty
/// means not one equation fell back. A fallback is legible in the document and easy to live
/// with, which is exactly why it needs a test to notice.
/// </summary>
public class MathCorpusTests
{
    /// <summary>
    /// Every <c>$$</c> block in <c>docs/UltimateMarkdownContent.md</c>. A number rather than a
    /// range, so adding equations to that document without re-running the generator fails
    /// here rather than silently testing the old set.
    /// </summary>
    private const int Equations = 16;

    [Fact]
    public async Task Every_display_equation_in_the_fixture_document_becomes_a_Word_equation()
    {
        using ExportedDocument exported = await FromCorpusAsync();

        string xml = exported.DocumentXml();

        CountOf(xml, "<m:oMath>").ShouldBe(Equations);

        // Named rather than counted: a failure here should say which construct stopped it.
        string.Join(" | ", exported.Skipped).ShouldBeEmpty();
    }

    /// <summary>
    /// The one that would have caught the matrix defect on its own. OMML is a schema sequence,
    /// and Word answers a wrong one by offering to repair the file rather than by saying what
    /// is wrong - so the first matrix ever written was also the first chance to find out that
    /// its column properties had never been in the element the schema wanted them in.
    /// </summary>
    [Fact]
    public async Task The_whole_corpus_is_schema_valid()
    {
        using ExportedDocument exported = await FromCorpusAsync();

        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// The constructs worth naming, so a regression says which one went rather than only that
    /// the count fell: a matrix, an n-ary with its integrand, a fraction and a radical.
    /// </summary>
    [Fact]
    public async Task The_corpus_carries_the_shapes_the_document_is_there_to_test()
    {
        using ExportedDocument exported = await FromCorpusAsync();

        string xml = exported.DocumentXml();

        xml.ShouldContain("<m:m>");      // matrices, aligned systems, cases
        xml.ShouldContain("<m:nary>");   // integrals and sums
        xml.ShouldContain("<m:f>");      // fractions
        xml.ShouldContain("<m:rad>");    // roots
    }

    /// <summary>
    /// Brackets round a matrix grow with it. KaTeX writes a pmatrix, bmatrix, vmatrix or cases
    /// as a \left ... \right group inside the equation's row; flattened into that row, the
    /// group's brackets were written as one-line characters beside a three-line matrix, and
    /// the cases brace came out short. Each is now a delimiter round the matrix - cases with
    /// no closing character, as \right. asks.
    /// </summary>
    [Fact]
    public async Task Brackets_round_a_matrix_stretch_with_it()
    {
        using ExportedDocument exported = await FromCorpusAsync();

        string xml = exported.DocumentXml();

        xml.ShouldContain("<m:begChr m:val=\"(\" /><m:endChr m:val=\")\" />");
        xml.ShouldContain("<m:begChr m:val=\"[\" /><m:endChr m:val=\"]\" />");
        xml.ShouldContain("<m:begChr m:val=\"∣\" /><m:endChr m:val=\"∣\" />");
        xml.ShouldContain("<m:begChr m:val=\"{\" /><m:endChr m:val=\"\" />");
    }

    /// <summary>
    /// \quad and \qquad between equations on one line are gaps the author put there, and
    /// come across as em spaces; dropped with TeX's thin spaces, they ran the fixture's limit,
    /// product, union and contour integral together into one expression.
    /// </summary>
    [Fact]
    public async Task Wide_spaces_between_equations_are_kept()
    {
        using ExportedDocument exported = await FromCorpusAsync();

        string xml = exported.DocumentXml();

        xml.ShouldContain("<m:t xml:space=\"preserve\">\u2003\u2003</m:t>");
        xml.ShouldContain("<m:t xml:space=\"preserve\">\u2003</m:t>");
    }

    /// <summary>
    /// \textcolor and \colorbox keep their colors: the ink on each run, and the box's fill
    /// as shading. Named colors take CSS's values, which are what the preview drew.
    /// </summary>
    [Fact]
    public async Task Color_and_colorbox_in_an_equation_keep_their_colors()
    {
        using ExportedDocument exported = await FromCorpusAsync();

        string xml = exported.DocumentXml();

        xml.ShouldContain("<w:color w:val=\"FF0000\" />");
        xml.ShouldContain("<w:color w:val=\"0000FF\" />");
        xml.ShouldContain("<w:color w:val=\"008000\" />");
        xml.ShouldContain("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"FFFF00\" />");
    }

    private static async Task<ExportedDocument> FromCorpusAsync()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "Fixtures");

        return await ExportedDocument.FromAsync(
            await File.ReadAllTextAsync(
                Path.Combine(folder, "MathCorpus.md"),
                TestContext.Current.CancellationToken),
            renderedPreviewHtml: await File.ReadAllTextAsync(
                Path.Combine(folder, "MathCorpus.preview.html"),
                TestContext.Current.CancellationToken));
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
}
