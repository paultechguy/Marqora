// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Folders;

/// <summary>What a look below a folder found.</summary>
public enum ProbeResult
{
    /// <summary>Not looked at yet. The folder is not shown until the answer is known.</summary>
    Pending,

    /// <summary>A markdown file or an image is somewhere below it.</summary>
    Leads,

    /// <summary>Nothing listed is below it. The folder is hidden.</summary>
    Empty,

    /// <summary>
    /// The look stopped at the ceiling or could not read the folder. The folder is shown,
    /// unchecked: hiding a folder that might hold documents is the worse mistake.
    /// </summary>
    Unknown,
}

/// <summary>
/// Answers "does this folder lead to markdown or images?" by looking below it, breadth first,
/// stopping at the first one found.
///
/// The look is bounded so a home folder or a whole drive stays responsive (P18): at most
/// <see cref="Ceiling"/> entries per look, at most <see cref="MaxConcurrent"/> looks at once, and
/// every look canceled when the root changes. Always-hidden folders and junctions are never
/// entered; a junction back up the tree would make the look endless.
/// </summary>
public sealed class FolderProbe : IDisposable
{
    public const int DefaultCeiling = 20_000;

    public const int MaxConcurrent = 2;

    private readonly SemaphoreSlim _slots = new(MaxConcurrent, MaxConcurrent);

    public FolderProbe(int ceiling = DefaultCeiling)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ceiling);
        Ceiling = ceiling;
    }

    /// <summary>The most entries one look reads before giving up with <see cref="ProbeResult.Unknown"/>.</summary>
    public int Ceiling { get; }

    /// <summary>Looks below <paramref name="folder"/> on a pool thread, waiting for a free slot first.</summary>
    public async Task<ProbeResult> ProbeAsync(string folder, CancellationToken cancellationToken)
    {
        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await Task.Run(() => Look(folder, Ceiling, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _slots.Release();
        }
    }

    /// <summary>The look itself, on the calling thread. Throws when canceled.</summary>
    public static ProbeResult Look(string folder, int ceiling, CancellationToken cancellationToken)
    {
        var queue = new Queue<string>();
        queue.Enqueue(folder);
        int seen = 0;
        bool incomplete = false;

        while (queue.TryDequeue(out string? current))
        {
            IEnumerable<FileSystemInfo> entries;

            try
            {
                entries = new DirectoryInfo(current).EnumerateFileSystemInfos("*", FolderListing.Enumeration);
            }
            catch (Exception ex) when (FolderListing.IsReadFailure(ex))
            {
                incomplete = true;
                continue;
            }

            try
            {
                foreach (FileSystemInfo info in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (++seen > ceiling)
                    {
                        return ProbeResult.Unknown;
                    }

                    if (info is DirectoryInfo dir)
                    {
                        if (!FolderListing.IsAlwaysHiddenFolder(dir.Name)
                            && !dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        {
                            queue.Enqueue(dir.FullName);
                        }
                    }
                    else if (FolderListing.KindOfFile(info.Name) is FolderEntryKind.Markdown or FolderEntryKind.Image
                        && !info.Name.StartsWith('.'))
                    {
                        return ProbeResult.Leads;
                    }
                }
            }
            catch (Exception ex) when (FolderListing.IsReadFailure(ex))
            {
                incomplete = true;
            }
        }

        // The folder that was asked about could not be read at all: say "unknown", so it is shown.
        return incomplete ? ProbeResult.Unknown : ProbeResult.Empty;
    }

    public void Dispose() => _slots.Dispose();
}
