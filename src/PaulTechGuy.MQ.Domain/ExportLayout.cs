// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// What surrounds a document on paper, whichever export writes it: a cover page, a contents
/// page, and a running header with page numbers.
///
/// One record for Word and the PDF, and one saved copy of it (docs/Export-Alignment-Plan.md,
/// D3). These lived on <see cref="DocxExportSetup"/> while only Word could draw them; now that
/// the paged engine draws them too, two copies would let the same document come out with a
/// cover one way and without it the other. Both export dialogs show these three boxes, and
/// ticking one in either changes both.
///
/// Settable rather than init-only for the reason set out at the top of <see cref="AppSettings"/>.
/// </summary>
public sealed record ExportLayout
{
    /// <summary>
    /// A title page, read from the front matter.
    ///
    /// Off by default. Pleasant for a report, wrong for a two-paragraph note, and only the
    /// author knows which they have written.
    /// </summary>
    public bool IncludeCoverPage { get; set; }

    /// <summary>
    /// A contents page listing the headings. In Word it is a field, which Word offers to update
    /// on open; in a PDF it is written with its page numbers.
    ///
    /// Off by default for the same reason as the cover: it adds visible content the markdown did
    /// not contain.
    /// </summary>
    public bool IncludeTableOfContents { get; set; }

    /// <summary>
    /// A header carrying the document title and a footer carrying the page number.
    ///
    /// On by default, the one default here that adds something the markdown did not ask for.
    /// A document people print and hand round without page numbers is the surprising outcome
    /// rather than the safe one.
    /// </summary>
    public bool IncludeHeaderAndFooter { get; set; } = true;

    public static ExportLayout Default => new();
}
