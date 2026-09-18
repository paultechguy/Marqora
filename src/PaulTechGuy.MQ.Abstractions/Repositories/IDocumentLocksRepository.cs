// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Abstractions.Repositories;

/// <summary>
/// Persists which documents the user has marked read-only. Never throws for missing or corrupt
/// storage.
///
/// Paths rather than identities, because the mark has to outlive the session that made it and a
/// document's <c>Id</c> does not. What that costs is written down where the marks are read:
/// a file renamed or moved outside Marqora takes its mark with it.
/// </summary>
public interface IDocumentLocksRepository
{
    Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default);
}
