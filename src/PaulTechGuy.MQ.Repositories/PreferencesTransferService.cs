// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Repositories;

/// <summary>
/// Reads and writes the preferences file that carries settings from one machine to another.
///
/// The work happens on the JSON tree rather than on <see cref="AppSettings"/>, and that is
/// the whole design. Deserializing a file straight into the record answers "what are the
/// settings now" and destroys the two questions the import report is made of: which keys this
/// build has never heard of, and which it has and the file did not mention. Both look exactly
/// like "absent" once the JSON is gone, so both are counted while it is still there.
///
/// Which keys this build knows is not written down anywhere. It is taken by serializing the
/// settings in hand and reading back the property names, so it is exactly right for the
/// running build and cannot drift. The one list kept by hand is
/// <see cref="AppSettings.SessionKeys"/>, which says which of those describe the machine
/// rather than the user, and it sits beside the method it mirrors.
/// </summary>
public sealed class PreferencesTransferService(
    string appVersion,
    ILogger<PreferencesTransferService> logger) : IPreferencesTransfer
{
    /// <summary>How many settings a report line names before it starts counting instead.</summary>
    private const int NamesPerNote = 6;

    public async Task ExportAsync(
        string path,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        JsonObject preferences = ToObject(settings);

        foreach (string key in AppSettings.SessionKeys)
        {
            preferences.Remove(key);
        }

        var document = new PreferencesDocument
        {
            AppVersion = appVersion,
            ExportedUtc = DateTimeOffset.UtcNow,
            ExportedFrom = MachineName(),
            Preferences = preferences,
        };

        string json = JsonSerializer.Serialize(document, PreferencesJsonContext.Default.PreferencesDocument);

        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Exported {Count} preferences to {Path}.", preferences.Count, path);
    }

    public async Task<PreferencesImportResult> ImportAsync(
        string path,
        AppSettings current,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);

        string json;

        try
        {
            json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "The preferences file at {Path} could not be read.", path);

            return PreferencesImportResult.Failed($"The file could not be read. {ex.Message}");
        }

        JsonNode? root;

        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "The preferences file at {Path} is not valid JSON.", path);

            return PreferencesImportResult.Failed(
                "The file is not valid JSON, so nothing could be read from it.");
        }

        if (root is not JsonObject envelope)
        {
            return PreferencesImportResult.Failed("The file does not hold a set of preferences.");
        }

        /*
            An exported file wraps its preferences in an envelope. A settings.json copied
            straight off the other machine does not - and copying settings.json is the obvious
            thing for someone to try - so a file with no envelope is read as the preferences
            themselves rather than turned away. It is called out in the report, because such a
            file also carries session state that import deliberately leaves behind.
        */
        bool wasRaw = envelope["preferences"] is not JsonObject;
        JsonObject filePreferences = envelope["preferences"] as JsonObject ?? envelope;

        return Merge(
            filePreferences,
            current,
            wasRaw,
            wasRaw ? null : ReadText(envelope, "appVersion"),
            wasRaw ? 0 : ReadNumber(envelope, "schemaVersion"));
    }

    /// <summary>
    /// The file's settings laid over this machine's, key by key, keeping a tally of everything
    /// that did not go across cleanly.
    /// </summary>
    private PreferencesImportResult Merge(
        JsonObject filePreferences,
        AppSettings current,
        bool wasRaw,
        string? sourceAppVersion,
        int schemaVersion)
    {
        // Every key this build writes, less the ones that describe this machine's session.
        var known = new HashSet<string>(
            ToObject(current).Select(p => p.Key),
            StringComparer.OrdinalIgnoreCase);

        known.ExceptWith(AppSettings.SessionKeys);

        List<KeyValuePair<string, JsonNode?>> offered = [];
        List<string> unrecognised = [];
        List<string> sessionIgnored = [];

        foreach (KeyValuePair<string, JsonNode?> entry in filePreferences)
        {
            if (AppSettings.SessionKeys.Contains(entry.Key))
            {
                sessionIgnored.Add(entry.Key);
            }
            else if (known.Contains(entry.Key))
            {
                offered.Add(entry);
            }
            else
            {
                unrecognised.Add(entry.Key);
            }
        }

        if (offered.Count == 0)
        {
            return PreferencesImportResult.Failed(
                unrecognised.Count == 0 && sessionIgnored.Count == 0
                    ? "This does not look like a Marqora preferences file. Nothing in it is a "
                        + "setting Marqora recognises."
                    : "The file holds nothing this version of Marqora can use. Every setting in "
                        + "it is either unknown to this build or describes the other machine's "
                        + "session.");
        }

        List<string> rejected = [];

        (AppSettings merged, int applied) = Apply(current, offered, rejected);

        List<string> adjusted = [];

        merged = Clamp(merged, adjusted).WithSessionOf(current);

        var mentioned = new HashSet<string>(offered.Select(e => e.Key), StringComparer.OrdinalIgnoreCase);
        List<string> absent = [.. known.Where(k => !mentioned.Contains(k)).Order(StringComparer.Ordinal)];

        logger.LogInformation(
            "Imported {Applied} of {Offered} preferences from Marqora {Version}: "
                + "{Unrecognised} unrecognised, {Rejected} rejected, {Absent} absent, {Adjusted} clamped.",
            applied,
            offered.Count,
            sourceAppVersion ?? "(unstated)",
            unrecognised.Count,
            rejected.Count,
            absent.Count,
            adjusted.Count);

        return new PreferencesImportResult
        {
            Settings = merged,
            Applied = applied,
            Offered = offered.Count,
            SourceAppVersion = sourceAppVersion,
            SourceSchemaVersion = schemaVersion,
            WasRawSettingsFile = wasRaw,
            Notes = Report(unrecognised, rejected, absent, adjusted, sessionIgnored),
        };
    }

    /// <summary>
    /// Lays the offered keys over the current settings and reads the result back.
    ///
    /// Tried whole first, because that is the ordinary case: a file from the same build, or a
    /// neighbouring one, reads entirely and costs a single deserialization. Only when
    /// something in it will not read - a misspelt enum, a string where a number belongs - does
    /// it fall back to one key at a time, which finds exactly which key was at fault instead
    /// of losing the whole file to it.
    /// </summary>
    private static (AppSettings Settings, int Applied) Apply(
        AppSettings current,
        List<KeyValuePair<string, JsonNode?>> offered,
        List<string> rejected)
    {
        JsonObject whole = ToObject(current);

        foreach ((string key, JsonNode? value) in offered)
        {
            // Cloned because a node still belonging to the file's own tree cannot be adopted
            // into this one.
            whole[key] = value?.DeepClone();
        }

        if (TryRead(whole, out AppSettings? settings))
        {
            return (settings, offered.Count);
        }

        // Fresh, because the attempt above left its own values in the object it was given.
        JsonObject working = ToObject(current);
        int applied = 0;

        foreach ((string key, JsonNode? value) in offered)
        {
            JsonNode? previous = working[key]?.DeepClone();

            working[key] = value?.DeepClone();

            if (TryRead(working, out _))
            {
                applied++;
            }
            else
            {
                working[key] = previous;
                rejected.Add(key);
            }
        }

        return (TryRead(working, out AppSettings? repaired) ? repaired : current, applied);
    }

    /// <summary>
    /// Every number back inside the range its control allows, naming the ones that had to
    /// move.
    ///
    /// Import is the one way into the settings that no dialog has clamped on the way past, so
    /// a hand-edited file is the only thing that can put a font size of 900 or a tab of -1
    /// into the record. Clamping here is what keeps the preferences dialog from opening on a
    /// value its own boxes could not have produced.
    ///
    /// The settings already in force pass through untouched - they came from those same
    /// controls and are already in range - so nothing is reported for a key the file never
    /// mentioned.
    /// </summary>
    private static AppSettings Clamp(AppSettings settings, List<string> adjusted)
    {
        return settings with
        {
            SourceZoomPercent = Whole(
                "sourceZoomPercent", settings.SourceZoomPercent, ZoomLevel.Minimum, ZoomLevel.Maximum),
            PreviewZoomPercent = Whole(
                "previewZoomPercent", settings.PreviewZoomPercent, ZoomLevel.Minimum, ZoomLevel.Maximum),
            SourceFontSize = Whole(
                "sourceFontSize",
                settings.SourceFontSize,
                TypographyDefaults.MinimumFontSize,
                TypographyDefaults.MaximumFontSize),
            PreviewFontSize = Fraction(
                "previewFontSize",
                settings.PreviewFontSize,
                TypographyDefaults.MinimumFontSize,
                TypographyDefaults.MaximumFontSize),
            PreviewMaxWidth = Width(settings.PreviewMaxWidth),
            TabSize = Whole(
                "tabSize", settings.TabSize, AppSettings.MinimumTabSize, AppSettings.MaximumTabSize),
            RecentFilesLimit = Whole(
                "recentFilesLimit",
                settings.RecentFilesLimit,
                AppSettings.MinimumRecentFilesLimit,
                AppSettings.MaximumRecentFilesLimit),
            AutoSaveDelaySeconds = Whole(
                "autoSaveDelaySeconds",
                settings.AutoSaveDelaySeconds,
                AppSettings.MinimumAutoSaveDelaySeconds,
                AppSettings.MaximumAutoSaveDelaySeconds),
            LogRetentionDays = Whole(
                "logRetentionDays", settings.LogRetentionDays, 0, AppSettings.MaximumLogRetentionDays),
            FormatRules = WrapWidth(settings.FormatRules),
        };

        int Whole(string name, int value, int minimum, int maximum)
        {
            int result = Math.Clamp(value, minimum, maximum);

            if (result != value)
            {
                adjusted.Add($"{name} ({value} became {result})");
            }

            return result;
        }

        double Fraction(string name, double value, double minimum, double maximum)
        {
            double result = Math.Clamp(value, minimum, maximum);

            if (result != value)
            {
                adjusted.Add($"{name} ({value:0.##} became {result:0.##})");
            }

            return result;
        }

        // Zero is not out of range - it is how "no limit" is stored - so the ordinary clamp
        // would drag it up to the minimum width and quietly switch the limit on.
        int Width(int value)
        {
            if (value == TypographyDefaults.UnlimitedPreviewWidth)
            {
                return value;
            }

            if (value < 0)
            {
                adjusted.Add($"previewMaxWidth ({value} became no limit)");

                return TypographyDefaults.UnlimitedPreviewWidth;
            }

            return Whole(
                "previewMaxWidth",
                value,
                TypographyDefaults.MinimumPreviewWidth,
                TypographyDefaults.MaximumPreviewWidth);
        }

        // The only clamped value that lives inside a nested record, so it is reached through
        // one rather than off the settings directly.
        //
        // A null goes back as a null. It means the file carried no formatting rules at all,
        // and building a record here to clamp one field of would turn a key the file never
        // mentioned into a stored one - which is exactly what the "absent" tally exists to
        // report instead.
        FormatOptions? WrapWidth(FormatOptions? rules)
        {
            if (rules is null)
            {
                return null;
            }

            int width = Whole(
                "formatRules.wrapColumn",
                rules.WrapColumn,
                FormatOptions.MinimumWrapColumn,
                FormatOptions.MaximumWrapColumn);

            return width == rules.WrapColumn ? rules : rules with { WrapColumn = width };
        }
    }

    /// <summary>
    /// The tallies, as the lines the report shows. A tally with nothing in it says nothing, so
    /// a clean import between matching builds produces no lines at all.
    /// </summary>
    private static List<PreferenceImportNote> Report(
        List<string> unrecognised,
        List<string> rejected,
        List<string> absent,
        List<string> adjusted,
        List<string> sessionIgnored)
    {
        List<PreferenceImportNote> notes = [];

        if (unrecognised.Count > 0)
        {
            Add(
                PreferenceImportIssue.Unrecognised,
                $"{Count(unrecognised, "setting")} in the file {Verb(unrecognised)} unknown to this "
                    + $"version of Marqora and {Verb(unrecognised)} skipped: {Join(unrecognised)}.");
        }

        if (rejected.Count > 0)
        {
            Add(
                PreferenceImportIssue.Rejected,
                $"{Count(rejected, "value")} could not be read and {Verb(rejected)} left "
                    + $"unchanged: {Join(rejected)}.");
        }

        if (absent.Count > 0)
        {
            Add(
                PreferenceImportIssue.NotInFile,
                $"{Count(absent, "setting")} {Verb(absent)} not in the file - it came from an "
                    + $"older Marqora - so {Verb(absent)} left unchanged: {Join(absent)}.");
        }

        if (adjusted.Count > 0)
        {
            Add(
                PreferenceImportIssue.Adjusted,
                $"{Count(adjusted, "value")} {Verb(adjusted)} outside the range Marqora allows and "
                    + $"{Verb(adjusted)} brought into it: {Join(adjusted)}.");
        }

        if (sessionIgnored.Count > 0)
        {
            Add(
                PreferenceImportIssue.SessionIgnored,
                $"{Count(sessionIgnored, "value")} describing the other machine - its open "
                    + $"documents, its window - {Verb(sessionIgnored)} ignored: "
                    + $"{Join(sessionIgnored)}.");
        }

        return notes;

        void Add(PreferenceImportIssue issue, string detail) =>
            notes.Add(new PreferenceImportNote(issue, detail));
    }

    private static string Count(List<string> names, string noun) =>
        names.Count == 1 ? $"One {noun}" : $"{names.Count} {noun}s";

    private static string Verb(List<string> names) => names.Count == 1 ? "was" : "were";

    /// <summary>
    /// A handful of names, then a count. A report that listed forty keys would be scrolled
    /// past rather than read, and the whole list is in the log for anyone who wants it.
    /// </summary>
    private static string Join(List<string> names) =>
        names.Count <= NamesPerNote
            ? string.Join(", ", names)
            : string.Join(", ", names.Take(NamesPerNote)) + $", and {names.Count - NamesPerNote} more";

    /// <summary>
    /// A string from the envelope, or null if it is missing or is not one. Never throws: the
    /// envelope is describing itself, and a file that describes itself badly is still worth
    /// importing.
    /// </summary>
    private static string? ReadText(JsonObject envelope, string key)
    {
        try
        {
            return envelope[key]?.GetValue<string>();
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>A number from the envelope, or zero. Never throws, for the same reason.</summary>
    private static int ReadNumber(JsonObject envelope, string key)
    {
        try
        {
            return envelope[key]?.GetValue<int>() ?? 0;
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            return 0;
        }
    }

    /// <summary>The settings as a JSON tree, every key present - see PreferencesJsonContext.</summary>
    private static JsonObject ToObject(AppSettings settings) =>
        JsonSerializer.SerializeToNode(settings, PreferencesJsonContext.Default.AppSettings)!.AsObject();

    /// <summary>The tree back as settings, or false when something in it will not read.</summary>
    private static bool TryRead(JsonObject source, [NotNullWhen(true)] out AppSettings? settings)
    {
        try
        {
            settings = JsonSerializer.Deserialize(source, PreferencesJsonContext.Default.AppSettings);

            return settings is not null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or FormatException)
        {
            settings = null;

            return false;
        }
    }

    /// <summary>
    /// The machine's name, so one exported file can be told from another. Never worth failing
    /// an export over, so a machine that will not say keeps the name out of the file.
    /// </summary>
    private static string? MachineName()
    {
        try
        {
            return Environment.MachineName;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
