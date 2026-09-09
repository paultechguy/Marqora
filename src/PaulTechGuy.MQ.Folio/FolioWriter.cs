// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Folio;

/// <summary>
/// Puts a planned Folio on disk, as a folder or as one zip.
///
/// All the deciding happened in <see cref="FolioPlanner"/>; what is left here is writing, which
/// is why this is the only part that can fail for a reason the author cannot see coming - a full
/// disk, a file locked by something else. Nothing is written until the whole plan has been made,
/// so a failure leaves an incomplete folder rather than a half-rewritten document.
///
/// Documents are written UTF-8 without a byte-order mark, and their text is written exactly as
/// planned: line endings are the author's business and a Folio is not the place to have an
/// opinion about them.
/// </summary>
public sealed class FolioWriter(ILogger<FolioWriter> logger)
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Writes the Folio as a folder of files.
    ///
    /// The folder has to be missing or empty. Unpacking into somewhere that already holds work is
    /// how a share quietly eats a document, and the asset store next door takes the same line with
    /// <c>FileMode.CreateNew</c>.
    /// </summary>
    public async Task WriteFolderAsync(
        FolioPlan plan,
        FolioManifest manifest,
        string targetFolder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFolder);

        string root = Path.GetFullPath(targetFolder);

        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new IOException($"{root} already has something in it.");
        }

        Directory.CreateDirectory(root);

        foreach (FolioDocumentPlan document in plan.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string path = Destination(root, document.EntryName);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, document.Text, Utf8NoBom, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (FolioAsset asset in plan.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string path = Destination(root, asset.EntryName);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            using FileStream source = File.OpenRead(asset.SourcePath);
            using FileStream target = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);

            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }

        await File.WriteAllTextAsync(
            Path.Combine(root, FolioManifest.FileName),
            Serialize(manifest),
            Utf8NoBom,
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Wrote a Folio of {Documents} document(s) and {Assets} image(s) to {Path}.",
            plan.Documents.Count,
            plan.Assets.Count,
            root);
    }

    /// <summary>
    /// Writes the Folio as one zip.
    ///
    /// Overwriting is the save dialog's business - it has already asked - so unlike the folder
    /// form this replaces what is there.
    /// </summary>
    public async Task WriteZipAsync(
        FolioPlan plan,
        FolioManifest manifest,
        string zipPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);

        using FileStream file = new(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using ZipArchive archive = new(file, ZipArchiveMode.Create);

        foreach (FolioDocumentPlan document in plan.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ZipArchiveEntry entry = archive.CreateEntry(document.EntryName, CompressionLevel.Optimal);

            await using Stream stream = entry.Open();
            await using StreamWriter writer = new(stream, Utf8NoBom);

            await writer.WriteAsync(document.Text.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        foreach (FolioAsset asset in plan.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Images arrive compressed already. Asking for Optimal spends real time re-deflating
            // a PNG to save a fraction of a percent, and a Folio is mostly images by weight.
            ZipArchiveEntry entry = archive.CreateEntry(asset.EntryName, CompressionLevel.Fastest);

            await using Stream target = entry.Open();
            using FileStream source = File.OpenRead(asset.SourcePath);

            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }

        ZipArchiveEntry manifestEntry =
            archive.CreateEntry(FolioManifest.FileName, CompressionLevel.Optimal);

        await using (Stream stream = manifestEntry.Open())
        await using (StreamWriter writer = new(stream, Utf8NoBom))
        {
            await writer.WriteAsync(Serialize(manifest).AsMemory(), cancellationToken)
                .ConfigureAwait(false);
        }

        logger.LogInformation(
            "Wrote a Folio of {Documents} document(s) and {Assets} image(s) to {Path}.",
            plan.Documents.Count,
            plan.Assets.Count,
            zipPath);
    }

    private static string Serialize(FolioManifest manifest) =>
        JsonSerializer.Serialize(manifest, FolioJsonContext.Default.FolioManifest);

    /// <summary>
    /// Where an entry lands, refusing anything that would climb out of the Folio.
    ///
    /// Every entry name here was minted by the planner, so this cannot currently fire. It is kept
    /// because the same names come back the other way when a Folio is unpacked, and that side
    /// reads a file somebody emailed - one shared rule beats two, one of which is load-bearing.
    /// </summary>
    private static string Destination(string root, string entryName)
    {
        return PathContainment.ResolveWithin(root, entryName)
            ?? throw new IOException($"\"{entryName}\" does not stay inside the Folio.");
    }
}
