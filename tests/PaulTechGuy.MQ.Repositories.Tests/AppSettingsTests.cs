// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Repositories.Tests;

/// <summary>
/// The two things about the settings record that are easy to break and impossible to notice.
///
/// The first is upgrading: every preference added after a release has to default to what the
/// app did before it existed, or installing a new build silently changes how the old one
/// behaved. A settings file with none of the new keys in it is exactly what every existing
/// user has, so that is what these load.
///
/// The second is the scope of Restore Defaults. It resets preferences and must leave the
/// session's record of itself alone; a reset that closed every tab and forgot the window
/// position would be a nasty surprise from a button that only offered to reset settings.
/// </summary>
public sealed class AppSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "marqora-settings", Guid.NewGuid().ToString("n"));

    private readonly AppPaths _paths;

    public AppSettingsTests()
    {
        _paths = new AppPaths(_root, _root);
        _paths.EnsureCreated();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>A settings file as an earlier build would have written it: no new keys.</summary>
    private const string LegacySettings = """
        {
          "theme": "Dark",
          "viewMode": "SideBySide",
          "sourceZoomPercent": 125,
          "wordWrapEnabled": false,
          "splitterPosition": 0.42,
          "openDocuments": [ "C:\\notes\\one.md", "C:\\notes\\two.md" ],
          "activeDocumentIndex": 1,
          "window": { "width": 1280, "height": 820 }
        }
        """;

    private async Task<AppSettings> LoadAsync(string json)
    {
        await File.WriteAllTextAsync(_paths.SettingsFilePath, json);

        var repository = new JsonSettingsRepository(_paths, NullLogger<JsonSettingsRepository>.Instance);

        try
        {
            return await repository.LoadAsync();
        }
        finally
        {
            repository.Dispose();
        }
    }

    [Fact]
    public async Task A_settings_file_from_an_earlier_build_keeps_what_it_said()
    {
        AppSettings settings = await LoadAsync(LegacySettings);

        settings.Theme.ShouldBe(AppTheme.Dark);
        settings.SourceZoomPercent.ShouldBe(125);
        settings.WordWrapEnabled.ShouldBeFalse();
        settings.SplitterPosition.ShouldBe(0.42);
        settings.DocumentsToRestore.Count.ShouldBe(2);
        settings.ActiveDocumentIndex.ShouldBe(1);
    }

    [Fact]
    public async Task Preferences_added_since_default_to_the_previous_behaviour()
    {
        AppSettings settings = await LoadAsync(LegacySettings);

        // Typography: no font named means the stylesheet's own stack, and the sizes are the
        // ones that were hardcoded in app.js and app.css.
        settings.SourceFontFamily.ShouldBeNull();
        settings.PreviewFontFamily.ShouldBeNull();
        settings.SourceFontSize.ShouldBe(TypographyDefaults.SourceFontSize);
        settings.PreviewFontSize.ShouldBe(TypographyDefaults.PreviewFontSize);

        // The preview filled its pane before this existed, and still must.
        settings.PreviewMaxWidth.ShouldBe(TypographyDefaults.UnlimitedPreviewWidth);

        settings.TabSize.ShouldBe(4);
        settings.InsertSpaces.ShouldBeTrue();
        settings.ShowMinimap.ShouldBeFalse();
        settings.HighlightCurrentLine.ShouldBeTrue();
        settings.ContinueLists.ShouldBeTrue();
        settings.AutoCloseBrackets.ShouldBeTrue();

        // Nothing that alters a document or a file may arrive switched on.
        settings.HeadingNumbering.ShouldBe(HeadingNumbering.Off);
        settings.AutoSave.ShouldBe(AutoSaveMode.Off);
        settings.NewFileLineEnding.ShouldBe(LineEndingStyle.Detect);
        settings.WriteUtf8Bom.ShouldBeFalse();

        // Session restore was unconditional before it was a preference.
        settings.Startup.ShouldBe(StartupBehavior.RestoreSession);
        settings.RecentFilesLimit.ShouldBe(AppSettings.DefaultRecentFilesLimit);

        // Complex members follow the nullable-plus-accessor pattern, so the accessor has to
        // answer even though the key is absent.
        settings.PdfSetup.ShouldBeNull();
        settings.PdfDefaults.ShouldBe(PdfPageSetup.Default);
    }

    [Fact]
    public async Task Every_new_preference_survives_a_round_trip()
    {
        var written = AppSettings.Default with
        {
            SourceFontFamily = "Fira Code",
            SourceFontSize = 17,
            PreviewFontFamily = "Georgia",
            PreviewFontSize = 18.5,
            PreviewMaxWidth = 900,
            TabSize = 2,
            InsertSpaces = false,
            ShowMinimap = true,
            HighlightCurrentLine = false,
            ContinueLists = false,
            AutoCloseBrackets = false,
            HeadingNumbering = HeadingNumbering.FromHeading2,
            Startup = StartupBehavior.EmptyTab,
            RecentFilesLimit = 30,
            AutoSave = AutoSaveMode.AfterDelay,
            AutoSaveDelaySeconds = 45,
            NewFileLineEnding = LineEndingStyle.Lf,
            WriteUtf8Bom = true,
            PdfSetup = new PdfPageSetup { Paper = PaperSize.A4, Orientation = PageOrientation.Landscape },
            LogRetentionDays = 30,
        };

        var repository = new JsonSettingsRepository(_paths, NullLogger<JsonSettingsRepository>.Instance);

        try
        {
            await repository.SaveAsync(written);

            AppSettings read = await repository.LoadAsync();

            read.ShouldBe(written);
        }
        finally
        {
            repository.Dispose();
        }
    }

    [Fact]
    public void Restoring_defaults_resets_preferences()
    {
        var current = AppSettings.Default with
        {
            Theme = AppTheme.Dark,
            HeadingNumbering = HeadingNumbering.FromHeading1,
            SourceFontSize = 22,
            AutoSave = AutoSaveMode.OnFocusLoss,
            RecentFilesLimit = 40,
        };

        AppSettings reset = AppSettings.ResetPreferences(current);

        reset.Theme.ShouldBe(AppSettings.Default.Theme);
        reset.HeadingNumbering.ShouldBe(HeadingNumbering.Off);
        reset.SourceFontSize.ShouldBe(TypographyDefaults.SourceFontSize);
        reset.AutoSave.ShouldBe(AutoSaveMode.Off);
        reset.RecentFilesLimit.ShouldBe(AppSettings.DefaultRecentFilesLimit);
    }

    [Fact]
    public void Restoring_defaults_leaves_the_session_alone()
    {
        var current = AppSettings.Default with
        {
            Theme = AppTheme.Dark,
            OpenDocuments = ["C:\\notes\\one.md", "C:\\notes\\two.md"],
            ActiveDocumentIndex = 1,
            SplitterPosition = 0.37,
            Window = new WindowPlacement { Width = 1280, Height = 820 },
            FindHistory = ["needle"],
            CheatsheetScrollTop = 420,
            LastWelcomeVersion = "1.2.3",
        };

        AppSettings reset = AppSettings.ResetPreferences(current);

        reset.DocumentsToRestore.Count.ShouldBe(2);
        reset.ActiveDocumentIndex.ShouldBe(1);
        reset.SplitterPosition.ShouldBe(0.37);
        reset.Window.ShouldBe(current.Window);
        reset.RecentSearches.Count.ShouldBe(1);
        reset.CheatsheetScrollTop.ShouldBe(420);

        // Resetting this would reintroduce the release's welcome document on the next launch.
        reset.LastWelcomeVersion.ShouldBe("1.2.3");
    }

    /// <summary>
    /// The preferences dialog decides whether Cancel has anything to undo by normalising the
    /// settings it opened with onto the session as it now stands, and comparing. If that
    /// comparison reported a difference for session state alone, the dialog would ask people
    /// whether they meant to change their settings every time the window moved.
    /// </summary>
    [Fact]
    public void Session_state_alone_does_not_read_as_a_preference_change()
    {
        AppSettings opening = AppSettings.Default with { Theme = AppTheme.Dark };

        AppSettings later = opening with
        {
            Window = new WindowPlacement { Width = 1400, Height = 900 },
            OpenDocuments = ["C:\\notes\\three.md"],
            ActiveDocumentIndex = 0,
            SplitterPosition = 0.61,
            CheatsheetScrollTop = 90,
        };

        opening.WithSessionOf(later).ShouldBe(later);
    }

    [Fact]
    public void A_changed_preference_does_read_as_a_change()
    {
        AppSettings opening = AppSettings.Default with { Theme = AppTheme.Dark };

        AppSettings later = opening with
        {
            Window = new WindowPlacement { Width = 1400, Height = 900 },
            SourceFontSize = 20,
        };

        opening.WithSessionOf(later).ShouldNotBe(later);
    }

    [Fact]
    public void Carrying_the_session_over_keeps_this_records_preferences()
    {
        AppSettings target = AppSettings.Default with { Theme = AppTheme.Light, TabSize = 8 };

        AppSettings current = AppSettings.Default with
        {
            Theme = AppTheme.Dark,
            TabSize = 2,
            ActiveDocumentIndex = 3,
            CheatsheetScrollTop = 120,
        };

        AppSettings merged = target.WithSessionOf(current);

        merged.Theme.ShouldBe(AppTheme.Light);
        merged.TabSize.ShouldBe(8);
        merged.ActiveDocumentIndex.ShouldBe(3);
        merged.CheatsheetScrollTop.ShouldBe(120);
    }

    [Fact]
    public async Task The_synchronous_reader_agrees_with_the_repository()
    {
        AppSettings loaded = await LoadAsync(LegacySettings);

        // Logging is configured before the container exists and reads the file this way, so
        // the two doors must not disagree about what the file says.
        AppSettings direct = SettingsFile.ReadOrDefault(_paths.SettingsFilePath);

        // Field by field rather than whole-record: AppSettings holds List<string> members,
        // and a record compares those by reference, so two separately parsed copies of the
        // same file are never equal to each other however identical their contents.
        direct.Theme.ShouldBe(loaded.Theme);
        direct.SourceZoomPercent.ShouldBe(loaded.SourceZoomPercent);
        direct.WordWrapEnabled.ShouldBe(loaded.WordWrapEnabled);
        direct.SplitterPosition.ShouldBe(loaded.SplitterPosition);
        direct.LogRetentionDays.ShouldBe(loaded.LogRetentionDays);
        direct.DocumentsToRestore.ShouldBe(loaded.DocumentsToRestore);
    }

    [Fact]
    public void The_synchronous_reader_falls_back_rather_than_throwing()
    {
        SettingsFile.ReadOrDefault(Path.Combine(_root, "no-such-file.json"))
            .ShouldBe(AppSettings.Default);

        string broken = Path.Combine(_root, "broken.json");
        File.WriteAllText(broken, "{ not json at all");

        SettingsFile.ReadOrDefault(broken).ShouldBe(AppSettings.Default);
    }
}
