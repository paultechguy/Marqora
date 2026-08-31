// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Abstractions.Services;

/// <summary>How much of the recent list a clear removes.</summary>
public enum RecentClearScope
{
    /// <summary>
    /// Everything except pinned entries. Pinning is how a user marks a file as worth keeping,
    /// so it survives the ordinary clear.
    /// </summary>
    Unpinned,

    /// <summary>Every entry, pins included.</summary>
    Everything,
}

/// <summary>Maintains the MRU list: ordering, pinning, trimming and pruning of deleted files.</summary>
public interface IRecentFilesService
{
    /// <summary>Pinned entries first, then most-recent first.</summary>
    IReadOnlyList<RecentFile> Items { get; }

    event EventHandler? Changed;

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task AddAsync(string path, CancellationToken cancellationToken = default);

    Task RemoveAsync(string path, CancellationToken cancellationToken = default);

    Task TogglePinAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Empties the list. The scope is explicit because pins are easy to lose by accident.</summary>
    Task ClearAsync(RecentClearScope scope, CancellationToken cancellationToken = default);

    /// <summary>Drops entries whose file no longer exists. Returns the number removed.</summary>
    Task<int> PruneMissingAsync(CancellationToken cancellationToken = default);
}
