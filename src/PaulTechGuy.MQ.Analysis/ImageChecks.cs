// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Analysis;

/// <summary>
/// What is wrong with an image that is actually there.
///
/// Its own file rather than a branch inside <see cref="LinkChecks"/> because of one useful
/// asymmetry: alt text needs no disk. LinkChecks gives up on everything relative the moment it
/// finds the document has never been saved - there is no folder for a path to be relative to -
/// and this rule keeps working, because whether an image says what it is has nothing to do with
/// where the document lives.
///
/// A LinkFinding rather than a Diagnostic, which was the second attempt. A Diagnostic becomes a
/// Monaco marker, and a marker at any severity Monaco takes seriously - Error, Warning or Info -
/// brings a hover carrying "View Problem" and "No quick fixes available" with it. That is the
/// exact chrome the dead-link findings were moved off markers to escape, and putting one marker
/// back on the same line brought all of it back for every finding sharing that hover.
///
/// There is still no repair to offer here beyond typing, so this kind carries no menu. What it
/// needs is a message and an underline, and a decoration gives both without the rest.
/// </summary>
internal static class ImageChecks
{
    public static void Run(AnalysisRequest request, List<LinkFinding> into)
    {
        foreach (LinkReference link in request.Links)
        {
            if (!link.IsImage)
            {
                continue;
            }

            // A picture written as "<img src=...>" is not asked about its alt text, because
            // nothing here reads one. MarkdownMediaReader collects the address and leaves Text
            // empty, so every raw tag would look like an image nobody had described - and the
            // first thing anyone would see after an update is a fresh mark on every such line in
            // a document they had already put right. Reading the attribute and lifting this is a
            // small change, and a separate one to judge on its own merits.
            if (link.IsRawHtml)
            {
                continue;
            }

            // A badge - "[![](build.svg)](https://ci.example)" - takes its accessible name from
            // the link around it, so an empty alt is correct there rather than missing. Skipping
            // these is the single biggest thing keeping the rule off a README's every line.
            if (link.IsInsideLink)
            {
                continue;
            }

            // Only a genuinely empty label. "![ ](x.png)" is an author saying, in the way the
            // accessibility guidance tells them to, that this image is decorative and should be
            // skipped - which is an answer, not an omission.
            if (link.Text.Length > 0)
            {
                continue;
            }

            into.Add(new LinkFinding
            {
                Line = link.SourceLine,
                Start = link.SourceColumn,
                Length = Math.Max(1, link.Length),
                Url = link.Url.Trim(),
                Kind = LinkFindingKind.MissingAltText,
                Message = "This image has no alt text.",
            });
        }
    }
}
