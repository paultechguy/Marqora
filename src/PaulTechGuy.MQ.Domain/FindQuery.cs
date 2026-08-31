// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// Which documents a find runs against.
///
/// Searching everything is first, and therefore the zero value, on purpose: the settings
/// deserializer does not run property initializers, so a settings file written before this
/// existed comes back holding whichever member happens to be zero. That has to be the one
/// worth defaulting to rather than whichever reads best in a list.
/// </summary>
public enum FindScope
{
    /// <summary>Every open tab, in tab order.</summary>
    AllDocuments,

    /// <summary>The document on screen, and only that one.</summary>
    ActiveDocument,
}

/// <summary>
/// What the user typed into the Find All window, and the switches set beside it.
///
/// <see cref="Scope"/> travels with the query for the window's sake — the results say what
/// they cover — but the finder itself ignores it. Choosing which documents to hand over is
/// the caller's job, and that is what keeps the finder indifferent to where the text came
/// from.
/// </summary>
public sealed record FindQuery
{
    public required string Term { get; init; }

    public bool MatchCase { get; init; }

    /// <summary>Match only where the term has a non-word character on both sides.</summary>
    public bool WholeWord { get; init; }

    /// <summary>Read <see cref="Term"/> as a regular expression rather than as literal text.</summary>
    public bool UseRegex { get; init; }

    public FindScope Scope { get; init; } = FindScope.AllDocuments;
}

/// <summary>
/// One document handed to the finder.
///
/// Deliberately not <see cref="MarkdownDocument"/>: a find wants a name, a path and some
/// text, and nothing else. Keeping the input this thin is what would let a search of a folder
/// feed the same finder from files that were never opened.
/// </summary>
public sealed record FindDocument(Guid Id, string Name, string Path, string Text);
