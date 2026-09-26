// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.RegularExpressions;

namespace PaulTechGuy.MQ.Domain;

/// <summary>An image a shared review page carried, by the type its bytes say it is.</summary>
public sealed record ReviewAsset(string MediaType, byte[] Bytes);

/// <summary>
/// The pictures a resumed review shows, taken back out of the page that carried them.
///
/// The page already holds every image it could embed as a data URI, and each one carries
/// <c>data-mq-asset</c> naming the path the document wrote it under - the attribute and the
/// idea a Folio uses. They are held in memory: a resumed review is a snapshot with no folder, and
/// the preview and every export answer from this map instead. They are written only where the
/// reader sends them - an export, a Folio - and for a Folio briefly into its temporary folder,
/// which is removed when the share ends and swept at the next start if Marqora stopped mid-share.
///
/// The page is a file somebody sent, so the bytes decide what each image is, never the type the
/// data URI claims; anything that is not an image is left out and never served.
/// </summary>
public static partial class ReviewAssets
{
    public const int MaximumAssets = 500;

    public const long MaximumTotalBytes = 256L * 1024 * 1024;

    /// <summary>
    /// A root that exists only to resolve ".." against. Nothing is ever read from or written to
    /// it; it is what lets <see cref="PathContainment"/> answer "does this climb out?" for a key.
    /// </summary>
    private static readonly string KeyRoot = Path.Combine(Path.GetTempPath(), "marqora-review-assets");

    /// <summary>
    /// The one spelling of an image's path that both ends agree on: the writer marking an image,
    /// the reader collecting it, and the preview asked for it. Each of those receives the path
    /// written differently - HTML-encoded in an attribute, percent-encoded in a URL, "./" or not -
    /// and an exact lookup between them would miss.
    /// </summary>
    /// <returns>A relative path with forward slashes, or null for one that is absolute or climbs out.</returns>
    public static string? NormalizeKey(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        string key = WebUtility.HtmlDecode(reference);

        int cut = key.IndexOfAny(['?', '#']);

        if (cut >= 0)
        {
            key = key[..cut];
        }

        // Not WebUtility.UrlDecode: that turns '+' into a space, and a file named "a+b.png" is
        // requested by the browser with its plus intact.
        key = Uri.UnescapeDataString(key).Replace('\\', '/');

        if (key.Length == 0 || key.StartsWith('/') || key.Contains(':', StringComparison.Ordinal) || Path.IsPathRooted(key))
        {
            return null;
        }

        if (PathContainment.ResolveWithin(KeyRoot, key) is not { } full)
        {
            return null;
        }

        string relative = Path.GetRelativePath(KeyRoot, full).Replace('\\', '/');

        return relative is "." or "" ? null : relative;
    }

    /// <summary>
    /// Every image in the page's article that says where it belongs, keyed by
    /// <see cref="NormalizeKey"/> and compared without regard to case, as the file system does.
    ///
    /// Only the article is read: the CriticMarkup block after it is the document's own text,
    /// and a document about Marqora could hold an img that looks exactly like one of these.
    /// Where one key appears twice, the last wins.
    /// </summary>
    public static IReadOnlyDictionary<string, ReviewAsset> Extract(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        Dictionary<string, ReviewAsset> assets = new(StringComparer.OrdinalIgnoreCase);

        int start = html.IndexOf("<article class=\"mq-preview\">", StringComparison.OrdinalIgnoreCase);
        int end = html.LastIndexOf($"<script type=\"{ReviewPage.SourceType}\" id=\"{ReviewPage.SourceId}\"", StringComparison.OrdinalIgnoreCase);

        if (start < 0 || end <= start)
        {
            return assets;
        }

        long total = 0;

        foreach (Match match in EmbeddedAsset().Matches(html, start))
        {
            if (match.Index >= end)
            {
                break;
            }

            if (NormalizeKey(match.Groups["entry"].Value) is not { } key)
            {
                continue;
            }

            byte[] bytes;

            try
            {
                bytes = Convert.FromBase64String(match.Groups["data"].Value);
            }
            catch (FormatException)
            {
                continue;
            }

            if (ImageFileTypes.ExtensionFor(bytes) is not { } extension)
            {
                continue;
            }

            if (assets.TryGetValue(key, out ReviewAsset? previous))
            {
                total -= previous.Bytes.Length;
            }
            else if (assets.Count >= MaximumAssets)
            {
                continue;
            }

            if (total + bytes.Length > MaximumTotalBytes)
            {
                continue;
            }

            total += bytes.Length;
            assets[key] = new ReviewAsset(MediaTypeFor(extension), bytes);
        }

        return assets;
    }

    /// <summary>The media type for an extension <see cref="ImageFileTypes.ExtensionFor"/> answered.</summary>
    public static string MediaTypeFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".svg" => "image/svg+xml",
        ".webp" => "image/webp",
        ".avif" => "image/avif",
        ".bmp" => "image/bmp",
        _ => "application/octet-stream",
    };

    /// <summary>
    /// An img carrying both the attribute naming where it belongs and its data URI, in either
    /// order - the two orders spelled out, as a Folio's reader does, rather than matched with
    /// something that could span from one element into the next.
    /// </summary>
    [GeneratedRegex(
        """
        <img\b[^>]*?(?:
            data-mq-asset\s*=\s*"(?<entry>[^"]+)"[^>]*?src\s*=\s*"data:[^;",]+;base64,(?<data>[A-Za-z0-9+/=]+)"
          | src\s*=\s*"data:[^;",]+;base64,(?<data>[A-Za-z0-9+/=]+)"[^>]*?data-mq-asset\s*=\s*"(?<entry>[^"]+)"
        )
        """,
        RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex EmbeddedAsset();
}
