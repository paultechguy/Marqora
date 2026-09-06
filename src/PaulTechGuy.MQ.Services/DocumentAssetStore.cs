// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Services;

/// <summary>
/// Writes pasted and picked images into the folder the document references them from.
///
/// Every decision about the name is <see cref="DocumentAssets"/>'s and every decision about
/// whether it is an image at all is <see cref="ImageFileTypes"/>'s, both of which are pure. What
/// is left here is the part that has to touch a disk: making the folder, not overwriting
/// anything, and refusing to write outside the folder it was asked for.
/// </summary>
public sealed class DocumentAssetStore(ILogger<DocumentAssetStore> logger) : IDocumentAssetStore
{
    /// <summary>
    /// How many times a name is allowed to collide before the attempt is abandoned.
    ///
    /// Reached only if something outside the app is creating files with the very names being
    /// minted, faster than they can be used. Bounded so a loop cannot spin forever on a folder
    /// that is fighting back.
    /// </summary>
    private const int MaximumAttempts = 32;

    public async Task<string?> SaveAsync(
        string documentPath,
        ReadOnlyMemory<byte> bytes,
        string? suggestedName,
        ImageFolderMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);

        // The bytes decide the extension, never the name it arrived with. This is the line a
        // renamed executable does not get to cross.
        if (ImageFileTypes.ExtensionFor(bytes.Span) is not { } extension)
        {
            logger.LogWarning(
                "Refused {Name}: the bytes are not a recognized image.",
                suggestedName ?? "a pasted image");

            return null;
        }

        if (Path.GetDirectoryName(Path.GetFullPath(documentPath)) is not { } documentFolder)
        {
            return null;
        }

        string folderName = DocumentAssets.FolderNameFor(documentPath, mode);
        string target = folderName.Length == 0
            ? documentFolder
            : Path.Combine(documentFolder, folderName);

        // The slug should make this unreachable. It is asserted anyway, because the cost of
        // being wrong is a file written somewhere the user did not agree to.
        string root = Path.GetFullPath(documentFolder + Path.DirectorySeparatorChar);

        if (!(Path.GetFullPath(target) + Path.DirectorySeparatorChar)
            .StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError("Refused to write outside {Folder}.", documentFolder);

            return null;
        }

        Directory.CreateDirectory(target);

        string stem = suggestedName is { Length: > 0 }
            ? DocumentAssets.Slug(Path.GetFileNameWithoutExtension(suggestedName))
            : "image";

        // Listed once and then adjusted in the loop, so a folder of several hundred images is
        // enumerated once per paste rather than once per collision.
        HashSet<string> taken = new(
            Directory.EnumerateFiles(target).Select(Path.GetFileName).OfType<string>(),
            StringComparer.OrdinalIgnoreCase);

        for (int attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string name = DocumentAssets.NextFreeName(stem, extension, taken);

            try
            {
                // CreateNew rather than the temp-file-and-replace pattern used elsewhere in the
                // app. That one makes an overwrite atomic; this must never overwrite at all, and
                // CreateNew is the only thing that fails rather than clobbering when the name is
                // taken between the listing above and this line - two windows editing sibling
                // documents in one folder, or one window pasting twice quickly.
                await using (var stream = new FileStream(
                    Path.Combine(target, name),
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                {
                    await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                return DocumentAssets.RelativeReference(folderName, name);
            }
            catch (IOException) when (File.Exists(Path.Combine(target, name)))
            {
                // Somebody else got that name in the moment between listing and writing. Take it
                // out of contention and mint the next one.
                taken.Add(name);
            }
        }

        logger.LogError("Gave up naming an image in {Folder} after {Attempts} tries.", target, MaximumAttempts);

        return null;
    }
}
