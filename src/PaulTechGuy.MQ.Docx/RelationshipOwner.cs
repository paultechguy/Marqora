// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using DocumentFormat.OpenXml.Packaging;

namespace PaulTechGuy.MQ.Docx;

/// <summary>
/// The part whose relationships the writing currently belongs to.
///
/// A relationship id means something only inside the part that declares it. The body's links
/// and pictures are the main document part's; a footnote's are the footnotes part's. A
/// footnote naming a relationship the main part declared is exactly what Word reports as
/// unreadable content and offers to repair - a link in a footnote did that until the renderers
/// stopped holding the main part and started asking this instead. The footnote writer switches
/// it for the length of one note.
///
/// Pictures are stored once whichever part first asked for them; a second part that draws the
/// same picture gets a relationship of its own to the same image part.
/// </summary>
internal sealed class RelationshipOwner
{
    public RelationshipOwner(MainDocumentPart main)
    {
        ArgumentNullException.ThrowIfNull(main);

        Main = main;
        Current = main;
    }

    public MainDocumentPart Main { get; }

    /// <summary>The part the paragraphs being written right now will live in.</summary>
    public OpenXmlPart Current { get; private set; }

    /// <summary>Writes into <paramref name="part"/> until the returned scope is disposed.</summary>
    public IDisposable Enter(OpenXmlPart part)
    {
        ArgumentNullException.ThrowIfNull(part);

        OpenXmlPart previous = Current;
        Current = part;

        return new Scope(this, previous);
    }

    /// <summary>
    /// A link target, escaped.
    ///
    /// The relationship is written from the URI as it was typed, so
    /// <c>&lt;https://example.com/a b c&gt;</c> reached the part with its spaces in, and Word
    /// printed "Error! Hyperlink reference not valid." where the link text belonged.
    /// <see cref="Uri.AbsoluteUri"/> is the escaped form, and a target already escaped passes
    /// through unchanged.
    /// </summary>
    public HyperlinkRelationship AddHyperlink(Uri target)
    {
        ArgumentNullException.ThrowIfNull(target);

        return Current.AddHyperlinkRelationship(new Uri(target.AbsoluteUri), isExternal: true);
    }

    /// <summary>A new image part, owned by the part being written.</summary>
    public ImagePart NewImagePart(PartTypeInfo type) => Current switch
    {
        MainDocumentPart main => main.AddImagePart(type),
        FootnotesPart notes => notes.AddImagePart(type),
        _ => throw new InvalidOperationException(
            $"Pictures cannot be written into a {Current.GetType().Name}."),
    };

    /// <summary>
    /// The id under which the part being written reaches <paramref name="image"/>, adding the
    /// relationship the first time this part draws it.
    /// </summary>
    public string IdOf(ImagePart image)
    {
        ArgumentNullException.ThrowIfNull(image);

        foreach (IdPartPair pair in Current.Parts)
        {
            if (ReferenceEquals(pair.OpenXmlPart, image))
            {
                return pair.RelationshipId;
            }
        }

        Current.AddPart(image);

        return Current.GetIdOfPart(image);
    }

    private sealed class Scope(RelationshipOwner owner, OpenXmlPart previous) : IDisposable
    {
        public void Dispose() => owner.Current = previous;
    }
}
