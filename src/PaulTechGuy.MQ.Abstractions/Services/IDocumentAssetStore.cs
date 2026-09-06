// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Abstractions.Services;

/// <summary>
/// Writes images beside the document that references them.
///
/// The only part of image paste that touches a user's folder, which is why it is behind a seam:
/// everything about naming, collision and refusing what is not an image can then be tested
/// against a temp directory rather than by pasting into a real document.
/// </summary>
public interface IDocumentAssetStore
{
    /// <summary>
    /// Writes <paramref name="bytes"/> beside <paramref name="documentPath"/> and returns the
    /// reference to write into the markdown - relative, encoded, ready to insert.
    ///
    /// Refuses anything whose bytes are not a recognized image, whatever it arrived claiming to
    /// be. Never overwrites: a name already taken produces the next one rather than replacing
    /// what is there.
    /// </summary>
    /// <param name="suggestedName">
    /// What to call it, from the file it was copied from. Null for a bitmap off the clipboard,
    /// which has no name and gets a numbered one.
    /// </param>
    /// <returns>The relative reference, or null when the bytes were not an image.</returns>
    Task<string?> SaveAsync(
        string documentPath,
        ReadOnlyMemory<byte> bytes,
        string? suggestedName,
        ImageFolderMode mode,
        CancellationToken cancellationToken = default);
}
