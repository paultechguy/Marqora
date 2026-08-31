// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Domain;
using MarkdigDocument = Markdig.Syntax.MarkdownDocument;

namespace PaulTechGuy.MQ.Rendering;

/// <summary>
/// Markdig-backed renderer. The pipeline is built once and reused; Markdig pipelines are
/// immutable and thread-safe after Build, so a singleton is both correct and fast.
/// </summary>
public sealed class MarkdigMarkdownRenderer : IMarkdownRenderer
{
    /// <summary>Fenced-code info strings that mermaid should pick up in the preview.</summary>
    private static readonly string[] DiagramLanguages = ["mermaid"];

    private readonly MarkdownPipeline _pipeline;
    private readonly ILogger<MarkdigMarkdownRenderer> _logger;

    public MarkdigMarkdownRenderer(ILogger<MarkdigMarkdownRenderer> logger)
    {
        _logger = logger;

        _pipeline = new MarkdownPipelineBuilder()
            // Tables, footnotes, task lists, definition lists, figures, math, auto-links,
            // custom containers and the diagram blocks that carry mermaid.
            .UseAdvancedExtensions()
            // GitHub-compatible anchors so links like #my-heading behave as users expect.
            .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
            // Front matter is metadata, not content: parse it so it is not rendered as a table.
            .UseYamlFrontMatter()
            .UseEmojiAndSmiley()
            .Use<SourceLineExtension>()
            .Build();
    }

    public RenderedMarkdown Render(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        if (markdown.Length == 0)
        {
            return RenderedMarkdown.Empty;
        }

        long startedAt = Stopwatch.GetTimestamp();

        try
        {
            // Parse and render in two steps rather than calling Markdown.ToHtml, because the
            // document is needed for the outline and diagram detection.
            MarkdigDocument document = Markdig.Markdown.Parse(markdown, _pipeline);

            using var writer = new StringWriter();
            var renderer = new Markdig.Renderers.HtmlRenderer(writer);
            _pipeline.Setup(renderer);
            renderer.Render(document);
            writer.Flush();

            RenderedMarkdown result = new()
            {
                Html = writer.ToString(),
                Outline = MarkdownHeadingReader.ReadOutline(document),
                Links = ReadLinks(document),
                Anchors = MarkdownAnchorReader.ReadAnchors(document),
                ContainsDiagrams = ContainsDiagram(document),
            };

            _logger.LogDebug(
                "Rendered {Characters} characters to {HtmlLength} bytes of HTML in {Elapsed}.",
                markdown.Length,
                result.Html.Length,
                Stopwatch.GetElapsedTime(startedAt));

            return result;
        }
        catch (Exception ex)
        {
            // A renderer crash must not take the window down; show the problem in the preview.
            _logger.LogError(ex, "Markdown rendering failed.");

            return new RenderedMarkdown
            {
                Html = BuildErrorHtml(ex),
                Outline = [],
            };
        }
    }

    /// <summary>
    /// Every link and image in the document, with its position.
    ///
    /// Markdig models both as LinkInline and tells them apart with IsImage, and the base
    /// MarkdownObject carries Line, Column and Span, so this needs nothing the parse has not
    /// already worked out. Riding along with the render is what keeps the analyzer from
    /// having to parse the document a second time on every keystroke.
    /// </summary>
    private static IReadOnlyList<LinkReference> ReadLinks(MarkdigDocument document) =>
        [.. document.Descendants<LinkInline>()
            .Where(link => !string.IsNullOrEmpty(link.Url))
            .Select(link => new LinkReference
            {
                Url = link.Url!,
                IsImage = link.IsImage,
                SourceLine = link.Line,
                SourceColumn = link.Column,
                Length = link.Span.Length,
            })];

    private static bool ContainsDiagram(MarkdigDocument document) =>
        document.Descendants<FencedCodeBlock>()
            .Any(block => block.Info is { Length: > 0 } info
                && DiagramLanguages.Contains(info, StringComparer.OrdinalIgnoreCase));

    private static string BuildErrorHtml(Exception ex)
    {
        string message = System.Net.WebUtility.HtmlEncode(ex.Message);

        return $"""
            <div class="mq-render-error" role="alert">
              <strong>This document could not be rendered.</strong>
              <p>{message}</p>
            </div>
            """;
    }
}
