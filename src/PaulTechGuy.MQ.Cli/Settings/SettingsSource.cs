// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Cli.Reporting;
using PaulTechGuy.MQ.Cli.Verbs;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Repositories;

namespace PaulTechGuy.MQ.Cli.Settings;

/// <summary>
/// The settings a run works with, and where they came from (CLI-SET-01 to CLI-SET-03).
///
/// <list type="bullet">
/// <item>No --settings: the built-in defaults, and nothing read from the profile, so a CI box and a
/// desk give the same bytes.</item>
/// <item>--settings app: the desktop app's own saved settings.json.</item>
/// <item>--settings &lt;file&gt;: an exported Preferences file, or a raw settings.json copied from
/// another machine - whatever the app's Import accepts.</item>
/// </list>
///
/// Both files are read through the app's Import, merged over the defaults, rather than through the
/// settings repository. The repository moves a damaged settings.json aside when it meets one - right
/// for an app that must start, wrong for a command that promised never to touch the app's files -
/// and falls back to defaults where a command line should stop and say so.
/// </summary>
internal sealed record SettingsSource(AppSettings Settings, string Origin)
{
    public static SettingsSource BuiltIn { get; } = new(new AppSettings(), "built-in defaults");

    /// <exception cref="UsageException">A settings file that is missing or cannot be read.</exception>
    public static async Task<SettingsSource> LoadAsync(RunContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Settings is not { } given)
        {
            return BuiltIn;
        }

        bool fromApp = given.Equals("app", StringComparison.OrdinalIgnoreCase);
        string path = fromApp ? context.Environment.AppPaths().SettingsFilePath : context.FullPath(given);
        string origin = fromApp ? $"the app's Preferences ({path})" : $"settings file {given}";

        if (!File.Exists(path))
        {
            throw new UsageException(fromApp
                ? $"--settings app found no saved Preferences at {path}. Open Marqora once, or give a settings file."
                : $"No settings file at {given}.");
        }

        var import = new PreferencesTransferService(VersionVerb.Current, new ReporterLogger<PreferencesTransferService>(context.Reporter));
        PreferencesImportResult result = await import.ImportAsync(path, new AppSettings(), context.CancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw new UsageException($"{origin} could not be read: {result.Failure}");
        }

        context.Reporter.Detail(null, $"Settings: {origin}.");

        return new SettingsSource(result.Settings!, origin);
    }
}
