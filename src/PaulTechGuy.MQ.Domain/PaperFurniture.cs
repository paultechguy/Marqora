// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>The cover page's lines. Title always has an answer; the rest are left out when empty.</summary>
public sealed record PaperCover(string Title, string? Subtitle, string Date, string? Version, string? Author);

/// <summary>The contents page: its title and the heading levels it lists (<see cref="ContentsListing"/>).</summary>
public sealed record PaperContents(string Title, int First, int Last);

/// <summary>
/// What surrounds a document on paper: a cover, a contents page, a running header and page
/// number. Resolved for one document - the cover's lines read from its front matter, the
/// contents' range from its own heading numbering - so the print side has nothing left to
/// decide (docs/Export-Alignment-Plan.md, §6.3 and §6.4).
/// </summary>
public sealed record PaperFurniture(PaperCover? Cover, PaperContents? Contents, bool HeaderAndFooter)
{
    /// <summary>No cover, no contents, a header and page number: the default a PDF had before.</summary>
    public static PaperFurniture Plain { get; } = new(null, null, HeaderAndFooter: true);
}

/// <summary>Which engine wrote a PDF or a printout.</summary>
public enum PrintEngine
{
    /// <summary>Paged.js laying the document out in a view of its own (docs/Export-Alignment-Plan.md, D2).</summary>
    Paged,

    /// <summary>The live preview printed as it stands - the engine before the paged one.</summary>
    Classic,
}
