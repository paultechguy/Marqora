// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.RegularExpressions;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Finding;

/// <summary>
/// Replace All's engine: the same search DocumentFinder runs, carried through to the text each
/// match becomes.
///
/// A second scan rather than a second use of the first one, for a reason that is not laziness.
/// DocumentFinder walks a line as a span and reports ValueMatch, which carries an index and a
/// length and nothing else - no groups - because that is what lets it leave a line that does not
/// match unallocated. A replacement of $1 needs Match.Groups, and Match needs a real string. The
/// two cannot be the same walk.
///
/// What they can share is the definition of a match. Line splitting, the whole-word rule, the
/// regular-expression options and the time budget all come from DocumentFinder, so a replace
/// cannot quietly disagree with the find that listed the matches - which would be the worst bug
/// available here, since the user confirms against the count the find reported.
///
/// The new text is built whole, in C#, before anything reaches the editor: matches arrive in
/// ascending order and do not overlap, so one forward pass assembles the document. Nothing
/// downstream has to reason about positions shifting as earlier edits are applied.
/// </summary>
public static class DocumentReplacer
{
    /// <summary>
    /// Runs <paramref name="query"/> over <paramref name="documents"/> and reports what each one
    /// would become. Nothing is written; that belongs to the caller.
    ///
    /// Never throws for a pattern the user got wrong; that comes back as
    /// <see cref="ReplaceResults.Error"/>. The replacement needs no such path - see CollectLine,
    /// where nothing the user can type into it is invalid.
    /// </summary>
    public static ReplaceResults Replace(
        ReplaceQuery query,
        IReadOnlyList<FindDocument> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(documents);

        FindQuery find = query.Find;

        if (string.IsNullOrEmpty(find.Term))
        {
            return ReplaceResults.None(query);
        }

        Regex? pattern = null;

        if (find.UseRegex)
        {
            try
            {
                pattern = new Regex(find.Term, DocumentFinder.RegexOptionsFor(find), DocumentFinder.RegexBudget);
            }
            catch (ArgumentException ex)
            {
                return ReplaceResults.Failed(query, ex.Message);
            }
        }

        var changed = new List<ReplaceDocumentResult>();
        int total = 0;
        bool truncated = false;

        foreach (FindDocument document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<Replacement> replacements;

            try
            {
                replacements = Collect(
                    document.Text,
                    query,
                    pattern,
                    DocumentFinder.MatchLimit + 1 - total,
                    cancellationToken);
            }
            catch (RegexMatchTimeoutException)
            {
                return ReplaceResults.Failed(
                    query,
                    $"The regular expression took too long in {document.Name}. Try a simpler pattern.");
            }

            // One match past the ceiling is collected and dropped, exactly as the finder does it,
            // so the two report the same total for the same query.
            if (total + replacements.Count > DocumentFinder.MatchLimit)
            {
                replacements.RemoveAt(replacements.Count - 1);
                truncated = true;
            }

            total += replacements.Count;

            if (replacements.Count > 0)
            {
                changed.Add(new ReplaceDocumentResult(
                    document.Id,
                    document.Name,
                    document.Path,
                    document.Text,
                    Build(document.Text, replacements),
                    replacements.Count));
            }

            if (truncated)
            {
                break;
            }
        }

        return new ReplaceResults
        {
            Query = query,
            Documents = changed,
            TotalMatches = total,
            Truncated = truncated,
        };
    }

    /// <summary>Where one match sits in the whole document, and what it becomes.</summary>
    private readonly record struct Replacement(int Start, int Length, string Text);

    /// <summary>Every match in one document, in document order, stopping at the budget.</summary>
    private static List<Replacement> Collect(
        string text,
        ReplaceQuery query,
        Regex? pattern,
        int budget,
        CancellationToken cancellationToken)
    {
        var found = new List<Replacement>();
        int start = 0;

        // Runs for start == text.Length too, so a document ending in a line break has its empty
        // last line looked at, exactly as the finder does.
        while (start >= 0 && found.Count < budget)
        {
            cancellationToken.ThrowIfCancellationRequested();

            (int end, int next) = DocumentFinder.LineBounds(text, start);

            CollectLine(text, start, end, query, pattern, budget, found);

            start = next;
        }

        return found;
    }

    private static void CollectLine(
        string text,
        int lineStart,
        int lineEnd,
        ReplaceQuery query,
        Regex? pattern,
        int budget,
        List<Replacement> into)
    {
        ReadOnlySpan<char> line = text.AsSpan(lineStart, lineEnd - lineStart);
        FindQuery find = query.Find;

        if (pattern is null)
        {
            StringComparison comparison = find.MatchCase
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            int at = 0;

            while (at <= line.Length - find.Term.Length && into.Count < budget)
            {
                int hit = line[at..].IndexOf(find.Term, comparison);

                if (hit < 0)
                {
                    return;
                }

                hit += at;

                if (!find.WholeWord || DocumentFinder.IsWholeWord(line, hit, find.Term.Length))
                {
                    // Verbatim: there is no pattern for a dollar sign to refer back to. See
                    // ReplaceQuery.Replacement.
                    into.Add(new Replacement(lineStart + hit, find.Term.Length, query.Replacement));
                }

                // Matches do not overlap: "aa" over "aaaa" is two, not three.
                at = hit + find.Term.Length;
            }

            return;
        }

        // Materialized because Match.Groups cannot come from a span. This is the allocation the
        // finder avoids and a replace cannot.
        string lineText = line.ToString();

        for (Match match = pattern.Match(lineText); match.Success; match = match.NextMatch())
        {
            if (into.Count >= budget)
            {
                return;
            }

            // A pattern that can match nothing, such as a*, matches nothing at every column. The
            // finder skips those, so this must too - otherwise a replace would insert itself
            // between every character of a document the find called untouched.
            if (match.Length == 0)
            {
                continue;
            }

            if (find.WholeWord && !DocumentFinder.IsWholeWord(line, match.Index, match.Length))
            {
                continue;
            }

            // Match.Result cannot fail. Its parser treats every sequence it does not recognise
            // as literal text - "${unclosed", "${undefined}" and "$99" all come through exactly
            // as typed - so there is no such thing as a replacement the user got wrong, and
            // nothing here to report. Only the pattern searched for can be rejected.
            into.Add(new Replacement(
                lineStart + match.Index,
                match.Length,
                match.Result(query.Replacement)));
        }
    }

    /// <summary>
    /// The document with every match swapped out, in one forward pass.
    ///
    /// The text between matches is copied straight across, so line endings survive whatever mix
    /// the file arrived with - there is no splitting and rejoining to get them wrong.
    /// </summary>
    private static string Build(string text, List<Replacement> replacements)
    {
        var builder = new StringBuilder(text.Length);
        int at = 0;

        foreach (Replacement replacement in replacements)
        {
            builder.Append(text, at, replacement.Start - at);
            builder.Append(replacement.Text);

            at = replacement.Start + replacement.Length;
        }

        builder.Append(text, at, text.Length - at);

        return builder.ToString();
    }
}
