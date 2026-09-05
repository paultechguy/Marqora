// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Abstractions.Spelling;

/// <summary>
/// Checks a whole document's prose and reports what looks misspelled, without changing anything.
///
/// The sibling of <see cref="Analysis.IMarkdownAnalyzer"/>, and deliberately separate from it:
/// the two are switched on and off independently, publish under different marker owners, and
/// answer different questions. Sharing an assembly is not sharing a lifecycle.
///
/// Takes text alone. Unlike the document checks it needs nothing from the render — no link list,
/// no outline — because markdown structure is decided from the raw lines.
/// </summary>
public interface ISpellingAnalyzer
{
    /// <summary>
    /// Everything that looks wrong, in reading order. An empty list means the document is clean,
    /// which is the common case and must stay cheap.
    /// </summary>
    /// <summary>
    /// Whether there is a dictionary to check against at all.
    ///
    /// False is an ordinary state: a machine with no language pack for the user's language has
    /// nothing to offer. Surfaced so the preferences page can grey the setting out and say why,
    /// rather than leaving a switch that appears to do nothing.
    /// </summary>
    bool IsAvailable { get; }

    IReadOnlyList<SpellingIssue> Check(string text);

    /// <summary>
    /// What the word might have been, best first, or empty if the engine has no idea.
    ///
    /// Cached by word. The call costs roughly three milliseconds and runs on the UI thread while
    /// a context menu is opening, so the second right-click on the same misspelling - which is
    /// the common case, since a word one gets wrong tends to be got wrong repeatedly - is free.
    /// </summary>
    IReadOnlyList<string> Suggest(string word);
}
