// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Abstractions.Services;

/// <summary>
/// In-memory owner of the current <see cref="AppSettings"/>, with debounced write-behind
/// so high-frequency changes (zoom, splitter drag, window resize) do not hammer the disk.
/// </summary>
public interface ISettingsService
{
    AppSettings Current { get; }

    event EventHandler<AppSettings>? SettingsChanged;

    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Applies a change and schedules a debounced save.</summary>
    void Update(Func<AppSettings, AppSettings> mutate);

    /// <summary>Writes any pending changes immediately. Call on shutdown.</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
