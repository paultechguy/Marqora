// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

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

        // The body's fence got the keyword red, not the note's text.
        xml.ShouldContain("D73A49");
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
            new PaulTechGuy.MQ.Domain.DocxExportSetup
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
}
