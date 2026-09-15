// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.RegularExpressions;

namespace PaulTechGuy.MQ.Markdown;

/// <summary>
/// Reads a document's headings off its lines: where each one is, what level it carries, where
/// its text begins in the source, and whether that text opens with something shaped like a
/// section number.
///
/// Structural only. Nothing here decides whether "2026" at the front of a heading is numbering
/// or the year in "2026 Budget" — that judgment needs the document's own counter to compare
/// against, which is policy and lives a layer up in HeadingNumberDetector. Keeping the two
/// apart is what lets this stay in the foundation project beside
/// <see cref="MarkdownRegionScanner"/> with nothing underneath it.
///
/// Line-based rather than parsed, for the same reason the formatter is: a caller that rewrites
/// headings has to put the line back almost exactly as it found it, and Markdig can parse
/// markdown but cannot render it back. <see cref="ScannedHeading.TextStart"/> is what makes
/// that splice possible without re-deriving the prefix.
/// </summary>
public static partial class HeadingScanner
{
    /// <summary>A section number read off the front of a heading's text.</summary>
    /// <param name="Length">
    /// How much of the heading's text the prefix occupies, trailing whitespace included, so
    /// that the words alone are <c>Text[Length..]</c> and putting a new number in front of
    /// them is one splice.
    /// </param>
    /// <param name="Components">The numbers themselves: <c>1.2.3</c> reads as 1, 2, 3.</param>
    /// <param name="Terminator">The <c>.</c> or <c>)</c> written after the last number, or <c>\0</c>.</param>
    /// <param name="Section">Whether a <c>§</c> was written in front of the number.</param>
    public readonly record struct NumberPrefix(
        int Length,
        IReadOnlyList<int> Components,
        char Terminator,
        bool Section)
    {
        /// <summary>
        /// The number as it was written, normalized to the dotted form: <c>§1.02)</c> reads
        /// back as <c>1.2</c>.
        ///
        /// Normalized rather than literal because this exists to be compared against what the
        /// document's own counter would have produced, and a leading zero is still the author
        /// numbering. The cost is that a document written <c>01.2</c> comes back <c>1.2</c>
        /// once it has been through a rewrite, which is a tidy-up rather than damage.
        /// </summary>
        public string Written => string.Join('.', Components);
    }

    /// <summary>One heading, as the source wrote it.</summary>
    /// <param name="Line">Zero-based line carrying the heading's text.</param>
    /// <param name="Level">1 to 6.</param>
    /// <param name="Setext">
    /// The underlined form, whose <c>===</c> or <c>---</c> sits on <c>Line + 1</c>. A caller
    /// that rewrites the text does not touch the underline; one that converts to <c>#</c> form
    /// has to remove it.
    /// </param>
    /// <param name="TextStart">
    /// Index into the source line where the heading's text begins — past the indent, the
    /// hashes and the space after them. Everything before it is written back untouched.
    /// </param>
    /// <param name="Text">
    /// The heading's text, with any number prefix still on the front and any closing
    /// <c>##</c> sequence already off the end.
    /// </param>
    /// <param name="Prefix">The leading section number, when the heading opens with one.</param>
    public sealed record ScannedHeading(
        int Line,
        int Level,
        bool Setext,
        int TextStart,
        string Text,
        NumberPrefix? Prefix)
    {
        /// <summary>The heading's words with any leading number taken off.</summary>
        public string Title => Prefix is { } prefix ? Text[prefix.Length..] : Text;
    }

    /// <summary>
    /// Every heading in <paramref name="lines"/>, in document order, skipping anything inside a
    /// fenced block or front matter.
    ///
    /// Unlike <see cref="OrderedRuns.Find"/> this computes the protected lines itself when it is
    /// not given them. The two differ deliberately: a run of numbered lines that turns out to be
    /// code is a run nobody acts on, while a hash-prefixed shell comment read as a heading is a
    /// line a rewriter would edit. Silence is not the safe default here.
    /// </summary>
    /// <param name="protectedLines">
    /// From <see cref="MarkdownRegionScanner.FindProtectedLines"/>, for a caller that has already
    /// paid for it. Indented code is left out, matching the formatter, because a four-space
    /// indented <c>#</c> is not a heading to either of them.
    /// </param>
    public static IReadOnlyList<ScannedHeading> Find(IReadOnlyList<string> lines, bool[]? protectedLines = null)
    {
        ArgumentNullException.ThrowIfNull(lines);

        bool[] guarded = protectedLines ?? MarkdownRegionScanner.FindProtectedLines(lines);
        List<ScannedHeading> headings = [];

        for (int i = 0; i < lines.Count; i++)
        {
            if (i < guarded.Length && guarded[i])
            {
                continue;
            }

            if (TryReadAtx(lines[i], out int level, out int start, out string text))
            {
                headings.Add(Build(i, level, setext: false, start, text));

                continue;
            }

            if (TryReadSetext(lines, guarded, i, out int underlined))
            {
                string title = lines[i];
                int indent = title.Length - title.TrimStart().Length;

                headings.Add(Build(i, underlined, setext: true, indent, title.Trim()));

                // The underline begins nothing of its own, and stepping over it keeps a run of
                // dashes from being weighed a second time as the title of whatever follows.
                i++;
            }
        }

        return headings;
    }

    private static ScannedHeading Build(int line, int level, bool setext, int start, string text) =>
        new(line, level, setext, start, text, ReadPrefix(text));

    /// <summary>
    /// Reads a leading section number, or returns null when the heading does not open with one.
    ///
    /// The rule is deliberately strict about what follows the digits, because that is the only
    /// thing separating a number from a word that starts with one. A separator is required, so
    /// "3D Printing" and "1970s Architecture" never match. Text is required after it, so a
    /// heading reading only "1." is left alone rather than being emptied.
    ///
    /// What the rule cannot do is tell "1.2 Scope" from "2026 Budget"; both are digits, a space
    /// and words. That is not a job for a pattern — it needs the rest of the document — and it
    /// is why this reports a candidate rather than a verdict.
    ///
    /// Nine digits a component, which is every section number anyone will write and comfortably
    /// short of overflowing the parse.
    /// </summary>
    private static NumberPrefix? ReadPrefix(string text)
    {
        Match match = Prefix().Match(text);

        if (!match.Success)
        {
            return null;
        }

        string[] parts = match.Groups["num"].Value.Split('.');
        int[] components = new int[parts.Length];

        for (int i = 0; i < parts.Length; i++)
        {
            components[i] = int.Parse(parts[i], CultureInfo.InvariantCulture);
        }

        Group terminator = match.Groups["term"];

        return new NumberPrefix(
            match.Length,
            components,
            terminator.Success ? terminator.Value[0] : '\0',
            match.Groups["sec"].Success);
    }

    /// <summary>
    /// Reads the <c>#</c> form. Up to three spaces of indent is still a heading; a fourth makes
    /// it code, which is the caller's protected-line scan talking rather than this.
    ///
    /// A closing run of hashes is furniture rather than words — <c>## Scope ##</c> is titled
    /// "Scope" — but only when a space separates it, so a heading ending in <c>C#</c> keeps its
    /// sharp.
    /// </summary>
    private static bool TryReadAtx(string line, out int level, out int start, out string text)
    {
        level = 0;
        start = 0;
        text = string.Empty;

        Match match = Atx().Match(line);

        if (!match.Success)
        {
            return false;
        }

        level = match.Groups["hashes"].Length;
        Group content = match.Groups["content"];

        if (!content.Success)
        {
            // "##" alone: a heading with no words, which still counts and still numbers.
            start = match.Groups["indent"].Length + level;

            return true;
        }

        start = content.Index;
        text = StripClosingHashes(content.Value);

        return true;
    }

    private static string StripClosingHashes(string content)
    {
        string trimmed = content.TrimEnd();
        int end = trimmed.Length;

        while (end > 0 && trimmed[end - 1] == '#')
        {
            end--;
        }

        if (end == trimmed.Length)
        {
            return trimmed;
        }

        // Furniture only when it stands apart from the words, or is the whole of them.
        return end == 0 || trimmed[end - 1] is ' ' or '\t'
            ? trimmed[..end].TrimEnd()
            : trimmed;
    }

    /// <summary>
    /// Reads the underlined form: a line of words with <c>===</c> or <c>---</c> beneath it.
    ///
    /// Only a single line of words, matching the formatter's own conversion rule. CommonMark
    /// lets a setext heading run over several lines, but a document relying on that is already
    /// read differently by every tool that touches it, and widening the rule here would put the
    /// two out of step with each other for no gain.
    ///
    /// The title has to be plain text. A list item, a blockquote or another heading followed by
    /// dashes is a list, a quote or a heading followed by a thematic break, and reading any of
    /// them as a title is how a rewriter deletes a horizontal rule.
    /// </summary>
    private static bool TryReadSetext(IReadOnlyList<string> lines, bool[] guarded, int index, out int level)
    {
        level = 0;

        int next = index + 1;

        if (next >= lines.Count || (next < guarded.Length && guarded[next]))
        {
            return false;
        }

        string title = lines[index];
        string opening = title.TrimStart();

        if (opening.Length == 0
            || IsIndentedFourColumns(title)
            || opening.StartsWith('#')
            || opening.StartsWith('>')
            || ListStructure.IsItem(title))
        {
            return false;
        }

        string underline = lines[next];

        if (IsIndentedFourColumns(underline))
        {
            return false;
        }

        string marks = underline.Trim();

        if (marks.Length == 0)
        {
            return false;
        }

        if (marks.All(c => c == '='))
        {
            level = 1;

            return true;
        }

        if (marks.All(c => c == '-'))
        {
            level = 2;

            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a line's content starts at the fourth column or later, where markdown stops
    /// reading it as text. A tab counts as four, which is what CommonMark does.
    /// </summary>
    private static bool IsIndentedFourColumns(string line)
    {
        int width = 0;

        foreach (char c in line)
        {
            if (c == ' ')
            {
                width++;
            }
            else if (c == '\t')
            {
                width += 4;
            }
            else
            {
                return false;
            }

            if (width >= 4)
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"^(?<indent>[ ]{0,3})(?<hashes>#{1,6})(?:[ \t]+(?<content>.*))?$")]
    private static partial Regex Atx();

    [GeneratedRegex(@"^(?<sec>§[ \t]*)?(?<num>\d{1,9}(?:\.\d{1,9})*)(?<term>[.)])?[ \t]+(?=\S)")]
    private static partial Regex Prefix();
}
