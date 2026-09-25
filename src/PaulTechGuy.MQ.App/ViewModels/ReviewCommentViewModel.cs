// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.App.ViewModels;

/// <summary>
/// One card in the comments sidebar.
///
/// A draft is a comment being written for the first time: it has a place in the preview and a
/// card with its box open, but the session does not hold it until Save, so a draft abandoned
/// with Cancel leaves nothing behind - not even a revision that would count as unshared.
/// </summary>
public sealed partial class ReviewCommentViewModel : ObservableObject
{
    public ReviewCommentViewModel(Guid id, ReviewAnchor anchor, string note, bool isDraft)
    {
        Id = id;
        Anchor = anchor;
        Note = note;
        EditText = note;
        IsDraft = isDraft;
        IsEditing = isDraft;
    }

    public Guid Id { get; }

    public ReviewAnchor Anchor { get; }

    /// <summary>The passage, on one line, as the card quotes it.</summary>
    public string Quote => string.Join(' ', Anchor.Quote.Split((char[])['\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries));

    [ObservableProperty]
    public partial int Number { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    /// <summary>What the edit box holds, apart from <see cref="Note"/> until Save.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial string EditText { get; set; }

    [ObservableProperty]
    public partial bool IsDraft { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsViewing))]
    public partial bool IsEditing { get; set; }

    /// <summary>
    /// Whether Save has something to save: the box holds more than whitespace. A comment of
    /// nothing is not one, so Save stays grayed rather than accepting it and quietly doing nothing.
    /// </summary>
    public bool CanSave => !string.IsNullOrWhiteSpace(EditText);

    /// <summary>The other half of <see cref="IsEditing"/>, for the card's reading face.</summary>
    public bool IsViewing => !IsEditing;

    /// <summary>Whether the pointer is over this comment in the preview, or over its card.</summary>
    [ObservableProperty]
    public partial bool IsHighlighted { get; set; }
}
