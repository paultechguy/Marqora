// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// Everything the analyzer needs to check one document.
///
/// <see cref="Links"/> and <see cref="Outline"/> come from the render that has just
/// happened, so checking a document costs a walk over results that already exist rather than
/// a second parse on every keystroke.
/// </summary>
public sealed record AnalysisRequest
{
    public required string Text { get; init; }

    /// <summary>
    /// Where the document lives, which is what relative links resolve against. Null for a
    /// document that has never been saved, and link checking is skipped for those: there is
    /// no folder for "./notes.md" to be relative to.
    /// </summary>
    public string? DocumentPath { get; init; }

    public IReadOnlyList<LinkReference> Links { get; init; } = [];

    /// <summary>Headings, for resolving in-document anchors.</summary>
    public IReadOnlyList<OutlineHeading> Outline { get; init; } = [];

    /// <summary>
    /// Anchor ids the author wrote as raw HTML, which resolve in-document just as heading
    /// anchors do and are the other half of what a "#" link can be pointing at.
    /// </summary>
    public IReadOnlyList<string> Anchors { get; init; } = [];
}
