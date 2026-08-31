// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// One hit, addressed the way everything inside the app is: zero-based line and column, with
/// Monaco's one-based numbering applied at the bridge.
///
/// <see cref="LineText"/> is the whole line the match sits on, carried with the match rather
/// than looked up later. Results are a snapshot, and a row has to keep showing what was found
/// even after the document has moved on beneath it. Every match on one line shares a single
/// string, so a line is held once however many times it was hit.
/// </summary>
public readonly record struct FindMatch(int Line, int Column, int Length, string LineText)
{
    /// <summary>The matched text itself, sliced back out of the line it was found on.</summary>
    public string Text => LineText.Substring(Column, Length);
}

/// <summary>Every match in one document, in document order.</summary>
public sealed record FindDocumentMatches(
    Guid DocumentId,
    string Name,
    string Path,
    IReadOnlyList<FindMatch> Matches);

/// <summary>
/// What one Find All produced: matches grouped by document, in the order the documents were
/// searched.
///
/// Documents with nothing in them are left out, so the list is exactly what the window draws.
///
/// <see cref="Error"/> is the regular-expression engine's own complaint, passed through
/// rather than summarised, and it arrives instead of results rather than alongside them.
/// </summary>
public sealed record FindResults
{
    public required FindQuery Query { get; init; }

    public IReadOnlyList<FindDocumentMatches> Documents { get; init; } = [];

    public int TotalMatches { get; init; }

    /// <summary>True when the finder stopped at its ceiling with text still unsearched.</summary>
    public bool Truncated { get; init; }

    /// <summary>Why there are no results, when the reason is worth showing. Null otherwise.</summary>
    public string? Error { get; init; }

    public bool IsEmpty => TotalMatches == 0;

    /// <summary>A search that ran and found nothing.</summary>
    public static FindResults None(FindQuery query) => new() { Query = query };

    /// <summary>A search that could not run at all.</summary>
    public static FindResults Failed(FindQuery query, string error) =>
        new() { Query = query, Error = error };
}
