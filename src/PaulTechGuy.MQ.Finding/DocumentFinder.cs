// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Finding;

/// <summary>
/// Find All's engine.
///
/// Searches a line at a time and reports zero-based line and column positions, which the
/// bridge turns into an editor selection. A pattern therefore cannot match across a line
/// break, even in regular-expression mode: a results list is a list of lines, and a match
/// spanning three of them would have no row to sit on. The editor's own find widget can do
/// it, and this is the one place the two deliberately disagree.
///
/// Lines are counted the way the editor counts them — CRLF, LF and a bare CR each end one —
/// so a file with mixed endings still reports the line numbers shown in the gutter.
/// </summary>
public static class DocumentFinder
{
    /// <summary>
    /// Where the search gives up.
    ///
    /// A one-character pattern across a dozen open documents would otherwise build a list
    /// nobody can read, in a window nobody can scroll, after a wait nobody asked for.
    /// </summary>
    public const int MatchLimit = 5000;

    /// <summary>
    /// How long one regular expression may spend on one line before it is abandoned.
    ///
    /// A pattern such as (a+)+$ backtracks essentially forever on the wrong input. The
    /// ceiling turns a frozen window into a sentence the user can act on.
    /// </summary>
    private static readonly TimeSpan RegexBudget = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Runs <paramref name="query"/> over <paramref name="documents"/> in the order given.
    ///
    /// Never throws for a pattern the user got wrong; that comes back as
    /// <see cref="FindResults.Error"/>. Cancellation does throw, because a cancelled search
    /// has no result to report and the caller is throwing it away regardless.
    /// </summary>
    public static FindResults Find(
        FindQuery query,
        IReadOnlyList<FindDocument> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(documents);

        // An empty term matches nothing. Whitespace is not empty: two spaces at the end of a
        // line are a markdown line break, and looking for them is a fair thing to ask.
        if (string.IsNullOrEmpty(query.Term))
        {
            return FindResults.None(query);
        }

        Regex? pattern = null;

        if (query.UseRegex)
        {
            try
            {
                pattern = new Regex(query.Term, RegexOptionsFor(query), RegexBudget);
            }
            catch (ArgumentException ex)
            {
                // .NET's own message names the offset and what it wanted there, which is
                // more use to whoever typed the pattern than anything written in its place.
                return FindResults.Failed(query, ex.Message);
            }
        }

        var found = new List<FindDocumentMatches>();
        int total = 0;
        bool truncated = false;

        foreach (FindDocument document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<FindMatch> matches = [];

            try
            {
                ScanDocument(document.Text, query, pattern, MatchLimit + 1 - total, matches, cancellationToken);
            }
            catch (RegexMatchTimeoutException)
            {
                return FindResults.Failed(
                    query,
                    $"The regular expression took too long in {document.Name}. Try a simpler pattern.");
            }

            // One match past the ceiling is collected on purpose. It is the whole difference
            // between "there are exactly five thousand" and "there are more than we will
            // show", and it costs one match to know which.
            if (total + matches.Count > MatchLimit)
            {
                matches.RemoveAt(matches.Count - 1);
                truncated = true;
            }

            total += matches.Count;

            if (matches.Count > 0)
            {
                found.Add(new FindDocumentMatches(document.Id, document.Name, document.Path, matches));
            }

            if (truncated)
            {
                break;
            }
        }

        return new FindResults
        {
            Query = query,
            Documents = found,
            TotalMatches = total,
            Truncated = truncated,
        };
    }

    /// <summary>
    /// One line of a document by its zero-based number, or null when the document has no such
    /// line.
    ///
    /// Lines are counted exactly as <see cref="Find"/> counts them, so a match's line number
    /// can be handed straight back here — which is what makes it possible to ask whether a
    /// match is still where it was found.
    /// </summary>
    public static string? LineAt(string text, int line)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (line < 0)
        {
            return null;
        }

        int start = 0;

        for (int number = 0; start >= 0; number++)
        {
            (int end, int next) = LineBounds(text, start);

            if (number == line)
            {
                return text[start..end];
            }

            start = next;
        }

        return null;
    }

    /// <summary>
    /// Walks one document's lines, stopping once <paramref name="budget"/> matches are in
    /// hand.
    ///
    /// The text is never split into an array of lines. Only lines that actually hold a match
    /// become strings, so searching a large document for something rare allocates almost
    /// nothing.
    /// </summary>
    private static void ScanDocument(
        string text,
        FindQuery query,
        Regex? pattern,
        int budget,
        List<FindMatch> into,
        CancellationToken cancellationToken)
    {
        int line = 0;
        int start = 0;

        // Runs for start == text.Length too, so a document ending in a line break has its
        // empty last line looked at, exactly as the editor shows one.
        while (start >= 0 && into.Count < budget)
        {
            cancellationToken.ThrowIfCancellationRequested();

            (int end, int next) = LineBounds(text, start);

            ScanLine(text.AsSpan(start, end - start), line, query, pattern, budget, into);

            start = next;
            line++;
        }
    }

    /// <summary>
    /// Where the line starting at <paramref name="start"/> ends, and where the next one
    /// begins — or -1 when that was the last line.
    ///
    /// CRLF is one break rather than two, so a Windows file does not report every other line
    /// as blank; a bare CR ends a line as well, because the editor's model says it does.
    /// </summary>
    private static (int End, int Next) LineBounds(string text, int start)
    {
        int end = start;

        while (end < text.Length && text[end] is not ('\n' or '\r'))
        {
            end++;
        }

        if (end >= text.Length)
        {
            return (end, -1);
        }

        int next = end + (text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1);

        return (end, next);
    }

    private static void ScanLine(
        ReadOnlySpan<char> line,
        int number,
        FindQuery query,
        Regex? pattern,
        int budget,
        List<FindMatch> into)
    {
        // Materialised on the first hit and shared by the rest of the line's matches.
        string? text = null;

        if (pattern is null)
        {
            StringComparison comparison = query.MatchCase
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            int at = 0;

            while (at <= line.Length - query.Term.Length && into.Count < budget)
            {
                int hit = line[at..].IndexOf(query.Term, comparison);

                if (hit < 0)
                {
                    return;
                }

                hit += at;

                if (!query.WholeWord || IsWholeWord(line, hit, query.Term.Length))
                {
                    text ??= line.ToString();
                    into.Add(new FindMatch(number, hit, query.Term.Length, text));
                }

                // Matches do not overlap, which is what the editor's own find does: "aa"
                // over "aaaa" is two matches, not three.
                at = hit + query.Term.Length;
            }

            return;
        }

        foreach (ValueMatch match in pattern.EnumerateMatches(line))
        {
            if (into.Count >= budget)
            {
                return;
            }

            // A pattern that can match nothing, such as a*, matches nothing at every column.
            // Reporting those would fill the list with rows pointing at no text.
            if (match.Length == 0)
            {
                continue;
            }

            if (query.WholeWord && !IsWholeWord(line, match.Index, match.Length))
            {
                continue;
            }

            text ??= line.ToString();
            into.Add(new FindMatch(number, match.Index, match.Length, text));
        }
    }

    /// <summary>
    /// Whether a match stands on its own rather than sitting inside a longer word.
    ///
    /// A word character is a letter, a digit or an underscore. Monaco reaches nearly the same
    /// answer from the other end, by listing the separators instead, so the two agree on
    /// every character anyone searches a markdown file for and part company only over
    /// oddities such as the section sign.
    /// </summary>
    private static bool IsWholeWord(ReadOnlySpan<char> line, int start, int length) =>
        (start == 0 || !IsWordCharacter(line[start - 1]))
        && (start + length == line.Length || !IsWordCharacter(line[start + length]));

    private static bool IsWordCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';

    /// <summary>
    /// No Multiline: each line is matched as a whole input of its own, so ^ and $ already
    /// anchor to the line the user is looking at. CultureInvariant keeps a case-insensitive
    /// search from taking the current culture's view of what I looks like.
    /// </summary>
    private static RegexOptions RegexOptionsFor(FindQuery query) =>
        query.MatchCase
            ? RegexOptions.CultureInvariant
            : RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
}
