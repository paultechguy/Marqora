// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// Copies the stylesheet's own table rules onto the table's own elements, for a paste into
/// something that will not read them any other way.
///
/// Word does not lay a pasted table out from CSS. It imports one into its own table model and
/// takes each cell's shading and borders from that cell, so a <c>thead th</c> rule in a style
/// block is parsed, understood, and then dropped at the table boundary. Outlook composes with
/// the same engine and behaves identically. That is why a pasted document picks up its code
/// colors and its callout tints from the style block - those are spans and divs - while the
/// table header stays blank however the color is written. Making the color opaque was needed
/// and could not be enough on its own; see <see cref="CssAlphaFlattening"/>.
///
/// Nothing here states a color, a border or a measurement. Every value is read back out of the
/// stylesheet that was just built for the same fragment, so <c>app.css</c> remains the one place
/// the table is described and a change to it moves the pasted table too. A selector that stops
/// matching yields no declarations and the element is simply left alone, which degrades to
/// today's behavior rather than to a wrong color.
/// </summary>
public static partial class InlineTableStyles
{
    /// <summary>The rules worth carrying onto elements, in the order they must be applied.</summary>
    private const string CellSelector = ".mq-preview th, .mq-preview td";
    private const string HeaderCellSelector = ".mq-preview thead th";
    private const string TableSelector = ".mq-preview table";
    private const string StripedRowSelector = ".mq-preview tbody tr:nth-child(even)";

    /// <summary>
    /// Writes the table rules from <paramref name="flattenedCss"/> into the markup as inline
    /// style attributes.
    ///
    /// Expects the CSS to have been through <see cref="CssAlphaFlattening"/> already - an
    /// element carrying <c>rgba()</c> in its style attribute is no better off than a rule
    /// carrying it, and the point of moving the value is that the destination can read it.
    ///
    /// An existing style attribute is kept and wins: the stamp goes in front of it, so anything
    /// the document already said about that cell is the later declaration and still applies.
    /// </summary>
    public static string Stamp(string html, string flattenedCss)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(flattenedCss);

        if (html.Length == 0)
        {
            return html;
        }

        string cell = DeclarationsFor(flattenedCss, CellSelector);
        string table = DeclarationsFor(flattenedCss, TableSelector);
        string stripe = DeclarationsFor(flattenedCss, StripedRowSelector);

        // The head's own rule sits after the shared one so its heavier bottom border wins the
        // same way it does in the cascade, which is what keeps the head visibly off the data.
        string headerCell = Join(cell, DeclarationsFor(flattenedCss, HeaderCellSelector));

        if (cell.Length == 0 && table.Length == 0 && headerCell.Length == 0)
        {
            return html;
        }

        // Which section a cell is in decides which rule it gets, and the markup says so only by
        // where the tag appears. Regex.Replace walks matches in document order, so a flag set by
        // the opening tag is still correct when the cells inside it come past.
        bool inHeader = false;
        bool inBody = false;
        int bodyRow = 0;

        return TableTag().Replace(html, match =>
        {
            switch (match.Groups["tag"].Value.ToLowerInvariant())
            {
                case "thead":
                    inHeader = true;
                    inBody = false;
                    return match.Value;

                case "/thead":
                    inHeader = false;
                    return match.Value;

                case "tbody":
                    inBody = true;
                    inHeader = false;
                    bodyRow = 0;
                    return match.Value;

                case "/tbody":
                    inBody = false;
                    return match.Value;

                // A table inside a table restarts the count rather than continuing the outer
                // one, which is what nth-child would have done.
                case "table":
                    inHeader = false;
                    inBody = false;
                    bodyRow = 0;
                    return WithStyle(match.Value, table);

                case "tr":
                    if (!inBody)
                    {
                        return match.Value;
                    }

                    // nth-child counts from one, so the even rows are the second, fourth and on.
                    return ++bodyRow % 2 == 0 ? WithStyle(match.Value, stripe) : match.Value;

                case "th":
                    return WithStyle(match.Value, inHeader ? headerCell : cell);

                case "td":
                    return WithStyle(match.Value, cell);

                default:
                    return match.Value;
            }
        });
    }

    /// <summary>Two declaration lists, later winning, with either side possibly empty.</summary>
    private static string Join(string first, string second) =>
        first.Length == 0 ? second
        : second.Length == 0 ? first
        : $"{first}; {second}";

    /// <summary>
    /// The declarations of one top-level rule, as a single line.
    ///
    /// Only rules written flush to the left margin are considered, which is the same trick the
    /// packager uses to find the light <c>:root</c>: everything inside an <c>@media</c> block in
    /// <c>app.css</c> is indented, so the dark theme and the print overrides cannot be picked up
    /// by accident. A fragment is always a light screen document and wants the light values.
    /// </summary>
    private static string DeclarationsFor(string css, string selector)
    {
        foreach (Match rule in TopLevelPreviewRule().Matches(css))
        {
            if (Normalize(rule.Groups["selector"].Value) != selector)
            {
                continue;
            }

            string body = CssComment().Replace(rule.Groups["body"].Value, " ");

            return string.Join(
                "; ",
                body.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return string.Empty;
    }

    /// <summary>
    /// A selector list as one line with single spaces, so the two-line form <c>app.css</c> writes
    /// the shared cell rule in compares equal to the one named here.
    /// </summary>
    private static string Normalize(string selector) =>
        string.Join(", ", Whitespace().Replace(selector, " ").Split(',', StringSplitOptions.TrimEntries));

    /// <summary>
    /// Puts declarations on an opening tag, merging rather than replacing.
    ///
    /// The stamp goes first so that a style attribute already on the element is the later
    /// declaration and keeps winning. A document that styles its own table cell meant it.
    /// </summary>
    private static string WithStyle(string tag, string declarations)
    {
        if (declarations.Length == 0)
        {
            return tag;
        }

        Match existing = StyleAttribute().Match(tag);

        if (existing.Success)
        {
            string current = existing.Groups["value"].Value.Trim().TrimEnd(';');

            return tag.Remove(existing.Index, existing.Length)
                .Insert(existing.Index, $"style=\"{declarations}; {current}\"");
        }

        // Before the closing angle bracket, and before any trailing slash a self-closed tag
        // would carry, so the attribute cannot end up outside the tag.
        int close = tag.LastIndexOf('>');

        if (close < 0)
        {
            return tag;
        }

        int insert = close > 0 && tag[close - 1] == '/' ? close - 1 : close;

        return tag.Insert(insert, $" style=\"{declarations}\"");
    }

    /// <summary>
    /// The table tags that decide anything, opening and closing. Longer names are written first
    /// in the alternation, or <c>th</c> would match the front of <c>thead</c> and every header
    /// section would read as a cell.
    /// </summary>
    [GeneratedRegex(@"<(?<tag>/?thead|/?tbody|table|tr|th|td)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex TableTag();

    /// <summary>A rule for the preview that starts at the left margin - see DeclarationsFor.</summary>
    [GeneratedRegex(@"^(?<selector>\.mq-preview[^{}]*?)\{(?<body>[^{}]*)\}", RegexOptions.Multiline)]
    private static partial Regex TopLevelPreviewRule();

    [GeneratedRegex(@"style\s*=\s*""(?<value>[^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex StyleAttribute();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CssComment();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
