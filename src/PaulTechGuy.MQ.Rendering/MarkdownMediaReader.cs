// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using PaulTechGuy.MQ.Domain;
using MarkdigDocument = Markdig.Syntax.MarkdownDocument;

namespace PaulTechGuy.MQ.Rendering;

/// <summary>
/// Collects the pictures, videos and frames an author wrote as raw HTML rather than as markdown.
///
/// <c>ReadLinks</c> walks <c>LinkInline</c>, which is every markdown image and link and nothing
/// else. Markdig keeps raw HTML as unparsed text - <see cref="HtmlInline"/> for a tag in the
/// middle of a paragraph, <see cref="HtmlBlock"/> for one sitting on its own lines - so
/// <c>&lt;img src="..."&gt;</c> reaches no check at all. It is not a rare way to write one
/// either: it is how an author pins a width, which markdown has no syntax for.
///
/// Read from the parsed tree rather than the raw text, for the reason
/// <see cref="MarkdownAnchorReader"/> gives: HTML inside a code fence never becomes an
/// <see cref="HtmlBlock"/>, so an example in a document about HTML costs nothing to exclude.
///
/// <b>The span is the address itself, not the whole tag.</b> A markdown reference is underlined
/// end to end because "![alt](x.png)" is one thing to a reader; a tag can carry two addresses
/// (<c>src</c> and <c>poster</c>) and underlining the tag twice would draw two marks over each
/// other. Marking the value also means a repair can replace exactly what it underlined, which is
/// what lets these reuse the dead-link repairs: <c>LinkTargetSpan</c> reads "](url)" syntax and
/// would find nothing here.
/// </summary>
internal static partial class MarkdownMediaReader
{
    /// <summary>
    /// Which attributes carry a fetchable address, per element.
    ///
    /// Only the ones the browser loads by itself. "href" is absent on purpose - an anchor is a
    /// navigation, and the whole rule this feeds is about loads.
    /// </summary>
    private static readonly Dictionary<string, string[]> MediaAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["img"] = ["src", "srcset"],
        ["video"] = ["src", "poster"],
        ["audio"] = ["src"],
        ["source"] = ["src", "srcset"],
        ["track"] = ["src"],
        ["iframe"] = ["src"],
        ["embed"] = ["src"],
        ["object"] = ["data"],
    };

    public static IReadOnlyList<LinkReference> ReadMedia(MarkdigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        List<LinkReference> found = [];

        // A tag in the middle of a paragraph. Line and Column are the "<", so an offset inside
        // the tag text is an offset from there.
        foreach (HtmlInline inline in document.Descendants<HtmlInline>())
        {
            AddFrom(inline.Tag, inline.Line, inline.Column, found);
        }

        foreach (HtmlBlock block in document.Descendants<HtmlBlock>())
        {
            // A comment renders nothing, and neither does the inside of script, style or
            // textarea. Flagging an address in one would be reporting a picture that was never
            // going to appear in the first place - a false mark, which costs more than a missed
            // one. The other block types are ordinary markup.
            if (block.Type is not (HtmlBlockType.InterruptingBlock or HtmlBlockType.NonInterruptingBlock))
            {
                continue;
            }

            for (int i = 0; i < block.Lines.Count; i++)
            {
                var line = block.Lines.Lines[i];

                // The block's own start line plus the offset into it, rather than StringLine.Line.
                // That field reads as the block's first line for every line in the block, so a
                // tag on the second line of a multi-line "<video ...>" was underlined on the
                // first - a mark pointing at the wrong line, which is worse than no mark.
                AddFrom(line.Slice.ToString(), block.Line + i, line.Column, found);
            }
        }

        return found;
    }

    /// <summary>
    /// Pulls every fetchable address out of one run of raw HTML, positioned against the line and
    /// column that run starts at.
    /// </summary>
    private static void AddFrom(string? html, int line, int column, List<LinkReference> into)
    {
        if (string.IsNullOrEmpty(html))
        {
            return;
        }

        foreach (Match tag in Tag().Matches(html))
        {
            // A tag split over several lines is left alone.
            //
            // Every column here is an offset into one line of the source. When the tag opens on
            // one line and carries its address on the next, that arithmetic produces a column
            // past the end of the opening line - an underline drawn in empty space, or on the
            // wrong characters. Markdig hands such a tag over as a single run with the newline
            // still in it, which is what makes it detectable rather than silently wrong.
            //
            // Saying nothing is the right failure. A media tag written across lines is rare, and
            // this whole rule exists to explain a blank picture: a mark in the wrong place
            // explains nothing and costs the reader their trust in the marks that are right.
            if (tag.Value.AsSpan().ContainsAny('\n', '\r'))
            {
                continue;
            }

            if (!MediaAttributes.TryGetValue(tag.Groups["tag"].Value, out string[]? wanted))
            {
                continue;
            }

            Group attributes = tag.Groups["attributes"];

            foreach (Match attribute in Attribute().Matches(attributes.Value))
            {
                if (!wanted.Contains(attribute.Groups["key"].Value, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                Group value = attribute.Groups["value"];

                if (value.Value.Length == 0)
                {
                    continue;
                }

                // Where the value sits in the original text: the run's own column, plus the
                // attribute block's offset inside the tag, plus the value's offset inside that.
                int start = column + attributes.Index + value.Index;

                bool isSrcset = attribute.Groups["key"].Value.Equals("srcset", StringComparison.OrdinalIgnoreCase);

                if (isSrcset)
                {
                    AddSrcSet(value.Value, line, start, into);

                    continue;
                }

                into.Add(Reference(value.Value, line, start));
            }
        }
    }

    /// <summary>
    /// One entry per candidate in a srcset.
    ///
    /// "a.png 1x, b.png 2x" is a list of addresses with a descriptor after each, and every one of
    /// them is fetched on its own terms. Reporting only the first would leave a document whose
    /// retina image is the broken one saying nothing.
    /// </summary>
    private static void AddSrcSet(string value, int line, int start, List<LinkReference> into)
    {
        int at = 0;

        while (at < value.Length)
        {
            int comma = value.IndexOf(',', at);
            int end = comma < 0 ? value.Length : comma;

            int from = at;

            while (from < end && char.IsWhiteSpace(value[from]))
            {
                from++;
            }

            int to = from;

            while (to < end && !char.IsWhiteSpace(value[to]))
            {
                to++;
            }

            if (to > from)
            {
                into.Add(Reference(value[from..to], line, start + from));
            }

            if (comma < 0)
            {
                return;
            }

            at = comma + 1;
        }
    }

    private static LinkReference Reference(string url, int line, int column) =>
        new()
        {
            Url = url,
            IsImage = true,
            SourceLine = line,
            SourceColumn = column,
            Length = url.Length,

            // No alt text is read, and none is wanted. These never reach the alt-text rule -
            // see ImageChecks - so a value here would be carried about and never asked for.
            Text = string.Empty,
            IsInsideLink = false,
            IsRawHtml = true,
        };

    /// <summary>An opening tag and everything up to the closing angle bracket.</summary>
    [GeneratedRegex(@"<(?<tag>[a-zA-Z][a-zA-Z0-9\-]*)(?<attributes>[^>]*)>", RegexOptions.CultureInvariant)]
    private static partial Regex Tag();

    /// <summary>An attribute, quoted either way or not at all.</summary>
    [GeneratedRegex(
        """(?<=[\s"'])(?<key>[a-zA-Z][a-zA-Z0-9\-]*)\s*=\s*(?:"(?<value>[^"]*)"|'(?<value>[^']*)'|(?<value>[^\s"'=<>`]+))""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Attribute();
}
