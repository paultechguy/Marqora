// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.RegularExpressions;

namespace PaulTechGuy.MQ.Markdown;

/// <summary>
/// Reads a line as a list item: where its marker starts, how wide the marker is, and — the
/// answer everything here exists for — which column its content begins in.
///
/// The content column is what decides nesting. A child list has to start at or past its
/// parent's content column to be inside it, so "indent one level" means "put the marker where
/// the parent's text starts" rather than any fixed number of spaces. That is why this returns a
/// column rather than a depth: <c>- </c> and <c>10. </c> nest their children differently.
///
/// Tabs count as one character throughout, which is what <c>LIST_ITEM</c> in the web shell and
/// the formatter's line rules both already assume. A list indented with tabs will therefore
/// nest by character count rather than by rendered column; documents that mix the two are
/// already ambiguous and nothing here makes them worse.
/// </summary>
public static partial class ListStructure
{
    /// <summary>One line read as a list item.</summary>
    /// <param name="IndentWidth">Leading whitespace before the marker.</param>
    /// <param name="Marker">The marker itself: <c>-</c>, <c>*</c>, <c>+</c>, <c>1.</c>, <c>10)</c>.</param>
    /// <param name="Ordered">Whether the marker is a number rather than a bullet.</param>
    /// <param name="Number">The number an ordered marker carries, or zero.</param>
    /// <param name="Delimiter">The <c>.</c> or <c>)</c> an ordered marker carries.</param>
    /// <param name="ContentColumn">The column a child's marker must reach to nest inside this item.</param>
    /// <param name="TaskBox">The <c>[ ]</c> or <c>[x]</c> and the gap after it, or empty.</param>
    /// <param name="Content">Everything after the marker, its gap and any task box.</param>
    public readonly record struct ListItem(
        int IndentWidth,
        string Marker,
        bool Ordered,
        int Number,
        char Delimiter,
        int ContentColumn,
        string TaskBox,
        string Content);

    /// <summary>
    /// Reads <paramref name="line"/> as a list item, or fails if it is not one.
    ///
    /// A thematic break is not a list item however much it looks like one: <c>- - -</c> matches
    /// every bullet pattern in the tree, and indenting it four columns turns a rule into a code
    /// block.
    /// </summary>
    public static bool TryRead(string line, out ListItem item)
    {
        ArgumentNullException.ThrowIfNull(line);

        item = default;

        Match match = Item().Match(line);

        if (!match.Success)
        {
            return false;
        }

        string gap = match.Groups["gap"].Value;
        string task = match.Groups["task"].Value;
        string content = match.Groups["content"].Value;

        // "-foo" is a word that happens to start with a dash, not an item.
        if (gap.Length == 0 && task.Length + content.Length > 0)
        {
            return false;
        }

        if (IsThematicBreak(line))
        {
            return false;
        }

        string marker = match.Groups["marker"].Value;
        int indentWidth = match.Groups["indent"].Value.Length;
        bool ordered = match.Groups["number"].Success;

        item = new ListItem(
            indentWidth,
            marker,
            ordered,
            ordered ? int.Parse(match.Groups["number"].ValueSpan, CultureInfo.InvariantCulture) : 0,
            ordered ? match.Groups["delim"].Value[0] : '\0',
            ContentColumnOf(indentWidth + marker.Length, gap.Length, task.Length + content.Length),
            task,
            content);

        return true;
    }

    /// <summary>Whether the line is a list item at all.</summary>
    public static bool IsItem(string line) => TryRead(line, out _);

    /// <summary>
    /// Where an item's content starts, which is where a child of it has to begin.
    ///
    /// Two cases stop this being "marker plus gap". An empty item has no content to line up
    /// with, and a gap of five or more spaces is the start of an indented code block inside the
    /// item rather than a wide gap — CommonMark reads both as content one column past the
    /// marker.
    /// </summary>
    private static int ContentColumnOf(int markerEnd, int gapWidth, int contentLength) =>
        gapWidth is 0 or >= 5 || contentLength == 0 ? markerEnd + 1 : markerEnd + gapWidth;

    /// <summary>
    /// Three or more of the same character with only spaces between them.
    ///
    /// A second copy of the formatter's <c>MarkdownLineRules.IsThematicBreak</c>, which cannot be
    /// referenced from here — <c>Formatting</c> is a concrete layer and this is a foundation. The
    /// two should go together when the formatter's line rules fold into this project; see the
    /// note on <see cref="MarkdownRegionScanner"/>.
    /// </summary>
    private static bool IsThematicBreak(string line)
    {
        string trimmed = line.Trim();

        if (trimmed.Length < 3 || trimmed[0] is not ('-' or '*' or '_'))
        {
            return false;
        }

        char rule = trimmed[0];
        int count = 0;

        foreach (char c in trimmed)
        {
            if (c == rule)
            {
                count++;
            }
            else if (c is not (' ' or '\t'))
            {
                return false;
            }
        }

        return count >= 3;
    }

    // The task box is deliberately not part of the marker. GitHub renders "- [ ] Foo" with a
    // checkbox, but CommonMark reads "[ ] Foo" as the item's text, so a child of it nests
    // against column 2 and not against the far side of the box.
    [GeneratedRegex(@"^(?<indent>[ \t]*)(?<marker>[-*+]|(?<number>\d{1,9})(?<delim>[.)]))(?<gap>[ \t]*)(?<task>\[[ xX]\][ \t]+)?(?<content>.*)$")]
    private static partial Regex Item();
}
