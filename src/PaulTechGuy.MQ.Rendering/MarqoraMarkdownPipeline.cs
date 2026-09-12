// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Markdig;
using Markdig.Extensions.AutoIdentifiers;

namespace PaulTechGuy.MQ.Rendering;

/// <summary>
/// The set of Markdig extensions Marqora understands a document to be written in.
///
/// This exists because there are now two readers of the same markdown: the preview, which
/// renders to HTML, and the Word export, which walks the parsed tree and writes
/// WordprocessingML. They have to agree on what the syntax means. A document where
/// <c>==highlight==</c> is a mark on screen and two literal equals signs in the exported
/// .docx is a bug nobody would think to look for, and the way to prevent it is to have one
/// place that says which extensions are on.
///
/// A builder rather than a finished pipeline, because the two callers do differ in one
/// respect: the preview adds <see cref="SourceLineExtension"/> so the shell can map rendered
/// elements back to source lines for scroll sync. The export does not need the attribute -
/// it reads <c>Block.Line</c> off the tree directly, which is the same number - but it does
/// need everything above it to be identical.
/// </summary>
public static class MarqoraMarkdownPipeline
{
    /// <summary>
    /// Every extension Marqora enables, ready for a caller to add its own and build.
    /// </summary>
    public static MarkdownPipelineBuilder CreateBuilder() =>
        new MarkdownPipelineBuilder()
            // Tables, footnotes, task lists, definition lists, figures, math, auto-links,
            // custom containers and the diagram blocks that carry mermaid.
            .UseAdvancedExtensions()
            // GitHub-compatible anchors so links like #my-heading behave as users expect.
            .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
            // Front matter is metadata, not content: parse it so it is not rendered as a table.
            .UseYamlFrontMatter()
            .UseEmojiAndSmiley();
}
