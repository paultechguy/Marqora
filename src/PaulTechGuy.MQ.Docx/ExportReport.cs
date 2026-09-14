// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Docx;

/// <summary>
/// What an export could not carry across, gathered as it goes.
///
/// A Word export never refuses - a picture that is not on this machine, a diagram that would
/// not draw, an equation using something Word has no form of, each cost the document that one
/// thing and nothing else. The trade is that the reader has to be told, or the document has a
/// hole in it that only the person it was sent to will find.
///
/// Every entry carries the line it happened on, so the report can be worked through rather
/// than only read. Deduplicating is per place, not per sentence: three copies of the same
/// picture in one document are three things to fix in three places, and collapsing them to
/// one line would hide two of them. What is still collapsed is the same problem reported
/// twice about the same line, which is one thing however many times the walk met it.
/// </summary>
internal sealed class ExportReport
{
    private readonly List<ExportIssue> _issues = [];
    private readonly HashSet<(int Line, string Problem, string Item)> _seen = [];

    /// <summary>
    /// Everything that could not be carried across, in document order.
    ///
    /// Sorted here rather than by the caller because the walk does not produce them in order:
    /// diagrams are fetched before it starts, and Markdig relocates footnote definitions to
    /// the end of the tree, so an issue inside a note arrives last and belongs in the middle.
    /// </summary>
    public IReadOnlyList<ExportIssue> Issues =>
        [.. _issues.OrderBy(i => i.Line).ThenBy(i => i.Problem, StringComparer.Ordinal)];

    /// <summary>
    /// Records something the document did not get.
    /// </summary>
    /// <param name="sourceLine">
    /// The line as Markdig counts it, from zero, or -1 for something with no place in the
    /// document. Converted to the line a reader sees here, once, so that nothing downstream
    /// has to know which of the two conventions it is holding.
    /// </param>
    public void Note(int sourceLine, string problem, string item)
    {
        if (string.IsNullOrWhiteSpace(problem))
        {
            return;
        }

        int line = sourceLine >= 0 ? sourceLine + 1 : 0;
        string what = string.IsNullOrWhiteSpace(item) ? "(unnamed)" : item.Trim();

        if (!_seen.Add((line, problem, what)))
        {
            return;
        }

        _issues.Add(new ExportIssue { Line = line, Problem = problem, Item = what });
    }

    /// <summary>
    /// An equation written as its TeX source because the converter met something it does not
    /// map.
    ///
    /// The element is named on purpose. Nobody can enumerate every shape TeX can produce, so
    /// the way this converter gets better is by finding out which constructs real documents
    /// actually use - and that only happens if each miss says what it was rather than quietly
    /// falling back.
    /// </summary>
    /// <param name="tex">
    /// The equation's own source, so the report names the equation as well as the element.
    /// One unmapped element can be met by several equations, and "an equation used
    /// &lt;mtd&gt;" six times over says nothing about which six.
    /// </param>
    public void UnsupportedMath(int sourceLine, string? element, string? tex) =>
        Note(
            sourceLine,
            element is { Length: > 0 }
                ? $"No Word form for <{element}>; its source is in the document instead"
                : "Could not be converted; its source is in the document instead",
            Shorten(tex) is { Length: > 0 } source ? source : "An equation");

    /// <summary>
    /// The first line of something, cut to what a list can show.
    ///
    /// An equation can be twenty lines of TeX and a diagram a hundred of mermaid. The report
    /// is read in a column beside a line number; what earns its place there is enough to
    /// recognize the thing by.
    /// </summary>
    public static string Shorten(string? text, int limit = 60)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string first = text.ReplaceLineEndings("\n").Split('\n')
            .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? string.Empty;

        return first.Length <= limit ? first : first[..limit].TrimEnd() + "…";
    }
}
