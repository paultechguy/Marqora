// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions;
using PaulTechGuy.MQ.Abstractions.Repositories;

namespace PaulTechGuy.MQ.Repositories;

/// <summary>Stores the read-only marks as JSON in the per-user data directory.</summary>
public sealed class JsonDocumentLocksRepository : IDocumentLocksRepository, IDisposable
{
    private readonly JsonFileStore<DocumentLocksDocument> _store;
    private readonly ILogger<JsonDocumentLocksRepository> _logger;

    public JsonDocumentLocksRepository(IAppPaths paths, ILogger<JsonDocumentLocksRepository> logger)
    {
        _logger = logger;
        _store = new JsonFileStore<DocumentLocksDocument>(
            paths.DocumentLocksFilePath,
            MarqoraJsonContext.Default.DocumentLocksDocument,
            logger);
    }

    public async Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default)
    {
        DocumentLocksDocument? document = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);

        if (document is null)
        {
            return [];
        }

        if (document.SchemaVersion > DocumentLocksDocument.CurrentSchemaVersion)
        {
            // Written by a newer build. Read it rather than lose it, but say so in the log.
            _logger.LogInformation(
                "Document-locks file uses schema {Found}, newer than the supported {Supported}.",
                document.SchemaVersion,
                DocumentLocksDocument.CurrentSchemaVersion);
        }

        return document.Paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList();
    }

    public Task SaveAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default) =>
        _store.WriteAsync(new DocumentLocksDocument { Paths = [.. paths] }, cancellationToken);

    /// <summary>Releases the store's write lock. Called by the DI container at shutdown.</summary>
    public void Dispose() => _store.Dispose();
}
