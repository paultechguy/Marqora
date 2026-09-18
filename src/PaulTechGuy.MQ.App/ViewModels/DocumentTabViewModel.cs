// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.App.ViewModels;

/// <summary>
/// One tab in the strip.
///
/// The underlying <see cref="MarkdownDocument"/> is an immutable record that the workspace
/// replaces on every change, so this wrapper holds the current snapshot and raises change
/// notifications when it is swapped. That keeps a stable object identity for the tab, which
/// TabView needs in order to track selection and drag-reordering.
/// </summary>
public sealed partial class DocumentTabViewModel : ObservableObject
{
    private MarkdownDocument _document;

    public DocumentTabViewModel(MarkdownDocument document) => _document = document;

    public Guid Id => _document.Id;

    public MarkdownDocument Document => _document;

    public string Title => _document.DisplayName;

    /// <summary>Shown when the file has gone from disk.</summary>
    public const string MissingMarker = "! ";

    /// <summary>Shown when the file has been written by something else.</summary>
    public const string ChangedMarker = "⟳ ";

    /// <summary>Shown when there is unsaved work in the tab.</summary>
    public const string DirtyMarker = "● ";

    /// <summary>
    /// Every marker <see cref="DisplayTitle"/> can put in front of a name.
    ///
    /// Listed rather than left implicit in the switch below because the tab strip books room
    /// for the widest of them on every tab, showing one or not — see UpdateTabTitles. A marker
    /// added here and nowhere else is booked for automatically; one added only to the switch
    /// would resize its tab the moment it appeared.
    /// </summary>
    public static IReadOnlyList<string> Markers { get; } = [MissingMarker, ChangedMarker, DirtyMarker];

    /// <summary>
    /// The marker in front of this tab's name, or empty when there is none.
    ///
    /// External state outranks the unsaved dot. A missing file is already dirty - that is what
    /// puts Ctrl+S back within reach - so the dot would be true but would say the smaller of
    /// two things.
    ///
    /// An exclamation mark rather than a warning sign: several Windows font stacks render
    /// U+26A0 as a color emoji, which lands at the wrong size and weight beside tab text and
    /// cannot be recolored with it.
    /// </summary>
    public string Marker => _document.External switch
    {
        ExternalState.Missing => MissingMarker,
        ExternalState.Changed => ChangedMarker,
        _ => _document.IsDirty ? DirtyMarker : string.Empty,
    };

    /// <summary>
    /// File name behind a one-glyph marker: what is going on with this tab, in the only space
    /// a tab has to say it in.
    ///
    /// The marker is never shortened. The strip fits the name alone and puts the marker in
    /// front of the result, so the glyph cannot be what a long name loses.
    /// </summary>
    public string DisplayTitle => Marker + Title;

    /// <summary>
    /// Which of <see cref="Markers"/> a display title starts with, or empty for none. Lets the
    /// strip take a title apart again without having to be told which state produced it.
    /// </summary>
    public static string MarkerOf(string displayTitle) =>
        Markers.FirstOrDefault(marker => displayTitle.StartsWith(marker, StringComparison.Ordinal))
        ?? string.Empty;

    /// <summary>
    /// What the tab says on hover.
    ///
    /// Built up rather than chosen, because the reload line composes with all of the others: a
    /// document can have been replaced from disk at noon and then have had its file deleted at
    /// one, and both are worth knowing. It is also the only lasting record that a silent
    /// reload happened - the status message that announces it is shown once and then fades -
    /// so it has to survive whatever else becomes true about the tab afterwards.
    /// </summary>
    public string Tooltip
    {
        get
        {
            string text = _document.External switch
            {
                ExternalState.Missing => _document.DisplayPath + "\nMissing from disk — saving writes it again",
                ExternalState.Changed => _document.DisplayPath + "\nChanged on disk — open this tab to review",
                _ => _document.IsDirty
                    ? _document.DisplayPath + "  (unsaved changes)"
                    : _document.DisplayPath,
            };

            // Local time in the machine's own format: "at 13:42" is wrong for half the world,
            // and this is the one place the exact moment is available to be read.
            if (_document.AutoReloadedUtc is { } reloaded)
            {
                text += "\nReloaded from disk at "
                    + reloaded.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
            }

            // The tab strip carries no glyph for this, so the tooltip is where it is said. It
            // composes with everything above rather than replacing any of it: a read-only
            // document can also hold unsaved edits and have been reloaded this morning, and all
            // three are worth knowing at once. The second line is for the case that reads as a
            // contradiction - unsaved work in a document that will not be written - and points
            // at the way out rather than leaving the reader to find it.
            if (_document.IsLocked)
            {
                text += _document.IsDirty
                    ? "\nRead-only — unmark or Save As to reach the unsaved edits"
                    : "\nRead-only";
            }

            // Said here as well as shown, because what the glyph cannot say is the half that is
            // not about position: a pinned document is also left alone by Close Other Tabs,
            // Close Tabs to the Right and Close All Tabs.
            if (_document.IsPinned)
            {
                text += "\nPinned — kept at the left, and left out of the bulk close commands";
            }

            return text;
        }
    }

    /// <summary>Whether this tab is waiting on a decision about its file.</summary>
    public bool HasExternalChange => _document.HasExternalChange;

    /// <summary>
    /// Whether this is the tab being looked at, which is the only one that shows a close
    /// button.
    ///
    /// A cross on every tab costs the width of a cross on every tab, and the ones you are
    /// not looking at still close with a middle click. Kept here rather than compared
    /// against the active tab in the view, because IsClosable has to be a binding that
    /// re-evaluates when the selection moves.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClosable))]
    public partial bool IsActive { get; set; }

    /// <summary>
    /// Whether this tab draws a close button: the active one, unless it is pinned.
    ///
    /// The pin takes the cross away permanently rather than while the tab is unselected, and
    /// that distinction is what makes the narrower chrome a pinned tab books safe. Were this
    /// still bound to <see cref="IsActive"/> alone, selecting a pinned tab would hand it a close
    /// button and resize it — which is booking chrome per tab *state*, the thing
    /// docs/BROKEN_TAB_BAR.md records as tried and abandoned.
    /// </summary>
    public bool IsClosable => IsActive && !IsPinned;

    public bool IsDirty => _document.IsDirty;

    public bool IsUntitled => _document.IsUntitled;

    /// <summary>Whether this document refuses to be written.</summary>
    public bool IsReadOnly => _document.IsReadOnly;

    /// <summary>
    /// Whether the mark is on, which is not the same question as <see cref="IsReadOnly"/>: a
    /// marked document whose file has been deleted still refuses nothing, because saving is how
    /// the file comes back. The menu's tick follows the mark, so it stays where the user put it.
    /// </summary>
    public bool IsLocked => _document.IsLocked;

    /// <summary>
    /// Whether this document is pinned to the left of the tab strip.
    ///
    /// Read from the document rather than held here, so there is one answer rather than two that
    /// could disagree about where the tab belongs. The strip reads it to place the tab and to
    /// decide how much chrome to book; the close commands read it to leave the tab alone.
    /// </summary>
    public bool IsPinned => _document.IsPinned;

    public string? Path => _document.Path;

    /// <summary>Replaces the snapshot and tells the view what changed.</summary>
    public void Update(MarkdownDocument document)
    {
        _document = document;

        OnPropertyChanged(nameof(Document));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(Tooltip));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(IsUntitled));
        OnPropertyChanged(nameof(Path));
        OnPropertyChanged(nameof(HasExternalChange));
        OnPropertyChanged(nameof(IsReadOnly));
        OnPropertyChanged(nameof(IsLocked));

        // IsClosable before IsPinned, and both before the strip next lays out. The fit pass
        // books a pinned tab's chrome from IsPinned and the template draws its close button from
        // IsClosable, so the two have to agree - and if they are ever split apart, a tab that has
        // lost its cross while still booking room for one is the harmless direction to be wrong
        // in. It clips nothing; the other way round does.
        OnPropertyChanged(nameof(IsClosable));
        OnPropertyChanged(nameof(IsPinned));
    }
}
