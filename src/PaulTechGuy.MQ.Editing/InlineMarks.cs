// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Editing;

/// <summary>One pair of emphasis markers found on a line, and where the two halves sit.</summary>
/// <param name="Marker">The marker text: "**", "*", "~~", or a run of backticks.</param>
/// <param name="OpenStart">Where the opening marker begins.</param>
/// <param name="CloseStart">Where the closing marker begins.</param>
internal readonly record struct InlineMark(string Marker, int OpenStart, int CloseStart)
{
    /// <summary>One past the opening marker, where the text it wraps starts.</summary>
    public int OpenEnd => OpenStart + Marker.Length;

    /// <summary>One past the closing marker, where the pair as a whole ends.</summary>
    public int CloseEnd => CloseStart + Marker.Length;

    /// <summary>Whether a span sits inside this pair, with the markers themselves outside it.</summary>
    public bool Wraps(int start, int end) => OpenEnd <= start && end <= CloseStart;
}

/// <summary>
/// Reads a line into the emphasis pairs it contains, nesting and all.
///
/// The toolbar has to answer "is the caret inside italics" for text that may be inside
/// several marks at once, and "***word***" is the case that forced this: matching markers
/// by looking just outside the caret cannot tell the middle asterisk of that run from
/// either of the two beside it. Pairing the whole line with a stack can, because it knows
/// which markers are still open when it reaches the closing run.
///
/// This is not a markdown parser and does not try to be one. It knows the four marks the
/// Format toolbar produces, and it follows CommonMark only as far as the questions the
/// toolbar asks: emphasis runs pair up last-opened-first, a marker with a space on the
/// inside opens or closes nothing, and a code span swallows whatever is between its
/// backticks.
/// </summary>
internal static class InlineMarks
{
    /// <summary>
    /// Every complete pair on the line, in the order the closing markers were reached —
    /// so an inner pair is always listed before the pair that contains it.
    /// </summary>
    public static IReadOnlyList<InlineMark> On(string text)
    {
        List<InlineMark> marks = [];
        List<(string Marker, int Start)> open = [];

        int i = 0;

        while (i < text.Length)
        {
            char delimiter = text[i];

            if (delimiter is not ('*' or '~' or '`'))
            {
                i++;

                continue;
            }

            int run = RunLength(text, i, delimiter);

            i = delimiter == '`'
                ? ReadCodeSpan(text, i, run, marks)
                : ReadEmphasis(text, i, run, delimiter, open, marks);
        }

        return marks;
    }

    /// <summary>
    /// A code span runs to the next backtick run of exactly the same length, and nothing
    /// inside it is a marker: the asterisks in `**literal**` are characters, not bold.
    /// Scanning resumes past the closing run, so they are never seen.
    /// </summary>
    private static int ReadCodeSpan(string text, int start, int run, List<InlineMark> marks)
    {
        int close = FindBacktickRun(text, start + run, run);

        if (close < 0)
        {
            return start + run;
        }

        marks.Add(new InlineMark(new string('`', run), start, close));

        return close + run;
    }

    private static int FindBacktickRun(string text, int from, int length)
    {
        for (int i = from; i < text.Length; i++)
        {
            if (text[i] != '`')
            {
                continue;
            }

            int run = RunLength(text, i, '`');

            if (run == length)
            {
                return i;
            }

            i += run - 1;
        }

        return -1;
    }

    /// <summary>
    /// Closes as many open marks as this run can reach, then opens marks with whatever is
    /// left over.
    ///
    /// Closing first, and innermost first, is what tells "***word***" apart: the run of
    /// three closes the "**" it finds on the stack and then the "*" underneath it, which
    /// is the nesting the opening run built and the one CommonMark renders.
    /// </summary>
    private static int ReadEmphasis(
        string text,
        int start,
        int run,
        char delimiter,
        List<(string Marker, int Start)> open,
        List<InlineMark> marks)
    {
        int end = start + run;

        // A marker with whitespace on the inside is punctuation rather than emphasis, so
        // "2 * 3 * 4" stays arithmetic.
        bool canOpen = end < text.Length && !char.IsWhiteSpace(text[end]);
        bool canClose = start > 0 && !char.IsWhiteSpace(text[start - 1]);

        int at = start;
        int left = run;

        while (canClose && left > 0 && open.Count > 0)
        {
            (string marker, int openStart) = open[^1];

            if (marker[0] != delimiter || marker.Length > left)
            {
                break;
            }

            open.RemoveAt(open.Count - 1);
            marks.Add(new InlineMark(marker, openStart, at));

            at += marker.Length;
            left -= marker.Length;
        }

        while (canOpen && left > 0)
        {
            if (MarkerFor(delimiter, left) is not { } opening)
            {
                break;
            }

            open.Add((opening, at));

            at += opening.Length;
            left -= opening.Length;
        }

        return end;
    }

    /// <summary>
    /// Which marker the next slice of an opening run is. An odd number of asterisks puts
    /// the single one outermost, so "***word***" opens italic and then bold inside it and
    /// the closing run unwinds them in that order.
    /// </summary>
    private static string? MarkerFor(char delimiter, int left) => delimiter switch
    {
        '*' => left % 2 == 1 ? "*" : "**",
        '~' when left >= 2 => "~~",
        _ => null,
    };

    private static int RunLength(string text, int start, char delimiter)
    {
        int end = start;

        while (end < text.Length && text[end] == delimiter)
        {
            end++;
        }

        return end - start;
    }
}
