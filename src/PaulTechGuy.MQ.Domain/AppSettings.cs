// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// User-facing settings persisted between sessions. Every member has a sensible
/// default so a missing or partially corrupt settings file still yields a usable app.
///
/// The properties are <c>set</c> rather than <c>init</c>, and that is load-bearing rather
/// than an oversight.
///
/// System.Text.Json treats an init-only property as a constructor parameter, because that is
/// the only point at which it could assign one. The source generator then emits a single
/// object initializer that assigns <em>every</em> property from an argument array, and any
/// property the JSON did not mention arrives as <c>default(T)</c> - so the initializers on
/// this type were silently overwritten with zero, false and null. A settings file written by
/// an earlier build, which is what every existing user has, therefore came back with word
/// wrap off, line numbers off, diagnostics off and a source font size of nothing.
///
/// An ordinary setter makes the generator construct the object normally and assign only the
/// properties actually present, which is what leaves the defaults below meaning what they
/// say. The record is still only ever changed through <c>with</c>, so nothing mutates one of
/// these in place; the setter exists for the deserializer's benefit.
///
/// The same applies to every record that is persisted - <see cref="WindowPlacement"/>,
/// <see cref="FormatOptions"/>, <see cref="PdfPageSetup"/> and <see cref="RecentFile"/> - and
/// AppSettingsTests holds the test that catches it coming back.
/// </summary>
public sealed record AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;

    public ViewMode ViewMode { get; set; } = ViewMode.SideBySide;

    public int SourceZoomPercent { get; set; } = ZoomLevel.Default;

    public int PreviewZoomPercent { get; set; } = ZoomLevel.Default;

    public bool ScrollSyncEnabled { get; set; } = true;

    public bool WordWrapEnabled { get; set; } = true;

    public bool ShowLineNumbers { get; set; } = true;

    /// <summary>Render spaces and tabs in the source pane.</summary>
    public bool ShowWhitespace { get; set; }

    /// <summary>Mark where a wrapped source line continues, in the manner of a text editor.</summary>
    public bool ShowWrapGlyph { get; set; }

    /// <summary>
    /// Underline broken links, missing images and the style rules the formatter would fix.
    /// On by default: a dead link renders exactly like a live one, so nothing else in the
    /// app would ever tell you about it.
    /// </summary>
    public bool ShowDiagnostics { get; set; } = true;

    /// <summary>Reload the document automatically when it changes on disk and has no unsaved edits.</summary>
    public bool ReloadOnExternalChange { get; set; } = true;

    /// <summary>Fraction of the content width given to the source pane in side-by-side view.</summary>
    public double SplitterPosition { get; set; } = 0.5;

    public WindowPlacement Window { get; set; } = WindowPlacement.Default;

    /// <summary>
    /// Geometry of the floating cheatsheet window.
    ///
    /// Nullable rather than defaulted, because the default it would need -
    /// <see cref="DefaultCheatsheetWindow"/> - is not the one <see cref="Window"/> uses, and
    /// a null here says "never placed" without a sentinel size having to stand for it. Read
    /// it through <see cref="CheatsheetPlacement"/>, which never returns null.
    /// </summary>
    public WindowPlacement? CheatsheetWindow { get; set; }

    /// <summary>
    /// The cheatsheet's geometry, safe to use whatever the settings file held. It carries
    /// its own default rather than <see cref="WindowPlacement.Default"/>, which is sized for
    /// the main window.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public WindowPlacement CheatsheetPlacement => CheatsheetWindow ?? DefaultCheatsheetWindow;

    /// <summary>A tall, narrow reference panel: wide enough for a table, narrow enough to sit beside the editor.</summary>
    public static WindowPlacement DefaultCheatsheetWindow { get; } = new() { Width = 560, Height = 840 };

    /// <summary>
    /// Where the cheatsheet was scrolled to, in CSS pixels from the top.
    ///
    /// An offset rather than a fraction of the document: the window size is restored
    /// alongside it, so the same offset puts the same text under the same edge. A fraction
    /// would survive a resize better but would drift whenever the cheatsheet was edited.
    /// </summary>
    public int CheatsheetScrollTop { get; set; }

    /// <summary>
    /// Geometry of the Find All window. Nullable for the same reason as
    /// <see cref="CheatsheetWindow"/>; read it through <see cref="FindAllPlacement"/>.
    /// </summary>
    public WindowPlacement? FindAllWindow { get; set; }

    /// <summary>Find All's geometry, safe to use whatever the settings file held.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public WindowPlacement FindAllPlacement => FindAllWindow ?? DefaultFindAllWindow;

    /// <summary>Wide enough for a line of markdown beside its line number, deep enough to scan.</summary>
    public static WindowPlacement DefaultFindAllWindow { get; } = new() { Width = 760, Height = 560 };

    /// <summary>
    /// Find All's switches, remembered so a search never has to be set up twice. They are
    /// separate from anything the editor's own find widget keeps, which lives in Monaco.
    /// </summary>
    public bool FindMatchCase { get; set; }

    public bool FindWholeWord { get; set; }

    public bool FindUseRegex { get; set; }

    public FindScope FindScope { get; set; } = FindScope.AllDocuments;

    /// <summary>
    /// Recent search terms, most recent first. Nullable for the same reason as
    /// <see cref="OpenDocuments"/>; read it through <see cref="RecentSearches"/>.
    /// </summary>
    public List<string>? FindHistory { get; set; }

    /// <summary>The recent-search list, safe to enumerate whatever the settings file held.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> RecentSearches => FindHistory ?? [];

    /// <summary>
    /// Documents to reopen at launch, in tab order. Untitled documents are absent because
    /// they have no path to reopen from.
    ///
    /// Nullable, and read through <see cref="DocumentsToRestore"/>, which never returns null.
    ///
    /// An empty list and "no list at all" mean the same thing here, so there is nothing to
    /// gain from an initializer that would have to be allocated for every settings object
    /// ever constructed. The accessor is what spares every caller the null check.
    /// </summary>
    public List<string>? OpenDocuments { get; set; }

    /// <summary>The open-document list, safe to enumerate whatever the settings file held.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> DocumentsToRestore => OpenDocuments ?? [];

    /// <summary>Index into <see cref="OpenDocuments"/> that was active when the app closed.</summary>
    public int ActiveDocumentIndex { get; set; }

    /// <summary>
    /// The version whose welcome document has already been shown, or null if none has.
    ///
    /// The whole trigger for the welcome document is this string differing from the running
    /// build's version, so a settings file from an earlier build - which has no such key -
    /// correctly reads as "not yet shown".
    /// </summary>
    public string? LastWelcomeVersion { get; set; }

    /// <summary>
    /// Which formatter rules are switched on. Nullable for the same reason as
    /// <see cref="OpenDocuments"/>; read it through <see cref="Formatting"/>.
    /// </summary>
    public FormatOptions? FormatRules { get; set; }

    /// <summary>The formatter rules, safe to use whatever the settings file held.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public FormatOptions Formatting => FormatRules ?? FormatOptions.Default;

    // ------------------------------------------------------------------ typography

    /// <summary>
    /// Source pane font, or null to keep the stylesheet's stack.
    ///
    /// Null rather than the name of a font: --mq-font-mono in app.css names four faces so a
    /// machine without Cascadia Code still gets something monospaced, and naming only the
    /// first of them here would throw the rest away.
    /// </summary>
    public string? SourceFontFamily { get; set; }

    public int SourceFontSize { get; set; } = TypographyDefaults.SourceFontSize;

    /// <summary>Preview font, or null to keep the stylesheet's stack.</summary>
    public string? PreviewFontFamily { get; set; }

    public double PreviewFontSize { get; set; } = TypographyDefaults.PreviewFontSize;

    /// <summary>Widest the preview column gets, or zero - the default - for no limit.</summary>
    public int PreviewMaxWidth { get; set; } = TypographyDefaults.UnlimitedPreviewWidth;

    // --------------------------------------------------------------------- editor

    public int TabSize { get; set; } = 4;

    /// <summary>Insert spaces when Tab is pressed rather than a tab character.</summary>
    public bool InsertSpaces { get; set; } = true;

    /// <summary>Off by default: the source pane is narrow in split view, and a minimap costs width.</summary>
    public bool ShowMinimap { get; set; }

    public bool HighlightCurrentLine { get; set; } = true;

    /// <summary>Carry a list marker onto the next line when Enter is pressed.</summary>
    public bool ContinueLists { get; set; } = true;

    /// <summary>Close a bracket, quote or emphasis marker as it is typed.</summary>
    public bool AutoCloseBrackets { get; set; } = true;

    // -------------------------------------------------------------------- preview

    /// <summary>
    /// Whether the preview numbers headings, and from which level.
    ///
    /// Rendered by the stylesheet, never written into the document, so switching it on and
    /// off cannot alter a file.
    /// </summary>
    public HeadingNumbering HeadingNumbering { get; set; }

    // ---------------------------------------------------------------------- files

    /// <summary>What a launch with no file named on the command line opens.</summary>
    public StartupBehavior Startup { get; set; } = StartupBehavior.RestoreSession;

    /// <summary>How many unpinned entries the recent list keeps. Pinned entries are never dropped.</summary>
    public int RecentFilesLimit { get; set; } = DefaultRecentFilesLimit;

    public const int DefaultRecentFilesLimit = 15;

    public const int MinimumRecentFilesLimit = 5;

    public const int MaximumRecentFilesLimit = 50;

    public AutoSaveMode AutoSave { get; set; } = AutoSaveMode.Off;

    /// <summary>Seconds of quiet before <see cref="AutoSaveMode.AfterDelay"/> writes.</summary>
    public int AutoSaveDelaySeconds { get; set; } = DefaultAutoSaveDelaySeconds;

    public const int DefaultAutoSaveDelaySeconds = 30;

    public const int MinimumAutoSaveDelaySeconds = 5;

    public const int MaximumAutoSaveDelaySeconds = 600;

    /// <summary>
    /// Line ending written into a document that has never been saved.
    ///
    /// A file that already exists keeps whatever it uses: the workspace reads each document's
    /// ending along with its encoding and writes the same back, and this preference does not
    /// override that. Detect - the default - means a new file gets the platform's own.
    /// </summary>
    public LineEndingStyle NewFileLineEnding { get; set; } = LineEndingStyle.Detect;

    /// <summary>
    /// Write a UTF-8 byte order mark into a document that has never been saved.
    ///
    /// Off by default, matching what the workspace has always written. An existing file keeps
    /// the encoding it was read with either way.
    /// </summary>
    public bool WriteUtf8Bom { get; set; }

    // ----------------------------------------------------------- export and print

    /// <summary>
    /// Page setup the PDF export dialog opens on. Nullable for the same reason as
    /// <see cref="FormatRules"/>; read it through <see cref="PdfDefaults"/>.
    /// </summary>
    public PdfPageSetup? PdfSetup { get; set; }

    /// <summary>The PDF page setup, safe to use whatever the settings file held.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public PdfPageSetup PdfDefaults => PdfSetup ?? PdfPageSetup.Default;

    // ------------------------------------------------------------------- advanced

    /// <summary>Days a log file is kept before it is swept up. Zero keeps them indefinitely.</summary>
    public int LogRetentionDays { get; set; } = DefaultLogRetentionDays;

    public const int DefaultLogRetentionDays = 14;

    public const int MaximumLogRetentionDays = 365;

    public static AppSettings Default => new();

    /// <summary>
    /// Every preference back to its default, with the session's own record of itself left
    /// alone.
    ///
    /// The settings file holds two different kinds of thing: preferences, which the user
    /// chose, and state, which the app recorded about itself - where the window was, which
    /// documents were open, what was searched for. "Restore defaults" means the first of
    /// those. Closing every tab and forgetting the window position would be a surprise, and
    /// not one the button offered to perform.
    ///
    /// See <see cref="WithSessionOf"/> for why the carried-over members are listed by hand.
    /// </summary>
    public static AppSettings ResetPreferences(AppSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);

        return Default.WithSessionOf(current);
    }

    /// <summary>
    /// These preferences, carrying <paramref name="current"/>'s record of the session.
    ///
    /// Used wherever a set of preferences has to be put back without disturbing what the app
    /// has since recorded about itself: restoring defaults, and abandoning a preferences
    /// dialog. In both, the user asked about their settings and said nothing at all about
    /// their open tabs or where the window is.
    ///
    /// It also gives a way to ask whether two settings differ *as preferences*, by
    /// normalising both onto the same session first - which is how the dialog knows whether
    /// there is anything to discard.
    ///
    /// The members are listed by name rather than discovered by reflection: the persistence
    /// layer is source-generated precisely so that the app stays trim- and AOT-friendly, and
    /// a reflective walk here would undo that. The cost is that a new piece of session state
    /// has to be added to this list, which is why they are grouped together and commented as
    /// state above.
    /// </summary>
    public AppSettings WithSessionOf(AppSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);

        return this with
        {
            Window = current.Window,
            CheatsheetWindow = current.CheatsheetWindow,
            CheatsheetScrollTop = current.CheatsheetScrollTop,
            FindAllWindow = current.FindAllWindow,
            FindHistory = current.FindHistory,
            OpenDocuments = current.OpenDocuments,
            ActiveDocumentIndex = current.ActiveDocumentIndex,
            SplitterPosition = current.SplitterPosition,
            LastWelcomeVersion = current.LastWelcomeVersion,
        };
    }
}
