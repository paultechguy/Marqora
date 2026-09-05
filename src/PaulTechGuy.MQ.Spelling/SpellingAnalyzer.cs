// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Abstractions.Spelling;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Markdown;

namespace PaulTechGuy.MQ.Spelling;

/// <summary>
/// Checks a document's prose, one line at a time, and reports what survives the filters.
///
/// The shape of the work: protect the lines a rule must keep out of, mask the runs of each
/// remaining line that are not prose, hand what is left to the engine, then drop anything the
/// skip rules, the seed list or the user's own list account for.
///
/// <b>The cache is what makes this affordable.</b> A Check call costs roughly 280 microseconds on
/// the machine this was measured on, which is nothing for one line and about a second and a half
/// for a five thousand line document. Keying on the line's own text means a keystroke re-checks
/// one line rather than the file, and a line that moves down ten rows is still a hit.
///
/// The cache holds <em>raw engine output</em>, deliberately - before the skip rules, the seed
/// list and the user's list are applied. Adding a word therefore invalidates nothing: the next
/// pass replays the same cached ranges through a filter that now excludes it, with no calls to
/// the engine at all. That is what makes "Add to Dictionary" feel instant.
/// </summary>
public sealed class SpellingAnalyzer : ISpellingAnalyzer
{
    /// <summary>
    /// Lines remembered before the cache is emptied. Generous enough for several large documents
    /// at once; small enough that the strings behind it stay a rounding error next to the
    /// documents themselves, which are holding the same text anyway.
    /// </summary>
    private const int CacheCapacity = 8000;

    /// <summary>
    /// Words remembered for their suggestions. Far smaller than the line cache: it only ever
    /// holds words that were both misspelled and right-clicked.
    /// </summary>
    private const int SuggestionCacheCapacity = 512;

    private readonly ISpellingEngine _engine;
    private readonly IUserDictionary _dictionary;
    private readonly Lock _sync = new();
    private readonly Dictionary<string, SpellingRange[]> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> _suggestions = new(StringComparer.Ordinal);

    public SpellingAnalyzer(ISpellingEngine engine, IUserDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(dictionary);

        _engine = engine;
        _dictionary = dictionary;
    }

    public bool IsAvailable => _engine.IsAvailable;

    public IReadOnlyList<SpellingIssue> Check(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0 || !_engine.IsAvailable)
        {
            return [];
        }

        string[] lines = text.Split('\n');

        // Split on \n alone, so a CRLF file leaves a stray \r at the end of every line. Trimming
        // it only removes trailing characters, so every column before it is still where it was.
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd('\r');
        }

        // Indented code blocks are protected here but not for the style checks, which have always
        // ignored them to stay in step with the formatter. A four-space code sample would
        // otherwise be underlined from end to end.
        bool[] isProtected = MarkdownRegionScanner.FindProtectedLines(lines, includeIndentedCode: true);

        List<SpellingIssue> found = [];

        for (int line = 0; line < lines.Length; line++)
        {
            if (isProtected[line] || lines[line].Length == 0)
            {
                continue;
            }

            CollectLine(lines[line], line, found);
        }

        return found;
    }

    public IReadOnlyList<string> Suggest(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        if (word.Length == 0 || !_engine.IsAvailable)
        {
            return [];
        }

        lock (_sync)
        {
            if (_suggestions.TryGetValue(word, out string[]? cached))
            {
                return cached;
            }
        }

        // Outside the lock: this is the call that costs milliseconds, and it happens on the UI
        // thread while a menu is opening. Nothing else should be made to wait behind it.
        string[] offered = [.. _engine.Suggest(word)];

        lock (_sync)
        {
            if (_suggestions.Count >= SuggestionCacheCapacity)
            {
                _suggestions.Clear();
            }

            _suggestions[word] = offered;
        }

        return offered;
    }

    private void CollectLine(string text, int line, List<SpellingIssue> into)
    {
        foreach (SpellingRange range in RangesFor(text))
        {
            // Defensive: an engine that reported a range past the end of what it was given would
            // otherwise take the whole check down over one bad line.
            if (range.Start < 0 || range.Length <= 0 || range.Start + range.Length > text.Length)
            {
                continue;
            }

            string word = text.Substring(range.Start, range.Length);

            if (WordPolicy.ShouldSkip(word) || IsAccepted(word))
            {
                continue;
            }

            into.Add(new SpellingIssue
            {
                Line = line,
                Start = range.Start,
                Length = range.Length,
                Word = word,
                Kind = range.Kind,
            });
        }
    }

    /// <summary>Whether there is a single letter left worth asking about.</summary>
    private static bool HasLetter(string text)
    {
        foreach (char c in text)
        {
            if (char.IsLetter(c))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether this word is one Marqora ships knowing or one the user has accepted - including
    /// when it is wearing a possessive.
    ///
    /// The possessive retry is what stops "Marqora's" being flagged when "Marqora" is in the
    /// list. The engine flags the whole token, so a plain lookup misses, and every word anyone
    /// ever added would have carried the same hole.
    /// </summary>
    private bool IsAccepted(string word)
    {
        if (SeedDictionary.Contains(word) || _dictionary.Contains(word))
        {
            return true;
        }

        return WordPolicy.WithoutPossessive(word) is { } root
            && (SeedDictionary.Contains(root) || _dictionary.Contains(root));
    }

    /// <summary>
    /// What the engine says about this line, from the cache where possible.
    ///
    /// The engine is called outside the lock. Two threads arriving with the same uncached line
    /// will both check it and both store the same answer, which costs one redundant call and is
    /// cheaper than holding a lock across a COM call that every other document is waiting on.
    /// </summary>
    private SpellingRange[] RangesFor(string text)
    {
        lock (_sync)
        {
            if (_cache.TryGetValue(text, out SpellingRange[]? cached))
            {
                return cached;
            }
        }

        string masked = LineMasker.MaskNonProse(text);

        // Nothing to ask about when the mask has taken everything. Tested for a letter rather
        // than for whitespace: a masked run is filled with a separator now, so a line that was
        // all code comes back as punctuation rather than as blanks - and this catches a line of
        // pure symbols or digits too, which the whitespace test never did.
        SpellingRange[] ranges = HasLetter(masked)
            ? [.. _engine.Check(masked)]
            : [];

        lock (_sync)
        {
            // Emptied rather than evicted one at a time. A least-recently-used list would keep
            // more of the working set, but this runs behind a debounce on a pool thread, and the
            // cost of being wrong is re-checking lines that are about to be re-checked anyway.
            if (_cache.Count >= CacheCapacity)
            {
                _cache.Clear();
            }

            _cache[text] = ranges;
        }

        return ranges;
    }
}
