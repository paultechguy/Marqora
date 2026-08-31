// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// User-facing settings persisted between sessions. Every member has a sensible
/// default so a missing or partially corrupt settings file still yields a usable app.
/// </summary>
public sealed record AppSettings
{
    public AppTheme Theme { get; init; } = AppTheme.System;

    public ViewMode ViewMode { get; init; } = ViewMode.SideBySide;

    public int SourceZoomPercent { get; init; } = ZoomLevel.Default;

    public int PreviewZoomPercent { get; init; } = ZoomLevel.Default;

    public bool ScrollSyncEnabled { get; init; } = true;

    public bool WordWrapEnabled { get; init; } = true;

    public bool ShowLineNumbers { get; init; } = true;

    /// <summary>Render spaces and tabs in the source pane.</summary>
    public bool ShowWhitespace { get; init; }

    /// <summary>Mark where a wrapped source line continues, in the manner of a text editor.</summary>
    public bool ShowWrapGlyph { get; init; }

    /// <summary>
    /// Underline broken links, missing images and the style rules the formatter would fix.
    /// On by default: a dead link renders exactly like a live one, so nothing else in the
    /// app would ever tell you about it.
    /// </summary>
    public bool ShowDiagnostics { get; init; } = true;

    /// <summary>Reload the document automatically when it changes on disk and has no unsaved edits.</summary>
    public bool ReloadOnExternalChange { get; init; } = true;

    /// <summary>Fraction of the content width given to the source pane in side-by-side view.</summary>
    public double SplitterPosition { get; init; } = 0.5;

    public WindowPlacement Window { get; init; } = WindowPlacement.Default;

    /// <summary>
    /// Geometry of the floating cheatsheet window.
    ///
    /// Nullable for the same reason as <see cref="OpenDocuments"/>: a settings file written
    /// by an earlier build has no such key, and the source-generated deserializer leaves the
    /// property null rather than applying the initializer. Read it through
    /// <see cref="CheatsheetPlacement"/>, which never returns null.
    /// </summary>
    public WindowPlacement? CheatsheetWindow { get; init; }

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
    public int CheatsheetScrollTop { get; init; }

    /// <summary>
    /// Geometry of the Find All window. Nullable for the same reason as
    /// <see cref="CheatsheetWindow"/>; read it through <see cref="FindAllPlacement"/>.
    /// </summary>
    public WindowPlacement? FindAllWindow { get; init; }

    /// <summary>Find All's geometry, safe to use whatever the settings file held.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public WindowPlacement FindAllPlacement => FindAllWindow ?? DefaultFindAllWindow;

    /// <summary>Wide enough for a line of markdown beside its line number, deep enough to scan.</summary>
    public static WindowPlacement DefaultFindAllWindow { get; } = new() { Width = 760, Height = 560 };

    /// <summary>
    /// Find All's switches, remembered so a search never has to be set up twice. They are
    /// separate from anything the editor's own find widget keeps, which lives in Monaco.
    /// </summary>
    public bool FindMatchCase { get; init; }

    public bool FindWholeWord { get; init; }

    public bool FindUseRegex { get; init; }

    public FindScope FindScope { get; init; } = FindScope.AllDocuments;

    /// <summary>
    /// Recent search terms, most recent first. Nullable for the same reason as
    /// <see cref="OpenDocuments"/>; read it through <see cref="RecentSearches"/>.
    /// </summary>
    public List<string>? FindHistory { get; init; }

    /// <summary>The recent-search list, safe to enumerate whatever the settings file held.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> RecentSearches => FindHistory ?? [];

    /// <summary>
    /// Documents to reopen at launch, in tab order. Untitled documents are absent because
    /// they have no path to reopen from.
    ///
    /// Nullable because a settings file written by an earlier build has no such key, and the
    /// serializer leaves the property null rather than applying the initializer. Read it
    /// through <see cref="DocumentsToRestore"/>, which never returns null.
    /// </summary>
    public List<string>? OpenDocuments { get; init; }

    /// <summary>The open-document list, safe to enumerate whatever the settings file held.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> DocumentsToRestore => OpenDocuments ?? [];

    /// <summary>Index into <see cref="OpenDocuments"/> that was active when the app closed.</summary>
    public int ActiveDocumentIndex { get; init; }

    /// <summary>
    /// The version whose welcome document has already been shown, or null if none has.
    ///
    /// The whole trigger for the welcome document is this string differing from the running
    /// build's version, so a settings file from an earlier build - which has no such key -
    /// correctly reads as "not yet shown".
    /// </summary>
    public string? LastWelcomeVersion { get; init; }

    /// <summary>
    /// Which formatter rules are switched on. Nullable for the same reason as
    /// <see cref="OpenDocuments"/>; read it through <see cref="Formatting"/>.
    /// </summary>
    public FormatOptions? FormatRules { get; init; }

    /// <summary>The formatter rules, safe to use whatever the settings file held.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public FormatOptions Formatting => FormatRules ?? FormatOptions.Default;

    public static AppSettings Default => new();
}
