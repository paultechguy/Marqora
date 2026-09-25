// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// The numbers Renumber List can write into an ordered list's markers.
///
/// The two repeated forms are the shorthand authors reach for so an item can be inserted
/// anywhere without renumbering the rest by hand. They differ only in what a renderer shows:
/// CommonMark counts up from the first number, so <c>1. 1. 1.</c> still reads 1, 2, 3, while
/// <c>0. 0. 0.</c> reads 0, 1, 2. The source keeps the repeated number either way, and Format
/// Document leaves a run that repeats one number exactly as it finds it.
/// </summary>
public enum ListNumbering
{
    /// <summary>Every item <c>0.</c></summary>
    Zeros,

    /// <summary>Every item <c>1.</c></summary>
    Ones,

    /// <summary><c>1. 2. 3.</c> — always from one, whatever the list started at.</summary>
    Sequential,
}

/// <summary>
/// The numbered list Renumber List would act on, as the prompt needs to see it.
/// </summary>
/// <param name="Items">How many items would be renumbered, across every list picked.</param>
/// <param name="Suggested">
/// The choice the prompt opens on: the one that would change something. A list that counts up
/// is offered <see cref="ListNumbering.Ones"/>; a list that repeats a number is offered
/// <see cref="ListNumbering.Sequential"/>.
/// </param>
/// <param name="CanStartAtZero">
/// False when a list sits directly under a line of text. CommonMark lets a list interrupt a
/// paragraph only when it starts at one, so <c>0.</c> there would turn the list into more of the
/// paragraph - the usual case is a sub-list written straight under its parent item's text.
/// </param>
public sealed record OrderedListSummary(int Items, ListNumbering Suggested, bool CanStartAtZero);
