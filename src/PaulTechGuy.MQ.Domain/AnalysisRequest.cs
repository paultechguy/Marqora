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

    /// <summary>
    /// Whether to report images with no alt text.
    ///
    /// A request field rather than something the analyzer reads for itself, because the analyzer
    /// has no settings and should not grow any: it is handed a document and says what is wrong
    /// with it. Defaulted true so a caller that has not heard of the rule still gets it, which
    /// matches how it ships.
    /// </summary>
    public bool CheckImageAltText { get; init; } = true;

    /// <summary>
    /// Whether to report pictures that will not appear: the ones addressed on the web, and the
    /// ones kept somewhere else on the machine.
    ///
    /// Its own flag rather than riding with the rest, because these arrive in a different
    /// quantity. A README carrying a row of build badges has eight of them on one line and
    /// nothing wrong with it, and somebody who works on such documents all day should be able to
    /// quiet this without also losing the dead links and the broken anchors.
    ///
    /// Defaulted true for the same reason the others are: it reports something no other surface
    /// in the app explains.
    /// </summary>
    public bool CheckBlockedImages { get; init; } = true;
}
