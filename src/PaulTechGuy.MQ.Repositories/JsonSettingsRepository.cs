// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions;
using PaulTechGuy.MQ.Abstractions.Repositories;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Repositories;

/// <summary>Stores <see cref="AppSettings"/> as JSON in the per-user data directory.</summary>
public sealed class JsonSettingsRepository : ISettingsRepository, IDisposable
{
    private readonly JsonFileStore<AppSettings> _store;

    public JsonSettingsRepository(IAppPaths paths, ILogger<JsonSettingsRepository> logger)
    {
        _store = new JsonFileStore<AppSettings>(
            paths.SettingsFilePath,
            MarqoraJsonContext.Default.AppSettings,
            logger);
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
        await _store.ReadAsync(cancellationToken).ConfigureAwait(false) ?? AppSettings.Default;

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
        _store.WriteAsync(settings, cancellationToken);

    /// <summary>Releases the store's write lock. Called by the DI container at shutdown.</summary>
    public void Dispose() => _store.Dispose();
}
