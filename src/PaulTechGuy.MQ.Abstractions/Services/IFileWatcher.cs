// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Abstractions.Services;

/// <summary>
/// Watches a single file for external modification. Implementations debounce the
/// burst of events editors produce when saving.
/// </summary>
public interface IFileWatcher : IDisposable
{
    /// <summary>Path currently watched, or null when idle.</summary>
    string? WatchedPath { get; }

    event EventHandler<string>? FileChanged;

    /// <summary>Raised when the watched file is deleted or renamed away.</summary>
    event EventHandler<string>? FileRemoved;

    void Watch(string path);

    void StopWatching();
}
