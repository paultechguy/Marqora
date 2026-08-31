// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions;
using PaulTechGuy.MQ.Abstractions.Repositories;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Repositories;

/// <summary>Stores the most-recently-used list as JSON in the per-user data directory.</summary>
public sealed class JsonRecentFilesRepository : IRecentFilesRepository, IDisposable
{
    private readonly JsonFileStore<RecentFilesDocument> _store;
    private readonly ILogger<JsonRecentFilesRepository> _logger;

    public JsonRecentFilesRepository(IAppPaths paths, ILogger<JsonRecentFilesRepository> logger)
    {
        _logger = logger;
        _store = new JsonFileStore<RecentFilesDocument>(
            paths.RecentFilesFilePath,
            MarqoraJsonContext.Default.RecentFilesDocument,
            logger);
    }

    public async Task<IReadOnlyList<RecentFile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        RecentFilesDocument? document = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);

        if (document is null)
        {
            return [];
        }

        if (document.SchemaVersion > RecentFilesDocument.CurrentSchemaVersion)
        {
            // Written by a newer build. Read it rather than lose it, but say so in the log.
            _logger.LogInformation(
                "Recent-files file uses schema {Found}, newer than the supported {Supported}.",
                document.SchemaVersion,
                RecentFilesDocument.CurrentSchemaVersion);
        }

        return document.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Path))
            .ToList();
    }

    public Task SaveAsync(IReadOnlyList<RecentFile> entries, CancellationToken cancellationToken = default) =>
        _store.WriteAsync(new RecentFilesDocument { Items = [.. entries] }, cancellationToken);

    /// <summary>Releases the store's write lock. Called by the DI container at shutdown.</summary>
    public void Dispose() => _store.Dispose();
}
