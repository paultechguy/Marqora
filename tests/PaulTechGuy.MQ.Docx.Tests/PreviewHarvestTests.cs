// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Binary;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Docx.Tests;

/// <summary>
/// Reading the diagrams, colors and equations back out of the preview's markup.
///
/// The markup here is the shape the real thing has, taken from what the renderer actually
/// emits: Markdig stamps the source line on the inner element for a fence and on the outer one
/// for a diagram, and the shell then adds the diagram hash, the highlight spans and KaTeX's
/// output. That asymmetry is exactly the sort of thing worth pinning down rather than guessing
/// at from the outside.
///
/// Everything goes through the exporter rather than at the reader directly, because what
/// matters is not that a string was parsed but that the right color reached the document.
/// Whether the shell really hands back markup of this shape is the one question only the
/// running app can answer.
/// </summary>
public class PreviewHarvestTests
{
    /// <summary>GitHub light draws a keyword in this red, which is what should reach the file.</summary>
    private const string KeywordRed = "D73A49";

    private const string NumberBlue = "005CC5";

    [Fact]
    public async Task Without_a_preview_the_document_is_written_anyway_just_without_colors()
    {
        using var exported = await ExportedDocument.FromAsync("```js\nvar a = 1;\n```\n");

        string xml = exported.DocumentXml();

        xml.ShouldContain("var a = 1;");
        xml.ShouldNotContain(KeywordRed);
        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task Highlighted_code_reaches_the_document_with_its_colors()
    {
        using var exported = await ExportedDocument.FromAsync(
            "```js\nvar a = 1;\n```\n",
            renderedPreviewHtml:
                "<pre><code class=\"language-js hljs\" data-src-line=\"0\">"
                + "<span class=\"hljs-keyword\">var</span> a = "
                + "<span class=\"hljs-number\">1</span>;\n</code></pre>");

        string xml = exported.DocumentXml();

        xml.ShouldContain(KeywordRed);
        xml.ShouldContain(NumberBlue);
        exported.PlainText().ShouldContain("var a = 1;");
    }

    /// <summary>
    /// The editor runs ahead of the preview by a debounce interval. A block edited a moment
    /// before the export comes back with the right shape and yesterday's text, and writing it
    /// would put the wrong code in the document without saying so.
    /// </summary>
    [Fact]
    public async Task Colors_from_a_stale_preview_are_refused()
    {
        using var exported = await ExportedDocument.FromAsync(
            "```js\nvar b = 2;\n```\n",
            renderedPreviewHtml:
                "<pre><code class=\"language-js hljs\" data-src-line=\"0\">"
                + "<span class=\"hljs-keyword\">var</span> a = 1;\n</code></pre>");

        string xml = exported.DocumentXml();

        // What the document holds is what the editor holds, not what the preview remembered.
        exported.PlainText().ShouldContain("var b = 2;");
        exported.PlainText().ShouldNotContain("var a = 1;");
        xml.ShouldNotContain(KeywordRed);
    }

    /// <summary>
    /// The reason the artifacts are keyed on the source line rather than counted in order. A
    /// display equation is a div in the markup and a fenced-code block in the tree, so a
    /// counter hands the fence the equation's colors - and every fence after it too.
    /// </summary>
    [Fact]
    public async Task Display_math_does_not_shift_the_colors_of_the_code_after_it()
    {
        using var exported = await ExportedDocument.FromAsync(
            "$$\nE = mc^2\n$$\n\n```js\nlet x;\n```\n",
            renderedPreviewHtml:
                "<div class=\"math\" data-src-line=\"0\">\\[E = mc^2\\]</div>"
                + "<pre><code class=\"language-js hljs\" data-src-line=\"4\">"
                + "<span class=\"hljs-keyword\">let</span> x;\n</code></pre>");

        string xml = exported.DocumentXml();

        xml.ShouldContain(KeywordRed);
        exported.PlainText().ShouldContain("let x;");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task An_uncolored_block_is_still_written_rather_than_dropped()
    {
        using var exported = await ExportedDocument.FromAsync(
            "```brainfuck\n+++.\n```\n",
            renderedPreviewHtml:
                "<pre><code class=\"language-brainfuck\" data-src-line=\"0\">+++.\n</code></pre>");

        exported.PlainText().ShouldContain("+++.");
    }

    [Fact]
    public async Task Entities_in_the_markup_come_back_as_the_characters_they_stand_for()
    {
        using var exported = await ExportedDocument.FromAsync(
            "```cs\nif (a < b && c > d)\n```\n",
            renderedPreviewHtml:
                "<pre><code class=\"language-cs hljs\" data-src-line=\"0\">"
                + "if (a &lt; b &amp;&amp; c &gt; d)\n</code></pre>");

        exported.PlainText().ShouldContain("if (a < b && c > d)");
    }

    [Fact]
    public async Task A_diagram_becomes_a_picture()
    {
        using var exported = await ExportedDocument.FromAsync(
            "```mermaid\ngraph TD; A-->B;\n```\n",
            renderedPreviewHtml:
                "<pre class=\"mermaid\" data-src-line=\"0\" data-mq-diagram=\"abc123\">"
                + "<svg></svg></pre>",
            diagramPng: _ => Task.FromResult<byte[]?>(Png(400, 200)));

        exported.DocumentXml().ShouldContain("<w:drawing>");
        exported.Skipped.ShouldBeEmpty();
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// The shell rasterizes at twice the natural size so a diagram stays crisp when it is
    /// zoomed or printed. Drawing those pixels one for one would put it on the page at double
    /// the size its author saw.
    /// </summary>
    [Fact]
    public async Task A_diagram_is_drawn_at_the_size_it_was_authored_not_the_size_it_was_rastered()
    {
        using var exported = await ExportedDocument.FromAsync(
            "```mermaid\ngraph TD; A-->B;\n```\n",
            renderedPreviewHtml:
                "<pre class=\"mermaid\" data-src-line=\"0\" data-mq-diagram=\"abc123\">"
                + "<svg></svg></pre>",
            diagramPng: _ => Task.FromResult<byte[]?>(Png(192, 96)));

        // 192 raster pixels at 2x is 96 real ones, which is an inch: 914400 EMU.
        exported.DocumentXml().ShouldContain("cx=\"914400\"");
    }

    /// <summary>
    /// <summary>
    /// A diagram is set apart from the text around it.
    ///
    /// The preview does that with a padded panel in a different color, so the picture never
    /// touches the prose. Word gets the picture alone, and at six points of spacing it sat
    /// hard against the paragraph below and read as part of it.
    ///
    /// The two figures are equal, and pinned here because an unequal pair looked unequal:
    /// 240 above and 360 below read as a diagram sitting low in its own gap.
    /// </summary>
    [Fact]
    public async Task A_diagram_is_set_apart_from_the_text_around_it()
    {
        using var exported = await ExportedDocument.FromAsync(
            "Before it.\n\n```mermaid\ngraph TD; A-->B;\n```\n\nAfter it.\n",
            renderedPreviewHtml:
                "<pre class=\"mermaid\" data-src-line=\"2\" data-mq-diagram=\"abc123\">"
                + "<svg></svg></pre>",
            diagramPng: _ => Task.FromResult<byte[]?>(Png(192, 96)));

        string xml = exported.DocumentXml();

        xml.ShouldContain("w:before=\"360\"");
        xml.ShouldContain("w:after=\"360\"");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// Without a picture the definition is written as code. Worse than a diagram, and much
    /// better than a gap - the reader can still see what was meant.
    /// </summary>
    [Fact]
    public async Task A_diagram_with_no_picture_falls_back_to_its_source()
    {
        using var exported = await ExportedDocument.FromAsync(
            "```mermaid\ngraph TD; A-->B;\n```\n",
            renderedPreviewHtml:
                "<pre class=\"mermaid\" data-src-line=\"0\" data-mq-diagram=\"abc123\">"
                + "<svg></svg></pre>",
            diagramPng: _ => Task.FromResult<byte[]?>(null));

        exported.PlainText().ShouldContain("graph TD; A-->B;");
        exported.Skipped.Count.ShouldBe(1);
        exported.DocumentXml().ShouldNotContain("<w:drawing>");
    }

    [Fact]
    public async Task A_diagram_with_no_preview_at_all_falls_back_to_its_source()
    {
        using var exported = await ExportedDocument.FromAsync("```mermaid\ngraph TD; A-->B;\n```\n");

        exported.PlainText().ShouldContain("graph TD; A-->B;");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>A PNG header carrying the dimensions; nothing reads past it.</summary>
    private static byte[] Png(uint width, uint height)
    {
        byte[] png = new byte[33];

        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(png);

        png[11] = 13;
        png[12] = (byte)'I';
        png[13] = (byte)'H';
        png[14] = (byte)'D';
        png[15] = (byte)'R';

        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20), height);

        png[24] = 8;
        png[25] = 6;

        return png;
    }
}
