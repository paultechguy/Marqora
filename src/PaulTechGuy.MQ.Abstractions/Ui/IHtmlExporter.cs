// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Abstractions.Ui;

/// <summary>
/// Writes rendered preview markup out as a standalone HTML document.
///
/// The caller supplies the markup, which comes from the live preview and therefore already
/// contains inline SVG diagrams, laid-out maths and highlighted code. The implementation
/// adds the surrounding document and makes it self-contained.
/// </summary>
public interface IHtmlExporter
{
    /// <param name="outputPath">Where to write the file.</param>
    /// <param name="title">Document title, used for the page title.</param>
    /// <param name="renderedHtml">The preview markup, as rendered.</param>
    /// <param name="sourceDocumentPath">
    /// The markdown file's own path, used to resolve relative images. Null for a document
    /// that has never been saved, in which case images are left as they are.
    /// </param>
    /// <remarks>
    /// Heading numbers, when they are switched on, are already in <paramref name="renderedHtml"/>:
    /// the shell writes them into the preview as ordinary text rather than drawing them with
    /// CSS, so they arrive here like any other content and nothing has to be told about them.
    /// </remarks>
    Task WriteAsync(
        string outputPath,
        string title,
        string renderedHtml,
        string? sourceDocumentPath,
        CancellationToken cancellationToken = default);
}
