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
}
