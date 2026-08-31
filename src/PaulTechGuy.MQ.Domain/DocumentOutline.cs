// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>A heading discovered while rendering, used for the outline flyout and jump-to.</summary>
public sealed record OutlineHeading
{
    public required int Level { get; init; }

    public required string Text { get; init; }

    /// <summary>Anchor id emitted into the preview HTML.</summary>
    public required string Slug { get; init; }

    /// <summary>Zero-based line in the markdown source that produced this heading.</summary>
    public required int SourceLine { get; init; }
}

/// <summary>
/// A link or image found while rendering, with where it came from in the source.
///
/// Collected during the same parse that produces the HTML, so checking a document's links
/// costs no extra work beyond walking a tree that already exists.
/// </summary>
public sealed record LinkReference
{
    /// <summary>What the link points at, exactly as written.</summary>
    public required string Url { get; init; }

    /// <summary>True for an image, false for a link. Markdig models both the same way.</summary>
    public required bool IsImage { get; init; }

    /// <summary>Zero-based line in the markdown source.</summary>
    public required int SourceLine { get; init; }

    /// <summary>Zero-based column in the markdown source.</summary>
    public required int SourceColumn { get; init; }

    /// <summary>How many characters the whole link occupies, for underlining it.</summary>
    public required int Length { get; init; }
}

/// <summary>The result of rendering markdown to a preview fragment.</summary>
public sealed record RenderedMarkdown
{
    public required string Html { get; init; }

    public required IReadOnlyList<OutlineHeading> Outline { get; init; }

    /// <summary>Every link and image in the document, for the analyzer to check.</summary>
    public IReadOnlyList<LinkReference> Links { get; init; } = [];

    /// <summary>
    /// Anchor ids written by hand as raw HTML, as in &lt;a id="notes"&gt;&lt;/a&gt;.
    ///
    /// Headings carry anchors of their own and are already in <see cref="Outline"/>. These
    /// are the ones an author placed deliberately, usually to give a paragraph or a glossary
    /// entry a target of its own, and nothing else in the document records them.
    /// </summary>
    public IReadOnlyList<string> Anchors { get; init; } = [];

    /// <summary>True when the source contains at least one mermaid block, so the shell can lazy-load mermaid.</summary>
    public bool ContainsDiagrams { get; init; }

    public static RenderedMarkdown Empty { get; } = new() { Html = string.Empty, Outline = [] };
}
