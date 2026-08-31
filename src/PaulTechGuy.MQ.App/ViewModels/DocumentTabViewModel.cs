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
    /// U+26A0 as a colour emoji, which lands at the wrong size and weight beside tab text and
    /// cannot be recoloured with it.
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
            return _document.AutoReloadedUtc is { } reloaded
                ? text + "\nReloaded from disk at "
                    + reloaded.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)
                : text;
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
    public partial bool IsActive { get; set; }

    public bool IsDirty => _document.IsDirty;

    public bool IsUntitled => _document.IsUntitled;

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
    }
}
