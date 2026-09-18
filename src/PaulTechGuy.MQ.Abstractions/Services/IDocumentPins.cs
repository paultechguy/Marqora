// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Abstractions.Services;

/// <summary>
/// Which documents the user has pinned to the left of the tab strip, in memory, keyed by where
/// they live.
///
/// A pin holds a tab's place. It moves the document to the front of the strip, takes its close
/// button away, and keeps it out of the three bulk close commands - but it never refuses an edit
/// and never touches the file. That is the whole of the difference from
/// <see cref="IDocumentLocks"/>, which guards a file and does not care where its tab sits.
///
/// Keyed by path for the same reason the marks are: a pin has to outlive the session that made
/// it, and a document's <c>Id</c> is minted fresh on every open. Unlike a mark, a pin *does*
/// travel through Save As - the file is being renamed, not replaced, and the tab has not moved.
///
/// An untitled document cannot be pinned. There is no path to remember it by.
/// </summary>
public interface IDocumentPins
{
    /// <summary>
    /// Reads the stored pins, dropping any whose file is gone. Call once, before the first
    /// document is opened - a document opened before this has run comes up unpinned and lands in
    /// the wrong place in the strip.
    /// </summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// How many pins were dropped by <see cref="LoadAsync"/> because their file no longer
    /// exists.
    ///
    /// Logged rather than announced, which is where this parts company with
    /// <see cref="IDocumentLocks.DroppedOnLoad"/>: a guard that has stopped guarding is worth
    /// interrupting somebody for, and a document that has lost its place at the front of the
    /// strip is not.
    /// </summary>
    int DroppedOnLoad { get; }

    /// <summary>Whether this path is pinned. False for a null or empty path.</summary>
    bool IsPinned(string? path);

    /// <summary>Pins or unpins a path, and writes the change out.</summary>
    Task SetAsync(string path, bool pinned, CancellationToken cancellationToken = default);
}
