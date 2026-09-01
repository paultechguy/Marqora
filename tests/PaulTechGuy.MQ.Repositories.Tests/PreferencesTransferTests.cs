// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Repositories.Tests;

/// <summary>
/// Moving preferences between two machines, which is the one path into the settings record
/// that no dialog has vetted on the way past.
///
/// The cases worth holding down are all about a file that does not match the build reading
/// it, because that is the situation the feature exists for - two machines that are not on
/// the same version yet. A file from a newer build names settings this one has never heard
/// of; a file from an older build is missing settings this one has; a hand-edited file can
/// hold a number no control could have produced, or a word that is not one of the enum's.
/// Every one of those has to leave the app with usable settings and an honest account of
/// what happened, and none of them is visible from the outside if it goes wrong.
///
/// The other half is the line between preferences and session state. Exporting must not
/// carry one machine's open documents onto another, and importing must not disturb them.
/// </summary>
public sealed class PreferencesTransferTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "marqora-transfer", Guid.NewGuid().ToString("n"));

    private readonly PreferencesTransferService _transfer =
        new("9.9.9", NullLogger<PreferencesTransferService>.Instance);

    public PreferencesTransferTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string PathFor(string name) => Path.Combine(_root, name);

    /// <summary>Settings with every preference moved off its default, so a copy is provable.</summary>
    private static AppSettings Customised() => new()
    {
        Theme = AppTheme.Dark,
        SourceFontFamily = "Fira Code",
        SourceFontSize = 18,
        PreviewFontFamily = "Georgia",
        PreviewFontSize = 17.5,
        PreviewMaxWidth = 900,
        TabSize = 2,
        InsertSpaces = false,
        ShowMinimap = true,
        HeadingNumbering = HeadingNumbering.FromHeading2,
        Startup = StartupBehavior.EmptyTab,
        RecentFilesLimit = 30,
        AutoSave = AutoSaveMode.AfterDelay,
        AutoSaveDelaySeconds = 45,
        NewFileLineEnding = LineEndingStyle.Lf,
        WriteUtf8Bom = true,
        LogRetentionDays = 90,
    };

    /// <summary>Settings carrying a session: open documents, a window, a search history.</summary>
    private static AppSettings WithSession(AppSettings settings) => settings with
    {
        OpenDocuments = ["C:\\work\\one.md", "C:\\work\\two.md"],
        ActiveDocumentIndex = 1,
        Window = new WindowPlacement { Width = 1234, Height = 987 },
        FindHistory = ["needle"],
        SplitterPosition = 0.31,
        LastWelcomeVersion = "0.1.0",
    };

    // ------------------------------------------------------------------ round trip

    [Fact]
    public async Task Export_then_import_reproduces_every_preference()
    {
        AppSettings source = Customised();
        string file = PathFor("prefs.json");

        await _transfer.ExportAsync(file, source, TestContext.Current.CancellationToken);

        PreferencesImportResult result = await _transfer.ImportAsync(
            file, AppSettings.Default, TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeTrue(result.Failure);

        // Nothing was lost, adjusted or unrecognised: same build, same shape.
        result.Notes.ShouldBeEmpty();
        result.Applied.ShouldBe(result.Offered);

        result.Settings.ShouldBe(source.WithSessionOf(AppSettings.Default));
    }

    [Fact]
    public async Task Export_records_the_version_that_wrote_the_file()
    {
        string file = PathFor("stamped.json");

        await _transfer.ExportAsync(file, AppSettings.Default, TestContext.Current.CancellationToken);

        PreferencesImportResult result = await _transfer.ImportAsync(
            file, AppSettings.Default, TestContext.Current.CancellationToken);

        result.SourceAppVersion.ShouldBe("9.9.9");
        result.SourceSchemaVersion.ShouldBe(PreferencesDocument.CurrentSchemaVersion);
        result.WasRawSettingsFile.ShouldBeFalse();
    }

    /// <summary>
    /// The envelope, spelled out.
    ///
    /// This is a file that leaves the machine and is read by a build that does not exist yet,
    /// so its shape is a promise rather than an implementation detail. Renaming a member here
    /// is a schema change, and this is what makes that decision deliberate.
    /// </summary>
    [Fact]
    public async Task The_file_says_what_it_is_before_it_says_anything_else()
    {
        string file = PathFor("shape.json");

        await _transfer.ExportAsync(
            file,
            AppSettings.Default with { Theme = AppTheme.Dark },
            TestContext.Current.CancellationToken);

        string json = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);

        json.ShouldContain("""  "format": "marqora-preferences",""");
        json.ShouldContain("""  "schemaVersion": 1,""");
        json.ShouldContain("""  "appVersion": "9.9.9",""");
        json.ShouldContain("""  "exportedUtc":""");
        json.ShouldContain("""  "exportedFrom":""");
        json.ShouldContain("""  "preferences": {""");

        // Enums by name, so the file still reads correctly if a member is ever renumbered -
        // and so it can be read by a person.
        json.ShouldContain("""    "theme": "Dark",""");

        // Nulls are written rather than omitted: "this machine has no source font override"
        // is a statement the other machine has to be able to act on. See PreferencesJsonContext.
        json.ShouldContain("""    "sourceFontFamily": null,""");
    }

    /// <summary>
    /// The name the export dialog opens on, which is the only thing standing between a second
    /// export and an overwrite prompt over the first.
    /// </summary>
    [Fact]
    public void The_offered_file_name_carries_the_moment_it_was_taken()
    {
        var noon = new DateTimeOffset(2026, 9, 1, 14, 30, 22, TimeSpan.Zero);

        string name = PreferencesDocument.SuggestedFileName(noon);

        name.ShouldStartWith("Marqora-preferences-");
        name.ShouldEndWith(".json");

        // The stamp is the local reading of that instant, because the name is read by a
        // person; the envelope inside keeps the unambiguous one.
        DateTime local = noon.ToLocalTime().DateTime;

        name.ShouldBe($"Marqora-preferences-{local:yyyy-MM-dd}-{local:HHmmss}.json");
    }

    /// <summary>
    /// Two exports a few seconds apart have to be two files. This is the whole reason the
    /// stamp goes down to seconds rather than stopping at the day.
    /// </summary>
    [Fact]
    public void Two_exports_in_the_same_minute_are_offered_different_names()
    {
        var first = new DateTimeOffset(2026, 9, 1, 14, 30, 22, TimeSpan.Zero);

        PreferencesDocument.SuggestedFileName(first)
            .ShouldNotBe(PreferencesDocument.SuggestedFileName(first.AddSeconds(1)));
    }

    /// <summary>
    /// Zero-padded and big-endian, so a folder of backups sorts into the order they were
    /// taken in - which is the only ordering anyone wants from a stack of them.
    /// </summary>
    [Fact]
    public void The_names_sort_into_the_order_they_were_taken()
    {
        var start = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        List<string> chronological =
        [
            PreferencesDocument.SuggestedFileName(start),
            PreferencesDocument.SuggestedFileName(start.AddSeconds(10)),
            PreferencesDocument.SuggestedFileName(start.AddHours(11)),
            PreferencesDocument.SuggestedFileName(start.AddDays(9)),
            PreferencesDocument.SuggestedFileName(start.AddMonths(10)),
            PreferencesDocument.SuggestedFileName(start.AddYears(1)),
        ];

        chronological.Order(StringComparer.Ordinal).ShouldBe(chronological);
    }

    /// <summary>
    /// A name Windows will actually accept, whatever the machine's locale. The obvious
    /// timestamp format - the one with colons in it - is not a legal file name.
    /// </summary>
    [Fact]
    public void The_name_is_usable_as_a_file_name()
    {
        string name = PreferencesDocument.SuggestedFileName(DateTimeOffset.Now);

        name.IndexOfAny(Path.GetInvalidFileNameChars()).ShouldBe(-1);
        name.ShouldAllBe(c => char.IsAscii(c));
    }

    // -------------------------------------------------- preferences, not the session

    [Fact]
    public async Task Export_leaves_the_session_behind()
    {
        string file = PathFor("no-session.json");

        await _transfer.ExportAsync(
            file, WithSession(Customised()), TestContext.Current.CancellationToken);

        string json = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);

        foreach (string key in AppSettings.SessionKeys)
        {
            json.ShouldNotContain($"\"{key}\"", Case.Insensitive, $"{key} is session state.");
        }

        // And the preferences really are in there, so the assertion above is not passing on
        // an empty file.
        json.ShouldContain("\"sourceFontFamily\": \"Fira Code\"");
    }

    [Fact]
    public async Task Import_does_not_disturb_this_machines_session()
    {
        string file = PathFor("prefs.json");

        await _transfer.ExportAsync(file, Customised(), TestContext.Current.CancellationToken);

        AppSettings here = WithSession(AppSettings.Default);

        PreferencesImportResult result = await _transfer.ImportAsync(
            file, here, TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeTrue(result.Failure);

        AppSettings imported = result.Settings!;

        imported.OpenDocuments.ShouldBe(here.OpenDocuments);
        imported.ActiveDocumentIndex.ShouldBe(here.ActiveDocumentIndex);
        imported.Window.ShouldBe(here.Window);
        imported.FindHistory.ShouldBe(here.FindHistory);
        imported.SplitterPosition.ShouldBe(here.SplitterPosition);
        imported.LastWelcomeVersion.ShouldBe(here.LastWelcomeVersion);

        // The preferences did come across, so the session survived an import that did something.
        imported.Theme.ShouldBe(AppTheme.Dark);
        imported.TabSize.ShouldBe(2);
    }

    /// <summary>
    /// The one list in this feature that is kept by hand, held against the method it mirrors.
    ///
    /// Reflection is fine here and nowhere in the app: the test assembly is not trimmed, and
    /// the alternative is that a session member added to WithSessionOf but not to SessionKeys
    /// starts leaking one machine's open documents into an exported file, which nothing else
    /// would notice.
    /// </summary>
    [Fact]
    public void SessionKeys_names_exactly_what_WithSessionOf_carries()
    {
        // A settings record whose every member differs from the default, so that anything
        // WithSessionOf copies across is visible as a difference.
        AppSettings session = WithSession(Customised()) with
        {
            CheatsheetWindow = new WindowPlacement { Width = 111, Height = 222 },
            CheatsheetScrollTop = 42,
            FindAllWindow = new WindowPlacement { Width = 333, Height = 444 },
        };

        AppSettings carried = AppSettings.Default.WithSessionOf(session);

        HashSet<string> moved = [.. typeof(AppSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .Where(p => !Equals(p.GetValue(carried), p.GetValue(AppSettings.Default)))
            .Select(p => p.Name)];

        HashSet<string> declared = [.. AppSettings.SessionKeys.Select(Pascal)];

        declared.ShouldBe(moved, ignoreOrder: true);

        static string Pascal(string camel) => char.ToUpperInvariant(camel[0]) + camel[1..];
    }

    // ------------------------------------------------------ files from other versions

    [Fact]
    public async Task A_setting_this_build_does_not_have_is_skipped_and_named()
    {
        string file = PathFor("from-the-future.json");

        await File.WriteAllTextAsync(
            file,
            """
            {
              "format": "marqora-preferences",
              "schemaVersion": 1,
              "appVersion": "99.0.0",
              "preferences": {
                "theme": "Dark",
                "tabSize": 2,
                "teleportOnSave": true,
                "quantumWrap": "always"
              }
            }
            """,
            TestContext.Current.CancellationToken);

        PreferencesImportResult result = await _transfer.ImportAsync(
            file, AppSettings.Default, TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeTrue(result.Failure);

        // The two it understood are in force...
        result.Settings!.Theme.ShouldBe(AppTheme.Dark);
        result.Settings.TabSize.ShouldBe(2);
        result.Applied.ShouldBe(2);

        // ...and the two it did not are named rather than passed over in silence.
        PreferenceImportNote unknown = result.Notes
            .Where(n => n.Issue == PreferenceImportIssue.Unrecognised)
            .ShouldHaveSingleItem();

        unknown.Detail.ShouldContain("teleportOnSave");
        unknown.Detail.ShouldContain("quantumWrap");
    }

    [Fact]
    public async Task A_setting_the_file_does_not_have_keeps_this_machines_value()
    {
        string file = PathFor("from-the-past.json");

        await File.WriteAllTextAsync(
            file,
            """
            {
              "format": "marqora-preferences",
              "schemaVersion": 1,
              "appVersion": "0.0.1",
              "preferences": { "theme": "Light" }
            }
            """,
            TestContext.Current.CancellationToken);

        AppSettings here = AppSettings.Default with { TabSize = 7, ShowMinimap = true };

        PreferencesImportResult result = await _transfer.ImportAsync(
            file, here, TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeTrue(result.Failure);

        result.Settings!.Theme.ShouldBe(AppTheme.Light);

        // Untouched, because a file written before a preference existed cannot have an
        // opinion about it, and resetting it would be an opinion.
        result.Settings.TabSize.ShouldBe(7);
        result.Settings.ShowMinimap.ShouldBeTrue();

        PreferenceImportNote absent = result.Notes
            .Where(n => n.Issue == PreferenceImportIssue.NotInFile)
            .ShouldHaveSingleItem();

        absent.Detail.ShouldContain("older Marqora");
        absent.Detail.ShouldContain("left unchanged");

        // A file this old leaves most of the settings unmentioned, and the note names a
        // handful and counts the rest rather than reciting forty keys into a flyout.
        absent.Detail.ShouldContain("more");
    }

    [Fact]
    public async Task A_value_that_will_not_read_costs_only_itself()
    {
        string file = PathFor("misspelt.json");

        await File.WriteAllTextAsync(
            file,
            """
            {
              "format": "marqora-preferences",
              "preferences": {
                "theme": "Neon",
                "tabSize": 3,
                "showMinimap": true
              }
            }
            """,
            TestContext.Current.CancellationToken);

        PreferencesImportResult result = await _transfer.ImportAsync(
            file, AppSettings.Default, TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeTrue(result.Failure);

        // The whole file used to be lost to one bad word. Its neighbours arrive.
        result.Settings!.TabSize.ShouldBe(3);
        result.Settings.ShowMinimap.ShouldBeTrue();
        result.Settings.Theme.ShouldBe(AppSettings.Default.Theme);
        result.Applied.ShouldBe(2);
        result.Offered.ShouldBe(3);

        result.Notes.ShouldContain(n =>
            n.Issue == PreferenceImportIssue.Rejected && n.Detail.Contains("theme"));
    }

    [Fact]
    public async Task A_number_outside_its_range_is_brought_back_into_it()
    {
        string file = PathFor("out-of-range.json");

        await File.WriteAllTextAsync(
            file,
            """
            {
              "format": "marqora-preferences",
              "preferences": {
                "sourceFontSize": 900,
                "tabSize": -4,
                "logRetentionDays": 100000
              }
            }
            """,
            TestContext.Current.CancellationToken);

        PreferencesImportResult result = await _transfer.ImportAsync(
            file, AppSettings.Default, TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeTrue(result.Failure);

        result.Settings!.SourceFontSize.ShouldBe(TypographyDefaults.MaximumFontSize);
        result.Settings.TabSize.ShouldBe(AppSettings.MinimumTabSize);
        result.Settings.LogRetentionDays.ShouldBe(AppSettings.MaximumLogRetentionDays);

        PreferenceImportNote adjusted = result.Notes
            .Where(n => n.Issue == PreferenceImportIssue.Adjusted)
            .ShouldHaveSingleItem();

        adjusted.Detail.ShouldContain("sourceFontSize");
        adjusted.Detail.ShouldContain("tabSize");
        adjusted.Detail.ShouldContain("logRetentionDays");
    }

    /// <summary>
    /// Zero is how "no limit" is stored, so the ordinary clamp would drag it up to the
    /// minimum width and switch a limit on that nobody asked for.
    /// </summary>
    [Fact]
    public async Task An_unlimited_preview_width_stays_unlimited()
    {
        string file = PathFor("unlimited.json");

        await _transfer.ExportAsync(
            file,
            AppSettings.Default with { PreviewMaxWidth = TypographyDefaults.UnlimitedPreviewWidth },
            TestContext.Current.CancellationToken);

        PreferencesImportResult result = await _transfer.ImportAsync(
            file, AppSettings.Default, TestContext.Current.CancellationToken);

        result.Settings!.PreviewMaxWidth.ShouldBe(TypographyDefaults.UnlimitedPreviewWidth);
        result.Notes.ShouldNotContain(n => n.Issue == PreferenceImportIssue.Adjusted);
    }

    /// <summary>
    /// The wrap column is the one clamped value inside a nested record, so it gets its own
    /// test: a file naming a width neither the preferences dialog nor the formatter's could
    /// produce still has to arrive as a width both of them can show.
    /// </summary>
    [Fact]
    public async Task A_wrap_column_outside_its_range_is_brought_back_into_it()
    {
        string file = PathFor("wide-wrap.json");

        await File.WriteAllTextAsync(
            file,
            """
            {
              "format": "marqora-preferences",
              "preferences": {
                "formatRules": {
                  "reflowParagraphs": true,
                  "wrapColumn": 5000
                }
              }
            }
            """,
            TestContext.Current.CancellationToken);

        PreferencesImportResult result = await _transfer.ImportAsync(
            file, AppSettings.Default, TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeTrue(result.Failure);

        result.Settings!.Formatting.WrapColumn.ShouldBe(FormatOptions.MaximumWrapColumn);

        // The rest of the imported rules are not collateral damage of the clamp.
        result.Settings.Formatting.ReflowParagraphs.ShouldBeTrue();

        result.Notes
            .Where(n => n.Issue == PreferenceImportIssue.Adjusted)
            .ShouldHaveSingleItem()
            .Detail.ShouldContain("wrapColumn");
    }

    /// <summary>
    /// A file with no formatting rules in it must not gain any. Clamping reaches into the
    /// nested record, and the lazy way to do that is to build one first - which would turn a
    /// key the file never mentioned into a stored value.
    /// </summary>
    [Fact]
    public async Task A_file_with_no_formatting_rules_does_not_grow_any()
    {
        string file = PathFor("no-rules.json");

        await File.WriteAllTextAsync(
            file,
            """
            {
              "format": "marqora-preferences",
              "preferences": {
                "tabSize": 2
              }
            }
            """,
            TestContext.Current.CancellationToken);

        PreferencesImportResult result = await _transfer.ImportAsync(
            file, AppSettings.Default, TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeTrue(result.Failure);
        result.Settings!.FormatRules.ShouldBeNull();
        result.Notes.ShouldNotContain(n => n.Issue == PreferenceImportIssue.Adjusted);
    }

    // --------------------------------------------------------------- awkward files

    /// <summary>
    /// Copying settings.json off the other machine is the obvious thing to try, so it works -
    /// and the session state such a file carries is left where it belongs.
    /// </summary>
    [Fact]
    public async Task A_raw_settings_file_is_read_as_preferences()
    {
        string file = PathFor("settings.json");

        await File.WriteAllTextAsync(
            file,
            """
            {
              "theme": "Dark",
              "tabSize": 8,
              "openDocuments": [ "D:\\elsewhere\\theirs.md" ],
              "activeDocumentIndex": 0,
              "window": { "width": 640, "height": 480 }
            }
            """,
            TestContext.Current.CancellationToken);

        AppSettings here = WithSession(AppSettings.Default);

        PreferencesImportResult result = await _transfer.ImportAsync(
            file, here, TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeTrue(result.Failure);
        result.WasRawSettingsFile.ShouldBeTrue();

        result.Settings!.Theme.ShouldBe(AppTheme.Dark);
        result.Settings.TabSize.ShouldBe(8);

        result.Settings.OpenDocuments.ShouldBe(here.OpenDocuments);
        result.Settings.Window.ShouldBe(here.Window);

        result.Notes.ShouldContain(n =>
            n.Issue == PreferenceImportIssue.SessionIgnored && n.Detail.Contains("openDocuments"));

        result.Describe().ShouldContain("settings.json");
    }

    [Fact]
    public async Task A_key_in_the_wrong_case_still_lines_up()
    {
        string file = PathFor("shouty.json");

        await File.WriteAllTextAsync(
            file,
            """
            { "preferences": { "TabSize": 6, "ShowMinimap": true } }
            """,
            TestContext.Current.CancellationToken);

        PreferencesImportResult result = await _transfer.ImportAsync(
            file, AppSettings.Default, TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeTrue(result.Failure);
        result.Settings!.TabSize.ShouldBe(6);
        result.Settings.ShowMinimap.ShouldBeTrue();
        result.Notes.ShouldNotContain(n => n.Issue == PreferenceImportIssue.Unrecognised);
    }

    [Theory]
    [InlineData("{ not json at all")]
    [InlineData("[ 1, 2, 3 ]")]
    [InlineData("""{ "nothing": "here", "we": "recognise" }""")]
    public async Task A_file_that_is_not_preferences_is_refused_rather_than_half_applied(string content)
    {
        string file = PathFor("wrong.json");

        await File.WriteAllTextAsync(file, content, TestContext.Current.CancellationToken);

        PreferencesImportResult result = await _transfer.ImportAsync(
            file, AppSettings.Default, TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeFalse();
        result.Settings.ShouldBeNull();
        result.Failure.ShouldNotBeNullOrWhiteSpace();
        result.Describe().ShouldBe(result.Failure);
    }

    [Fact]
    public async Task A_file_that_is_not_there_is_reported_rather_than_thrown()
    {
        PreferencesImportResult result = await _transfer.ImportAsync(
            PathFor("absent.json"), AppSettings.Default, TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeFalse();
        result.Failure.ShouldNotBeNullOrWhiteSpace();
    }

    // -------------------------------------------------------------------- reporting

    /// <summary>
    /// What the dialog colours its report by, so it is worth being exact about.
    ///
    /// The rule is not "were there notes". A file from an older build produces a note for
    /// every key it predates, and a settings.json produces one for the session state left
    /// behind - both of which are the feature working, not failing. Counting those would put
    /// a warning on almost every cross-version import, which is the ordinary case and the one
    /// the feature exists for, and a warning that is always on is a warning nobody reads.
    /// </summary>
    [Theory]
    [InlineData("""{ "preferences": { "theme": "Dark" } }""", false, "an older file, missing keys")]
    [InlineData("""{ "theme": "Dark", "openDocuments": [] }""", false, "a settings.json, session left behind")]
    [InlineData("""{ "preferences": { "teleport": 1, "theme": "Dark" } }""", true, "a key this build lacks")]
    [InlineData("""{ "preferences": { "theme": "Neon", "tabSize": 2 } }""", true, "a value that will not read")]
    [InlineData("""{ "preferences": { "tabSize": 999 } }""", true, "a value out of range")]
    public async Task Only_a_setting_this_build_could_not_honour_makes_an_import_partial(
        string content,
        bool expected,
        string because)
    {
        string file = PathFor("partial.json");

        await File.WriteAllTextAsync(file, content, TestContext.Current.CancellationToken);

        PreferencesImportResult result = await _transfer.ImportAsync(
            file, AppSettings.Default, TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeTrue(result.Failure);
        result.IsPartial.ShouldBe(expected, because);
    }

    [Fact]
    public async Task An_import_between_matching_builds_is_not_partial()
    {
        string file = PathFor("matching.json");

        await _transfer.ExportAsync(file, Customised(), TestContext.Current.CancellationToken);

        PreferencesImportResult result = await _transfer.ImportAsync(
            file, AppSettings.Default, TestContext.Current.CancellationToken);

        result.IsPartial.ShouldBeFalse();
        result.Notes.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_clean_import_says_so_and_nothing_more()
    {
        string file = PathFor("clean.json");

        await _transfer.ExportAsync(file, Customised(), TestContext.Current.CancellationToken);

        PreferencesImportResult result = await _transfer.ImportAsync(
            file, AppSettings.Default, TestContext.Current.CancellationToken);

        string report = result.Describe();

        report.ShouldStartWith($"Applied {result.Applied} preferences.");
        report.ShouldContain("Marqora 9.9.9");
        report.ShouldNotContain("skipped");
    }
}
