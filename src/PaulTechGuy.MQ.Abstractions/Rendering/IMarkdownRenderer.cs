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
}
