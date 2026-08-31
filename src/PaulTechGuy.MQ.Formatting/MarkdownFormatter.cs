// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using PaulTechGuy.MQ.Abstractions.Formatting;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Formatting;

/// <summary>
/// Tidies markdown source, rule by rule.
///
/// This works on lines rather than on a parsed syntax tree. That is a deliberate choice:
/// Markdig can parse markdown but cannot render it back as markdown, so a tree-based
/// formatter would have to reconstruct every construct from scratch and would rewrite far
/// more of the document than the user asked for. Working on lines keeps the output close to
/// the input — a rule that is switched off leaves no trace at all.
///
/// The overriding rule is that content inside fenced code blocks and YAML front matter is
/// never touched. Everything else is arranged so that if a rule cannot be applied safely, it
/// is not applied.
/// </summary>
public sealed class MarkdownFormatter : IMarkdownFormatter
{
    public FormattedMarkdown Format(string markdown, FormatOptions options)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(options);

        return Run(markdown, options, null, null);
    }

    public FormattedMarkdown FormatLines(string markdown, int firstLine, int lastLine, FormatOptions options)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(options);

        return Run(markdown, options, firstLine, lastLine);
    }

    private static FormattedMarkdown Run(string markdown, FormatOptions options, int? first, int? last)
    {
        if (markdown.Length == 0)
        {
            return new FormattedMarkdown(markdown, 0);
        }

        string ending = ResolveLineEnding(markdown, options);
        string[] original = SplitLines(markdown);

        List<Line> lines = Classify(original);

        // Outside the requested range nothing may change. Marking those lines frozen is
        // simpler and safer than slicing the document up, and it means the block scan above
        // still sees the whole file — a line's meaning depends on what came before it.
        if (first is int from && last is int to)
        {
            Freeze(lines, from, to);

            // The end of the file is outside the selection like anything else, so the rule
            // that guarantees a trailing newline sits this one out.
            options = options with { EofNewline = false };
        }

        ApplyLineRules(lines, options);

        if (options.SetextToAtx)
        {
            ConvertSetextHeadings(lines);
        }

        if (options.OrderedNumbering)
        {
            RenumberOrderedLists(lines);
        }

        if (options.FormatTables)
        {
            MarkdownTableFormatter.Apply(lines);
        }

        if (options.TidyCodeFences)
        {
            TidyCodeFences(lines, options);
        }

        if (options.ReflowParagraphs)
        {
            ReflowParagraphs(lines, options);
        }

        if (options.BlankLines)
        {
            EnsureBlankLinesAroundBlocks(lines);
        }

        if (options.CollapseBlanks)
        {
            CollapseBlankRuns(lines);
        }

        string text = Join(lines, ending, options);

        return new FormattedMarkdown(text, CountChangedLines(original, lines));
    }

    // ------------------------------------------------------------------- lines

    /// <summary>What a line is, as far as the formatter is allowed to care.</summary>
    internal enum LineKind
    {
        /// <summary>Ordinary markdown, open to every rule.</summary>
        Text,

        /// <summary>The ``` or ~~~ line opening or closing a fence.</summary>
        FenceDelimiter,

        /// <summary>Inside a fence. Never altered.</summary>
        FenceContent,

        /// <summary>The --- lines around YAML front matter.</summary>
        FrontMatterDelimiter,

        /// <summary>Inside front matter. Never altered.</summary>
        FrontMatterContent,
    }

    /// <summary>One line of the document as it is being worked on.</summary>
    internal sealed class Line(string text, LineKind kind)
    {
        public string Text { get; set; } = text;

        public LineKind Kind { get; set; } = kind;

        /// <summary>Set for lines outside a requested range, which must survive untouched.</summary>
        public bool Frozen { get; set; }

        /// <summary>True when this line may be rewritten.</summary>
        public bool IsEditable => !Frozen && Kind is LineKind.Text or LineKind.FenceDelimiter;

        /// <summary>True when this line is plain markdown text, open to every rule.</summary>
        public bool IsText => !Frozen && Kind == LineKind.Text;

        public bool IsBlank => Text.Trim().Length == 0;

        public override string ToString() => Text;
    }

    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

    /// <summary>
    /// Chooses the line ending to write. Detect counts what the document already uses, so a
    /// file that came from another platform is not silently converted.
    /// </summary>
    private static string ResolveLineEnding(string text, FormatOptions options)
    {
        if (!options.LineEndings)
        {
            return DetectEnding(text);
        }

        return options.LineEndingStyle switch
        {
            LineEndingStyle.Crlf => "\r\n",
            LineEndingStyle.Lf => "\n",
            _ => DetectEnding(text),
        };
    }

    private static string DetectEnding(string text)
    {
        int crlf = 0;
        int lf = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
            {
                continue;
            }

            if (i > 0 && text[i - 1] == '\r')
            {
                crlf++;
            }
            else
            {
                lf++;
            }
        }

        // Windows is the default for a Windows editor when a file has nothing to say.
        return lf > crlf ? "\n" : "\r\n";
    }

    /// <summary>
    /// Walks the document once, marking which lines are code or front matter.
    ///
    /// Everything downstream trusts this: a rule only ever looks at <see cref="Line.IsText"/>
    /// rather than re-deciding for itself what is safe to touch.
    /// </summary>
    private static List<Line> Classify(string[] source)
    {
        var lines = new List<Line>(source.Length);

        string? fence = null;
        bool inFrontMatter = false;

        for (int i = 0; i < source.Length; i++)
        {
            string raw = source[i];
            string trimmed = raw.TrimStart();

            // Front matter is only front matter at the very top of the file.
            if (i == 0 && trimmed is "---")
            {
                inFrontMatter = true;
                lines.Add(new Line(raw, LineKind.FrontMatterDelimiter));
                continue;
            }

            if (inFrontMatter)
            {
                bool closing = trimmed is "---" or "...";
                lines.Add(new Line(raw, closing ? LineKind.FrontMatterDelimiter : LineKind.FrontMatterContent));
                inFrontMatter = !closing;
                continue;
            }

            if (fence is null)
            {
                if (TryReadFence(trimmed, out string? opened))
                {
                    fence = opened;
                    lines.Add(new Line(raw, LineKind.FenceDelimiter));
                    continue;
                }

                lines.Add(new Line(raw, LineKind.Text));
                continue;
            }

            // A fence closes on a run of the same character at least as long as the opener.
            if (TryReadFence(trimmed, out string? closer)
                && closer![0] == fence[0]
                && closer.Length >= fence.Length
                && trimmed.TrimEnd().Length == closer.Length)
            {
                fence = null;
                lines.Add(new Line(raw, LineKind.FenceDelimiter));
                continue;
            }

            lines.Add(new Line(raw, LineKind.FenceContent));
        }

        return lines;
    }

    /// <summary>Reads the run of backticks or tildes opening a fence, if this line is one.</summary>
    private static bool TryReadFence(string trimmed, out string? marker)
    {
        marker = null;

        if (trimmed.Length < 3)
        {
            return false;
        }

        char c = trimmed[0];

        if (c is not ('`' or '~'))
        {
            return false;
        }

        int run = 0;
        while (run < trimmed.Length && trimmed[run] == c)
        {
            run++;
        }

        if (run < 3)
        {
            return false;
        }

        // A backtick fence's info string may not contain a backtick.
        if (c == '`' && trimmed[run..].Contains('`', StringComparison.Ordinal))
        {
            return false;
        }

        marker = new string(c, run);
        return true;
    }

    private static void Freeze(List<Line> lines, int first, int last)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            if (i < first || i > last)
            {
                lines[i].Frozen = true;
            }
        }
    }

    // -------------------------------------------------------------- line rules

    private static void ApplyLineRules(List<Line> lines, FormatOptions options)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            Line line = lines[i];

            if (!line.IsEditable)
            {
                continue;
            }

            string text = line.Text;

            if (line.Kind == LineKind.Text)
            {
                if (options.HeadingSpace)
                {
                    text = MarkdownLineRules.AddHeadingSpace(text);
                }

                if (options.BlockquoteSpace)
                {
                    text = MarkdownLineRules.AddBlockquoteSpace(text);
                }

                if (options.ListMarkerSpace)
                {
                    text = MarkdownLineRules.AddListMarkerSpace(text);
                }

                if (options.NormalizeMarkers)
                {
                    text = MarkdownLineRules.NormalizeBullet(text, options.Bullet);
                }

                if (options.LinkSyntax)
                {
                    text = MarkdownLineRules.OutsideCodeSpans(text, MarkdownLineRules.TidyLinkSyntax);
                }

                if (options.UnifyEmphasis)
                {
                    text = MarkdownLineRules.OutsideCodeSpans(
                        text,
                        segment => MarkdownLineRules.UnifyEmphasis(segment, options.Emphasis));
                }
            }

            if (options.TrailingWhitespace)
            {
                bool nextIsText = i + 1 < lines.Count
                    && lines[i + 1].Kind == LineKind.Text
                    && !lines[i + 1].IsBlank;

                text = MarkdownLineRules.TrimTrailing(text, keepHardBreak: line.Kind == LineKind.Text && nextIsText);
            }

            line.Text = text;
        }
    }

    // ---------------------------------------------------------------- headings

    /// <summary>
    /// Turns underlined headings into the hash form.
    ///
    /// The underline has to be distinguished from a thematic break: a run of dashes is a
    /// heading only when the line above it is ordinary paragraph text, and a break
    /// otherwise. Getting that wrong would silently delete a horizontal rule.
    /// </summary>
    private static void ConvertSetextHeadings(List<Line> lines)
    {
        for (int i = lines.Count - 1; i >= 1; i--)
        {
            Line underline = lines[i];
            Line title = lines[i - 1];

            if (!underline.IsText || !title.IsText || title.IsBlank)
            {
                continue;
            }

            string u = underline.Text.Trim();

            if (u.Length == 0 || !(u.All(c => c == '=') || u.All(c => c == '-')))
            {
                continue;
            }

            string heading = title.Text.Trim();

            // Only a plain paragraph can carry a setext underline.
            if (heading.StartsWith('#')
                || heading.StartsWith('>')
                || heading.StartsWith('|')
                || MarkdownLineRules.IsListItem(heading))
            {
                continue;
            }

            title.Text = (u[0] == '=' ? "# " : "## ") + heading;
            lines.RemoveAt(i);
        }
    }

    // ------------------------------------------------------------------- lists

    /// <summary>
    /// Renumbers ordered lists so each run counts up from whatever it started at.
    ///
    /// The starting number is kept rather than forced to 1: a list that deliberately begins
    /// at 5 is continuing an earlier one, and renumbering it would change what the document
    /// says.
    /// </summary>
    private static void RenumberOrderedLists(List<Line> lines)
    {
        // Indent width to the next number expected at that depth.
        var counters = new Dictionary<int, int>();
        int blanks = 0;
        bool blankBefore = false;

        foreach (Line line in lines)
        {
            if (line.Kind != LineKind.Text)
            {
                counters.Clear();
                continue;
            }

            if (line.IsBlank)
            {
                // One blank line inside a list is a loose list, not the end of it. Two is.
                blankBefore = true;

                if (++blanks > 1)
                {
                    counters.Clear();
                }

                continue;
            }

            blanks = 0;
            bool hadBlank = blankBefore;
            blankBefore = false;

            if (!MarkdownLineRules.TryReadOrderedItem(line.Text, out int indent, out int number, out char delimiter, out string rest))
            {
                // A line indented under the item is its continuation; anything at the left
                // margin ends the list.
                if (line.Text.Length > 0 && !char.IsWhiteSpace(line.Text[0]))
                {
                    counters.Clear();
                }

                continue;
            }

            // A deeper list restarts; stepping back out drops the deeper counters.
            foreach (int deeper in counters.Keys.Where(k => k > indent).ToList())
            {
                counters.Remove(deeper);
            }

            bool known = counters.TryGetValue(indent, out int expected);

            // A number that jumps forward after a blank line is the author starting a new
            // list, not losing count. Renumbering it would change what the document says, so
            // the sequence restarts from whatever they wrote. A number that falls behind is
            // the familiar "1. 1. 1." shorthand and does get renumbered.
            if (known && hadBlank && number > expected)
            {
                known = false;
            }

            int next = known ? expected : number;
            counters[indent] = next + 1;

            if (line.Frozen)
            {
                continue;
            }

            line.Text = string.Concat(new string(' ', indent), next.ToString(System.Globalization.CultureInfo.InvariantCulture), delimiter.ToString(), " ", rest);
        }
    }

    // ------------------------------------------------------------------ fences

    /// <summary>Unifies fence characters and guarantees a blank line either side.</summary>
    private static void TidyCodeFences(List<Line> lines, FormatOptions options)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Kind != LineKind.FenceDelimiter || lines[i].Frozen)
            {
                continue;
            }

            string trimmed = lines[i].Text.TrimStart();

            // Tildes are only switched to backticks when nothing inside would end the fence
            // early, which is the whole reason a document uses tildes in the first place.
            if (trimmed.StartsWith('~') && !FenceBodyContainsBackticks(lines, i))
            {
                int indent = lines[i].Text.Length - trimmed.Length;
                lines[i].Text = new string(' ', indent) + trimmed.Replace('~', '`');
            }
        }

        // Blank lines around fences are handled with the other block spacing.
        _ = options;
    }

    private static bool FenceBodyContainsBackticks(List<Line> lines, int fenceStart)
    {
        for (int i = fenceStart + 1; i < lines.Count; i++)
        {
            if (lines[i].Kind == LineKind.FenceDelimiter)
            {
                return false;
            }

            if (lines[i].Kind != LineKind.FenceContent)
            {
                return false;
            }

            if (lines[i].Text.Contains("```", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------- blank lines

    /// <summary>
    /// Puts a blank line above and below headings, fences and tables.
    ///
    /// Only those three. It is tempting to space out lists and paragraphs too, but a blank
    /// line between list items turns a tight list into a loose one, which changes the
    /// rendered output — and a formatter that changes output is a bug.
    /// </summary>
    private static void EnsureBlankLinesAroundBlocks(List<Line> lines)
    {
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            Line line = lines[i];

            if (line.Frozen || !NeedsSurroundingBlanks(line))
            {
                continue;
            }

            bool opensBlock = line.Kind != LineKind.FenceDelimiter || IsFenceOpener(lines, i);
            bool closesBlock = line.Kind != LineKind.FenceDelimiter || !IsFenceOpener(lines, i);

            if (closesBlock && i + 1 < lines.Count && !lines[i + 1].IsBlank && !lines[i + 1].Frozen
                && !ContinuesSameBlock(line, lines[i + 1]))
            {
                lines.Insert(i + 1, new Line(string.Empty, LineKind.Text));
            }

            if (opensBlock && i > 0 && !lines[i - 1].IsBlank && !lines[i - 1].Frozen
                && !ContinuesSameBlock(lines[i - 1], line))
            {
                lines.Insert(i, new Line(string.Empty, LineKind.Text));
            }
        }
    }

    private static bool NeedsSurroundingBlanks(Line line)
    {
        if (line.Kind == LineKind.FenceDelimiter)
        {
            return true;
        }

        if (line.Kind != LineKind.Text || line.IsBlank)
        {
            return false;
        }

        return line.Text.TrimStart().StartsWith('#') || MarkdownLineRules.IsTableRow(line.Text);
    }

    /// <summary>True when two adjacent lines belong to the same block and must stay together.</summary>
    private static bool ContinuesSameBlock(Line above, Line below)
    {
        // Rows of one table, or the fence line and its own contents.
        if (MarkdownLineRules.IsTableRow(above.Text) && MarkdownLineRules.IsTableRow(below.Text))
        {
            return true;
        }

        return above.Kind == LineKind.FenceDelimiter && below.Kind == LineKind.FenceContent
            || above.Kind == LineKind.FenceContent && below.Kind == LineKind.FenceDelimiter;
    }

    private static bool IsFenceOpener(List<Line> lines, int index)
    {
        // The first delimiter in the file opens; they alternate from there.
        int count = 0;

        for (int i = 0; i < index; i++)
        {
            if (lines[i].Kind == LineKind.FenceDelimiter)
            {
                count++;
            }
        }

        return count % 2 == 0;
    }

    private static void CollapseBlankRuns(List<Line> lines)
    {
        for (int i = lines.Count - 1; i >= 1; i--)
        {
            if (lines[i].IsBlank && lines[i - 1].IsBlank
                && lines[i].Kind == LineKind.Text && !lines[i].Frozen && !lines[i - 1].Frozen)
            {
                lines.RemoveAt(i);
            }
        }
    }

    // ----------------------------------------------------------------- reflow

    /// <summary>
    /// Re-wraps plain paragraphs to the requested column.
    ///
    /// Only paragraphs at the left margin. List items, blockquotes and tables are left alone:
    /// wrapping them correctly means reproducing their continuation indent, and getting that
    /// wrong silently changes the structure of the document. Wrapping ordinary prose is where
    /// nearly all the value is, and it is the part that can be done safely.
    /// </summary>
    private static void ReflowParagraphs(List<Line> lines, FormatOptions options)
    {
        int width = Math.Max(20, options.WrapColumn);

        int i = 0;

        while (i < lines.Count)
        {
            if (!IsReflowable(lines[i]))
            {
                i++;
                continue;
            }

            int start = i;
            int end = i;

            while (end + 1 < lines.Count && IsReflowable(lines[end + 1]))
            {
                end++;
            }

            // A hard line break is a deliberate instruction about layout; leave the
            // paragraph containing one exactly as the author wrote it.
            bool hasHardBreak = false;

            for (int n = start; n < end; n++)
            {
                if (lines[n].Text.EndsWith("  ", StringComparison.Ordinal)
                    || lines[n].Text.EndsWith('\\'))
                {
                    hasHardBreak = true;
                    break;
                }
            }

            if (!hasHardBreak && end > start || !hasHardBreak && lines[start].Text.Length > width)
            {
                var words = new List<string>();

                for (int n = start; n <= end; n++)
                {
                    words.AddRange(lines[n].Text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                }

                List<string> wrapped = Wrap(words, width);

                lines.RemoveRange(start, end - start + 1);

                for (int n = 0; n < wrapped.Count; n++)
                {
                    lines.Insert(start + n, new Line(wrapped[n], LineKind.Text));
                }

                i = start + wrapped.Count;
                continue;
            }

            i = end + 1;
        }
    }

    private static bool IsReflowable(Line line)
    {
        if (!line.IsText || line.IsBlank)
        {
            return false;
        }

        string text = line.Text;

        // Anything indented is inside a list or a code block by indentation.
        if (text.Length > 0 && char.IsWhiteSpace(text[0]))
        {
            return false;
        }

        if (text.StartsWith('#') || text.StartsWith('>') || text.StartsWith('|') || text.StartsWith('<'))
        {
            return false;
        }

        // A link reference definition has to stay on its own line.
        if (text.StartsWith('[') && text.Contains("]:", StringComparison.Ordinal))
        {
            return false;
        }

        return !MarkdownLineRules.IsListItem(text) && !MarkdownLineRules.IsThematicBreak(text);
    }

    private static List<string> Wrap(List<string> words, int width)
    {
        var result = new List<string>();
        var current = new StringBuilder();

        foreach (string word in words)
        {
            if (current.Length == 0)
            {
                current.Append(word);
                continue;
            }

            if (current.Length + 1 + word.Length <= width)
            {
                current.Append(' ').Append(word);
                continue;
            }

            result.Add(current.ToString());
            current.Clear().Append(word);
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    // ------------------------------------------------------------------ output

    private static string Join(List<Line> lines, string ending, FormatOptions options)
    {
        var builder = new StringBuilder();

        int lastContent = lines.Count - 1;

        if (options.EofNewline)
        {
            while (lastContent >= 0 && lines[lastContent].IsBlank)
            {
                lastContent--;
            }
        }

        for (int i = 0; i <= lastContent; i++)
        {
            builder.Append(lines[i].Text);

            if (i < lastContent)
            {
                builder.Append(ending);
            }
        }

        if (options.EofNewline && lastContent >= 0)
        {
            builder.Append(ending);
        }

        return builder.ToString();
    }

    private static int CountChangedLines(string[] original, List<Line> formatted)
    {
        int changed = Math.Abs(original.Length - formatted.Count);
        int shared = Math.Min(original.Length, formatted.Count);

        for (int i = 0; i < shared; i++)
        {
            if (!string.Equals(original[i], formatted[i].Text, StringComparison.Ordinal))
            {
                changed++;
            }
        }

        return changed;
    }
}
