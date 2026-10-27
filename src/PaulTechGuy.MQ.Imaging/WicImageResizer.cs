// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Imaging;

/// <summary>
/// <see cref="IImageResizer"/> through Windows Imaging: what ClipboardImage.ResizeForFileAsync did
/// for a picked file, now reachable from the Folio's shrinker wherever it runs.
/// </summary>
public sealed class WicImageResizer(ILogger<WicImageResizer> logger) : IImageResizer
{
    public Task<byte[]?> ResizeForFileAsync(byte[] bytes, int maxWidth, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        cancellationToken.ThrowIfCancellationRequested();

        return WicImaging.EncodeAsync(bytes, maxWidth, ClipboardImageTiers.MustReencode(ClipboardImageTier.Files), logger);
    }
}
