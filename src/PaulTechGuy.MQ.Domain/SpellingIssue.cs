// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// What is wrong with a run of text.
///
/// A kind rather than a boolean because not every engine can find both. The Windows spell
/// service reports a repeated word as an error whose corrective action is "delete"; a bundled
/// dictionary would only ever report a misspelling. An engine that cannot find repeats simply
/// never emits that kind, so the capability degrades on a swap rather than breaking.
/// </summary>
public enum SpellingIssueKind
{
    /// <summary>No dictionary knows this word.</summary>
    Misspelling = 0,

    /// <summary>The same word twice in a row — "the the".</summary>
    RepeatedWord = 1,
}

/// <summary>
/// One misspelling, and where it is.
///
/// Deliberately not a <see cref="Diagnostic"/>. That type carries a severity whose values map
/// onto Monaco's marker scale, which is a fact about the editor rather than about the document;
/// mapping happens once, in the app layer, so nothing below it needs to know a preview exists.
/// Keeping the analyzer's own output plain is also what lets a status bar count or an export
/// report consume it later without going through the marker pipeline.
///
/// Positions are zero-based, like everything else inside the app.
/// </summary>
public sealed record SpellingIssue
{
    public required int Line { get; init; }

    /// <summary>Column the word starts at.</summary>
    public required int Start { get; init; }

    public required int Length { get; init; }

    /// <summary>
    /// The word as it appears in the document. Carried rather than re-read from the line,
    /// because the caller offering suggestions has the issue and not the text.
    /// </summary>
    public required string Word { get; init; }

    public required SpellingIssueKind Kind { get; init; }
}

/// <summary>
/// A run of one line that an engine objected to, before anything has been filtered.
///
/// This is the engine's own currency: an offset into the string it was handed, with no idea
/// which line that string came from. <see cref="SpellingIssue"/> is what it becomes once the
/// analyzer has placed it in a document and decided it survives the skip rules.
/// </summary>
public readonly record struct SpellingRange(int Start, int Length, SpellingIssueKind Kind);
