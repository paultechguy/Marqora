// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Abstractions.Repositories;

/// <summary>Persists the most-recently-used list. Never throws for missing or corrupt storage.</summary>
public interface IRecentFilesRepository
{
    Task<IReadOnlyList<RecentFile>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyList<RecentFile> entries, CancellationToken cancellationToken = default);
}
