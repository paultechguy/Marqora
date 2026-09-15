// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;

namespace PaulTechGuy.MQ.Markdown;

/// <summary>
/// Finds the places a document links to one of its own headings: every <c>#anchor</c> written as
/// a link destination, and where in the source it sits.
///
/// Exists so that a rewrite which changes heading text can move the links that pointed at the old
/// name. A table of contents written by hand is the common case, and it is precisely what breaks
/// when a heading is renamed — silently, because a dead anchor still looks like a link.
///
/// Destinations only. The text of a link is prose and is never touched, and a target carrying a
/// path — <c>other.md#notes</c> — is somebody else's document and is skipped: this can only speak
/// for anchors in the file it was given.
///
/// Conservative by construction rather than by being a parser. Whole-line exclusions come from
/// <see cref="MarkdownRegionScanner.FindProtectedLines"/>, and an inline code span is blanked by
/// <see cref="LineMasker.MaskCodeSpans"/> before the patterns run — which is what stops the
/// <c>[text](#anchor)</c> inside a worked example in this very file from being rewritten. The
/// masker's promise that a masked line keeps its length is what lets the offsets it produces be
/// read straight off the original line.
/// </summary>
public static partial class LinkTargetScanner
{
    /// <summary>One <c>#anchor</c> destination, located precisely enough to be replaced.</summary>
    /// <param name="Line">Zero-based line it sits on.</param>
    /// <param name="Column">
    /// Index within the line of the first character *after* the hash, so a rewrite replaces the
    /// name and leaves the hash, the brackets and any title where they are.
    /// </param>
    /// <param name="Anchor">The name itself, without the hash.</param>
    public readonly record struct LinkTarget(int Line, int Column, string Anchor)
    {
        /// <summary>Where the name ends, one past its last character.</summary>
        public int End => Column + Anchor.Length;
    }

    /// <summary>
    /// Every same-document anchor destination in <paramref name="lines"/>, in source order.
    /// </summary>
    /// <param name="protectedLines">
    /// From <see cref="MarkdownRegionScanner.FindProtectedLines"/>, for a caller that already has
    /// one. Computed here when it is not given, for the same reason
    /// <see cref="HeadingScanner.Find"/> does it: a link-shaped run inside a fenced example is a
    /// line a rewriter would otherwise edit.
    /// </param>
    public static IReadOnlyList<LinkTarget> Find(IReadOnlyList<string> lines, bool[]? protectedLines = null)
    {
        ArgumentNullException.ThrowIfNull(lines);

        bool[] guarded = protectedLines ?? MarkdownRegionScanner.FindProtectedLines(lines);
        List<LinkTarget> targets = [];

        for (int i = 0; i < lines.Count; i++)
        {
            if (i < guarded.Length && guarded[i])
            {
                continue;
            }

            string line = lines[i];

            if (line.Length == 0 || !line.Contains('#', StringComparison.Ordinal))
            {
                continue;
            }

            // Runs over the masked copy and reads the answer off the original. The two are the
            // same length and differ only where a code span was, which is exactly where a match
            // must not be found.
            string masked = LineMasker.MaskCodeSpans(line);

            Collect(Inline().Matches(masked), line, i, targets);
            Collect(Definition().Matches(masked), line, i, targets);
            Collect(Href().Matches(masked), line, i, targets);
        }

        // Source order, because a caller splicing them has to walk forward. The three patterns
        // each run over the whole line, so their hits arrive interleaved.
        targets.Sort((a, b) => a.Line != b.Line ? a.Line - b.Line : a.Column - b.Column);

        return targets;
    }

    private static void Collect(MatchCollection matches, string line, int lineIndex, List<LinkTarget> into)
    {
        foreach (Match match in matches)
        {
            Group anchor = match.Groups["a"];

            // An empty "#" is a link to the top of the page rather than to a heading, and has
            // no name to move.
            if (!anchor.Success || anchor.Length == 0)
            {
                continue;
            }

            into.Add(new LinkTarget(lineIndex, anchor.Index, line.Substring(anchor.Index, anchor.Length)));
        }
    }

    /// <summary>
    /// An inline destination: <c>](#name)</c>, with or without angle brackets, stopping before a
    /// title. Images are the same shape and count too — one can carry a link in its own right.
    /// </summary>
    [GeneratedRegex(@"\]\(\s*<?#(?<a>[^\s()<>""']*)")]
    private static partial Regex Inline();

    /// <summary>A reference definition: <c>[label]: #name</c>, at the head of its line.</summary>
    [GeneratedRegex(@"^[ ]{0,3}\[[^\]]*\]:[ \t]*<?#(?<a>[^\s<>""']*)")]
    private static partial Regex Definition();

    /// <summary>
    /// Raw HTML: <c>href="#name"</c>. Markdown passes HTML through untouched, so a document that
    /// writes its table of contents as a real list of anchors is as ordinary as one that does
    /// not, and its links break the same way.
    /// </summary>
    [GeneratedRegex(@"href[ \t]*=[ \t]*(?:""#(?<a>[^""]*)""|'#(?<a>[^']*)'|#(?<a>[^\s>""']+))")]
    private static partial Regex Href();
}
