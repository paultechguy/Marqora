// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions;
using PaulTechGuy.MQ.Abstractions.Repositories;

namespace PaulTechGuy.MQ.Repositories;

/// <summary>Stores the pinned documents as JSON in the per-user data directory.</summary>
public sealed class JsonDocumentPinsRepository : IDocumentPinsRepository, IDisposable
{
    private readonly JsonFileStore<DocumentPinsDocument> _store;
    private readonly ILogger<JsonDocumentPinsRepository> _logger;

    public JsonDocumentPinsRepository(IAppPaths paths, ILogger<JsonDocumentPinsRepository> logger)
    {
        _logger = logger;
        _store = new JsonFileStore<DocumentPinsDocument>(
            paths.DocumentPinsFilePath,
            MarqoraJsonContext.Default.DocumentPinsDocument,
            logger);
    }

    public async Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default)
    {
        DocumentPinsDocument? document = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);

        if (document is null)
        {
            return [];
        }

        if (document.SchemaVersion > DocumentPinsDocument.CurrentSchemaVersion)
        {
            // Written by a newer build. Read it rather than lose it, but say so in the log.
            _logger.LogInformation(
                "Document-pins file uses schema {Found}, newer than the supported {Supported}.",
                document.SchemaVersion,
                DocumentPinsDocument.CurrentSchemaVersion);
        }

        return document.Paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList();
    }

    public Task SaveAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default) =>
        _store.WriteAsync(new DocumentPinsDocument { Paths = [.. paths] }, cancellationToken);

    /// <summary>Releases the store's write lock. Called by the DI container at shutdown.</summary>
    public void Dispose() => _store.Dispose();
}
