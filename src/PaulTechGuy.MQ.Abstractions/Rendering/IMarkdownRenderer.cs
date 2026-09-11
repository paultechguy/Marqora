// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Abstractions.Rendering;

/// <summary>
/// Converts markdown source into the HTML fragment injected into the preview shell.
/// Implementations must be safe to call from a background thread.
/// </summary>
public interface IMarkdownRenderer
{
    /// <summary>
    /// Renders the supplied markdown to an HTML fragment. Block elements carry a
    /// <c>data-src-line</c> attribute so the shell can map preview position back to source line.
    /// </summary>
    RenderedMarkdown Render(string markdown);

    /// <summary>
    /// The same, with the headings numbered as the preference asks.
    ///
    /// An overload rather than a parameter on the one above, because most callers have no
    /// opinion: the cheatsheet, the link checks and the folio scan want the document's
    /// links and headings, not a rendering of it for someone to read. Only the preview
    /// passes a numbering.
    ///
    /// The numbers are written into the HTML as ordinary text and reported alongside each
    /// heading in <see cref="RenderedMarkdown.Outline"/>, so the preview and the outline
    /// panel cannot disagree about what a section is called.
    /// </summary>
    RenderedMarkdown Render(string markdown, HeadingNumbering headingNumbering);
}
