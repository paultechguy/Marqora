// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// A find, and what to put in place of what it finds.
///
/// Wraps <see cref="FindQuery"/> rather than folding a replacement into it, so a plain search
/// never carries a field that means nothing to it.
/// </summary>
public sealed record ReplaceQuery
{
    public required FindQuery Find { get; init; }

    /// <summary>
    /// What each match becomes.
    ///
    /// In regular-expression mode this is a .NET replacement pattern - $1 and $2 for numbered
    /// groups, ${name} for a named one, $&amp; for the whole match, $$ for a literal dollar -
    /// resolved by Match.Result.
    ///
    /// In literal mode it is inserted exactly as typed. A dollar sign has no special meaning
    /// there, because there is no pattern for it to refer back to. That asymmetry is the one
    /// place the two modes part company, and it is why the help beside the box says which mode
    /// it is talking about.
    /// </summary>
    public required string Replacement { get; init; }
}

/// <summary>
/// One document's replacement: its whole new text, and how many matches went into it.
/// </summary>
/// <param name="OriginalText">
/// What the document held when it was scanned.
///
/// The version stamp, checked again before the rewrite is applied. Documents are immutable
/// records, so an edit allocates a new string and reference equality answers "has this moved
/// under us?" exactly, for nothing. Without it a rewrite computed before a confirmation was
/// answered would overwrite whatever was typed while it was up.
/// </param>
public sealed record ReplaceDocumentResult(
    Guid DocumentId,
    string Name,
    string Path,
    string OriginalText,
    string NewText,
    int Count);

/// <summary>
/// What one Replace All produced, in the shape <see cref="FindResults"/> uses: documents with
/// nothing to change are left out, and <see cref="Error"/> arrives instead of results rather
/// than alongside them.
/// </summary>
public sealed record ReplaceResults
{
    public required ReplaceQuery Query { get; init; }

    public IReadOnlyList<ReplaceDocumentResult> Documents { get; init; } = [];

    public int TotalMatches { get; init; }

    /// <summary>True when the scan stopped at its ceiling with text still unexamined.</summary>
    public bool Truncated { get; init; }

    /// <summary>Why there is nothing to replace, when the reason is worth showing.</summary>
    public string? Error { get; init; }

    public bool IsEmpty => TotalMatches == 0;

    /// <summary>A scan that ran and found nothing to change.</summary>
    public static ReplaceResults None(ReplaceQuery query) => new() { Query = query };

    /// <summary>A scan that could not run at all.</summary>
    public static ReplaceResults Failed(ReplaceQuery query, string error) =>
        new() { Query = query, Error = error };
}
