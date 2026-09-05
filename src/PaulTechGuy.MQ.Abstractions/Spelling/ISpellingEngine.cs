// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Abstractions.Spelling;

/// <summary>
/// Whatever actually knows how to spell. One implementation ships — the Windows spell service —
/// and the seam exists so that a second could be dropped in without the analyzer changing.
///
/// <b>The contract is line-shaped on purpose, and that shape is not neutral.</b>
/// <see cref="Check"/> is handed a whole line and returns the runs it objects to, because the
/// Windows API works that way and does its own word-splitting — contractions, possessives,
/// hyphenation, locale rules — far better than a hand-rolled tokenizer would. An engine that can
/// only judge one word at a time, as Hunspell does, must therefore carry its own tokenizer
/// internally to satisfy this interface. Swapping engines is not a uniform-cost operation, and
/// that is a deliberate trade rather than an oversight. See docs/Spelling.md.
///
/// Implementations are called from the thread pool and must be safe to call concurrently, or
/// serialize internally.
/// </summary>
public interface ISpellingEngine
{
    /// <summary>
    /// Whether there is a dictionary to check against at all.
    ///
    /// False is an ordinary state, not a failure: a machine with no language pack for the user's
    /// language has nothing to offer, and the feature switches itself off quietly rather than
    /// interrupting a first run. Every other member returns empty while this is false.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// The runs of <paramref name="text"/> that are not spelled correctly, as offsets into the
    /// string that was passed in.
    ///
    /// Callers hand this a <em>masked</em> line — code spans, URLs and the rest already blanked —
    /// so the offsets are valid against the original line only because masking never changes a
    /// line's length.
    /// </summary>
    IReadOnlyList<SpellingRange> Check(string text);

    /// <summary>
    /// What the word might have been, best first, or empty if the engine has no idea. Called on
    /// the UI thread when a menu is opening, so it must be quick.
    /// </summary>
    IReadOnlyList<string> Suggest(string word);
}
