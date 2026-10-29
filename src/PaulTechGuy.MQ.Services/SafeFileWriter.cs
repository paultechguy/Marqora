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

    /// <summary>The waits, in milliseconds, before each further try at swapping a file in (<see cref="SwapInAsync"/>).</summary>
    private static readonly int[] SwapRetries = [50, 100, 200, 400];

    /// <summary>Text, written as UTF-8 without a byte order mark.</summary>
    /// <param name="scratchRoot">Where scratch folders go; the temp folder's own unless a test says otherwise.</param>
    public static Task WriteAsync(
        string path,
        string contents,
        ILogger logger,
        string? scratchRoot = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contents);

        return WriteAsync(path, Utf8NoBom.GetBytes(contents), logger, scratchRoot, cancellationToken);
    }

    /// <summary>
    /// Bytes, exactly as given. For a writer that has to keep what a file already was - its byte
    /// order mark, its line endings - which text through a fixed encoding cannot promise.
    /// </summary>
    /// <param name="scratchRoot">Where scratch folders go; the temp folder's own unless a test says otherwise.</param>
    public static async Task WriteAsync(
        string path,
        byte[] contents,
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

            await File.WriteAllBytesAsync(temporary, contents, cancellationToken).ConfigureAwait(false);
            await SwapInAsync(temporary, full, logger, cancellationToken).ConfigureAwait(false);

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

    private static async Task WriteBesideAsync(string full, byte[] contents, ILogger logger, CancellationToken cancellationToken)
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

                await stream.WriteAsync(contents, cancellationToken).ConfigureAwait(false);
            }

            await SwapInAsync(temporary, full, logger, cancellationToken).ConfigureAwait(false);
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
    ///
    /// Tried again, briefly, when Windows says the file is in use. A virus scanner or the search
    /// indexer opens a file just written to look at it, and while it does, Replace fails with
    /// "Unable to remove the file to be replaced": about one write in a hundred, when the same
    /// file was written a moment before, as a script running two commands over it does. The
    /// waits total 750 ms, so a file that really cannot be written still fails at once, as far as
    /// anyone waiting can tell, with the error it gave.
    /// </summary>
    private static async Task SwapInAsync(string temporary, string full, ILogger logger, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (File.Exists(full))
                {
                    File.Replace(temporary, full, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(temporary, full);
                }

                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < SwapRetries.Length)
            {
                logger.LogDebug(ex, "{Path} was in use; trying again in {Delay} ms.", full, SwapRetries[attempt]);
                await Task.Delay(SwapRetries[attempt], cancellationToken).ConfigureAwait(false);
            }
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
