// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.RegularExpressions;
using Markdig.Syntax;

namespace PaulTechGuy.MQ.Docx;

/// <summary>
/// The words a raw HTML block says, with its markup taken away.
///
/// Word has no home for a block of HTML, and the block used to be dropped whole - the styled
/// callout in the fixture vanished, heading and all of its text, with nothing in the export
/// report to say so. The fixture's own pass condition is the floor: a converter that cannot
/// draw the HTML still shows its sentences as plain text. The styling is the part that is lost,
/// and the report says so.
///
/// Lost except for the inline tags: a <c>&lt;strong&gt;</c> or a <c>&lt;code&gt;</c> inside the
/// block is read the way <see cref="InlineHtml"/> reads one anywhere else, so the fixture's
/// callout keeps its bold first sentence. A tag Word has no form of still only separates words.
///
/// Only ordinary markup counts. A comment renders nothing; nor does the inside of a script,
/// style or textarea, so writing their text would put into the document something no reader
/// of the preview ever saw. That is the same line <c>MarkdownMediaReader</c> draws for the same
/// reason. A block that is nothing but tags - a <c>&lt;details&gt;</c> line on its own - has no
/// words and writes nothing.
/// </summary>
/// <summary>A stretch of a raw HTML block's text, and the formatting its inline tags gave it.</summary>
internal readonly record struct HtmlRun(string Text, RunFormat Format);

/// <summary>A paragraph of a raw HTML block's text, and whether it was a details summary.</summary>
internal readonly record struct HtmlParagraph(IReadOnlyList<HtmlRun> Runs, bool IsSummary)
{
    /// <summary>The paragraph's words, without their formatting.</summary>
    public string Text => string.Concat(Runs.Select(run => run.Text));
}

internal static partial class HtmlBlockText
{
    /// <summary>
    /// One entry per paragraph the block reads as, or none. A block-level close or a
    /// <c>&lt;br&gt;</c> ends a paragraph; whitespace inside one collapses, as HTML does.
    ///
    /// A <c>&lt;details&gt;</c> summary is marked, so it can be set in bold above the content
    /// it opens - the line the preview shows with its disclosure triangle, and the only
    /// thing that says the paragraphs under it were a collapsible section at all.
    /// </summary>
    public static IReadOnlyList<HtmlParagraph> Paragraphs(HtmlBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);

        if (block.Type is not (HtmlBlockType.InterruptingBlock or HtmlBlockType.NonInterruptingBlock))
        {
            return [];
        }

        string html = block.Lines.ToString();

        html = BlockBreak().Replace(html, "\n\n");

        // One stack for the whole block, as a browser keeps one: a tag opened in one paragraph
        // and closed in the next goes on formatting the words between.
        var formats = new Stack<RunFormat>();
        var quoteNesting = 0;

        return
        [
            .. html.Split("\n\n")
                .Select(chunk => new HtmlParagraph(
                    Runs(chunk, formats, ref quoteNesting),
                    chunk.Contains("<summary", StringComparison.OrdinalIgnoreCase)))
                .Where(paragraph => paragraph.Runs.Count > 0),
        ];
    }

    /// <summary>
    /// A paragraph's text as runs. Whitespace collapses across the run boundaries, as a
    /// browser collapses it, and the paragraph is trimmed at both ends.
    ///
    /// The tags are read as the inline walker reads them (<c>InlineRenderer.ReadHtmlTag</c>):
    /// a known one pushes its formatting or pops it, a <c>&lt;q&gt;</c> writes its quotation
    /// marks, a closing tag with nothing open is ignored. Any other tag stands for a space, as
    /// every tag did before - a <c>&lt;div&gt;</c> or a <c>&lt;td&gt;</c> separates the words
    /// either side of it - except a link, which wraps words without separating them.
    /// </summary>
    private static List<HtmlRun> Runs(string chunk, Stack<RunFormat> formats, ref int quoteNesting)
    {
        var pieces = new List<HtmlRun>();
        var at = 0;

        RunFormat Current(Stack<RunFormat> stack) => stack.Count > 0 ? stack.Peek() : default;

        foreach (Match tag in Tag().Matches(chunk))
        {
            if (tag.Index > at)
            {
                pieces.Add(new HtmlRun(WebUtility.HtmlDecode(chunk[at..tag.Index]), Current(formats)));
            }

            at = tag.Index + tag.Length;

            HtmlTag? parsed = InlineHtml.Parse(tag.Value);

            if (parsed is not { } known || !InlineHtml.IsKnown(known.Name))
            {
                if (parsed is not { Name: "a" })
                {
                    pieces.Add(new HtmlRun(" ", Current(formats)));
                }

                continue;
            }

            if (known.Name == "q" && !known.IsSelfClosing)
            {
                if (known.IsClosing && quoteNesting > 0)
                {
                    quoteNesting--;
                    pieces.Add(new HtmlRun(quoteNesting % 2 == 0 ? "\u201D" : "\u2019", Current(formats)));
                }
                else if (!known.IsClosing)
                {
                    pieces.Add(new HtmlRun(quoteNesting % 2 == 0 ? "\u201C" : "\u2018", Current(formats)));
                    quoteNesting++;
                }
            }

            if (known.IsClosing)
            {
                if (formats.Count > 0)
                {
                    formats.Pop();
                }
            }
            else if (!known.IsSelfClosing)
            {
                formats.Push(InlineHtml.Apply(known, Current(formats)) ?? Current(formats));
            }
        }

        if (at < chunk.Length)
        {
            pieces.Add(new HtmlRun(WebUtility.HtmlDecode(chunk[at..]), Current(formats)));
        }

        return Collapse(pieces);
    }

    /// <summary>
    /// Runs with their whitespace collapsed across the boundaries, trimmed at both ends, empty
    /// ones dropped and neighbors wearing the same formatting joined.
    /// </summary>
    private static List<HtmlRun> Collapse(List<HtmlRun> pieces)
    {
        var runs = new List<HtmlRun>();
        var afterSpace = true;

        foreach (HtmlRun piece in pieces)
        {
            string text = Whitespace().Replace(piece.Text, " ");

            if (afterSpace)
            {
                text = text.TrimStart(' ');
            }

            if (text.Length == 0)
            {
                continue;
            }

            afterSpace = text[^1] == ' ';

            if (runs.Count > 0 && runs[^1].Format == piece.Format)
            {
                runs[^1] = runs[^1] with { Text = runs[^1].Text + text };
            }
            else
            {
                runs.Add(piece with { Text = text });
            }
        }

        // The trailing space, which may stand alone in a run of its own.
        while (runs.Count > 0 && runs[^1].Text.TrimEnd(' ') is var trimmed && trimmed.Length < runs[^1].Text.Length)
        {
            if (trimmed.Length == 0)
            {
                runs.RemoveAt(runs.Count - 1);
            }
            else
            {
                runs[^1] = runs[^1] with { Text = trimmed };
                break;
            }
        }

        return runs;
    }

    /// <summary>
    /// Each opening tag in the block for something that shows without words
    /// (<see cref="InlineHtml.IsEmbedded"/>), as written. Taking the markup away keeps a
    /// block's sentences and loses these, so each needs its own report row - an
    /// <c>&lt;iframe&gt;</c> alone on a line has no words at all, and wrote nothing and said
    /// nothing.
    /// </summary>
    public static IReadOnlyList<string> Embedded(HtmlBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);

        if (block.Type is not (HtmlBlockType.InterruptingBlock or HtmlBlockType.NonInterruptingBlock))
        {
            return [];
        }

        return
        [
            .. OpeningTag().Matches(block.Lines.ToString())
                .Where(match => InlineHtml.IsEmbedded(match.Groups[1].Value.ToLowerInvariant()))
                .Select(match => match.Value),
        ];
    }

    /// <summary>A fragment of HTML as the words it reads as, on one line.</summary>
    public static string PlainText(string html) =>
        Whitespace().Replace(WebUtility.HtmlDecode(Tag().Replace(html, " ")), " ").Trim();

    /// <summary>The first line of the block's source, for the report.</summary>
    public static string SourceOf(HtmlBlock block) =>
        ExportReport.Shorten(block?.Lines.ToString());

    [GeneratedRegex(@"<br\s*/?>|</(?:p|div|li|h[1-6]|tr|summary|blockquote|pre|table|ul|ol|dl|dt|dd)\s*>",
        RegexOptions.IgnoreCase)]
    private static partial Regex BlockBreak();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"<([A-Za-z][A-Za-z0-9]*)\b[^>]*>")]
    private static partial Regex OpeningTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
