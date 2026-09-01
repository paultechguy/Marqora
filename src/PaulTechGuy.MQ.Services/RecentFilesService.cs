// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Repositories;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Services;

/// <summary>
/// Maintains the most-recently-used list: ordering, pinning, de-duplication and trimming.
///
/// Paths are compared case-insensitively and in their fully-qualified form, so opening a
/// file through a relative path or with different casing updates the existing entry rather
/// than creating a second one.
/// </summary>
public sealed class RecentFilesService(
    IRecentFilesRepository repository,
    ISettingsService settings,
    ILogger<RecentFilesService> logger)
    : IRecentFilesService
{

    private readonly Lock _sync = new();

    private List<RecentFile> _items = [];

    public IReadOnlyList<RecentFile> Items
    {
        get
        {
            lock (_sync)
            {
                return _items;
            }
        }
    }

    public event EventHandler? Changed;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RecentFile> loaded = await repository.LoadAsync(cancellationToken).ConfigureAwait(false);

        lock (_sync)
        {
            _items = Order([.. loaded]);
        }

        logger.LogInformation("Loaded {Count} recent files.", loaded.Count);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task AddAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);

        return MutateAsync(
            items =>
            {
                RecentFile? existing = Find(items, fullPath);

                // Preserve the pin when an already-pinned file is reopened.
                items.RemoveAll(item => Matches(item, fullPath));

                items.Insert(0, new RecentFile
                {
                    Path = fullPath,
                    LastOpenedUtc = DateTimeOffset.UtcNow,
                    IsPinned = existing?.IsPinned ?? false,
                });
            },
            cancellationToken);
    }

    public Task RemoveAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);

        return MutateAsync(items => items.RemoveAll(item => Matches(item, fullPath)), cancellationToken);
    }

    public Task TogglePinAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);

        return MutateAsync(
            items =>
            {
                int index = items.FindIndex(item => Matches(item, fullPath));

                if (index >= 0)
                {
                    items[index] = items[index] with { IsPinned = !items[index].IsPinned };
                }
            },
            cancellationToken);
    }

    public async Task ClearAsync(RecentClearScope scope, CancellationToken cancellationToken = default)
    {
        int removed = 0;

        await MutateAsync(
            items => removed = items.RemoveAll(item => scope is RecentClearScope.Everything || !item.IsPinned),
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Cleared {Count} recent entries, scope {Scope}.", removed, scope);
    }

    public async Task<int> PruneMissingAsync(CancellationToken cancellationToken = default)
    {
        int removed = 0;

        await MutateAsync(
            items => removed = items.RemoveAll(item => !File.Exists(item.Path)),
            cancellationToken).ConfigureAwait(false);

        if (removed > 0)
        {
            logger.LogInformation("Pruned {Count} recent entries whose files no longer exist.", removed);
        }

        return removed;
    }

    /// <summary>Applies a change under the lock, re-orders, trims, then persists.</summary>
    private async Task MutateAsync(Action<List<RecentFile>> mutate, CancellationToken cancellationToken)
    {
        List<RecentFile> snapshot;

        lock (_sync)
        {
            List<RecentFile> working = [.. _items];
            mutate(working);
            _items = Trim(Order(working));
            snapshot = _items;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        await repository.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }

    private static RecentFile? Find(List<RecentFile> items, string fullPath) =>
        items.FirstOrDefault(item => Matches(item, fullPath));

    private static bool Matches(RecentFile item, string fullPath) =>
        string.Equals(item.Path, fullPath, StringComparison.OrdinalIgnoreCase);

    private static List<RecentFile> Order(List<RecentFile> items) =>
        [.. items
            .OrderByDescending(item => item.IsPinned)
            .ThenByDescending(item => item.LastOpenedUtc)];

    /// <summary>
    /// Drops unpinned entries past the user's chosen limit. A pinned entry always survives:
    /// pinning it is a statement that it should not age out, and a limit that discarded it
    /// would make pinning meaningless.
    ///
    /// The limit is read at each trim rather than captured once, so lowering it in
    /// preferences takes effect on the next file opened rather than at the next launch. It
    /// is clamped because the settings file is a text file a user can edit by hand.
    /// </summary>
    private List<RecentFile> Trim(List<RecentFile> items)
    {
        int limit = Math.Clamp(
            settings.Current.RecentFilesLimit,
            AppSettings.MinimumRecentFilesLimit,
            AppSettings.MaximumRecentFilesLimit);

        List<RecentFile> pinned = [.. items.Where(item => item.IsPinned)];
        List<RecentFile> unpinned = [.. items.Where(item => !item.IsPinned).Take(limit)];

        return [.. pinned, .. unpinned];
    }
}
