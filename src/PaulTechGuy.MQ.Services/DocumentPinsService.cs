// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Repositories;
using PaulTechGuy.MQ.Abstractions.Services;

namespace PaulTechGuy.MQ.Services;

/// <summary>
/// The pinned documents, held as a set of normalized paths and written out as they change.
///
/// The same shape as <see cref="DocumentLocksService"/> down to the locking, and deliberately so:
/// both are a set of paths that has to survive a restart, and the two share
/// <see cref="DocumentPathKeys"/> for the only part of either that is subtle. Written straight
/// through rather than behind a debounce - a pin changes when somebody picks a menu item or
/// finishes a drag, not many times a second.
///
/// The lock is never held across an await - the set is copied inside it and the file is written
/// outside - so <see cref="IsPinned"/> can be answered from the UI thread without waiting on a
/// disk write. It is asked on every document open and on every pass that decides where a tab
/// sits, so it has to be cheap.
/// </summary>
public sealed class DocumentPinsService(
    IDocumentPinsRepository repository,
    ILogger<DocumentPinsService> logger) : IDocumentPins
{
    private readonly HashSet<string> _pinned = new(StringComparer.OrdinalIgnoreCase);
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
            _pinned.Clear();
            _pinned.UnionWith(kept);
            DroppedOnLoad = dropped;
        }

        if (dropped > 0)
        {
            // Logged and not announced, which is the one place this parts company with the
            // read-only marks. That announcement exists because a guard which has quietly
            // stopped guarding is worth interrupting somebody for. A pin that has lapsed costs
            // a tab its place at the front of the strip, and saying so on launch would be
            // charging a full-width status message for it.
            logger.LogInformation("Dropped {Count} pin(s) whose file no longer exists.", dropped);

            // Rewritten so the same entries are not weighed again on every launch.
            await repository.SaveAsync(kept, cancellationToken).ConfigureAwait(false);
        }
    }

    public bool IsPinned(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string key = DocumentPathKeys.Normalize(path);

        lock (_sync)
        {
            return _pinned.Contains(key);
        }
    }

    public async Task SetAsync(string path, bool pinned, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string key = DocumentPathKeys.Normalize(path);
        List<string> snapshot;

        lock (_sync)
        {
            bool changed = pinned ? _pinned.Add(key) : _pinned.Remove(key);

            if (!changed)
            {
                return;
            }

            snapshot = [.. _pinned];
        }

        await repository.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }
}
