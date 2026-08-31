// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkdigDocument = Markdig.Syntax.MarkdownDocument;

namespace PaulTechGuy.MQ.Rendering;

/// <summary>
/// Collects the anchor ids an author wrote by hand as raw HTML.
///
/// The convention long predates markdown's own heading anchors and is still the only way to
/// give something that is not a heading a link target: a paragraph, a glossary entry, a row
/// of a table. It is written as an empty anchor immediately before the thing it names,
/// <c>&lt;a id="notes"&gt;&lt;/a&gt;</c>, and markdown passes it through untouched, so the
/// preview and every other renderer honour it.
///
/// Reading them from the parsed tree rather than the raw text is what keeps HTML inside a
/// code fence from counting: the parser has already decided that is an example, not markup.
/// </summary>
internal static partial class MarkdownAnchorReader
{
    public static IReadOnlyList<string> ReadAnchors(MarkdigDocument document)
    {
        List<string> anchors = [];

        // Inline HTML, as in "<a id="g-tenant"></a>**Tenant** - ..." in the middle of a
        // paragraph. Markdig hands over each tag on its own.
        foreach (HtmlInline inline in document.Descendants<HtmlInline>())
        {
            AddFrom(inline.Tag, anchors);
        }

        // HTML sitting on its own lines, which the parser keeps as an unparsed block.
        foreach (HtmlBlock block in document.Descendants<HtmlBlock>())
        {
            for (int i = 0; i < block.Lines.Count; i++)
            {
                AddFrom(block.Lines.Lines[i].Slice.ToString(), anchors);
            }
        }

        return anchors;
    }

    /// <summary>
    /// Pulls every anchor id out of one run of raw HTML.
    ///
    /// "id" counts on any element, because any element can be a link target. "name" counts
    /// only on an anchor, where it is the old spelling of the same thing; on an input or a
    /// meta tag it means something else entirely.
    /// </summary>
    private static void AddFrom(string? html, List<string> into)
    {
        if (string.IsNullOrEmpty(html))
        {
            return;
        }

        foreach (Match tag in Tag().Matches(html))
        {
            string attributes = tag.Groups["attributes"].Value;
            bool isAnchor = tag.Groups["tag"].Value.Equals("a", StringComparison.OrdinalIgnoreCase);

            foreach (Match attribute in Attribute().Matches(attributes))
            {
                if (!isAnchor && attribute.Groups["key"].Value.Equals("name", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string value = attribute.Groups["value"].Value;

                if (value.Length > 0)
                {
                    into.Add(value);
                }
            }
        }
    }

    /// <summary>An opening tag and everything up to the closing angle bracket.</summary>
    [GeneratedRegex(@"<(?<tag>[a-zA-Z][a-zA-Z0-9\-]*)(?<attributes>[^>]*)>", RegexOptions.CultureInvariant)]
    private static partial Regex Tag();

    /// <summary>An id or name attribute, quoted either way or not at all.</summary>
    [GeneratedRegex(
        """(?<=[\s"'])(?<key>id|name)\s*=\s*(?:"(?<value>[^"]*)"|'(?<value>[^']*)'|(?<value>[^\s"'=<>`]+))""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Attribute();
}
