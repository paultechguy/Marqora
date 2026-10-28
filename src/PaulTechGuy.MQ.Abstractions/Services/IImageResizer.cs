// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Abstractions.Services;

/// <summary>
/// Makes a picture no wider than a limit, for the Folio's "Shrink images wider than".
///
/// A contract rather than a call because the only implementation uses Windows Imaging, which a
/// net10.0 library cannot reach: the Folio's shrinking lives in such a library so that the app and
/// marqora-cli share it, and the Windows half is handed to it (docs/CLI-Implementation-Plan.md,
/// WP-M1-15, F7).
/// </summary>
public interface IImageResizer
{
    /// <summary>
    /// The picture at most <paramref name="maxWidth"/> pixels wide, as PNG, keeping its proportions;
    /// the bytes unchanged when it is already narrow enough; null when they will not decode, which
    /// the caller treats as "keep the original".
    /// </summary>
    Task<byte[]?> ResizeForFileAsync(byte[] bytes, int maxWidth, CancellationToken cancellationToken = default);
}
