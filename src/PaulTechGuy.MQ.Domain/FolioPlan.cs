// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace PaulTechGuy.MQ.Domain;

/// <summary>One document on its way into a Folio, exactly as the workspace holds it.</summary>
/// <param name="Path">Absolute. A document that has never been saved cannot be planned - there
/// is no folder for its relative references to resolve against - and is excluded before here.</param>
/// <param name="Text">The buffer, not the file. Unsaved edits travel.</param>
/// <param name="Links">Every link and image, from the parse that already happened.</param>
public sealed record FolioSource(string Path, string Text, IReadOnlyList<LinkReference> Links);

/// <summary>What is wrong with a document, in the author's terms rather than the planner's.</summary>
public enum FolioWarningKind
{
    /// <summary>The reference names an image that is not there. Nothing is invented for it.</summary>
    MissingImage,

    /// <summary>A link leaving the Folio: it will still point at this machine after sharing.</summary>
    OutsideLink,

    /// <summary>
    /// The reference has to move, and the planner could not find it in the source to rewrite -
    /// a reference-style image, most likely, whose target sits in a definition elsewhere.
    /// </summary>
    NotRewritable,

    /// <summary>
    /// The image is wider than the cap the author set, so what travels will be a smaller copy
    /// than what they have.
    ///
    /// Reported rather than done quietly, because it is the one thing in a Folio that is
    /// lossy - and because a Folio carrying its own sources hands these back at the reduced
    /// size, so the round trip does not return what went in.
    /// </summary>
    WillBeShrunk,
}

/// <summary>Something the author should see before anything is written.</summary>
public sealed record FolioWarning
{
    public required FolioWarningKind Kind { get; init; }

    /// <summary>The document it was found in, so the preflight can name a file rather than a number.</summary>
    public required string DocumentPath { get; init; }

    /// <summary>Zero-based, as <see cref="LinkReference.SourceLine"/> is.</summary>
    public required int Line { get; init; }

    public required string Url { get; init; }

    public required string Message { get; init; }
}

/// <summary>A file that travels beside the documents.</summary>
public sealed record FolioAsset
{
    public required string SourcePath { get; init; }

    /// <summary>Where it sits inside the Folio. Forward-slashed, relative to the root.</summary>
    public required string EntryName { get; init; }

    public required long Bytes { get; init; }

    /// <summary>
    /// How wide the picture is, or zero when it could not be established - an unrecognized
    /// format, a truncated header, or a vector image with no pixel size at all.
    ///
    /// Zero is read everywhere as "leave this one alone": an image whose size is unknown is not
    /// one to start re-encoding.
    /// </summary>
    public uint PixelWidth { get; init; }

    /// <summary>
    /// True when the reference had to be repointed. False means the path the author wrote
    /// already worked and was left exactly as it was, which is the common and preferable case.
    /// </summary>
    public required bool Relocated { get; init; }
}

/// <summary>A document as it will appear in the Folio.</summary>
public sealed record FolioDocumentPlan
{
    public required string SourcePath { get; init; }

    /// <summary>Its name inside the Folio. Every document sits at the root.</summary>
    public required string EntryName { get; init; }

    /// <summary>The text to write, with any repointed references already applied.</summary>
    public required string Text { get; init; }

    /// <summary>Whether <see cref="Text"/> differs from what the author has open.</summary>
    public required bool Rewritten { get; init; }
}

/// <summary>
/// Everything a Folio will contain, and everything wrong with it, worked out before a single
/// byte is written.
///
/// The preflight dialog is this record rendered. Nothing here has touched the destination yet,
/// so a plan can be thrown away as cheaply as it was made.
/// </summary>
public sealed record FolioPlan
{
    public required IReadOnlyList<FolioDocumentPlan> Documents { get; init; }

    public required IReadOnlyList<FolioAsset> Assets { get; init; }

    public required IReadOnlyList<FolioWarning> Warnings { get; init; }

    /// <summary>Images that kept the path the author wrote.</summary>
    public int KeptCount => Assets.Count(a => !a.Relocated);

    /// <summary>Images collected from outside their document's folder, or renamed to avoid a clash.</summary>
    public int RelocatedCount => Assets.Count(a => a.Relocated);

    /// <summary>
    /// Roughly what the artifact will weigh: the images, plus the documents themselves.
    ///
    /// Roughly, because a zip will compress the text and a single HTML file will inflate the
    /// images by a third turning them into base64. It is the number that answers "will this go
    /// through email", which is the only question anybody asks of it.
    /// </summary>
    public long TotalBytes =>
        Assets.Sum(a => a.Bytes)
        + Documents.Sum(d => (long)Encoding.UTF8.GetByteCount(d.Text));

    public IEnumerable<FolioWarning> WarningsOf(FolioWarningKind kind) => Warnings.Where(w => w.Kind == kind);

    public static FolioPlan Empty { get; } = new()
    {
        Documents = [],
        Assets = [],
        Warnings = [],
    };
}
