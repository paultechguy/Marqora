// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Markdown;

namespace PaulTechGuy.MQ.Formatting;

/// <summary>
/// Writes section numbers into a document's markdown, and takes them out again.
///
/// The counterpart to HeadingNumberPass, which puts numbers into the rendered copy and leaves
/// the source alone. This is the other choice, and it is the user's rather than the app's: the
/// preview, the PDF, the HTML export and the printed page are all numbered already, so the only
/// reason to freeze numbers into the text is that the markdown itself is going somewhere — a
/// pull request, a wiki, an email — where nothing will number it on the way.
///
/// Both directions come from one judgment. <see cref="HeadingNumberDetector"/> decides which
/// leading numbers are the author's numbering rather than a year or a version, and neither
/// method here second-guesses it: removing strips exactly what it named, and numbering replaces
/// exactly what it named and prepends to everything else. That is what makes the pair invertible
/// — numbering an unnumbered document and then removing the numbers gives the original text back
/// character for character.
///
/// Changing a heading's text changes its anchor, so the links that pointed at the old name are
/// moved in the same edit. See <see cref="Anchors"/> for how far that can be trusted and what
/// happens when it cannot.
///
/// Nothing is reflowed, re-fenced or retabbed on the way through. The result is the input with
/// prefixes and link targets spliced, so a document's line endings, its trailing spaces and its
/// indentation come out exactly as they went in. That matters more than it sounds: this runs on
/// a file the user did not necessarily write, and a command called "Remove Heading Numbers" that
/// also normalized line endings would be a command nobody could safely run.
/// </summary>
public static class HeadingNumberRewriter
{
    /// <summary>One run to splice: what to drop, and what to put there instead.</summary>
    /// <param name="Offset">Where the run starts in the whole document.</param>
    /// <param name="Consume">Characters to drop, zero to insert only.</param>
    /// <param name="Insert">What to write there, empty to remove only.</param>
    private readonly record struct Edit(int Offset, int Consume, string Insert);

    /// <summary>The rewritten markdown, and what moved.</summary>
    /// <param name="Text">The document, spliced.</param>
    /// <param name="Changed">How many headings actually moved.</param>
    public sealed record Result(string Text, int Changed)
    {
        /// <summary>How many link destinations were repointed at a renamed heading.</summary>
        public int LinksMoved { get; init; }

        /// <summary>
        /// Headings whose anchor could not be worked out from the source text alone, so any link
        /// pointing at one was left exactly as it was. See <see cref="Anchors.IsDerivable"/>.
        /// </summary>
        public int UncertainHeadings { get; init; }

        /// <summary>
        /// Nothing to do. Said plainly by the caller rather than silently, because a command
        /// that appears to do nothing reads as a broken one.
        /// </summary>
        public bool IsUnchanged => Changed == 0 && LinksMoved == 0;
    }

    /// <summary>
    /// What a rewrite of this document would be acting on: its headings, and which of their
    /// leading numbers are the author's own numbering.
    ///
    /// Here rather than left to the caller so that a dialog counting headings and the rewrite
    /// that follows it are reading the same document the same way — including how the text was
    /// split into lines, which is the part easiest to get quietly different.
    /// </summary>
    public static HeadingNumberDetector.Scan Read(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        (string[] lines, _) = SplitLines(markdown);

        return HeadingNumberDetector.Read(lines);
    }

    /// <summary>
    /// Takes the author's section numbers out of the heading text, leaving the words.
    ///
    /// A document nobody judged to be numbered comes back untouched, which is the answer for a
    /// document whose headings merely open with digits. A heading inside a numbered document
    /// that did not write the number the count would have given it — the "2026 Budget" sitting
    /// between "2" and "4" — keeps what it has, because it was never a section number.
    /// </summary>
    public static Result Remove(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        (string[] lines, int[] starts) = SplitLines(markdown);
        HeadingNumberDetector.Scan scan = HeadingNumberDetector.Read(lines);

        if (!scan.IsNumbered)
        {
            return new Result(markdown, 0);
        }

        string?[] rewritten = new string?[scan.Headings.Count];

        for (int i = 0; i < scan.Headings.Count; i++)
        {
            if (scan.Numbered[i] && scan.Headings[i].Prefix is not null)
            {
                rewritten[i] = scan.Headings[i].Title;
            }
        }

        return Splice(markdown, lines, starts, scan, rewritten);
    }

    /// <summary>
    /// Writes the numbers <paramref name="start"/> would give this document into its heading
    /// text, replacing any the document already carries.
    ///
    /// Replacing rather than prepending is the whole of it. A document that already numbers
    /// itself and has had a section inserted in the middle is renumbered by this, which is the
    /// case worth having the command for at all — the alternative is hand-fixing every heading
    /// below the insert. Prepending blindly would give "1.2  1.2 Scope".
    ///
    /// A heading above <paramref name="start"/> gets no number, and loses one it was carrying:
    /// renumbering from "##" is also a decision that the title should not be numbered.
    /// </summary>
    public static Result Apply(string markdown, HeadingNumbering start, HeadingNumberStyle style)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        if (start == HeadingNumbering.Off)
        {
            // "Number from nothing" is what Remove means, and answering it that way keeps the
            // caller from having to special-case a level the dialog cannot offer.
            return Remove(markdown);
        }

        (string[] lines, int[] starts) = SplitLines(markdown);
        HeadingNumberDetector.Scan scan = HeadingNumberDetector.Read(lines);

        if (scan.Headings.Count == 0)
        {
            return new Result(markdown, 0);
        }

        int[] levels = new int[scan.Headings.Count];

        for (int i = 0; i < scan.Headings.Count; i++)
        {
            levels[i] = scan.Headings[i].Level;
        }

        IReadOnlyList<string> numbers = HeadingNumbers.Compute(levels, start);
        string?[] rewritten = new string?[scan.Headings.Count];

        for (int i = 0; i < scan.Headings.Count; i++)
        {
            HeadingScanner.ScannedHeading heading = scan.Headings[i];

            // A heading with no words of its own still counts - leaving it out would renumber
            // everything below it - but nothing is written into it. There is nothing there for
            // a number to label, and "##" given one stops being an empty heading and becomes a
            // heading titled "1".
            if (heading.Text.Length == 0)
            {
                continue;
            }

            // Only a prefix this document was judged to own may be dropped. Anything else at
            // the front of a heading is words, and the new number goes in front of it.
            string body = scan.Numbered[i] ? heading.Title : heading.Text;
            string number = numbers[i].Length == 0 ? string.Empty : Format(numbers[i], style);

            rewritten[i] = number + body;
        }

        return Splice(markdown, lines, starts, scan, rewritten);
    }

    /// <summary>The number dressed for the source, as <see cref="HeadingNumberStyle"/> asks.</summary>
    private static string Format(string number, HeadingNumberStyle style) => style switch
    {
        HeadingNumberStyle.OneSpace => number + " ",
        HeadingNumberStyle.DotSpace => number + ". ",
        HeadingNumberStyle.ParenSpace => number + ") ",
        HeadingNumberStyle.Tab => number + "\t",
        _ => number + "  ",
    };

    /// <summary>
    /// Turns the new heading texts into edits, works out which anchors that renames, adds the
    /// edits that move the links pointing at them, and writes the document out once.
    /// </summary>
    /// <param name="rewritten">
    /// Index-for-index with the scan's headings: the heading's new text, or null to leave it
    /// exactly as it is.
    /// </param>
    private static Result Splice(
        string markdown,
        string[] lines,
        int[] starts,
        HeadingNumberDetector.Scan scan,
        string?[] rewritten)
    {
        List<Edit> edits = [];

        for (int i = 0; i < scan.Headings.Count; i++)
        {
            if (rewritten[i] is not { } text)
            {
                continue;
            }

            HeadingScanner.ScannedHeading heading = scan.Headings[i];

            edits.Add(new Edit(starts[heading.Line] + heading.TextStart, heading.Text.Length, text));
        }

        int headings = edits.Count(e => !markdown.AsSpan(e.Offset, e.Consume).SequenceEqual(e.Insert));

        Dictionary<string, string> renamed = Anchors.Renamed(scan.Headings, rewritten, out int uncertain);
        int moved = 0;

        if (renamed.Count > 0)
        {
            foreach (LinkTargetScanner.LinkTarget target in LinkTargetScanner.Find(lines))
            {
                if (!renamed.TryGetValue(target.Anchor, out string? now))
                {
                    continue;
                }

                edits.Add(new Edit(starts[target.Line] + target.Column, target.Anchor.Length, now));
                moved++;
            }

            // Heading edits and link edits are found by two separate walks, and a link can sit
            // on a heading's own line, so the merged list is put back into document order before
            // anything is written. They never overlap: a link is in the words, and a heading
            // edit reaches only as far as the words begin.
            edits.Sort((a, b) => a.Offset - b.Offset);
        }

        return Write(markdown, edits) with
        {
            Changed = headings,
            LinksMoved = moved,
            UncertainHeadings = uncertain,
        };
    }

    /// <summary>
    /// Applies the edits, copying everything between them through untouched.
    ///
    /// The edits arrive in document order and never overlap, so one forward pass with a cursor
    /// is all this needs. An edit whose replacement is already what is there is dropped rather
    /// than counted, so a renumber that changes nothing reports nothing.
    /// </summary>
    private static Result Write(string markdown, IReadOnlyList<Edit> edits)
    {
        if (edits.Count == 0)
        {
            return new Result(markdown, 0);
        }

        var builder = new StringBuilder(markdown.Length);
        int cursor = 0;
        int changed = 0;

        foreach (Edit edit in edits)
        {
            if (markdown.AsSpan(edit.Offset, edit.Consume).SequenceEqual(edit.Insert))
            {
                continue;
            }

            builder.Append(markdown, cursor, edit.Offset - cursor);
            builder.Append(edit.Insert);

            cursor = edit.Offset + edit.Consume;
            changed++;
        }

        if (changed == 0)
        {
            return new Result(markdown, 0);
        }

        builder.Append(markdown, cursor, markdown.Length - cursor);

        return new Result(builder.ToString(), changed);
    }

    /// <summary>
    /// The document's lines and where each one begins, keeping the terminators out of the lines
    /// and in the offsets.
    ///
    /// Written here rather than borrowed from the formatter because the offsets are the point:
    /// the formatter splits a document in order to rebuild it, and rebuilding is exactly what
    /// this must not do. Every line ending stays where it was because nothing ever reassembles
    /// them — they are copied through as part of the untouched text between two edits.
    /// </summary>
    private static (string[] Lines, int[] Starts) SplitLines(string text)
    {
        List<string> lines = [];
        List<int> starts = [];

        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\n' or '\r'))
            {
                continue;
            }

            lines.Add(text[start..i]);
            starts.Add(start);

            // Only "\r\n" is one ending; a lone "\r" is its own, which is what an old Mac file
            // and a document assembled by hand both produce.
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                i++;
            }

            start = i + 1;
        }

        lines.Add(text[start..]);
        starts.Add(start);

        return ([.. lines], [.. starts]);
    }
}
