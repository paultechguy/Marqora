// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace PaulTechGuy.MQ.Markdown;

/// <summary>
/// Writes review comments into a copy of the markdown they are about, as CriticMarkup:
/// <c>{==the passage==}{&gt;&gt;the comment&lt;&lt;}</c>.
///
/// The copy is what a review page carries for a reader who wants the source rather than the
/// page - most often an AI asked to act on the comments, which reads a comment sitting beside
/// its words far more reliably than a list of line numbers. It is never written over the
/// original; the caller puts it in the review page and on the clipboard, nowhere else.
///
/// A comment is anchored in the rendered preview, not in the source, so each one arrives as a
/// source line (the block Markdig stamped) and the text the reviewer selected. Finding that text
/// again in the source is the whole job, and rendered text is not source text: emphasis markers,
/// link targets and escapes are gone, and soft line breaks have become spaces. The search tries
/// the text as it stands first, then again with markup skipped on both sides.
///
/// Anything that still cannot be placed inline - a quote that emoji or an entity changed on its
/// way to the screen, or anything inside a code block, which must never be altered - becomes a
/// standalone comment on its own line after the block, quoting the passage it was about. The
/// comment survives; only its exact position is approximate.
/// </summary>
public static class CriticMarkupWriter
{
    /// <summary>How far past its first line a block is searched before giving up on it.</summary>
    private const int MaxBlockLines = 200;

    /// <summary>
    /// One comment to write.
    /// </summary>
    /// <param name="Line">Zero-based source line of the block the passage is in.</param>
    /// <param name="Start">Where the passage starts in the block's rendered text; picks between repeats.</param>
    /// <param name="Quote">The passage as the preview showed it.</param>
    /// <param name="Text">The comment.</param>
    public readonly record struct Note(int Line, int Start, string Quote, string Text);

    /// <summary>
    /// Characters that mark text up rather than being part of it: emphasis, strikethrough and
    /// code, and the <c>^sup^</c>, <c>++inserted++</c> and <c>==marked==</c> the pipeline's
    /// advanced extensions add.
    /// </summary>
    private static bool IsInlineMarkup(char c) => c is '*' or '_' or '~' or '`' or '^' or '+' or '=';

    public static string Write(string source, IReadOnlyList<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(notes);

        if (notes.Count == 0)
        {
            return source;
        }

        string newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        List<int> lineStarts = LineStarts(source);
        string[] lines = [.. Enumerable.Range(0, lineStarts.Count).Select(i => LineText(source, lineStarts, i))];
        bool[] protectedLines = MarkdownRegionScanner.FindProtectedLines(lines, includeIndentedCode: true);

        var inline = new List<(int Start, int End, int Order, string Note)>();
        var standalone = new List<(int Position, int Order, string Text)>();

        for (int order = 0; order < notes.Count; order++)
        {
            Note note = notes[order];
            int line = Math.Clamp(note.Line, 0, lines.Length - 1);

            // A line past the end - a preview that drifted - means the last thing written, not
            // the empty line after the final newline.
            while (line > 0 && note.Line >= lines.Length && lines[line].Trim().Length == 0)
            {
                line--;
            }

            int blockEnd = BlockEnd(lines, protectedLines, line);

            if (!protectedLines[line]
                && Locate(source, lineStarts, lines, line, blockEnd, note) is { } found
                && !inline.Any(r => found.Start < r.End && r.Start < found.End))
            {
                inline.Add((found.Start, found.End, order, InlineNote(note.Text, lines[line])));
                continue;
            }

            int position = lineStarts[blockEnd] + lines[blockEnd].Length;
            bool followedByText = blockEnd + 1 < lines.Length && lines[blockEnd + 1].Trim().Length > 0;

            string text = newline + newline
                + "{>>On \"" + Flatten(note.Quote) + "\": " + Standalone(note.Text, newline) + "<<}"
                + (followedByText ? newline : string.Empty);

            standalone.Add((position, order, text));
        }

        // Every insertion, applied from the end of the text backwards so each one's offsets are
        // still true when it is reached. At one position the later note goes in first, which
        // leaves the notes reading in the order they were given.
        //
        // An inline note is keyed on its end: that is the later of its two insertions, and no
        // other inline note can fall between its start and end because overlaps were turned away.
        // A standalone note can share that key - it goes at the end of the block's last line, which
        // is where a wrap of the block's last words ends - and it is applied first: the wrap's own
        // opening, three characters earlier, would otherwise shift it into the middle of the
        // wrapped words, and applying it first also leaves it after the wrap, where it belongs.
        var edits = new List<(int Key, bool Inline, int Order, Action<StringBuilder> Apply)>();

        edits.AddRange(inline.Select(r => (r.End, true, r.Order, (Action<StringBuilder>)(b =>
        {
            b.Insert(r.End, "==}{>>" + r.Note + "<<}");
            b.Insert(r.Start, "{==");
        }))));

        edits.AddRange(standalone.Select(s => (s.Position, false, s.Order, (Action<StringBuilder>)(b => b.Insert(s.Position, s.Text)))));

        var builder = new StringBuilder(source);

        foreach (var edit in edits.OrderByDescending(e => e.Key).ThenBy(e => e.Inline).ThenByDescending(e => e.Order))
        {
            edit.Apply(builder);
        }

        return builder.ToString();
    }

    // ---------------------------------------------------------------- locating

    /// <summary>
    /// The passage's range in the source, or null. The exact text first; then, failing that, a
    /// match with markup skipped on both sides and whitespace runs treated as one space. Where
    /// the passage occurs more than once in the block, the occurrence nearest where the
    /// reviewer selected it wins.
    /// </summary>
    private static (int Start, int End)? Locate(
        string source,
        List<int> lineStarts,
        string[] lines,
        int firstLine,
        int lastLine,
        Note note)
    {
        if (string.IsNullOrWhiteSpace(note.Quote))
        {
            return null;
        }

        int regionStart = lineStarts[firstLine];
        int regionEnd = lineStarts[lastLine] + lines[lastLine].Length;
        string region = source[regionStart..regionEnd];

        // Searched with link targets, URLs and code spans blanked out, length for length, so a
        // passage that is a link's text cannot be found inside the link's own address and
        // wrapped there - which would break the link.
        char[] masked = region.ToCharArray();
        char[] code = region.ToCharArray();

        for (int i = firstLine; i <= lastLine; i++)
        {
            LineMasker.MaskNonProse(lines[i]).CopyTo(0, masked, lineStarts[i] - regionStart, lines[i].Length);
            LineMasker.MaskCodeSpans(lines[i]).CopyTo(0, code, lineStarts[i] - regionStart, lines[i].Length);
        }

        int best = Nearest(AllIndexes(new string(masked), note.Quote), note.Start);

        if (best >= 0)
        {
            (int s, int e) = Widen(region, best, best + note.Quote.Length);

            return CutsCode(region, code, s, e) ? null : (regionStart + s, regionStart + e);
        }

        (string text, int[] map) = Normalize(region);
        string quote = Normalize(note.Quote).Text.Trim();

        if (quote.Length == 0)
        {
            return null;
        }

        int match = Nearest(AllIndexes(text, quote), note.Start);

        if (match < 0)
        {
            return null;
        }

        (int start, int end) = Widen(region, map[match], map[match + quote.Length - 1] + 1);

        return CutsCode(region, code, start, end) ? null : (regionStart + start, regionStart + end);
    }

    /// <summary>
    /// Whether a wrap from <paramref name="start"/> to <paramref name="end"/> would put markup inside
    /// a code span. Inside one, CriticMarkup is not markup but literal text, so it would change
    /// the code - the one thing this never does. A wrap that takes in a whole span, backticks and
    /// all, is fine; one that starts or ends among its characters is not, and the note goes after
    /// the block instead.
    ///
    /// <paramref name="code"/> is the region with each code span masked, so a span is a run
    /// of positions where the two differ. A character in the code that happens to be the mask
    /// character splits its run in two, which asks the same question twice and answers it the
    /// same way.
    /// </summary>
    private static bool CutsCode(string region, char[] code, int start, int end)
    {
        for (int i = 0; i < region.Length; i++)
        {
            if (code[i] == region[i])
            {
                continue;
            }

            int runStart = i;

            while (i < region.Length && code[i] != region[i])
            {
                i++;
            }

            int runEnd = i;
            bool touches = start < runEnd && end > runStart;
            bool whole = start <= runStart && end >= runEnd;

            if (touches && !whole)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Takes in the markers standing directly against the passage, so "**bold** word" is wrapped
    /// whole rather than split through the middle of its emphasis, and a link's text takes its
    /// brackets and target with it.
    ///
    /// Only at a word boundary. An underscore inside <c>snake_case</c> is part of the word the
    /// reader saw, and pulling it into the wrap would cut the word in two.
    /// </summary>
    private static (int Start, int End) Widen(string region, int start, int end)
    {
        int s = start;

        while (s > 0 && (IsInlineMarkup(region[s - 1]) || region[s - 1] == '[' || region[s - 1] == '!'))
        {
            s--;
        }

        if (s < start && s > 0 && char.IsLetterOrDigit(region[s - 1]))
        {
            s = start;
        }

        int e = end;

        while (e < region.Length)
        {
            if (IsInlineMarkup(region[e]))
            {
                e++;
            }
            else if (region[e] == ']')
            {
                e = SkipLinkTail(region, e);
            }
            else
            {
                break;
            }
        }

        if (e > end && e < region.Length && char.IsLetterOrDigit(region[e]))
        {
            e = end;
        }

        // A bracket taken on one side and not the other would leave a link half inside the
        // markup; better to wrap only the words.
        bool openedLink = region.AsSpan(s, start - s).Contains('[');
        bool closedLink = region.AsSpan(end, e - end).Contains(']');

        if (openedLink != closedLink)
        {
            return (openedLink ? start : s, closedLink ? end : e);
        }

        return (s, e);
    }

    /// <summary>
    /// The passage's text reduced to what the preview would have shown, with a map from each
    /// kept character back to where it came from.
    /// </summary>
    private static (string Text, int[] Map) Normalize(string text)
    {
        var kept = new StringBuilder(text.Length);
        var map = new List<int>(text.Length);
        bool atLineStart = true;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (c is '\r' or '\n' || char.IsWhiteSpace(c))
            {
                if (c == '\n')
                {
                    atLineStart = true;
                }

                if (kept.Length > 0 && kept[^1] != ' ')
                {
                    kept.Append(' ');
                    map.Add(i);
                }

                continue;
            }

            // A blockquote's marker on a continuation line is not part of the paragraph.
            if (atLineStart && c == '>')
            {
                continue;
            }

            atLineStart = false;

            if (IsInlineMarkup(c) || c == '[' || (c == '!' && i + 1 < text.Length && text[i + 1] == '['))
            {
                continue;
            }

            if (c == ']')
            {
                i = SkipLinkTail(text, i) - 1;
                continue;
            }

            if (c == '\\' && i + 1 < text.Length && (char.IsPunctuation(text[i + 1]) || char.IsSymbol(text[i + 1])))
            {
                i++;
                c = text[i];
            }

            kept.Append(c);
            map.Add(i);
        }

        return (kept.ToString(), [.. map]);
    }

    /// <summary>
    /// Past a link's closing bracket and whatever target follows it: <c>](url)</c>,
    /// <c>][ref]</c>, or just the bracket.
    /// </summary>
    private static int SkipLinkTail(string text, int bracket)
    {
        int i = bracket + 1;

        if (i < text.Length && text[i] is '(' or '[')
        {
            char open = text[i];
            char close = open == '(' ? ')' : ']';
            int depth = 0;

            for (; i < text.Length; i++)
            {
                if (text[i] == open)
                {
                    depth++;
                }
                else if (text[i] == close && --depth == 0)
                {
                    return i + 1;
                }
                else if (text[i] == '\n')
                {
                    break;
                }
            }

            return bracket + 1;
        }

        return i;
    }

    private static List<int> AllIndexes(string text, string value)
    {
        var found = new List<int>();

        for (int i = text.IndexOf(value, StringComparison.Ordinal);
            i >= 0;
            i = text.IndexOf(value, i + 1, StringComparison.Ordinal))
        {
            found.Add(i);
        }

        return found;
    }

    private static int Nearest(List<int> candidates, int wanted) =>
        candidates.Count == 0 ? -1 : candidates.MinBy(c => Math.Abs(c - wanted));

    // ------------------------------------------------------------------ shape

    /// <summary>
    /// The last line of the block starting at <paramref name="line"/>: through a whole code
    /// block or front matter if it starts in one, otherwise up to the next blank line.
    /// </summary>
    private static int BlockEnd(string[] lines, bool[] protectedLines, int line)
    {
        int last = line;
        bool inProtected = protectedLines[line];
        int cap = Math.Min(lines.Length - 1, line + MaxBlockLines);

        while (last < cap)
        {
            int next = last + 1;

            if (inProtected ? !protectedLines[next] : lines[next].Trim().Length == 0 || protectedLines[next])
            {
                break;
            }

            last = next;
        }

        return last;
    }

    /// <summary>
    /// A note written inside a line. A line break would end a table row or a heading and start a
    /// new paragraph anywhere else, so breaks become a pilcrow between paragraphs and a space
    /// within one; a pipe in a table row would open a new cell, so it is escaped.
    /// </summary>
    private static string InlineNote(string note, string line)
    {
        string text = Sanitize(note)
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        text = string.Join(" ¶ ", text
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Replace('\n', ' ')));

        return line.TrimStart().StartsWith('|') ? text.Replace("|", "\\|", StringComparison.Ordinal) : text;
    }

    /// <summary>A note on a line of its own keeps its paragraphs.</summary>
    private static string Standalone(string note, string newline) =>
        Sanitize(note)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim()
            .Replace("\n", newline, StringComparison.Ordinal);

    /// <summary>A quoted passage on one line, with its quotes made safe.</summary>
    private static string Flatten(string quote) =>
        string.Join(' ', Sanitize(quote).Split((char[])['\r', '\n', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
            .Replace("\"", "'", StringComparison.Ordinal);

    /// <summary>Keeps a note from closing the markup around it early.</summary>
    private static string Sanitize(string text) =>
        text.Replace("<<}", "<< }", StringComparison.Ordinal)
            .Replace("==}", "== }", StringComparison.Ordinal);

    private static List<int> LineStarts(string source)
    {
        var starts = new List<int> { 0 };

        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        return starts;
    }

    /// <summary>A line's text without its terminator.</summary>
    private static string LineText(string source, List<int> starts, int line)
    {
        int start = starts[line];
        int end = line + 1 < starts.Count ? starts[line + 1] - 1 : source.Length;

        if (end > start && source[end - 1] == '\r')
        {
            end--;
        }

        return source[start..Math.Max(start, end)];
    }
}
