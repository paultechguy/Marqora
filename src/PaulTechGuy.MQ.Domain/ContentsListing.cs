// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// What an exported document's contents listing says and which headings it lists - one rule
/// for Word's contents field and the PDF's contents page, so the two cannot list different
/// headings under different titles (docs/Export-Alignment-Plan.md, §6.3).
/// </summary>
public static class ContentsListing
{
    /// <summary>The listing's own title. Not a heading in either export: it lists nothing of its own.</summary>
    public const string Title = "Contents";

    /// <summary>
    /// The first and last heading level listed.
    ///
    /// Tied to the heading numbering rather than fixed at one to three, so the contents begin
    /// at the first numbered section. A document numbering from Heading 2 opens with an H1
    /// title, and that title listing itself as the first line of its own contents - above
    /// section 1, and the only entry with no number beside it - reads as a mistake rather
    /// than as a title.
    ///
    /// Numbering switched off is treated as starting at Heading 2 too, because a markdown
    /// file that opens with a single H1 title is the common shape whether or not anything in
    /// it is numbered.
    ///
    /// Three levels deep from wherever it starts, which is what Word's own contents does.
    /// </summary>
    public static (int First, int Last) Levels(HeadingNumbering numbering)
    {
        int first = numbering switch
        {
            HeadingNumbering.FromHeading1 => 1,
            HeadingNumbering.FromHeading3 => 3,
            _ => 2,
        };

        return (first, first + 2);
    }
}
