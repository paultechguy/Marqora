// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Spelling;

/// <summary>
/// The words a spell checker should stay out of, in a document about software.
///
/// These are not niceties. Measured on Marqora's own README — 6,013 word-ish tokens — the three
/// rules below account for around seventy would-be squiggles that no dictionary will ever know:
/// MQ, SDK, YAML, JSON, UAC, win-x64, net10, KaTeX, MarqoraLogo, PaulTechGuy, ExePath. Without
/// them, spell checking on by default is not defensible.
///
/// Every rule errs towards silence. A skipped word that really was misspelled costs one missed
/// squiggle; a false squiggle in the middle of prose costs the reader's trust in all of them.
/// </summary>
public static class WordPolicy
{
    /// <summary>
    /// Whether this word should never be reported, whatever the engine thought of it.
    /// </summary>
    public static bool ShouldSkip(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        return word.Length == 0
            || ContainsDigit(word)
            || IsAcronym(word)
            || IsMixedCaseIdentifier(word);
    }

    /// <summary>
    /// The word without its trailing possessive, or null if it does not carry one.
    ///
    /// Windows spells possessives perfectly well - "company's" and "James's" both come back
    /// clean. The problem this solves is a word in the user's own dictionary: the engine flags
    /// the whole token "Marqora's", the list holds "Marqora", and a plain lookup misses. Every
    /// word anyone ever adds would carry that hole.
    ///
    /// Both apostrophes count. Prose pasted out of Word carries the typographic one, and nobody
    /// can tell the two apart by looking.
    /// </summary>
    public static string? WithoutPossessive(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        // "Marqora's"
        if (word.Length > 2 && IsApostrophe(word[^2]) && word[^1] is 's' or 'S')
        {
            return word[..^2];
        }

        // "James'" - the plural and classical form, which is just as common in prose.
        if (word.Length > 1 && IsApostrophe(word[^1]))
        {
            return word[..^1];
        }

        return null;
    }

    private static bool IsApostrophe(char c) => c is '\'' or '’';

    /// <summary>
    /// "win-x64", "net10", "SHA256", "v0". Nothing carrying a digit is a word anyone meant to
    /// spell, and every one of them would otherwise be flagged.
    /// </summary>
    private static bool ContainsDigit(string word)
    {
        foreach (char c in word)
        {
            if (char.IsDigit(c))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// "SDK", "YAML", "HTML". Two letters or more, none of them lower case.
    ///
    /// One letter is deliberately not enough: "I" and "A" are words, and treating them as
    /// acronyms would quietly exempt them from checking for no gain.
    /// </summary>
    private static bool IsAcronym(string word)
    {
        if (word.Length < 2)
        {
            return false;
        }

        bool sawLetter = false;

        foreach (char c in word)
        {
            if (char.IsLower(c))
            {
                return false;
            }

            sawLetter |= char.IsLetter(c);
        }

        return sawLetter;
    }

    /// <summary>
    /// "MainViewModel", "setModelMarkers", "KaTeX", "NuGet" — an upper-case letter following a
    /// lower-case one, which is how an identifier written in prose gives itself away.
    ///
    /// Note what this deliberately does not catch: a single capitalised word such as "Marqora"
    /// has no interior capital, so it falls through to the seed list. That is the whole reason
    /// <see cref="SeedDictionary"/> exists.
    ///
    /// The known cost is a name like "McDonald", which is skipped rather than checked. Rare
    /// enough, and on the safe side of the trade.
    /// </summary>
    private static bool IsMixedCaseIdentifier(string word)
    {
        for (int i = 1; i < word.Length; i++)
        {
            if (char.IsUpper(word[i]) && char.IsLower(word[i - 1]))
            {
                return true;
            }
        }

        return false;
    }
}
