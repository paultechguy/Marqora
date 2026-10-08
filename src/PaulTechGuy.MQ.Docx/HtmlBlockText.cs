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
/// Only ordinary markup counts. A comment renders nothing; nor does the inside of a script,
/// style or textarea, so writing their text would put into the document something no reader
/// of the preview ever saw. That is the same line <c>MarkdownMediaReader</c> draws for the same
/// reason. A block that is nothing but tags - a <c>&lt;details&gt;</c> line on its own - has no
/// words and writes nothing.
/// </summary>
/// <summary>A paragraph of a raw HTML block's text, and whether it was a details summary.</summary>
internal readonly record struct HtmlParagraph(string Text, bool IsSummary);

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

        return
        [
            .. html.Split("\n\n")
                .Select(chunk => new HtmlParagraph(
                    Whitespace().Replace(WebUtility.HtmlDecode(Tag().Replace(chunk, " ")), " ").Trim(),
                    chunk.Contains("<summary", StringComparison.OrdinalIgnoreCase)))
                .Where(paragraph => paragraph.Text.Length > 0),
        ];
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
