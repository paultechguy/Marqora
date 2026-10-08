// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Docx.Tests;

/// <summary>
/// The constructs the two shipped documents do not contain, and the shapes that broke earlier
/// drafts of the walker.
///
/// Marqora's own Welcome document and cheatsheet cover a great deal between them, and they are
/// what the other tests lean on - but neither has a grid table, a custom container or a
/// nomnoml fence in it, and neither puts a heading inside a callout or a fence inside a
/// footnote. Those last two are not hypothetical: they are exactly the placements that made an
/// earlier design get the numbering and the colors wrong, silently, in documents that looked
/// fine everywhere else.
///
/// Everything here has to produce a document Word will open. Several of them have no real Word
/// equivalent at all, and for those the test is that the content survives in some form rather
/// than that it survives in a particular one.
/// </summary>
public class AwkwardDocumentTests
{
    /// <summary>
    /// A heading buried inside a callout is still a heading, so it still takes its heading
    /// style - and because the numbering hangs off the style rather than being applied by the
    /// walker, Word counts it wherever it ended up.
    /// </summary>
    [Fact]
    public async Task A_heading_inside_a_callout_is_numbered_like_any_other()
    {
        using var exported = await ExportedDocument.FromAsync(
            "# First\n\n> [!NOTE]\n> ## Inside the callout\n\n# Second\n",
            headingNumbering: PaulTechGuy.MQ.Domain.HeadingNumbering.FromHeading1);

        string xml = exported.DocumentXml();

        xml.ShouldContain("w:val=\"Heading2\"");
        exported.PlainText().ShouldContain("Inside the callout");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// Markdig moves footnote definitions to the end of the document, so a fence inside one
    /// renders last in the preview and is met mid-document by a naive walker. Keying the
    /// colors on the source line rather than on the order things appear in is what makes this
    /// come out right - and the counts match either way, so nothing would have complained.
    /// </summary>
    [Fact]
    public async Task Code_inside_a_footnote_does_not_shift_the_colors_of_code_in_the_body()
    {
        using var exported = await ExportedDocument.FromAsync(
            "Body text.[^1]\n\n```js\nlet body = 1;\n```\n\n[^1]: A note.\n\n    ```js\n    let note = 2;\n    ```\n",
            renderedPreviewHtml:
                "<p data-src-line=\"0\">Body text.</p>"
                + "<pre><code class=\"language-js hljs\" data-src-line=\"2\">"
                + "<span class=\"hljs-keyword\">let</span> body = 1;\n</code></pre>"
                + "<div class=\"footnotes\"><pre><code class=\"language-js hljs\" data-src-line=\"8\">"
                + "<span class=\"hljs-keyword\">let</span> note = 2;\n</code></pre></div>");

        string xml = exported.DocumentXml();

        // The body's fence got the keyword color, not the note's text.
        xml.ShouldContain(DefaultColors.Rgb("syntax-keyword"));
        exported.PlainText().ShouldContain("let body = 1;");
        exported.FootnotesText().ShouldContain("let note = 2;");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_grid_table_survives()
    {
        using var exported = await ExportedDocument.FromAsync(
            "+-----------+--------+\n| Name      | Count  |\n+===========+========+\n"
            + "| Apples    | 3      |\n+-----------+--------+\n| Pears     | 12     |\n"
            + "+-----------+--------+\n");

        string text = exported.PlainText();

        text.ShouldContain("Apples");
        text.ShouldContain("12");
        exported.DocumentXml().ShouldContain("<w:tbl>");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// A custom container has no Word equivalent and is not one of the five callouts, so the
    /// content is what has to survive. Losing the block entirely would be the wrong answer.
    /// </summary>
    [Fact]
    public async Task A_custom_container_keeps_what_is_inside_it()
    {
        using var exported = await ExportedDocument.FromAsync(
            ":::warning\nSomething worth knowing.\n:::\n");

        exported.PlainText().ShouldContain("Something worth knowing.");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// Markdig treats nomnoml as a diagram alongside mermaid, so it renders as a div and never
    /// reaches the code sequence. Nothing in Marqora draws one, so its source is what the
    /// reader gets - which is better than the empty space a silent drop would leave.
    /// </summary>
    [Fact]
    public async Task A_nomnoml_fence_keeps_its_source()
    {
        using var exported = await ExportedDocument.FromAsync(
            "```nomnoml\n[A] -> [B]\n```\n");

        exported.PlainText().ShouldContain("[A] -> [B]");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task Raw_html_does_not_stop_the_rest_of_the_document()
    {
        using var exported = await ExportedDocument.FromAsync(
            "Before.\n\n<div class=\"custom\">\n  <p>Raw markup.</p>\n</div>\n\nAfter.\n");

        string text = exported.PlainText();

        text.ShouldContain("Before.");
        text.ShouldContain("After.");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// A heading with an explicit identifier is what the generic-attributes extension is for,
    /// and the bookmark has to follow the identifier the author chose rather than one worked
    /// out from the text - or every link written against it stops resolving.
    /// </summary>
    [Fact]
    public async Task A_heading_with_its_own_identifier_is_linkable_by_it()
    {
        using var exported = await ExportedDocument.FromAsync(
            "# A Very Long Title {#short}\n\nSee [above](#short).\n");

        exported.DocumentXml().ShouldContain("w:anchor=");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// A media link would be an iframe on a web page. There is no network at export time and
    /// nowhere in Word to put a video, so what matters is that the document still opens.
    /// </summary>
    [Fact]
    public async Task A_media_link_does_not_break_the_document()
    {
        using var exported = await ExportedDocument.FromAsync(
            "Watch this:\n\n![](https://www.youtube.com/watch?v=dQw4w9WgXcQ)\n");

        exported.PlainText().ShouldContain("Watch this:");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task Citations_and_footers_do_not_break_the_document()
    {
        using var exported = await ExportedDocument.FromAsync(
            "A \"\"quoted thing\"\" here.\n\n^^ A footer line ^^\n");

        exported.PlainText().ShouldContain("quoted thing");
        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// Everything awkward at once. Not a realistic document, which is the point: the walker
    /// has to hold up when constructs are nested inside one another rather than only when each
    /// appears on its own.
    /// </summary>
    [Fact]
    public async Task All_of_it_together_still_produces_a_document_Word_will_open()
    {
        using var exported = await ExportedDocument.FromAsync(
            """
            ---
            title: Everything
            ---

            # One

            > [!WARNING]
            > ## A heading in a callout
            >
            > - a list
            >   - nested
            >
            > | a | b |
            > | - | - |
            > | 1 | 2 |

            :::note
            A container holding a `fence`:

            ```js
            let x = 1;
            ```
            :::

            Term
            :   A definition with a footnote.[^1]

            1. First
            2. Second

               ```python
               print("inside an item")
               ```

            [^1]: The note, holding a table.

                | x |
                | - |
                | 1 |
            """,
            new PaulTechGuy.MQ.Domain.ExportLayout
            {
                IncludeTableOfContents = true,
                IncludeCoverPage = true,
            },
            PaulTechGuy.MQ.Domain.HeadingNumbering.FromHeading1);

        exported.ValidationErrors().ShouldBeEmpty();

        string text = exported.PlainText();

        text.ShouldContain("A heading in a callout");
        text.ShouldContain("inside an item");
        text.ShouldContain("A definition with a footnote.");
    }

    /// <summary>
    /// A document that is nothing but its own metadata. The body would be empty, and Word
    /// wants a paragraph to put the cursor in.
    /// </summary>
    [Fact]
    public async Task A_document_with_nothing_in_it_is_still_a_document()
    {
        using var exported = await ExportedDocument.FromAsync("---\ntitle: Empty\n---\n");

        exported.ValidationErrors().ShouldBeEmpty();
    }

    [Fact]
    public async Task An_entirely_empty_document_is_still_a_document()
    {
        using var exported = await ExportedDocument.FromAsync(string.Empty);

        exported.ValidationErrors().ShouldBeEmpty();
    }

    /// <summary>
    /// Raw HTML that shows something but holds no words was left out with no report row, so
    /// the report closed on "Everything else came across" over three things that had not. A
    /// frame Word cannot show is reported; an inline rule is drawn after its paragraph, as a
    /// markdown "---" is; an inline image goes the way a markdown image does, embedded or
    /// reported as a picture.
    /// </summary>
    [Fact]
    public async Task Raw_html_that_shows_without_words_is_drawn_or_reported()
    {
        using var exported = await ExportedDocument.FromAsync(
            "<iframe src=\"https://example.com\" title=\"frame\"></iframe>\n\n"
            + "A rule as HTML: <hr />\n\n"
            + "An image tag: <img src=\"x.png\" alt=\"square\" /> and <b>bold</b> after it.\n");

        exported.ValidationErrors().ShouldBeEmpty();

        PaulTechGuy.MQ.Domain.ExportIssue[] leftOut =
        [
            .. exported.Issues.Where(i => i.Problem.StartsWith("Raw HTML that Word cannot show", StringComparison.Ordinal)),
        ];

        leftOut.Select(i => i.Line).ShouldBe([1]);
        leftOut[0].Item.ShouldStartWith("<iframe");

        // The rule: one bottom-bordered paragraph, after the one that held it and before the
        // next.
        string xml = exported.DocumentXml();
        int rule = xml.IndexOf("<w:bottom ", StringComparison.Ordinal);

        rule.ShouldBeGreaterThan(xml.IndexOf("A rule as HTML:", StringComparison.Ordinal));
        rule.ShouldBeLessThan(xml.IndexOf("An image tag:", StringComparison.Ordinal));
        xml.LastIndexOf("<w:bottom ", StringComparison.Ordinal).ShouldBe(rule);

        // The picture is a picture's row, under its alt text, on its own line.
        exported.Issues.ShouldContain(i => i.Line == 5 && i.Item == "square");

        string text = exported.PlainText();

        text.ShouldContain("[square]");
        text.ShouldContain("bold after it.");
    }

    /// <summary>
    /// The inline HTML a browser gives a look Word does not infer from the tag: a q's
    /// quotation marks - curly, single inside double - and small's smaller size.
    /// </summary>
    [Fact]
    public async Task Q_gets_its_quotation_marks_and_small_its_size()
    {
        using var exported = await ExportedDocument.FromAsync(
            "She said <q>go <q>now</q></q> and a <small>small span</small>.\n");

        exported.ValidationErrors().ShouldBeEmpty();
        exported.PlainText().ShouldContain("She said \u201Cgo \u2018now\u2019\u201D and a small span.");
        exported.DocumentXml().ShouldContain("<w:sz w:val=\"18\" /><w:szCs w:val=\"18\" /></w:rPr><w:t xml:space=\"preserve\">small span");
    }

    /// <summary>
    /// A raw HTML table is a Word table, laid out as the browser lays it: the fixture's own,
    /// whose "North" spans two rows and whose "merged across two columns" therefore starts in
    /// the second column of the row below, not the first. Its caption is a caption; its head
    /// row repeats; it is not reported, because nothing was lost.
    /// </summary>
    [Fact]
    public async Task A_raw_html_table_is_a_word_table_with_its_spans()
    {
        using var exported = await ExportedDocument.FromAsync(
            "<table>\n"
            + "  <caption>Quarterly <code>figures</code></caption>\n"
            + "  <thead>\n    <tr><th>Region</th><th>Q1</th><th>Q2</th></tr>\n  </thead>\n"
            + "  <tbody>\n"
            + "    <tr><td rowspan=\"2\">North</td><td>100</td><td>120</td></tr>\n"
            + "    <tr><td colspan=\"2\" style=\"text-align:center\">merged across two columns</td></tr>\n"
            + "    <tr><td>South</td><td>80</td><td>95</td></tr>\n"
            + "  </tbody>\n"
            + "</table>\n");

        exported.ValidationErrors().ShouldBeEmpty();
        exported.Issues.ShouldBeEmpty();

        string xml = exported.DocumentXml();

        xml.ShouldContain("w:val=\"Caption\"");
        xml.ShouldContain("Quarterly figures");
        xml.ShouldContain("<w:tblHeader />");

        // North starts a vertical merge; the row below continues it, then the two-column cell.
        xml.ShouldContain("<w:vMerge w:val=\"restart\" />");
        xml.ShouldContain(
            "<w:vMerge w:val=\"continue\" /></w:tcPr><w:p /></w:tc><w:tc><w:tcPr><w:tcW w:w=\"0\" w:type=\"auto\" />"
            + "<w:gridSpan w:val=\"2\" /></w:tcPr><w:p><w:pPr><w:jc w:val=\"center\" /></w:pPr>");
        Regex.Count(xml, "<w:gridCol ").ShouldBe(3);
    }

    /// <summary>
    /// A styled HTML block keeps its box: the borders and fill the preview drew, measured by
    /// the shell and stamped on the block, become the paragraphs' borders and shading. The
    /// fixture's callout - a colored rule down the left and a tinted fill - read as two plain
    /// paragraphs before.
    /// </summary>
    [Fact]
    public async Task A_styled_html_block_keeps_its_box()
    {
        using var exported = await ExportedDocument.FromAsync(
            "Before.\n\n<div style=\"border-left: 4px solid #6366f1; background: #eef2ff; padding: 12px\">\n"
            + "<strong>A styled callout.</strong><br>Second line.\n</div>\n",
            renderedPreviewHtml:
                "<p data-src-line=\"0\">Before.</p>"
                + "<div class=\"mq-src-marker\" data-src-line=\"2\"></div>"
                + "<div style=\"border-left: 4px solid #6366f1\" data-mq-box=\"border-left:4px solid #6366f1;background:#eef2ff;padding-left:12px\">"
                + "<strong>A styled callout.</strong><br>Second line.</div>");

        exported.ValidationErrors().ShouldBeEmpty();

        string xml = exported.DocumentXml();

        // 4px is 3pt, 24 eighths; 12px of padding is 9pt.
        Regex.Count(xml, "<w:left w:val=\"single\" w:color=\"6366F1\" w:sz=\"24\" w:space=\"9\" /></w:pBdr><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"EEF2FF\" />")
            .ShouldBe(2);
        exported.Issues.ShouldContain(i => i.Problem.Contains("its box are in the document", StringComparison.Ordinal));
    }

    /// <summary>
    /// A diagram of a type Word draws from SVG goes in as a vector, its PNG kept as the
    /// fallback; a type Word draws wrong - a flowchart, whose labels sit in foreignObject -
    /// stays a PNG and its SVG is never asked for.
    /// </summary>
    [Fact]
    public async Task Diagrams_word_draws_from_svg_go_in_as_vectors()
    {
        byte[] pixel = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        var asked = new List<string>();

        using var exported = await ExportedDocument.FromAsync(
            "```mermaid\npie\n  \"A\" : 1\n```\n\n```mermaid\nflowchart LR\n  A --> B\n```\n",
            renderedPreviewHtml:
                "<pre class=\"mermaid\" data-src-line=\"0\" data-mq-diagram=\"pie1\"><svg aria-roledescription=\"pie\"></svg></pre>"
                + "<pre class=\"mermaid\" data-src-line=\"5\" data-mq-diagram=\"flow1\"><svg aria-roledescription=\"flowchart-v2\"></svg></pre>",
            diagramPng: _ => Task.FromResult<byte[]?>(pixel),
            diagramSvg: hash =>
            {
                asked.Add(hash);
                return Task.FromResult<string?>("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"></svg>");
            });

        exported.ValidationErrors().ShouldBeEmpty();
        asked.ShouldBe(["pie1"]);

        string xml = exported.DocumentXml();

        Regex.Count(xml, "<a:blip ").ShouldBe(2);
        Regex.Count(xml, "svgBlip").ShouldBe(1);
        xml.ShouldContain("{96DAC541-7B7A-43D3-8B79-37D633B846F1}");
    }

    /// <summary>
    /// A details block's summary is set in bold above what it opens, the line the preview
    /// shows beside its disclosure triangle.
    /// </summary>
    [Fact]
    public async Task A_details_summary_is_bold()
    {
        using var exported = await ExportedDocument.FromAsync(
            "<details>\n<summary>Click to expand</summary>\n\nHidden content.\n\n</details>\n");

        exported.ValidationErrors().ShouldBeEmpty();
        exported.DocumentXml().ShouldContain("<w:b /></w:rPr><w:t xml:space=\"preserve\">Click to expand");
    }

    /// <summary>
    /// Pandoc's {width=64px height=64px} sets the picture's size. Ignored, the fixture's
    /// one-pixel image went into Word one pixel wide.
    /// </summary>
    [Fact]
    public async Task A_pandoc_size_sets_the_picture_size()
    {
        using var exported = await ExportedDocument.FromAsync(
            "![Sized](data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==){width=64px height=64px}\n");

        exported.ValidationErrors().ShouldBeEmpty();

        // 64 CSS pixels at 9525 EMU each.
        exported.DocumentXml().ShouldContain("<wp:extent cx=\"609600\" cy=\"609600\" />");
    }

    /// <summary>
    /// Something in a pipe table's cell is reported on the line of its row. A cell holding a
    /// broken image link - the fixture's "![dot](data:...&lt;svg ...&gt;)" - is left at line
    /// zero by Markdig, and its row at line 761 was reported as line 1. The row is the
    /// fixture's own, with the cells around it that a plain table did not need.
    /// </summary>
    [Fact]
    public async Task Something_in_a_table_cell_is_reported_on_its_row()
    {
        using var exported = await ExportedDocument.FromAsync(
            "Intro.\n\n| Element | Example | Renders as |\n|---|---|---|\n"
            + "| Code | `` `code` `` | `code` |\n"
            + "| Image | `![a](data:...)` | ![dot](data:image/svg+xml;utf8,<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"12\" height=\"12\"><circle cx=\"6\" cy=\"6\" r=\"5\" fill=\"%23ef4444\"/></svg>) |\n"
            + "| Escaped pipe | `a \\| b` | a \\| b |\n"
            + "| Inline math | `$x^2$` | $x^2$ |\n");

        exported.Issues
            .Where(i => i.Problem.StartsWith("Raw HTML that Word cannot show", StringComparison.Ordinal))
            .Select(i => i.Line)
            .ShouldBe([6]);
    }
}
