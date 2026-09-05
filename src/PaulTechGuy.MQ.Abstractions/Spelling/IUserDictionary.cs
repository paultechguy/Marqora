// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;

namespace PaulTechGuy.MQ.Abstractions.Spelling;

/// <summary>
/// The words this user has accepted, on top of whatever the engine already knows.
///
/// Stored as a plain text file rather than inside the settings, for three reasons: "Restore
/// defaults" then structurally cannot reach it, it can be shared and diffed like any other file,
/// and it can be opened and edited in Marqora itself - which is why <see cref="Changed"/> exists.
///
/// Reads happen on the thread pool, once per misspelled word per pass, so
/// <see cref="Contains"/> must be cheap and safe to call concurrently.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Dictionary is the domain word and the user-facing one: the menu says Add to "
        + "Dictionary and the file is user-dictionary.txt. Renaming this to WordList would make "
        + "the code disagree with the product.")]
public interface IUserDictionary
{
    /// <summary>
    /// Whether the user has accepted this word. Case-insensitive: a word at the start of a
    /// sentence is the same word.
    /// </summary>
    bool Contains(string word);

    /// <summary>
    /// Every accepted word, for export and for anything that wants to show the list.
    /// </summary>
    IReadOnlyCollection<string> Words { get; }

    /// <summary>
    /// Accepts a word and writes the file. Adding a word already present is not an error and
    /// does not rewrite anything.
    /// </summary>
    Task AddAsync(string word, CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised when the list has changed - a word added here, or the file edited outside and
    /// noticed. Listeners re-publish; the analyzer's cache does not need clearing, because it
    /// holds what the engine said rather than what survived the filter.
    /// </summary>
    event EventHandler? Changed;
}
