// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// Reduces the pictures that are wider than the author asked for, and hands back a plan pointing
/// at the smaller copies.
///
/// Rewriting the plan rather than the writers is what keeps this to one place. The zip writer,
/// the folder writer and the page's image embedding all take their bytes from
/// <see cref="FolioAsset.SourcePath"/>, so pointing that at a reduced copy means none of them
/// need to know shrinking exists - and the round trip stays consistent by construction, because
/// what the page embeds and what the sources describe are once again the same bytes.
///
/// The copies live in a scratch folder the caller owns and deletes. Nothing is written back over
/// the author's own images; a share must not edit what it is sharing.
/// </summary>
public sealed class FolioShrinker(ILogger<FolioShrinker> logger)
{
    /// <summary>
    /// The plan with oversized images repointed at reduced copies under
    /// <paramref name="scratch"/>, or the plan unchanged when there is nothing to do.
    /// </summary>
    public async Task<FolioPlan> ShrinkAsync(
        FolioPlan plan,
        int maxWidth,
        string scratch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(scratch);

        if (maxWidth <= 0)
        {
            return plan;
        }

        List<FolioAsset> reduced = [];
        bool changed = false;

        foreach (FolioAsset asset in plan.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Zero is "size unknown", which is not a licence to re-encode something blind.
            if (asset.PixelWidth == 0 || asset.PixelWidth <= maxWidth)
            {
                reduced.Add(asset);
                continue;
            }

            FolioAsset result = await ReduceAsync(asset, maxWidth, scratch, cancellationToken)
                .ConfigureAwait(false);

            changed |= !ReferenceEquals(result, asset);

            reduced.Add(result);
        }

        return changed ? plan with { Assets = reduced } : plan;
    }

    private async Task<FolioAsset> ReduceAsync(
        FolioAsset asset,
        int maxWidth,
        string scratch,
        CancellationToken cancellationToken)
    {
        try
        {
            byte[] original = await File.ReadAllBytesAsync(asset.SourcePath, cancellationToken)
                .ConfigureAwait(false);

            byte[]? smaller = await ClipboardImage.ResizeForFileAsync(original, maxWidth, logger)
                .ConfigureAwait(false);

            /*
                Two ways this declines, and both hand the original back.

                Null is an image that would not decode - an SVG, something exotic - and there is
                nothing to reduce. The size test is the one that earns its place: the encoder
                writes PNG, so a photograph already stored as JPEG can come back *larger* at half
                the width, and a "shrink" that grows the file is not one worth having. Screenshots,
                which are what this feature is really for, go the other way emphatically.
            */
            if (smaller is null || smaller.LongLength >= asset.Bytes)
            {
                logger.LogInformation(
                    "Left {Path} at its original size; re-encoding it did not make it smaller.",
                    asset.SourcePath);

                return asset;
            }

            Directory.CreateDirectory(scratch);

            // Named by where it is going, flattened, so two images with the same file name in
            // different folders cannot collide in here.
            string copy = Path.Combine(
                scratch,
                asset.EntryName.Replace('/', '-').Replace('\\', '-') + ".png");

            await File.WriteAllBytesAsync(copy, smaller, cancellationToken).ConfigureAwait(false);

            return asset with
            {
                SourcePath = copy,
                Bytes = smaller.LongLength,
                PixelWidth = (uint)maxWidth,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not reduce {Path}; sharing it at its original size.", asset.SourcePath);

            return asset;
        }
    }
}
