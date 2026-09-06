// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;

namespace PaulTechGuy.MQ.Domain;

/// <summary>One image that has to travel with a document, and what it will be called there.</summary>
/// <param name="OldReference">The reference exactly as it appears in the markdown today.</param>
/// <param name="NewReference">What it becomes once the document has moved.</param>
/// <param name="SourcePath">Where the file is now.</param>
/// <param name="TargetPath">Where it needs to be.</param>
public sealed record AssetMove(
    string OldReference,
    string NewReference,
    string SourcePath,
    string TargetPath);

/// <summary>
/// Works out which images a Save As has to bring with it.
///
/// This is the bill for putting images in a folder named after the document. Save As to another
/// folder leaves every relative reference pointing at the old one, and Save As within a folder
/// leaves "guide.assets" beside a document now called something else - the links still resolve,
/// but the folder is named after a document that no longer exists.
///
/// Pure: paths in, a plan out, nothing touched. The copying is the store's job and the asking is
/// the view model's, so what is left here - which files count as this document's, and what they
/// should be called afterwards - can be tested by handing it strings.
/// </summary>
public static partial class AssetRelocation
{
    /// <summary>
    /// Every image that would stop resolving, or stop being named sensibly, once the document
    /// moves from <paramref name="oldDocumentPath"/> to <paramref name="newDocumentPath"/>.
    ///
    /// Empty when there is nothing to do, which is the common case: a Save As over the same file,
    /// a document with no images, or a document whose images are all somewhere shared.
    /// </summary>
    public static IReadOnlyList<AssetMove> Plan(
        string oldDocumentPath,
        string newDocumentPath,
        string text,
        ImageFolderMode mode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldDocumentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(newDocumentPath);
        ArgumentNullException.ThrowIfNull(text);

        if (Path.GetDirectoryName(Path.GetFullPath(oldDocumentPath)) is not { } oldFolder
            || Path.GetDirectoryName(Path.GetFullPath(newDocumentPath)) is not { } newFolder)
        {
            return [];
        }

        string oldAssets = DocumentAssets.FolderNameFor(oldDocumentPath, mode);
        string newAssets = DocumentAssets.FolderNameFor(newDocumentPath, mode);

        // Nothing has changed about where this document's images belong, so nothing has to move.
        if (string.Equals(oldFolder, newFolder, StringComparison.OrdinalIgnoreCase)
            && string.Equals(oldAssets, newAssets, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        List<AssetMove> moves = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in ImageReference().Matches(text))
        {
            string reference = match.Groups["target"].Value;

            if (!seen.Add(reference) || !IsRelative(reference))
            {
                continue;
            }

            string decoded = Uri.UnescapeDataString(reference).Replace('/', Path.DirectorySeparatorChar);

            // Only what this document owns. A reference reaching outside the folder, or into
            // somewhere shared that another document also uses, is not ours to move - copying it
            // would leave the other document pointing at a file that is now in two places.
            if (!Owns(decoded, oldAssets, mode))
            {
                continue;
            }

            string source = Path.GetFullPath(Path.Combine(oldFolder, decoded));

            if (!Contained(source, oldFolder))
            {
                continue;
            }

            string newReference = Rename(reference, oldAssets, newAssets);
            string target = Path.GetFullPath(Path.Combine(
                newFolder,
                Uri.UnescapeDataString(newReference).Replace('/', Path.DirectorySeparatorChar)));

            if (!string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            {
                moves.Add(new AssetMove(reference, newReference, source, target));
            }
        }

        return moves;
    }

    /// <summary>
    /// The document text with every moved reference repointed.
    ///
    /// Replaces the reference inside its brackets rather than the whole construct, so an alt
    /// text or a title is untouched - the same rule the dead-link repair follows.
    /// </summary>
    public static string Rewrite(string text, IReadOnlyList<AssetMove> moves)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(moves);

        foreach (AssetMove move in moves)
        {
            text = text.Replace($"]({move.OldReference})", $"]({move.NewReference})", StringComparison.Ordinal);
            text = text.Replace($"]({move.OldReference} ", $"]({move.NewReference} ", StringComparison.Ordinal);
        }

        return text;
    }

    /// <summary>Whether this reference is one the document's own move should carry.</summary>
    private static bool Owns(string decoded, string assetFolder, ImageFolderMode mode)
    {
        if (mode == ImageFolderMode.BesideDocument)
        {
            // Everything relevant sits directly beside the document, so the only safe test is
            // "an image file, in this folder, not in a subfolder of it".
            return !decoded.Contains(Path.DirectorySeparatorChar)
                && ImageFileTypes.IsAllowedExtension(decoded);
        }

        return decoded.StartsWith(assetFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Swaps the asset folder's name at the front of a reference, if it is there.</summary>
    private static string Rename(string reference, string oldAssets, string newAssets)
    {
        if (oldAssets.Length == 0 || string.Equals(oldAssets, newAssets, StringComparison.OrdinalIgnoreCase))
        {
            return reference;
        }

        string prefix = oldAssets + "/";

        return reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? newAssets + "/" + reference[prefix.Length..]
            : reference;
    }

    private static bool IsRelative(string reference) =>
        reference.Length > 0
        && reference[0] != '#'
        && !reference.StartsWith("//", StringComparison.Ordinal)
        && !Scheme().IsMatch(reference);

    private static bool Contained(string path, string folder) =>
        (path + Path.DirectorySeparatorChar).StartsWith(
            Path.GetFullPath(folder + Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    /// <summary>An image reference, capturing just the target.</summary>
    [GeneratedRegex(@"!\[[^\]]*\]\(\s*(?<target>[^()\s]+)", RegexOptions.CultureInvariant)]
    private static partial Regex ImageReference();

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.\-]*:", RegexOptions.CultureInvariant)]
    private static partial Regex Scheme();
}
