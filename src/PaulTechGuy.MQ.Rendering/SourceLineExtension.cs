// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace PaulTechGuy.MQ.Rendering;

/// <summary>
/// Stamps every block element with the zero-based markdown line that produced it.
///
/// This attribute is what makes side-by-side scroll synchronization accurate: the shell
/// reads the data-src-line of the elements around the viewport and interpolates, rather
/// than guessing from relative scroll percentage, which drifts badly on documents with
/// tall images, tables or diagrams.
/// </summary>
public sealed class SourceLineExtension : IMarkdownExtension
{
    /// <summary>Attribute name shared with the JavaScript side of the bridge.</summary>
    public const string AttributeName = "data-src-line";

    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        pipeline.DocumentProcessed -= StampLineNumbers;
        pipeline.DocumentProcessed += StampLineNumbers;
    }

    public void Setup(MarkdownPipeline pipeline, Markdig.Renderers.IMarkdownRenderer renderer)
    {
        // The attributes ride along on the existing renderers, with one exception: raw HTML
        // blocks are copied out verbatim, attributes and all, so they get a renderer that
        // puts the line somewhere the shell can see it.
        if (renderer is Markdig.Renderers.HtmlRenderer html)
        {
            html.ObjectRenderers.ReplaceOrAdd<HtmlBlockRenderer>(new SourceLineHtmlBlockRenderer());
        }
    }

    private static void StampLineNumbers(MarkdownDocument document)
    {
        foreach (MarkdownObject node in document.Descendants())
        {
            // Only blocks map cleanly onto a source line. Inlines share their parent's line.
            if (node is not Block block || block is MarkdownDocument)
            {
                continue;
            }

            block.GetAttributes().AddProperty(
                AttributeName,
                block.Line.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
