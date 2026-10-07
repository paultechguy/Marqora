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
internal static partial class HtmlBlockText
{
    /// <summary>
    /// One string per paragraph the block reads as, or none. A block-level close or a
    /// <c>&lt;br&gt;</c> ends a paragraph; whitespace inside one collapses, as HTML does.
    /// </summary>
    public static IReadOnlyList<string> Paragraphs(HtmlBlock block)
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
                .Select(chunk => WebUtility.HtmlDecode(Tag().Replace(chunk, " ")))
                .Select(text => Whitespace().Replace(text, " ").Trim())
                .Where(text => text.Length > 0),
        ];
    }

    /// <summary>The first line of the block's source, for the report.</summary>
    public static string SourceOf(HtmlBlock block) =>
        ExportReport.Shorten(block?.Lines.ToString());

    [GeneratedRegex(@"<br\s*/?>|</(?:p|div|li|h[1-6]|tr|summary|blockquote|pre|table|ul|ol|dl|dt|dd)\s*>",
        RegexOptions.IgnoreCase)]
    private static partial Regex BlockBreak();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
