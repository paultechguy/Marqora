// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Abstractions.Services;

/// <summary>
/// Which documents the user has marked read-only, in memory, keyed by where they live.
///
/// The mark is Marqora's own and never touches the file: nothing here reads or writes the
/// read-only attribute Windows keeps. A marked document is one this app refuses to write, not
/// one the operating system would.
///
/// Keyed by path because the mark has to outlive the session that made it, and a document's
/// <c>Id</c> is minted fresh on every open. The cost is real and is not hidden: rename or move
/// a file outside Marqora and its mark is left behind, because there is nothing left to match.
/// </summary>
public interface IDocumentLocks
{
    /// <summary>
    /// Reads the stored marks, dropping any whose file is gone. Call once, before the first
    /// document is opened.
    /// </summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// How many marks were dropped by <see cref="LoadAsync"/> because their file no longer
    /// exists, so the app can say so rather than letting a guard lapse in silence.
    /// </summary>
    int DroppedOnLoad { get; }

    /// <summary>Whether this path carries a mark. False for a null or empty path.</summary>
    bool IsLocked(string? path);

    /// <summary>Marks or unmarks a path, and writes the change out.</summary>
    Task SetAsync(string path, bool locked, CancellationToken cancellationToken = default);
}
