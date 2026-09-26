// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Net;

namespace PaulTechGuy.MQ.Domain;

/// <summary>What looking an image up found.</summary>
public enum DocumentImageStatus
{
    Found,

    /// <summary>There is nowhere to look: a document that has never been saved and did not come from a page.</summary>
    NoSource,

    /// <summary>The reference climbs out of the document's folder, which is never followed.</summary>
    Outside,

    NotFound,
}

/// <summary>
/// One image as an export found it: on disk, with a path the caller streams and sizes itself,
/// or from a review page, with its bytes already in hand.
/// </summary>
/// <param name="Reference">The reference decoded, as a report or the too-large list names it.</param>
public sealed record DocumentImage(
    DocumentImageStatus Status,
    string Reference,
    string? FilePath = null,
    ReviewAsset? Asset = null);

/// <summary>
/// Where a document's pictures are, asked the same way by every export: the document's own
/// folder, or - for a review resumed from its page - the pictures that page carried.
///
/// One question, "the bytes for this reference?", so that HTML, Copy as Rich Text, the review
/// page and Word agree on what a reference means and what may be followed. The folder route
/// keeps the rule every export already had: a reference is resolved inside the document's
/// folder and never outside it. The page route never looks at a folder at all, so a resumed
/// review is never shown an unrelated file that happens to sit where its image once did.
///
/// The folder route does not read the file. Each caller reads its own way - one checks a size
/// ceiling first, another streams into a package - so the lookup only says where.
/// </summary>
public sealed class DocumentImages
{
    private readonly IReadOnlyDictionary<string, ReviewAsset>? _assets;

    private DocumentImages(string? folder, IReadOnlyDictionary<string, ReviewAsset>? assets, bool hasSource)
    {
        Folder = folder;
        _assets = assets;
        HasSource = hasSource;
    }

    /// <summary>Nowhere to look: every reference answers <see cref="DocumentImageStatus.NoSource"/>.</summary>
    public static DocumentImages None { get; } = new(null, null, hasSource: false);

    /// <summary>
    /// The folder a document lives in. A document with a path has a source even when its folder
    /// has since gone - its images are then not found, which is the truth, rather than "never
    /// saved", which is not.
    /// </summary>
    public static DocumentImages FromDocument(string? documentPath)
    {
        if (string.IsNullOrWhiteSpace(documentPath))
        {
            return None;
        }

        try
        {
            return new DocumentImages(Path.GetDirectoryName(Path.GetFullPath(documentPath)), null, hasSource: true);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new DocumentImages(null, null, hasSource: true);
        }
    }

    /// <summary>The pictures a review page carried, keyed by <see cref="ReviewAssets.NormalizeKey"/>.</summary>
    public static DocumentImages FromPage(IReadOnlyDictionary<string, ReviewAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        return new DocumentImages(null, assets, hasSource: true);
    }

    public bool HasSource { get; }

    /// <summary>The document's folder, for the folder route; null for a page or for nowhere.</summary>
    public string? Folder { get; }

    /// <summary>Whether this answers from a review page rather than from disk.</summary>
    public bool IsPage => _assets is not null;

    /// <summary>
    /// The image a reference names, as written in the markdown or as the path part of a
    /// marqora.document address. Nothing after a '?' or '#' is removed on the folder route: no
    /// export ever did, and doing so now would change what they find.
    /// </summary>
    public DocumentImage Find(string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (!HasSource)
        {
            return new DocumentImage(DocumentImageStatus.NoSource, Unescape(reference));
        }

        if (_assets is not null)
        {
            string? key = ReviewAssets.NormalizeKey(reference);

            return key is not null && _assets.TryGetValue(key, out ReviewAsset? asset)
                ? new DocumentImage(DocumentImageStatus.Found, key, Asset: asset)
                : new DocumentImage(DocumentImageStatus.NotFound, key ?? Unescape(reference));
        }

        return FindOnDisk(reference);
    }

    /// <summary>
    /// The folder route. Decoded as a URL decodes - '+' kept - and, only when that finds nothing
    /// and there is a '+' to reconsider, again as a form decodes, with '+' as a space. The first
    /// is what the reference means; the second is what the HTML exports used to do, and a
    /// document that relied on it keeps working.
    /// </summary>
    private DocumentImage FindOnDisk(string reference)
    {
        string literal = Unescape(reference);

        if (Folder is null)
        {
            return new DocumentImage(DocumentImageStatus.NotFound, literal);
        }

        if (PathContainment.ResolveWithin(Folder, literal) is not { } full)
        {
            return new DocumentImage(DocumentImageStatus.Outside, literal);
        }

        if (File.Exists(full))
        {
            return new DocumentImage(DocumentImageStatus.Found, literal, FilePath: full);
        }

        if (reference.Contains('+', StringComparison.Ordinal))
        {
            string spaced = WebUtility.UrlDecode(reference);

            if (PathContainment.ResolveWithin(Folder, spaced) is { } alternative && File.Exists(alternative))
            {
                return new DocumentImage(DocumentImageStatus.Found, spaced, FilePath: alternative);
            }
        }

        return new DocumentImage(DocumentImageStatus.NotFound, literal);
    }

    private static string Unescape(string reference)
    {
        try
        {
            return Uri.UnescapeDataString(reference);
        }
        catch (UriFormatException)
        {
            return reference;
        }
    }
}
