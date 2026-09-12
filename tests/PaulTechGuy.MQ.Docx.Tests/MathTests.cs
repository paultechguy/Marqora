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
    /// A document with twenty equations using the same unmapped construct has one thing wrong
    /// with it, not twenty. A dialog listing the same sentence twenty times says less.
    /// </summary>
    [Fact]
    public async Task The_same_gap_in_several_equations_is_reported_once()
    {
        using var exported = await ExportedDocument.FromAsync(
            "$$\n\\a\n$$\n\nText.\n\n$$\n\\b\n$$\n",
            renderedPreviewHtml:
                "<div class=\"math\" data-src-line=\"0\">"
                + Mathml("<munknownthing><mi>x</mi></munknownthing>", "\\a") + "</div>"
                + "<p data-src-line=\"4\">Text.</p>"
                + "<div class=\"math\" data-src-line=\"6\">"
                + Mathml("<munknownthing><mi>y</mi></munknownthing>", "\\b") + "</div>");

        exported.Skipped.Count.ShouldBe(1);
        exported.PlainText().ShouldContain("\\a");
        exported.PlainText().ShouldContain("\\b");
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
