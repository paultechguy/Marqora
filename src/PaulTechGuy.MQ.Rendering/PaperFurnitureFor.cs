// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Rendering;

/// <summary>
/// What a PDF or a print of one document calls itself and carries around its text, read from
/// the document's own front matter. The app and marqora-cli both ask here, so a PDF made from
/// either has the same title, cover and contents (CLI-EX-PDF-01).
/// </summary>
public static class PaperFurnitureFor
{
    /// <summary>
    /// The front matter's title, which is the one the Word export puts in the same property, or
    /// the file's own name. Never the path - that went into every PDF's Title property, and so
    /// to whoever the PDF was sent.
    /// </summary>
    public static string Title(string text, string displayName) =>
        FrontMatter.Read(text).Title is { Length: > 0 } title
            ? title
            : displayName;

    /// <summary>
    /// The shared <see cref="ExportLayout"/> resolved against one document, so a PDF and a .docx
    /// of it carry the same furniture (docs/Export-Alignment-Plan.md, §6.4).
    ///
    /// The cover reads the same front matter and the same date rule as Word's cover; the
    /// contents lists the same heading levels, from this document's own numbering.
    /// </summary>
    public static PaperFurniture Document(string text, string displayName, ExportLayout layout, HeadingNumbering numbering)
    {
        ArgumentNullException.ThrowIfNull(layout);

        FrontMatter front = FrontMatter.Read(text);

        PaperCover? cover = layout.IncludeCoverPage
            ? new PaperCover(front.Title ?? displayName, front.Subject, front.CoverDate(), front.Version, front.Author)
            : null;

        PaperContents? contents = null;

        if (layout.IncludeTableOfContents)
        {
            (int first, int last) = ContentsListing.Levels(numbering);

            contents = new PaperContents(ContentsListing.Title, first, last);
        }

        return new PaperFurniture(cover, contents, layout.IncludeHeaderAndFooter);
    }
}
