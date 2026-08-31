// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace PaulTechGuy.MQ.Rendering;

/// <summary>
/// Gives raw HTML blocks a source line, which Markdig's own renderer does not.
///
/// The stock renderer copies an HTML block to the output verbatim and ignores any attributes
/// stamped on it, so a document that opens with hand-written HTML has nothing for the scroll
/// sync to anchor on until the first markdown block after it. The two panes then disagree
/// about where the top of the document is.
///
/// The block itself cannot be wrapped: a block may be a lone closing tag, and wrapping that
/// would close the wrapper instead. So an empty, zero-height marker element is written in
/// front of the block instead, carrying the attributes the block would have carried.
/// </summary>
public sealed class SourceLineHtmlBlockRenderer : HtmlBlockRenderer
{
    /// <summary>Class name shared with the stylesheet and the JavaScript side of the bridge.</summary>
    public const string MarkerClass = "mq-src-marker";

    protected override void Write(HtmlRenderer renderer, HtmlBlock obj)
    {
        if (renderer.EnableHtmlForBlock)
        {
            renderer.Write("<div class=\"").Write(MarkerClass).Write('"');
            renderer.WriteAttributes(obj);
            renderer.WriteLine("></div>");
        }

        base.Write(renderer, obj);
    }
}
