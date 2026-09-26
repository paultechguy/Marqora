// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace PaulTechGuy.MQ.Services;

/// <summary>
/// Writes a file that must never be left half-written: the whole of it is written somewhere
/// else first and then swapped in, so a failure part way leaves the file that was there.
///
/// Its first use is a shared review page, which is the reviewer's save file - a review is resumed
/// from it - so overwriting it in place would risk the only copy.
///
/// The temporary copy is never left behind for long. The swap has to be a rename, which works
/// only within one drive, so where it is written depends on where the target is:
/// <list type="bullet">
///   <item>On the same drive as the temp folder - nearly always - it is written into a share's
///   locked scratch folder (<see cref="FolioScratch"/>). That folder is removed when the write
///   ends however it ends, and a crash part way is swept at the next share or the next start.</item>
///   <item>On another drive - a USB stick, a network share - it is written beside the target as a
///   hidden file, held open with no sharing while it is written. Anything like it that nothing
///   holds is cleared the next time that file is written or opened
///   (<see cref="ClearStaleTemporaries"/>).</item>
/// </list>
/// </summary>
public static partial class SafeFileWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <param name="scratchRoot">Where scratch folders go; the temp folder's own unless a test says otherwise.</param>
    public static async Task WriteAsync(
        string path,
        string contents,
        ILogger logger,
        string? scratchRoot = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(logger);

        string full = Path.GetFullPath(path);
        string root = scratchRoot ?? FolioScratch.Root;

        if (SameDrive(full, root))
        {
            using FolioScratchFolder scratch = FolioScratch.Create(logger, root);

            string temporary = Path.Combine(scratch.Path, Path.GetFileName(full));

            await File.WriteAllTextAsync(temporary, contents, Utf8NoBom, cancellationToken).ConfigureAwait(false);
            SwapIn(temporary, full);

            return;
        }

        await WriteBesideAsync(full, contents, logger, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes the hidden temporary copies beside <paramref name="path"/> that nothing is still
    /// writing - what a crash part way through a write to another drive leaves. One that is held
    /// open belongs to a write still running, and is left.
    /// </summary>
    /// <returns>How many were removed.</returns>
    public static int ClearStaleTemporaries(string path, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(logger);

        int removed = 0;

        try
        {
            string full = Path.GetFullPath(path);
            string? folder = Path.GetDirectoryName(full);
            string name = Path.GetFileName(full);

            if (folder is null || !Directory.Exists(folder))
            {
                return 0;
            }

            foreach (string candidate in Directory.EnumerateFiles(folder, $".{name}.*.tmp"))
            {
                if (!IsTemporaryFor(Path.GetFileName(candidate), name)
                    || File.GetAttributes(candidate).HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                try
                {
                    File.Delete(candidate);
                    removed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogDebug(ex, "Left {Path}; a write is still using it.", candidate);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            logger.LogDebug(ex, "Could not look for leftover temporary copies of {Path}.", path);
        }

        if (removed > 0)
        {
            logger.LogInformation("Cleared {Count} leftover temporary copy(ies) of {Path}.", removed, path);
        }

        return removed;
    }

    /// <summary>The name a temporary copy beside a file takes: hidden by its dot, and matched only by this.</summary>
    internal static string TemporaryNameFor(string fileName) =>
        $".{fileName}.{Guid.NewGuid():N}.tmp";

    public static bool IsTemporaryFor(string candidate, string fileName) =>
        candidate.Length == fileName.Length + 38
        && candidate.StartsWith("." + fileName + ".", StringComparison.OrdinalIgnoreCase)
        && TemporarySuffix().IsMatch(candidate[(fileName.Length + 2)..]);

    private static async Task WriteBesideAsync(string full, string contents, ILogger logger, CancellationToken cancellationToken)
    {
        string folder = Path.GetDirectoryName(full) ?? throw new IOException($"{full} has no folder.");
        string temporary = Path.Combine(folder, TemporaryNameFor(Path.GetFileName(full)));

        // Any copy a crash left here earlier goes first; this one is held while it is written.
        ClearStaleTemporaries(full, logger);

        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                File.SetAttributes(temporary, File.GetAttributes(temporary) | FileAttributes.Hidden);

                byte[] bytes = Utf8NoBom.GetBytes(contents);
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            }

            SwapIn(temporary, full);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Hidden and unlocked now; the next write or open of this file clears it.
                    logger.LogDebug(ex, "Could not remove the temporary copy {Path}.", temporary);
                }
            }
        }
    }

    /// <summary>
    /// Replace rather than delete-and-move keeps what the existing file had - its permissions and
    /// its creation time. Nothing is shown hidden that should not be: Replace carries the
    /// temporary file's attributes over on some file systems.
    /// </summary>
    private static void SwapIn(string temporary, string full)
    {
        if (File.Exists(full))
        {
            File.Replace(temporary, full, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temporary, full);
        }

        File.SetAttributes(full, File.GetAttributes(full) & ~FileAttributes.Hidden);
    }

    private static bool SameDrive(string full, string root)
    {
        try
        {
            string? target = Path.GetPathRoot(full);
            string? scratch = Path.GetPathRoot(Path.GetFullPath(root));

            return target is { Length: > 0 }
                && string.Equals(target, scratch, StringComparison.OrdinalIgnoreCase)
                && !target.StartsWith(@"\\", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    [GeneratedRegex("^[0-9a-f]{32}\\.tmp$", RegexOptions.CultureInvariant)]
    private static partial Regex TemporarySuffix();
}
