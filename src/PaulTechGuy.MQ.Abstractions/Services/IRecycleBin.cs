// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Abstractions.Services;

/// <summary>What a delete is about to do, for the confirmation to say before it happens.</summary>
/// <param name="WillRecycle">
/// False when the shell would delete outright: a network drive, a USB stick, a bin set to delete
/// immediately, or more than the bin holds. The confirmation then says "permanently".
/// </param>
/// <param name="FileCount">Files inside a folder, or 1 for a file. For "and its 12 files".</param>
/// <param name="TotalBytes">How much is going, which the bin's size limit is weighed against.</param>
/// <param name="ReadOnlyCount">
/// Files carrying the read-only box from their Windows properties. It does not stop a delete,
/// in Explorer or here, so the confirmation names it instead: somebody ticked it for a reason.
/// Folders are not counted; Windows sets the attribute on folders for its own purposes.
/// </param>
public sealed record RecycleForecast(bool WillRecycle, int FileCount, long TotalBytes, int ReadOnlyCount = 0);

/// <summary>How a delete went.</summary>
/// <param name="Permanent">The item was deleted outright rather than recycled.</param>
/// <param name="Error">Why it failed, written for the status line. Null on success.</param>
public sealed record RecycleResult(bool Succeeded, bool Permanent, string? Error)
{
    public static RecycleResult Recycled { get; } = new(true, false, null);

    public static RecycleResult Failed(string error) => new(false, false, error);
}

/// <summary>
/// Sends files and folders to the Recycle Bin (C6: nothing in the app did before the folder
/// panel). Behind an interface so the workspace's delete can be tested without a shell.
/// </summary>
public interface IRecycleBin
{
    /// <summary>Whether a delete of this path would really be recycled, and how much it is.</summary>
    RecycleForecast Forecast(string path);

    /// <summary>Moves the path to the Recycle Bin, or deletes it if the shell will not recycle it.</summary>
    Task<RecycleResult> RecycleAsync(string path, CancellationToken cancellationToken = default);
}
