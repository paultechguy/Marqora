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

    /// <summary>
    /// The section number shown beside the heading, as "1.2.1", or empty when numbering is
    /// off or the heading sits above the level the count starts at.
    ///
    /// Kept apart from <see cref="Text"/> rather than folded into it. The outline panel
    /// dims the number and leaves the words alone, the filter box searches the words only,
    /// and neither is possible once the two are one string.
    ///
    /// Not required, and defaulted, so every existing construction site still compiles.
    /// </summary>
    public string Number { get; init; } = string.Empty;

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

    /// <summary>
    /// The label between the brackets - an image's alt text. Empty when there is none.
    ///
    /// Not required, and defaulted, so every existing construction site still compiles. Note
    /// this is the label's <em>text</em>: Markdig's own LinkInline.Label is the reference name in
    /// "![alt][ref]", which is a different thing and the wrong property to read.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Whether this image sits inside a link, as a badge does: "[![](badge.svg)](https://ci)".
    ///
    /// The surrounding link carries the accessible name in that arrangement, so the image is
    /// decorative and an empty alt text is correct rather than a defect. Without this, a README
    /// full of shields would light up on every line, which is the fastest way to teach someone
    /// to switch a check off.
    /// </summary>
    public bool IsInsideLink { get; init; }
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
