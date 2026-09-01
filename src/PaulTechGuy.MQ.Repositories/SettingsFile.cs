// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Repositories;

/// <summary>
/// Reads the settings file directly, for the handful of values that are needed before the
/// container exists.
///
/// <see cref="JsonSettingsRepository"/> is the ordinary way in and stays so: it is async, it
/// takes a logger, and it is what <see cref="Abstractions.Services.ISettingsService"/> is
/// built on. But logging is configured at the very top of startup - before the container is
/// built, because everything in the container wants a logger - and the log retention setting
/// has to be known by then. Hence one small synchronous door, used once.
///
/// Anything read this way necessarily takes effect at the next launch rather than
/// immediately, which is worth saying in the preferences UI wherever it applies.
/// </summary>
public static class SettingsFile
{
    /// <summary>
    /// The settings as they stand on disk, or the defaults if the file is absent, empty or
    /// unreadable.
    ///
    /// Never throws. A settings file that cannot be parsed must not stop the app starting,
    /// and at this point in startup there is nowhere to report it to anyway - the failure
    /// shows up as the app running on defaults, and the file is rewritten from those on the
    /// first change.
    /// </summary>
    public static AppSettings ReadOrDefault(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return AppSettings.Default;
            }

            string json = File.ReadAllText(path);

            if (string.IsNullOrWhiteSpace(json))
            {
                return AppSettings.Default;
            }

            return JsonSerializer.Deserialize(json, MarqoraJsonContext.Default.AppSettings)
                ?? AppSettings.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return AppSettings.Default;
        }
    }
}
