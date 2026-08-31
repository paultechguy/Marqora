// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Xaml.Controls;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.App.ViewModels;

/// <summary>
/// Everything the change banner puts on screen, worked out once rather than assembled in
/// bindings.
///
/// The wording is the feature. A prompt that says only "the file changed" leaves the user to
/// work out which file, whether their typing is at stake, and what each button will do to it,
/// so all three are answered here in plain words - and the strings can be checked in a test
/// without a UI thread.
/// </summary>
public sealed record ExternalChangeNotice
{
    /// <summary>
    /// What the view model holds while the banner is shut.
    ///
    /// The banner's visibility is a flag of its own rather than a null check on this, so that
    /// no binding in the markup has to walk through a null. An x:Bind path with a nullable
    /// link in it is the sort of thing that works until the one frame where it does not.
    /// </summary>
    public static ExternalChangeNotice None { get; } = new()
    {
        DocumentId = Guid.Empty,
        Severity = InfoBarSeverity.Warning,
        Title = string.Empty,
        PathLine = string.Empty,
        CanReload = false,
    };

    public required Guid DocumentId { get; init; }

    public required InfoBarSeverity Severity { get; init; }

    public required string Title { get; init; }

    /// <summary>The full path. This is the line that answers "which file".</summary>
    public required string PathLine { get; init; }

    /// <summary>What resolving it will do, or empty when there is nothing at stake to say.</summary>
    public string Detail { get; init; } = string.Empty;

    public bool HasDetail => !string.IsNullOrEmpty(Detail);

    /// <summary>"1 of 4" when other tabs are waiting too, otherwise empty.</summary>
    public string CountLabel { get; init; } = string.Empty;

    public bool HasCount => !string.IsNullOrEmpty(CountLabel);

    /// <summary>Whether this is a rewritten file, which is the only kind that can be reloaded.</summary>
    public required bool CanReload { get; init; }

    /// <summary>Whether more than one document is waiting, which is what a sweep is worth offering for.</summary>
    public bool CanReloadAll { get; init; }

    /// <summary>How many pending documents a sweep would leave alone because they hold edits.</summary>
    public int DirtyPendingCount { get; init; }

    public int PendingCount { get; init; }

    public string ReloadAllLabel => $"Reload {PendingCount - DirtyPendingCount} unmodified";

    public string ReloadAllDiscardingLabel => $"Reload all {PendingCount}, discarding my edits";

    /// <summary>Whether a sweep would spare anything, which is the only time both items are worth showing.</summary>
    public bool HasUnmodifiedToSweep => PendingCount - DirtyPendingCount > 0;

    /// <summary>
    /// Builds the notice for one document. <paramref name="pendingCount"/> and
    /// <paramref name="position"/> place it among the others still waiting.
    /// </summary>
    public static ExternalChangeNotice For(
        MarkdownDocument document,
        int position,
        int pendingCount,
        int dirtyPendingCount)
    {
        bool deleted = document.External == ExternalState.Missing;

        // Compared directly rather than through IsDirty, which a missing file sets by itself.
        // Warning about edits the user never made would be nonsense.
        bool hasEdits = !string.Equals(document.Text, document.SavedText, StringComparison.Ordinal);

        return new ExternalChangeNotice
        {
            DocumentId = document.Id,
            Severity = deleted ? InfoBarSeverity.Error : InfoBarSeverity.Warning,
            Title = deleted
                ? $"{document.DisplayName} was deleted or moved"
                : $"{document.DisplayName} changed on disk",
            PathLine = document.DisplayPath,
            Detail = deleted
                ? "Your text is still here and now counts as unsaved. Saving writes the file again."
                : hasEdits
                    ? "You have unsaved edits. Reloading replaces them with what is on disk."
                    : string.Empty,
            CountLabel = pendingCount > 1 ? $"{position} of {pendingCount}" : string.Empty,
            CanReload = !deleted,
            CanReloadAll = pendingCount > 1,
            PendingCount = pendingCount,
            DirtyPendingCount = dirtyPendingCount,
        };
    }
}
