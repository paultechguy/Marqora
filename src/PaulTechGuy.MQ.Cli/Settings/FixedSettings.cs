// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Cli.Settings;

/// <summary>
/// A run's settings, for the shared code that reads them through the app's settings service. Read
/// only: a command line never changes the app's Preferences, so anything that tries to is a bug,
/// and says so rather than quietly dropping the change.
/// </summary>
internal sealed class FixedSettings(AppSettings settings) : ISettingsService
{
    public AppSettings Current => settings;

    // Nothing changes, so nothing is ever raised.
    public event EventHandler<AppSettings>? SettingsChanged
    {
        add { }
        remove { }
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Update(Func<AppSettings, AppSettings> mutate) =>
        throw new NotSupportedException("marqora-cli never changes the app's Preferences.");

    public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
