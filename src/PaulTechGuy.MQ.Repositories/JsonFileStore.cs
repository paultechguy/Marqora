// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;

namespace PaulTechGuy.MQ.Repositories;

/// <summary>
/// Reads and writes a single JSON state file.
///
/// Two properties matter for a desktop app that can be killed at any moment:
/// writes are atomic (temp file plus replace, so a crash mid-write cannot truncate the
/// real file), and reads never throw -- a missing, empty or corrupt file yields the
/// caller's default rather than blocking startup.
/// </summary>
internal sealed class JsonFileStore<T>(string filePath, JsonTypeInfo<T> typeInfo, ILogger logger) : IDisposable
    where T : class
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public void Dispose() => _gate.Dispose();

    public async Task<T?> ReadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(filePath))
            {
                logger.LogDebug("State file {Path} does not exist yet; using defaults.", filePath);
                return null;
            }

            await using FileStream stream = File.Open(
                filePath,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.ReadWrite,
                    Options = FileOptions.Asynchronous,
                });

            if (stream.Length == 0)
            {
                return null;
            }

            return await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Corrupt or locked state must never prevent the app from starting.
            logger.LogWarning(ex, "Could not read state file {Path}; falling back to defaults.", filePath);
            TryQuarantine();
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WriteAsync(T value, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempPath = filePath + ".tmp";

            await using (FileStream stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, value, typeInfo, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            // Replace rather than delete-then-move so the old file survives a failure here.
            if (File.Exists(filePath))
            {
                File.Replace(tempPath, filePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, filePath);
            }

            logger.LogDebug("Wrote state file {Path}.", filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing a settings write is an annoyance, not a reason to crash the app.
            logger.LogWarning(ex, "Could not write state file {Path}.", filePath);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Moves an unreadable file aside so the next write starts from a clean slate.</summary>
    private void TryQuarantine()
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Move(filePath, filePath + ".corrupt", overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Could not quarantine unreadable state file {Path}.", filePath);
        }
    }
}
