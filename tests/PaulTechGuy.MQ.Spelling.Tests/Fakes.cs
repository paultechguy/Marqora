// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Abstractions.Spelling;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Spelling.Tests;

/// <summary>
/// An engine that objects to a fixed set of words.
///
/// Hand-written rather than mocked, matching the convention in Services.Tests, and it earns its
/// keep twice over: the tests say the same thing on a machine with no language pack installed,
/// and this is a second implementation of <see cref="ISpellingEngine"/> - the cheapest available
/// proof that the seam is a real one rather than a shape fitted to a single caller.
///
/// It splits words the way the Windows engine does closely enough for these tests: runs of
/// letters and digits, with an apostrophe allowed inside. Two identical words in a row are
/// reported as a repeat on the second, which is what the real engine does through its "delete"
/// corrective action.
/// </summary>
internal sealed class FakeSpellingEngine : ISpellingEngine
{
    private readonly HashSet<string> _wrong;

    public FakeSpellingEngine(params string[] wrong) =>
        _wrong = new HashSet<string>(wrong, StringComparer.OrdinalIgnoreCase);

    public bool IsAvailable { get; set; } = true;

    /// <summary>How many times the engine has actually been asked. The cache tests read this.</summary>
    public int CheckCalls { get; private set; }

    public IReadOnlyList<SpellingRange> Check(string text)
    {
        CheckCalls++;

        List<SpellingRange> found = [];
        string? previous = null;
        int previousEnd = -1;
        int i = 0;

        while (i < text.Length)
        {
            if (!char.IsLetterOrDigit(text[i]))
            {
                i++;

                continue;
            }

            int start = i;

            while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '\''))
            {
                i++;
            }

            string word = text[start..i];

            // Only whitespace may separate the two, which is what the Windows engine does and is
            // the whole reason the masker fills with a separator rather than spaces: "and `^` and"
            // is not a word typed twice, and would be read as one if the code span became blanks.
            bool repeated = previous is not null
                && string.Equals(previous, word, StringComparison.OrdinalIgnoreCase)
                && IsOnlyWhitespace(text, previousEnd, start);

            if (repeated)
            {
                found.Add(new SpellingRange(start, word.Length, SpellingIssueKind.RepeatedWord));
            }
            else if (_wrong.Contains(word))
            {
                found.Add(new SpellingRange(start, word.Length, SpellingIssueKind.Misspelling));
            }

            previous = word;
            previousEnd = i;
        }

        return found;
    }

    private static bool IsOnlyWhitespace(string text, int from, int to)
    {
        for (int i = from; i < to; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                return false;
            }
        }

        return true;
    }

    public IReadOnlyList<string> Suggest(string word) =>
        _wrong.Contains(word) ? [word.ToUpperInvariant()] : [];
}

/// <summary>A word list held in memory, so a test can add to it and see what changes.</summary>
internal sealed class FakeUserDictionary : IUserDictionary
{
    private readonly HashSet<string> _words;

    public FakeUserDictionary(params string[] words) =>
        _words = new HashSet<string>(words, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> Words => _words;

    public bool Contains(string word) => _words.Contains(word);

    public Task AddAsync(string word, CancellationToken cancellationToken = default)
    {
        if (_words.Add(word))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return Task.CompletedTask;
    }

    public event EventHandler? Changed;
}
