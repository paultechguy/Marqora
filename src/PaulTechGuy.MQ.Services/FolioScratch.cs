// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace PaulTechGuy.MQ.Services;

/// <summary>
/// One share's temporary folder, held for as long as the share runs and removed when it ends.
///
/// It holds a <c>.lock</c> file open for its whole life, with no sharing. That is how the startup
/// sweep tells a folder something is still using - a Folio preflight can sit open overnight -
/// from one a stopped process left behind, rather than guessing from its age.
/// </summary>
public sealed class FolioScratchFolder : IDisposable
{
    private readonly FileStream _lock;
    private readonly ILogger _logger;

    internal FolioScratchFolder(string path, ILogger logger)
    {
        Path = path;
        _logger = logger;

        Directory.CreateDirectory(path);

        _lock = new FileStream(
            System.IO.Path.Combine(path, FolioScratch.LockName),
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
    }

    public string Path { get; }

    /// <summary>Lets go of the lock and removes the folder with everything in it. Safe to call twice.</summary>
    public void Dispose()
    {
        _lock.Dispose();

        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // What cannot go now is unlocked, and the next start's sweep takes it.
            _logger.LogDebug(ex, "Could not clear the Folio scratch folder {Path}.", Path);
        }
    }
}

/// <summary>
/// Where a Folio share keeps what it makes along the way - reduced copies of large pictures,
/// pictures fetched from the web when the author asked for that, and a review resumed from its
/// page set down as a stand-in - and the sweep that clears what a stopped process left there.
///
/// Every share removes its own folder when it ends, however it ends. What that cannot cover is a
/// process that stopped before it could: killed, crashed, or powered off. Those folders are
/// swept once at the next start.
/// </summary>
public static partial class FolioScratch
{
    internal const string LockName = ".lock";

    /// <summary>
    /// How old a folder with no lock at all must be before it is swept. Such a folder was made by
    /// a build from before the lock existed, which may be sharing right now in another window;
    /// no share takes an hour to write.
    /// </summary>
    public static readonly TimeSpan LocklessAge = TimeSpan.FromHours(1);

    /// <summary>The one place the scratch root is named.</summary>
    public static string Root => Path.Combine(Path.GetTempPath(), "marqora-folio");

    /// <summary>A new folder for one share, locked until it is disposed.</summary>
    public static FolioScratchFolder Create(ILogger logger, string? root = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        return new FolioScratchFolder(Path.Combine(root ?? Root, Guid.NewGuid().ToString("n")), logger);
    }

    /// <summary>
    /// Removes the folders under the root that nothing is using. Run once at startup, off the UI
    /// thread; never throws.
    ///
    /// Only folders named as <see cref="Create"/> names them are touched. Nothing that is a
    /// junction or a symbolic link is followed or removed - nor anything at all, if the root
    /// itself is one - so the sweep cannot be pointed somewhere else.
    /// </summary>
    /// <returns>How many folders were removed.</returns>
    public static int SweepStale(ILogger logger, string? root = null, DateTime? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        string folder = root ?? Root;
        DateTime now = nowUtc ?? DateTime.UtcNow;
        int removed = 0;

        try
        {
            var rootInfo = new DirectoryInfo(folder);

            if (!rootInfo.Exists || rootInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return 0;
            }

            foreach (DirectoryInfo entry in rootInfo.EnumerateDirectories())
            {
                if (!ScratchName().IsMatch(entry.Name) || entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                if (TryRemove(entry, now, logger))
                {
                    removed++;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Could not look through the Folio scratch folder {Path}.", folder);
        }

        if (removed > 0)
        {
            logger.LogInformation("Cleared {Count} Folio scratch folder(s) left by an earlier run.", removed);
        }

        return removed;
    }

    private static bool TryRemove(DirectoryInfo entry, DateTime now, ILogger logger)
    {
        try
        {
            string lockPath = Path.Combine(entry.FullName, LockName);

            if (File.Exists(lockPath))
            {
                // Held open with no sharing by a share still running, so deleting it fails;
                // closed, it belongs to nothing.
                File.Delete(lockPath);
            }
            else if (now - entry.LastWriteTimeUtc < LocklessAge)
            {
                return false;
            }

            entry.Delete(recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Left {Path}; something is still using it.", entry.FullName);
            return false;
        }
    }

    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex ScratchName();
}
