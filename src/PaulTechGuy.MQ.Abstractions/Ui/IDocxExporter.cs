// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Abstractions.Ui;

/// <summary>
/// Writes a markdown document out as a Word file.
///
/// This is the one export that does not take the rendered preview. HTML and PDF both carry
/// the preview across as it stands - which is what makes diagrams, math and highlighting
/// already correct in those files - but WordprocessingML is not HTML, so there is nothing to
/// carry. The markdown is parsed again here and walked into Word's own constructs: real
/// heading styles, real numbering, real tables, real footnotes.
///
/// The preview still has a part to play, because three things in a document exist only after
/// a browser has drawn them: the SVG a mermaid definition becomes, the layout KaTeX gives an
/// equation, and the colors highlight.js puts on code. Those arrive through
/// <paramref name="renderedPreviewHtml"/> and are matched back onto the parsed tree by source
/// line. All three are optional, and that is the point - a Word export degrades rather than
/// failing when the preview is busy or has not caught up.
/// </summary>
public interface IDocxExporter
{
    /// <param name="outputPath">Where to write the file.</param>
    /// <param name="title">
    /// The document's display name. Becomes the Word document title, the header text when
    /// headers are on, and the cover page heading when there is no front-matter title.
    /// </param>
    /// <param name="markdown">The document's source text, parsed here rather than passed in
    /// already parsed: the parser belongs to the renderer, not to this contract.</param>
    /// <param name="setup">Paper, orientation, margins, and which Word extras to include.</param>
    /// <param name="headingNumbering">
    /// Matches the reader's own preference, so the numbers in the .docx are the numbers on
    /// screen. Unlike the HTML export - where the numbers are already text in the markup by
    /// the time it arrives - this export has to apply them itself.
    /// </param>
    /// <param name="sourceDocumentPath">
    /// The markdown file's own path, used to resolve relative images. Null for a document
    /// that has never been saved, in which case images are named rather than embedded.
    /// </param>
    /// <param name="renderedPreviewHtml">
    /// The preview's markup as rendered, or null when it could not be had. Supplies mermaid
    /// diagram hashes, KaTeX MathML and highlight.js token colors, each matched to the parsed
    /// tree on the source line Markdig recorded. Null costs the document its diagram images,
    /// its typeset equations and its code colors, and nothing else.
    /// </param>
    /// <param name="diagramPng">
    /// Turns a diagram hash into PNG bytes, or null when that diagram could not be produced.
    /// Passed in rather than reached for because rasterizing happens in the web shell, which
    /// this layer cannot see and should not learn about.
    /// </param>
    /// <returns>
    /// Anything that could not be carried into the file - an image that is not on this
    /// machine, a remote image that would need the network, a diagram that never rendered -
    /// so the caller can say so. An export with a hole in it is otherwise discovered by
    /// whoever opens the document, somewhere else, with no way to know what happened.
    /// </returns>
    Task<IReadOnlyList<string>> WriteAsync(
        string outputPath,
        string title,
        string markdown,
        DocxExportSetup setup,
        HeadingNumbering headingNumbering,
        string? sourceDocumentPath,
        string? renderedPreviewHtml,
        Func<string, Task<byte[]?>>? diagramPng = null,
        CancellationToken cancellationToken = default);
}
