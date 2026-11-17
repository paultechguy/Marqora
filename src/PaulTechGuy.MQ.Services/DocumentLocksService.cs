// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Repositories;
using PaulTechGuy.MQ.Abstractions.Services;

namespace PaulTechGuy.MQ.Services;

/// <summary>
/// The read-only marks, held as a set of normalized paths and written out as they change.
///
/// Written straight through rather than behind a debounce like the settings: a mark changes when
/// somebody picks a menu item, not many times a second, and the one thing worse than a slow write
/// here is a mark that was not saved.
///
/// The lock is never held across an await - the set is copied inside it and the file is written
/// outside - so <see cref="IsLocked"/> can be answered from the UI thread without waiting on a
/// disk write.
/// </summary>
public sealed class DocumentLocksService(
    IDocumentLocksRepository repository,
    ILogger<DocumentLocksService> logger) : IDocumentLocks
{
    private readonly HashSet<string> _locked = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _sync = new();

    public int DroppedOnLoad { get; private set; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> stored = await repository.LoadAsync(cancellationToken).ConfigureAwait(false);

        var kept = new List<string>(stored.Count);
        int dropped = 0;

        foreach (string path in stored)
        {
            if (DocumentPathKeys.HasBeenDeleted(path))
            {
                dropped++;
                continue;
            }

            kept.Add(DocumentPathKeys.Normalize(path));
        }

        lock (_sync)
        {
            _locked.Clear();
            _locked.UnionWith(kept);
            DroppedOnLoad = dropped;
        }

        if (dropped > 0)
        {
            logger.LogInformation("Dropped {Count} read-only mark(s) whose file no longer exists.", dropped);

            // Rewritten so the same entries are not weighed again on every launch.
            await repository.SaveAsync(kept, cancellationToken).ConfigureAwait(false);
        }
    }

    public bool IsLocked(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string key = DocumentPathKeys.Normalize(path);

        lock (_sync)
        {
            return _locked.Contains(key);
        }
    }

    public async Task SetAsync(string path, bool locked, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string key = DocumentPathKeys.Normalize(path);
        List<string> snapshot;

        lock (_sync)
        {
            bool changed = locked ? _locked.Add(key) : _locked.Remove(key);

            if (!changed)
            {
                return;
            }

            snapshot = [.. _locked];
        }

        await repository.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }

    public PreparedMove PrepareMove(string from)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);

        List<string> keys;

        lock (_sync)
        {
            keys = [.. _locked];
        }

        return PathKeyMoves.Prepare(from, keys);
    }

    public async Task CompleteMoveAsync(PreparedMove prepared, string destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        List<string> snapshot;

        lock (_sync)
        {
            if (!PathKeyMoves.Apply(_locked, prepared, destination))
            {
                return;
            }

            snapshot = [.. _locked];
        }

        try
        {
            await repository.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file has already moved, so the mark has to move with it in memory whatever
            // happens to the write; the next write of the set carries it.
            logger.LogWarning(ex, "Could not write the read-only marks after a rename to {Path}.", destination);
        }
    }

    public bool AnyUnder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return false;
        }

        List<string> keys;

        lock (_sync)
        {
            keys = [.. _locked];
        }

        return PathKeyMoves.AnyUnder(keys, folder);
    }
}
