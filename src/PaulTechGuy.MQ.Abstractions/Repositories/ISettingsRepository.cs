// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Abstractions.Repositories;

/// <summary>Persists <see cref="AppSettings"/>. Never throws for missing or corrupt storage.</summary>
public interface ISettingsRepository
{
    /// <summary>Loads settings, falling back to <see cref="AppSettings.Default"/> when unavailable.</summary>
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
