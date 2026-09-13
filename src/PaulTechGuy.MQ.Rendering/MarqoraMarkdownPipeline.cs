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
    public static MarkdownPipelineBuilder CreateBuilder()
    {
        MarkdownPipelineBuilder builder = new MarkdownPipelineBuilder()
            // Tables, footnotes, task lists, definition lists, figures, math, auto-links,
            // custom containers, the diagram blocks that carry mermaid, and generic attributes -
            // {#id} on a heading, honored below whichever way its id gets there. This also
            // registers its own AutoIdentifierExtension, removed just below.
            .UseAdvancedExtensions()
            // Front matter is metadata, not content: parse it so it is not rendered as a table.
            .UseYamlFrontMatter()
            .UseEmojiAndSmiley();

        // UseAdvancedExtensions' own auto-identifier collapses a run of separators into one
        // hyphen for a heading's id, which is not what github.com actually does - a heading's
        // own hand-written table of contents is written against the real thing, and
        // "Comparison Operators & Equality" has to resolve to the same
        // "comparison-operators--equality" airbnb/javascript's does, not
        // "comparison-operators-equality". Calling UseAutoIdentifiers again to turn it off would
        // be a no-op - Markdig will not add a second extension of a type it already has - so it
        // is removed outright and GitHubHeadingSlug (below) is the only thing left to assign an
        // id no one wrote explicitly, which needs it gone rather than merely reconfigured: its
        // own idea of "does this heading already have an id" only means anything if nothing else
        // can have set one first.
        if (builder.Extensions.Find<AutoIdentifierExtension>() is { } autoIdentifiers)
        {
            builder.Extensions.Remove(autoIdentifiers);
        }

        builder.DocumentProcessed += GitHubHeadingSlug.FixIdentifiers;

        return builder;
    }
}
