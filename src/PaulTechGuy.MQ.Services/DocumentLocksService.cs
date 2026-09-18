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
            if (HasBeenDeleted(path))
            {
                dropped++;
                continue;
            }

            kept.Add(Normalize(path));
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

        string key = Normalize(path);

        lock (_sync)
        {
            return _locked.Contains(key);
        }
    }

    public async Task SetAsync(string path, bool locked, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string key = Normalize(path);
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

    /// <summary>
    /// The one spelling of a path that every caller has to agree on.
    ///
    /// <see cref="Path.GetFullPath(string)"/> settles relative paths and casing, but it resolves
    /// no links: a junction, a symlink and a <c>subst</c> drive each reach the same file under a
    /// different name, and a mark put on one spelling would not be found by the other. Resolving
    /// to the final target collapses them.
    ///
    /// A path that cannot be resolved - it does not exist yet, it lives on a share that is not
    /// mounted, the link is broken - keeps its full form. That is the honest answer: an
    /// unresolvable path is still a perfectly good key, it just cannot be unified with its
    /// aliases until the file is reachable again.
    /// </summary>
    private static string Normalize(string path)
    {
        string full;

        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }

        try
        {
            if (File.ResolveLinkTarget(full, returnFinalTarget: true) is { } target)
            {
                return target.FullName;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not reachable right now. The full path is still a usable key.
        }

        return full;
    }

    /// <summary>
    /// Whether this file is really gone, as opposed to merely out of reach.
    ///
    /// The obvious test - does the file exist - is wrong on its own. A document on a detached
    /// drive or a share the machine is not currently on does not exist either, and dropping its
    /// mark would quietly unlock a whole shareful of reference documents the first time Marqora
    /// opened away from the network. Nothing is dropped unless the volume holding it answers.
    /// </summary>
    private static bool HasBeenDeleted(string path)
    {
        try
        {
            string? root = Path.GetPathRoot(path);

            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                return false;
            }

            return !File.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Keep it. A mark wrongly kept is a document that refuses to be written until the
            // user says otherwise; a mark wrongly dropped is one that is written without asking.
            return false;
        }
    }
}
