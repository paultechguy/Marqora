// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Folders;

namespace PaulTechGuy.MQ.App.ViewModels;

/// <summary>What a row's marker says about the document open at its path, if one is.</summary>
public enum FolderRowMarker
{
    None,
    Open,
    ReadOnly,
    Unsaved,
    Changed,
    Missing,
}

/// <summary>
/// One visible row of the folder panel.
///
/// Updated in place rather than replaced, so a row that changes (expanded, grayed, its document
/// saved) keeps its container, and with it the keyboard's place and Narrator's (P1). The view
/// draws the row in code and listens to <see cref="ObservableObject.PropertyChanged"/>.
/// </summary>
public sealed partial class FolderRowViewModel : ObservableObject
{
    public FolderRowViewModel(FolderRow row) => Row = row;

    /// <summary>The library's row: path, name, depth, kind, expansion and place among its siblings.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tooltip))]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    public partial FolderRow Row { get; set; }

    /// <summary>The active document's row wears the teal bar. A row property, not list selection (P1).</summary>
    [ObservableProperty]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tooltip))]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    public partial FolderRowMarker Marker { get; set; }

    /// <summary>The row's name is being edited in place (F2, or Rename on its menu).</summary>
    [ObservableProperty]
    public partial bool IsRenaming { get; set; }

    /// <summary>Why the name typed so far cannot be used, shown under the box while it stays open.</summary>
    [ObservableProperty]
    public partial string? RenameError { get; set; }

    public string FullPath => Row.FullPath;

    public string Name => Row.Name;

    public bool IsFolder => Row.IsFolder;

    public bool IsMarkdown => Row.Kind == FolderEntryKind.Markdown;

    public bool IsImage => Row.Kind == FolderEntryKind.Image;

    /// <summary>The marker for a document, in the tab strip's order of importance.</summary>
    public static FolderRowMarker MarkerFor(MarkdownDocument? document) => document switch
    {
        null => FolderRowMarker.None,
        { External: ExternalState.Missing } => FolderRowMarker.Missing,
        { External: ExternalState.Changed } => FolderRowMarker.Changed,
        { IsDirty: true } => FolderRowMarker.Unsaved,
        { IsReadOnly: true } => FolderRowMarker.ReadOnly,
        _ => FolderRowMarker.Open,
    };

    /// <summary>The full path, and whatever the row's dimming or marker means (P15: the tooltip is enough).</summary>
    public string Tooltip
    {
        get
        {
            string text = Row.FullPath;

            if (Row.Kind == FolderEntryKind.Image)
            {
                text += "\nImage";
            }
            else if (Row.Kind == FolderEntryKind.Other)
            {
                text += "\nNot a markdown file";
            }

            if (Row.IsIgnored)
            {
                text += "\nIgnored by .gitignore";
            }

            string? marker = MarkerText(Marker);

            return marker is null ? text : text + "\n" + marker;
        }
    }

    /// <summary>What Narrator reads for the row: the name, then its state.</summary>
    public string AutomationName
    {
        get
        {
            string text = Row.Name;

            if (Row.IsIgnored)
            {
                text += ", ignored by .gitignore";
            }

            if (MarkerText(Marker) is { } marker)
            {
                text += ", " + marker.ToLowerInvariant();
            }

            return text;
        }
    }

    private static string? MarkerText(FolderRowMarker marker) => marker switch
    {
        FolderRowMarker.Open => "Open",
        FolderRowMarker.ReadOnly => "Open, read-only",
        FolderRowMarker.Unsaved => "Open, unsaved changes",
        FolderRowMarker.Changed => "Open, changed on disk",
        FolderRowMarker.Missing => "Open, missing from disk",
        _ => null,
    };
}
