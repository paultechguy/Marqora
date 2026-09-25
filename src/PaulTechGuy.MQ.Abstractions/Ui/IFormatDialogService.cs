// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Abstractions.Ui;

/// <summary>What the user chose in the formatting dialog.</summary>
/// <param name="Rules">The rules to apply, and to remember.</param>
/// <param name="SelectionOnly">
/// True when only the selected lines should be reformatted. Never true unless there was a
/// selection to begin with.
/// </param>
public readonly record struct FormatChoice(FormatOptions Rules, bool SelectionOnly);

/// <summary>What the user chose in the heading numbering dialog.</summary>
/// <param name="Start">The level the count begins at. Never <see cref="HeadingNumbering.Off"/>.</param>
/// <param name="Style">The separator to write, and to remember.</param>
public readonly record struct HeadingNumberChoice(HeadingNumbering Start, HeadingNumberStyle Style);

/// <summary>Shows the formatter's rule list and hands back what the user chose.</summary>
public interface IFormatDialogService
{
    /// <summary>
    /// Presents the rules, starting from the ones currently in force.
    ///
    /// <paramref name="selectedLines"/> is how many lines the user has selected, zero if
    /// none. The dialog offers to limit formatting to the selection only when there is one.
    ///
    /// Returns null if the user cancelled, in which case nothing is saved or formatted.
    /// </summary>
    Task<FormatChoice?> RequestFormatRulesAsync(
        FormatOptions current,
        int selectedLines,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks where the count starts and what separator to write, before section numbers go into
    /// the markdown itself.
    ///
    /// <paramref name="suggested"/> is the level the document is currently showing, which is
    /// what the dialog opens on. <paramref name="renumbering"/> is true when the document
    /// already numbers itself, and only changes what the dialog calls itself — the work is the
    /// same either way, because numbering always replaces what is there.
    ///
    /// <paramref name="headingLevels"/> is the level of every heading that can carry a number,
    /// one entry each, so the dialog can restate its count as the chosen level moves rather
    /// than quoting a figure that was only true for the level it opened on.
    ///
    /// Returns null if the user cancelled, in which case nothing is written or remembered.
    /// </summary>
    Task<HeadingNumberChoice?> RequestHeadingNumbersAsync(
        HeadingNumbering suggested,
        HeadingNumberStyle style,
        IReadOnlyList<int> headingLevels,
        bool renumbering,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks how Renumber List should number <paramref name="list"/>, opening on its
    /// <see cref="OrderedListSummary.Suggested"/> choice.
    ///
    /// Returns null if the user cancelled, in which case nothing is written.
    /// </summary>
    Task<ListNumbering?> RequestListNumberingAsync(
        OrderedListSummary list,
        CancellationToken cancellationToken = default);
}
