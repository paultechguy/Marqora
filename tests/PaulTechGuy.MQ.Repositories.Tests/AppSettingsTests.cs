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
    public async Task Preferences_added_since_default_to_the_previous_behavior()
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

        // Images. A folder per document is what the feature shipped with; the width cap is on,
        // because a 4K screenshot is several megabytes and wider than the preview shows it; and
        // a file the user picked is copied as it is, so that one is off.
        settings.ImageFolder.ShouldBe(ImageFolderMode.DocumentAssets);
        settings.LimitPastedImageWidth.ShouldBeTrue();
        settings.MaxPastedImageWidth.ShouldBe(1920);
        settings.DownscaleImageFiles.ShouldBeFalse();

        // The accessibility nudge arrives on, like spell checking did, and a file written
        // before it existed must come up with it on rather than silently without.
        settings.CheckImageAltText.ShouldBeTrue();

        // Marking pictures that will not appear arrives on too. It reports something no other
        // surface in the app explains, so a settings file written before it existed must come
        // back with it on - silently off would leave those blank boxes as unexplained as they
        // have always been, which is the whole thing it was added to fix.
        settings.ShowBlockedImages.ShouldBeTrue();

        // Spell checking arrived on by default, and a settings file written before it existed
        // must come back with it on rather than silently off.
        settings.SpellCheckEnabled.ShouldBeTrue();
        settings.HighlightCurrentLine.ShouldBeTrue();
        settings.ContinueLists.ShouldBeTrue();
        settings.AutoCloseBrackets.ShouldBeTrue();

        // Nothing that alters a document or a file may arrive switched on.
        settings.HeadingNumbering.ShouldBe(HeadingNumbering.Off);
        settings.AutoSave.ShouldBe(AutoSaveMode.Off);
        settings.NewFileLineEnding.ShouldBe(LineEndingStyle.Detect);
        settings.WriteUtf8Bom.ShouldBeFalse();

        // Find All listed matches and waited to be told which one to go to, so a file written
        // before this existed must not start jumping to the first one - and possibly to
        // another tab - the moment a search finishes.
        settings.FindSelectFirstResult.ShouldBeFalse();

        // Session restore was unconditional before it was a preference.
        settings.Startup.ShouldBe(StartupBehavior.RestoreSession);
        settings.RecentFilesLimit.ShouldBe(AppSettings.DefaultRecentFilesLimit);

        // Complex members follow the nullable-plus-accessor pattern, so the accessor has to
        // answer even though the key is absent.
        settings.PdfSetup.ShouldBeNull();
        settings.PdfDefaults.ShouldBe(PdfPageSetup.Default);

        // The Word setup arrived later still, and starts from the PDF one so that somebody
        // who already works in A4 does not have to say so twice. A file written before Word
        // export existed has neither key, and must still answer with Letter rather than with
        // a record of zeroes.
        settings.DocxSetup.ShouldBeNull();
        settings.DocxDefaults.ShouldBe(DocxExportSetup.SeededFrom(PdfPageSetup.Default));
        settings.DocxDefaults.IncludeHeaderAndFooter.ShouldBeTrue();
        settings.DocxDefaults.IncludeTableOfContents.ShouldBeFalse();
        settings.DocxDefaults.IncludeCoverPage.ShouldBeFalse();
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
            ShowBlockedImages = false,
            HeadingNumbering = HeadingNumbering.FromHeading2,
            Startup = StartupBehavior.EmptyTab,
            RecentFilesLimit = 30,
            AutoSave = AutoSaveMode.AfterDelay,
            AutoSaveDelaySeconds = 45,
            NewFileLineEnding = LineEndingStyle.Lf,
            WriteUtf8Bom = true,
            PdfSetup = new PdfPageSetup { Paper = PaperSize.A4, Orientation = PageOrientation.Landscape },
            DocxSetup = new DocxExportSetup
            {
                Paper = PaperSize.Legal,

                // Word's own margin presets, not the PDF's - they share some names and none of
                // their measurements, which is why they are separate enums.
                Margin = PageMargin.Moderate,
                IncludeTableOfContents = true,
                IncludeCoverPage = true,
            },
            LogRetentionDays = 30,
            SpellCheckEnabled = false,
            FindSelectFirstResult = true,
            UpdateReminderDays = 60,

            // The reminder's own record, which is state rather than a preference but is written
            // to the same file. A DateTimeOffset is the only one of its kind in here, and if the
            // source-generated context could not carry it the clock would silently reset on every
            // launch - which looks exactly like a reminder that simply never comes due.
            LastUpdateReminderUtc = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero),
            LastUpdateReminderVersion = "0.2.2",
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

    /// <summary>
    /// Computed values do not belong in the settings file.
    ///
    /// A read-only property is serialized by default and ignored on the way back in, so one
    /// that derives from another setting gets written out as a fact and then never read - a
    /// stale record of a calculation, sitting in the file looking authoritative. Both page
    /// setups had several: the page size in inches, the margin in inches. They cost nothing to
    /// write and are actively misleading to anyone reading the file to find out what the app
    /// thinks.
    /// </summary>
    [Fact]
    public async Task Derived_measurements_are_not_written_to_the_settings_file()
    {
        var written = AppSettings.Default with
        {
            PdfSetup = new PdfPageSetup { Paper = PaperSize.A4 },
            DocxSetup = new DocxExportSetup { Margin = PageMargin.Wide },
        };

        var repository = new JsonSettingsRepository(_paths, NullLogger<JsonSettingsRepository>.Instance);

        try
        {
            await repository.SaveAsync(written, TestContext.Current.CancellationToken);

            string json = await File.ReadAllTextAsync(
                _paths.SettingsFilePath,
                TestContext.Current.CancellationToken);

            json.ShouldNotContain("marginInches");
            json.ShouldNotContain("widthInches");
            json.ShouldNotContain("heightInches");
            json.ShouldNotContain("verticalMarginInches");
            json.ShouldNotContain("horizontalMarginInches");

            // The answers themselves are still there; it is only the arithmetic that is not.
            json.ShouldContain("\"margin\": \"Wide\"");
            json.ShouldContain("\"paper\": \"A4\"");
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
