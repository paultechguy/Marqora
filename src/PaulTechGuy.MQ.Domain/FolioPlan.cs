// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace PaulTechGuy.MQ.Domain;

/// <summary>One document on its way into a Folio, exactly as the workspace holds it.</summary>
/// <param name="Path">Absolute. A document that has never been saved cannot be planned - there
/// is no folder for its relative references to resolve against - and is excluded before here.</param>
/// <param name="Text">The buffer, not the file. Unsaved edits travel.</param>
/// <param name="Links">Every link and image, from the parse that already happened.</param>
/// <param name="ContainedOnly">
/// Only pictures inside <paramref name="Path"/>'s own folder may be collected. For a review
/// resumed from its page, which stands in for a document as a temporary folder holding the
/// pictures that page carried: its text came from a file somebody sent, and an absolute path or a
/// "..\" in it must not be able to gather files from the reader's own disk into their Folio.
/// </param>
public sealed record FolioSource(string Path, string Text, IReadOnlyList<LinkReference> Links, bool ContainedOnly = false);

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

    /// <summary>
    /// A picture named by web address that is not being fetched, so it will not travel.
    ///
    /// This is the case that was silent before any of this existed, and saying it out loud is
    /// worth as much as the fetching is: the address goes into the Folio exactly as written, and
    /// the person who opens it has <em>their</em> browser reach out to that site. The author is
    /// the only one who can weigh that, and they could not weigh what they were never told.
    /// </summary>
    RemoteImageNotIncluded,

    /// <summary>
    /// Something other than a picture that the document loads from the web - an iframe, a video,
    /// a subtitle track.
    ///
    /// Reported for the same reason as a picture left on the web, and never offered for
    /// fetching for a reason of its own: a live page cannot be carried inside a file. It works
    /// perfectly well in the Folio exactly as written, and the reader's browser is what goes and
    /// gets it - which is the part worth knowing before sending it to somebody.
    /// </summary>
    RemoteMediaNotIncluded,

    /// <summary>
    /// A picture the author asked to have fetched, that did not come back.
    ///
    /// Raised by the fetcher and merged into the plan afterwards, never by the planner: by the
    /// time a plan is built, a picture that failed and a picture nobody asked for look exactly
    /// alike - both are simply absent from the map. Reporting this from the planner would tell
    /// an author they had made a choice when in fact a server returned an error.
    /// </summary>
    RemoteImageFailed,
}

/// <summary>
/// A picture a document names by web address.
///
/// Collected whether or not anything is going to be fetched, because the preflight has to be
/// able to say which sites are involved <em>before</em> the author decides - naming them
/// afterwards would be asking permission for something already done. Nothing here costs a
/// network call: it is what the text said, read off the parse that already happened.
/// </summary>
public sealed record FolioRemoteImage
{
    /// <summary>Exactly as written, suffix and query intact.</summary>
    public required string Url { get; init; }

    /// <summary>The document it was found in, so the preflight can name a file.</summary>
    public required string DocumentPath { get; init; }

    /// <summary>Zero-based, as <see cref="LinkReference.SourceLine"/> is.</summary>
    public required int Line { get; init; }

    /// <summary>
    /// The host, for the preflight's "from 2 sites" line, or the whole address when it cannot be
    /// parsed as one - a protocol-relative "//host/path" included, which <see cref="Uri"/> will
    /// not take on its own.
    /// </summary>
    public string Host =>
        Uri.TryCreate(
            Url.StartsWith("//", StringComparison.Ordinal) ? "https:" + Url : Url,
            UriKind.Absolute,
            out Uri? parsed)
            ? parsed.Host
            : Url;
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

    /// <summary>
    /// Worth knowing, but nothing is broken.
    ///
    /// The test is whether anything went wrong. A picture that is not on the machine, a link
    /// that will not resolve, a reference nobody could repoint, a fetch that was asked for and
    /// did not arrive - those are faults. The rest are the Folio working as designed: an iframe
    /// could never have travelled inside a file, a picture left on the web was left there
    /// because nobody ticked the box, and a reduced picture was reduced on purpose.
    ///
    /// Here rather than in either window, because both of them ask. The preflight counts these
    /// before the share and the report counts them after, and the two disagreeing about what
    /// counts as a problem is worse than either answer on its own.
    /// </summary>
    public bool IsAdvisory => Kind
        is FolioWarningKind.RemoteMediaNotIncluded
        or FolioWarningKind.RemoteImageNotIncluded
        or FolioWarningKind.WillBeShrunk;
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

    /// <summary>
    /// Where this picture came from, when it came from the web. Null for everything on disk,
    /// which is every picture unless the author asked for the other thing.
    ///
    /// Kept so the result can say what was fetched and from where. By the time an asset exists
    /// it is an ordinary file in a scratch folder and nothing downstream can tell the
    /// difference - which is the point, and also why the fact has to be written down here or it
    /// is lost.
    /// </summary>
    public string? RemoteUrl { get; init; }
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

    /// <summary>
    /// Every picture named by a web address, whether or not any of them are being fetched.
    ///
    /// Always populated. The preflight reads this to say how many there are and which sites they
    /// sit on, which it has to be able to do before the author has decided anything.
    /// </summary>
    public IReadOnlyList<FolioRemoteImage> RemoteImages { get; init; } = [];

    /// <summary>The sites the pictures in <see cref="RemoteImages"/> sit on, each named once.</summary>
    public IReadOnlyList<string> RemoteHosts =>
        [.. RemoteImages.Select(i => i.Host).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Pictures that came from the web rather than off the disk.</summary>
    public int FetchedCount => Assets.Count(a => a.RemoteUrl is not null);

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

    /// <summary>Things that are genuinely missing or broken.</summary>
    public int FailureCount => Warnings.Count(w => !w.IsAdvisory);

    /// <summary>Things worth knowing that are not faults.</summary>
    public int AdvisoryCount => Warnings.Count(w => w.IsAdvisory);

    public static FolioPlan Empty { get; } = new()
    {
        Documents = [],
        Assets = [],
        Warnings = [],
    };
}
