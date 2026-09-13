// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Docx.Tests;

/// <summary>
/// Equations, and the difference between one Word understands and a picture of one.
///
/// The MathML here is the shape KaTeX emits: wrapped in semantics, with an annotation holding
/// the TeX it came from. Both of those have to be stepped past, and the annotation has to be
/// kept rather than dropped - it is what a reader sees when an expression uses something the
/// converter does not know.
/// </summary>
public class MathTests
{
    [Fact]
    public async Task Display_math_becomes_a_real_Word_equation()
    {
        using var exported = await ExportedDocument.FromAsync(
            "$$\nE = mc^2\n$$\n",
            renderedPreviewHtml: Display(
                "<mrow><mi>E</mi><mo>=</mo><mi>m</mi>"
                + "<msup><mi>c</mi><mn>2</mn></msup></mrow>",
                "E = mc^2"));

        string xml = exported.DocumentXml();

        xml.ShouldContain("oMathPara");
        xml.ShouldContain("oMath");
        xml.ShouldContain("sSup");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task Inline_math_sits_inside_the_sentence_it_belongs_to()
    {
        using var exported = await ExportedDocument.FromAsync(
            "The value $x^2$ matters.\n",
            renderedPreviewHtml: Paragraph(
                0,
                "The value " + Mathml("<msup><mi>x</mi><mn>2</mn></msup>", "x^2") + " matters."));

        string xml = exported.DocumentXml();

        xml.ShouldContain("sSup");

        // Inline, so no equation paragraph wrapping it.
        xml.ShouldNotContain("oMathPara");
        exported.PlainText().ShouldContain("The value ");
    }

    /// <summary>
    /// Several equations can share a paragraph, and all carry its line. They are told apart by
    /// the order they appear in - which means the ordinal has to restart with each block, or
    /// the second paragraph's equations would be looked up past the end of the first's.
    /// </summary>
    [Fact]
    public async Task Two_equations_in_one_sentence_are_both_found()
    {
        using var exported = await ExportedDocument.FromAsync(
            "Both $a$ and $b$ here.\n",
            renderedPreviewHtml: Paragraph(
                0,
                "Both " + Mathml("<mi>a</mi>", "a")
                + " and " + Mathml("<mi>b</mi>", "b") + " here."));

        string xml = exported.DocumentXml();

        xml.ShouldContain("<m:t>a</m:t>");
        xml.ShouldContain("<m:t>b</m:t>");
    }

    [Fact]
    public async Task A_fraction_becomes_a_fraction()
    {
        using var exported = await ExportedDocument.FromAsync(
            "$$\n\\frac{a}{b}\n$$\n",
            renderedPreviewHtml: Display(
                "<mfrac><mi>a</mi><mi>b</mi></mfrac>",
                "\\frac{a}{b}"));

        string xml = exported.DocumentXml();

        xml.ShouldContain("<m:f>");
        xml.ShouldContain("<m:num>");
        xml.ShouldContain("<m:den>");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_square_root_hides_its_degree_rather_than_omitting_it()
    {
        using var exported = await ExportedDocument.FromAsync(
            "$$\n\\sqrt{x}\n$$\n",
            renderedPreviewHtml: Display("<msqrt><mi>x</mi></msqrt>", "\\sqrt{x}"));

        string xml = exported.DocumentXml();

        xml.ShouldContain("<m:rad>");
        xml.ShouldContain("degHide");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// An integral with bounds is a single Word construct rather than a symbol with scripts
    /// beside it. That is what puts the bounds where a reader expects and lets them grow with
    /// the expression - the corpus has exactly this equation in it.
    /// </summary>
    [Fact]
    public async Task An_integral_with_limits_is_written_as_one()
    {
        using var exported = await ExportedDocument.FromAsync(
            "$$\n\\int_{0}^{\\infty} e^{-x^2}\\,dx\n$$\n",
            renderedPreviewHtml: Display(
                "<msubsup><mo>∫</mo><mn>0</mn><mi>∞</mi></msubsup><mi>d</mi><mi>x</mi>",
                "\\int_{0}^{\\infty} dx"));

        string xml = exported.DocumentXml();

        xml.ShouldContain("<m:nary>");
        xml.ShouldContain("∫");
        exported.ValidationErrors().ShouldBeEmpty();
    }


    /// <summary>
    /// The integral's expression goes inside it, and what comes after the equals sign does not.
    ///
    /// MathML does not group an integrand with its integral: KaTeX writes this equation as a
    /// sub-superscripted operator followed by six unrelated siblings. Built straight across,
    /// the n-ary got an empty base - and Word draws an empty base as a small dotted rectangle,
    /// which appeared between the integral sign and its own integrand where the PDF had
    /// nothing at all.
    ///
    /// The differential is what says where the expression stops, so the fraction on the far
    /// side of the equals sign stays outside the integral rather than being swallowed by it.
    /// </summary>
    [Fact]
    public async Task An_integral_takes_its_expression_inside_and_leaves_the_rest_out()
    {
        const string tex = @"\int_{0}^{\infty} e^{-x^2}\,dx = \frac{\sqrt{\pi}}{2}";

        using var exported = await ExportedDocument.FromAsync(
            "$$\n" + tex + "\n$$\n",
            renderedPreviewHtml: Display(
                "<msubsup><mo>∫</mo><mn>0</mn><mi>∞</mi></msubsup>"
                + "<msup><mi>e</mi><mrow><mo>−</mo><msup><mi>x</mi><mn>2</mn></msup></mrow></msup>"
                + "<mspace width=\"0.1667em\"/><mi>d</mi><mi>x</mi><mo>=</mo>"
                + "<mfrac><msqrt><mi>π</mi></msqrt><mn>2</mn></mfrac>",
                tex));

        string xml = exported.DocumentXml();

        xml.ShouldContain("<m:nary>");

        // Nothing empty anywhere: an empty argument is the dotted box.
        xml.ShouldNotContain("<m:e />");

        // And the fraction is beyond the integral rather than inside it.
        xml.IndexOf("<m:f>", StringComparison.Ordinal)
            .ShouldBeGreaterThan(xml.IndexOf("</m:nary>", StringComparison.Ordinal));

        exported.ValidationErrors().ShouldBeEmpty();
    }
    /// <summary>
    /// KaTeX writes a bracket pair as two stretchy operators rather than as a fence, so an
    /// unrecognized pair stays one line tall next to a fraction three lines high.
    /// </summary>
    [Fact]
    public async Task A_bracket_pair_becomes_a_delimiter_that_can_grow()
    {
        using var exported = await ExportedDocument.FromAsync(
            "$$\n\\left(\\frac{a}{b}\\right)\n$$\n",
            renderedPreviewHtml: Display(
                "<mrow><mo stretchy=\"true\">(</mo>"
                + "<mfrac><mi>a</mi><mi>b</mi></mfrac>"
                + "<mo stretchy=\"true\">)</mo></mrow>",
                "\\left(\\frac{a}{b}\\right)"));

        string xml = exported.DocumentXml();

        xml.ShouldContain("<m:d>");
        xml.ShouldContain("begChr");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// A non-stretchy operator is the author asking for a bracket that does not grow, and
    /// folding it into a delimiter would override them.
    /// </summary>
    [Fact]
    public async Task A_bracket_marked_not_to_stretch_is_left_alone()
    {
        using var exported = await ExportedDocument.FromAsync(
            "$$\n\\lparen x \\rparen\n$$\n",
            renderedPreviewHtml: Display(
                "<mrow><mo stretchy=\"false\">(</mo><mi>x</mi>"
                + "<mo stretchy=\"false\">)</mo></mrow>",
                "\\lparen x \\rparen"));

        exported.DocumentXml().ShouldNotContain("<m:d>");
    }

    /// <summary>
    /// A single letter is a variable and is italic; several letters are a function name and
    /// are upright. KaTeX does not always mark the second case, so the length decides.
    /// </summary>
    [Fact]
    public async Task A_function_name_is_upright_and_a_variable_is_not()
    {
        using var exported = await ExportedDocument.FromAsync(
            "$$\n\\sin x\n$$\n",
            renderedPreviewHtml: Display("<mi>sin</mi><mi>x</mi>", "\\sin x"));

        string xml = exported.DocumentXml();

        // "sin" is styled plain; "x" carries no style and takes OMML's italic default.
        xml.ShouldContain("<m:sty m:val=\"p\" />");
        xml.ShouldContain("<m:t>x</m:t>");
    }

    [Fact]
    public async Task Text_inside_an_equation_is_not_set_in_the_math_face()
    {
        using var exported = await ExportedDocument.FromAsync(
            "$$\n\\text{where } x > 0\n$$\n",
            renderedPreviewHtml: Display(
                "<mtext>where </mtext><mi>x</mi>",
                "\\text{where } x > 0"));

        exported.DocumentXml().ShouldContain("<m:nor />");
    }

    /// <summary>
    /// An expression the converter does not know sends the whole equation to the fallback
    /// rather than writing a document with a gap in the middle of it. The TeX is what KaTeX
    /// kept in the annotation, which is why that is worth reading rather than discarding.
    /// </summary>
    [Fact]
    public async Task An_unmappable_expression_falls_back_to_the_source_it_came_from()
    {
        using var exported = await ExportedDocument.FromAsync(
            "$$\n\\bizarre{x}\n$$\n",
            renderedPreviewHtml: Display(
                "<munknownthing><mi>x</mi></munknownthing>",
                "\\bizarre{x}"));

        string xml = exported.DocumentXml();

        xml.ShouldNotContain("oMath");
        exported.PlainText().ShouldContain("\\bizarre{x}");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// A miss has to name itself, or the gap is invisible. Nobody can enumerate everything TeX
    /// can express, so the only way this converter gets better is by finding out which
    /// constructs real documents actually use - and that needs each fallback to say what
    /// stopped it rather than quietly becoming a line of source.
    /// </summary>
    [Fact]
    public async Task An_unmappable_expression_names_what_stopped_it()
    {
        using var exported = await ExportedDocument.FromAsync(
            "$$\n\\bizarre{x}\n$$\n",
            renderedPreviewHtml: Display(
                "<munknownthing><mi>x</mi></munknownthing>",
                "\\bizarre{x}"));

        exported.Skipped.Count.ShouldBe(1);
        exported.Skipped[0].ShouldContain("munknownthing");
    }

    /// <summary>
    /// Two equations with the same gap are two things to fix, in two places.
    ///
    /// This used to collapse to a single line, on the reasoning that a document with twenty
    /// equations using one unmapped construct has one thing wrong with it. That was right
    /// while the report was a sentence in a dialog, and wrong the moment it carried a line
    /// number: the report is now a list to work through, and nineteen of those twenty would
    /// never be visited. What is still collapsed is the same problem met twice at one line.
    /// </summary>
    [Fact]
    public async Task The_same_gap_in_two_equations_is_reported_against_each_of_them()
    {
        using var exported = await ExportedDocument.FromAsync(
            "$$\n\\a\n$$\n\nText.\n\n$$\n\\b\n$$\n",
            renderedPreviewHtml:
                "<div class=\"math\" data-src-line=\"0\">"
                + Mathml("<munknownthing><mi>x</mi></munknownthing>", "\\a") + "</div>"
                + "<p data-src-line=\"4\">Text.</p>"
                + "<div class=\"math\" data-src-line=\"6\">"
                + Mathml("<munknownthing><mi>y</mi></munknownthing>", "\\b") + "</div>");

        exported.Skipped.Count.ShouldBe(2);

        // Counted from one, and in document order: the first block opens the file, the second
        // is six lines further down.
        exported.Issues[0].Line.ShouldBe(1);
        exported.Issues[1].Line.ShouldBe(7);

        // Each names its own equation rather than the element alone, so the two rows are
        // telling the reader about two different places.
        exported.Issues[0].Item.ShouldBe("\\a");
        exported.Issues[1].Item.ShouldBe("\\b");

        exported.PlainText().ShouldContain("\\a");
        exported.PlainText().ShouldContain("\\b");
    }

    /// <summary>
    /// A matrix, and the test that should have existed from the start.
    ///
    /// <c>mtable</c> was handled and read its own rows and cells, and not one of them ever
    /// reached a document: each cell was handed to the helper that converts an element rather
    /// than the one that opens it, so the converter was asked to turn an <c>mtd</c> into OMML,
    /// nothing does, and the whole equation declined to its TeX source. Every matrix, every
    /// aligned system and every set of cases, silently, for as long as the feature existed -
    /// because no test covered a table.
    ///
    /// The cells carry the <c>mstyle</c> KaTeX really wraps them in, so this fails if that
    /// wrapper stops being stepped past as well.
    /// </summary>
    [Fact]
    public async Task A_matrix_becomes_a_matrix_rather_than_its_source()
    {
        using var exported = await ExportedDocument.FromAsync(
            "$$\n\\begin{bmatrix} 1 & 0 \\\\ 0 & 1 \\end{bmatrix}\n$$\n",
            renderedPreviewHtml: Display(
                "<mrow><mo fence=\"true\">[</mo>"
                + "<mtable rowspacing=\"0.16em\" columnalign=\"center center\">"
                + Row("1", "0") + Row("0", "1")
                + "</mtable><mo fence=\"true\">]</mo></mrow>",

                // The annotation is serialized markup, so the ampersand is an entity in it -
                // a raw one makes the equation unparseable, which is a fixture mistake worth
                // not making twice.
                "\\begin{bmatrix} 1 &amp; 0 \\\\ 0 &amp; 1 \\end{bmatrix}"));

        string xml = exported.DocumentXml();

        xml.ShouldContain("<m:m>");
        xml.ShouldContain("<m:mr>");
        exported.Skipped.ShouldBeEmpty();
        exported.PlainText().ShouldNotContain("bmatrix");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// An aligned system arrives as the same table, with more cells - the alignment points are
    /// columns. Word has no aligned-equation construct of its own, so a matrix is what it
    /// becomes, and the columns keep the parts lined up.
    /// </summary>
    [Fact]
    public async Task An_aligned_system_arrives_as_the_table_it_is()
    {
        using var exported = await ExportedDocument.FromAsync(
            "$$\n\\begin{align}\na &= b \\\\\nc &= d\n\\end{align}\n$$\n",
            renderedPreviewHtml: Display(
                "<mtable rowspacing=\"0.25em\" columnalign=\"right left\">"
                + Row("a", "b") + Row("c", "d")
                + "</mtable>",
                "\\begin{align} a &amp;= b \\\\ c &amp;= d \\end{align}"));

        string xml = exported.DocumentXml();

        xml.ShouldContain("<m:m>");
        exported.Skipped.ShouldBeEmpty();
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// Padding is not meaning. KaTeX wraps anything it has to shift or squeeze in
    /// <c>mpadded</c> - <c>\raisebox</c>, <c>\smash</c>, and the overlapped arrows an mhchem
    /// reaction is built from - and OMML has no equivalent because it spaces itself. Before it
    /// was stepped past, a single padded arrow cost the whole equation: every <c>\ce{}</c> in
    /// a document came out as TeX source, and the dialog said <c>mpadded</c> once for the lot.
    /// </summary>
    [Fact]
    public async Task Padding_round_a_symbol_does_not_cost_the_equation()
    {
        using var exported = await ExportedDocument.FromAsync(
            "$$\n\\ce{CO2 + C -> 2 CO}\n$$\n",
            renderedPreviewHtml: Display(
                "<mrow><mi>CO</mi><msub><mi>C</mi><mn>2</mn></msub>"
                + "<mpadded width=\"0\" lspace=\"-1em\"><mo>+</mo></mpadded>"
                + "<mi>C</mi><mpadded height=\"0\"><mo>→</mo></mpadded><mn>2</mn><mi>CO</mi></mrow>",
                "\\ce{CO2 + C -> 2 CO}"));

        string xml = exported.DocumentXml();

        xml.ShouldContain("oMath");
        xml.ShouldContain("→");

        // Converted, so nothing to report and no source in the body.
        exported.Skipped.ShouldBeEmpty();
        exported.PlainText().ShouldNotContain("\\ce{");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_a_preview_an_equation_keeps_its_source()
    {
        using var exported = await ExportedDocument.FromAsync("$$\nE = mc^2\n$$\n");

        exported.PlainText().ShouldContain("E = mc^2");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// The non-breaking space the browser's serializer writes is not a name XML defines, and
    /// it turns up in exactly the equations that use spacing.
    /// </summary>
    [Fact]
    public async Task An_equation_carrying_a_serialized_entity_still_converts()
    {
        using var exported = await ExportedDocument.FromAsync(
            "$$\na b\n$$\n",
            renderedPreviewHtml: Display("<mi>a</mi><mtext>&nbsp;</mtext><mi>b</mi>", "a b"));

        exported.DocumentXml().ShouldContain("oMath");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// One row of a table, in the shape KaTeX writes it: every cell's content wrapped in the
    /// <c>mstyle</c> that carries the script level.
    /// </summary>
    private static string Row(params string[] cells) =>
        "<mtr>"
        + string.Concat(cells.Select(cell =>
            "<mtd><mstyle scriptlevel=\"0\" displaystyle=\"false\">"
            + $"<mi>{cell}</mi>"
            + "</mstyle></mtd>"))
        + "</mtr>";

    /// <summary>KaTeX's own wrapper: the MathML, then the TeX it was written as.</summary>
    private static string Mathml(string inner, string tex) =>
        "<span class=\"katex\"><span class=\"katex-mathml\">"
        + "<math xmlns=\"http://www.w3.org/1998/Math/MathML\"><semantics>"
        + inner
        + $"<annotation encoding=\"application/x-tex\">{tex}</annotation>"
        + "</semantics></math></span></span>";

    private static string Display(string inner, string tex) =>
        $"<div class=\"math\" data-src-line=\"0\">{Mathml(inner, tex)}</div>";

    private static string Paragraph(int line, string inner) =>
        $"<p data-src-line=\"{line}\">{inner}</p>";
}
