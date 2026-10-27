// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Themes;

namespace PaulTechGuy.MQ.Cli.Settings;

/// <summary>Where the theme in force came from, for --verbose, --dry-run and the JSON (CLI-TH-06).</summary>
internal enum ThemeSourceKind
{
    /// <summary>--theme on the command line.</summary>
    Option,

    /// <summary>The export theme the --settings source names.</summary>
    SettingsExport,

    /// <summary>The --settings source's screen theme, which exports follow when it names no export theme.</summary>
    SettingsScreen,

    /// <summary>Nothing named one.</summary>
    Default,
}

/// <summary>A theme, and the rung of the ladder that chose it.</summary>
internal sealed record ResolvedTheme(ColorTheme Theme, ThemeSourceKind Source)
{
    /// <summary>The source as the JSON carries it: "option", "settingsExport", "settingsScreen" or "default".</summary>
    public string SourceToken => Source switch
    {
        ThemeSourceKind.Option => "option",
        ThemeSourceKind.SettingsExport => "settingsExport",
        ThemeSourceKind.SettingsScreen => "settingsScreen",
        _ => "default",
    };

    /// <summary>The source in words, for a person.</summary>
    public string Describe(SettingsSource settings) => Source switch
    {
        ThemeSourceKind.Option => "--theme",
        ThemeSourceKind.SettingsExport => $"the export theme in {settings.Origin}",
        ThemeSourceKind.SettingsScreen => $"the theme in {settings.Origin}",
        _ => "the default",
    };
}

/// <summary>
/// Which color theme a run's outputs wear (CLI-TH-01 to CLI-TH-03): --theme, then the export theme
/// the settings name, then the settings' own theme, then Default - the app's rule, with the command
/// line on top and Default standing in for a screen.
///
/// A theme is named by its id or by its gallery name, either ignoring case. One that does not exist,
/// from the command line or from a settings file, ends the run before any work: the app quietly
/// falls back to Default for a saved setting that has gone missing, but a wrongly colored PDF made
/// from a typo is found only after it has been sent.
/// </summary>
internal static class ThemeResolver
{
    /// <exception cref="UsageException">A theme named anywhere that the catalog does not have.</exception>
    public static ResolvedTheme Resolve(string? option, SettingsSource settings, ThemeCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(catalog);

        if (option is not null)
        {
            return new ResolvedTheme(Find(option, "--theme", catalog), ThemeSourceKind.Option);
        }

        if (settings.Settings.ExportColorTheme is { Length: > 0 } export)
        {
            return new ResolvedTheme(Find(export, $"The export theme in {settings.Origin}", catalog), ThemeSourceKind.SettingsExport);
        }

        if (settings.Settings.ColorTheme is { Length: > 0 } screen)
        {
            return new ResolvedTheme(Find(screen, $"The theme in {settings.Origin}", catalog), ThemeSourceKind.SettingsScreen);
        }

        return new ResolvedTheme(catalog.Default, ThemeSourceKind.Default);
    }

    /// <summary>A theme by id or gallery name, ignoring case; never a fallback.</summary>
    public static ColorTheme Find(string named, string where, ThemeCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(named);
        ArgumentNullException.ThrowIfNull(catalog);

        string wanted = named.Trim();

        ColorTheme? match = catalog.Themes.FirstOrDefault(theme =>
            string.Equals(theme.Id, wanted, StringComparison.OrdinalIgnoreCase)
            || string.Equals(theme.Name, wanted, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            return match;
        }

        string ids = string.Join(", ", catalog.Themes.Select(theme => theme.Id));
        string? near = Suggest.Closest(wanted, catalog.Themes.Select(theme => theme.Id).Concat(catalog.Themes.Select(theme => theme.Name)));
        string hint = near is null ? string.Empty : $" Did you mean '{catalog.Themes.First(t => t.Id == near || t.Name == near).Id}'?";

        throw new UsageException(
            $"{where} names a color theme that does not exist: '{named}'.{hint} Themes: {ids}. Run 'marqora-cli themes list' for details.");
    }
}
