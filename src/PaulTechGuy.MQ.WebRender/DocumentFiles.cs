// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.WebRender;

/// <summary>
/// The files beside a document, served to the shell at https://marqora.document/... - the address
/// it rewrites relative sources to. The live preview and the headless host both answer those
/// requests here, so a picture the preview shows is the picture an export lays out.
///
/// The folder is not mapped with SetVirtualHostNameToFolderMapping, although that is what the
/// assets host uses. A folder mapping is handed to a page when it navigates and the shell page
/// navigates once, at startup; a mapping added or changed after that is never seen by it, so
/// every relative image came back ERR_NAME_NOT_RESOLVED until the page was reloaded. The folder
/// changes with every document, so instead each request is answered against whatever folder is
/// current at the time.
/// </summary>
public static class DocumentFiles
{
    /// <summary>The host name the shell writes a document's relative sources under.</summary>
    public const string VirtualHost = "marqora.document";

    /// <summary>
    /// Added to every picture served for a document. Two things depend on it. The address is
    /// the same for every tab - https://marqora.document/img/a.png names a different file in
    /// each document's folder - so a cached answer could show one tab's picture in another.
    /// And a resumed review's pictures came from a page somebody sent; kept out of the browser's
    /// disk cache, they live only as long as the tab that holds them.
    /// </summary>
    public const string NoStore = "\r\nCache-Control: no-store";

    public static string BaseUrl => $"https://{VirtualHost}/";

    /// <summary>
    /// The folder a document's relative sources resolve against, or null when it has none: not
    /// saved, or saved in a folder that is no longer there.
    /// </summary>
    public static string? FolderOf(string? documentPath)
    {
        string? directory = string.IsNullOrWhiteSpace(documentPath)
            ? null
            : Path.GetDirectoryName(Path.GetFullPath(documentPath));

        return directory is not null && Directory.Exists(directory) ? directory : null;
    }

    /// <summary>
    /// Answers a request for a document file from <paramref name="directory"/>. Anything outside
    /// the folder, anything with no folder to resolve against and anything that is not a file
    /// gets a 404 rather than falling through, because falling through would leave the request
    /// to Chromium, and marqora.document is not a real host.
    /// </summary>
    public static void Serve(
        CoreWebView2 sender,
        CoreWebView2WebResourceRequestedEventArgs e,
        string? directory,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(logger);

        string? path = Resolve(directory, e.Request.Uri, logger);

        if (path is null)
        {
            e.Response = sender.Environment.CreateWebResourceResponse(null, 404, "Not Found", string.Empty);
            return;
        }

        try
        {
            FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            e.Response = sender.Environment.CreateWebResourceResponse(
                stream.AsRandomAccessStream(),
                200,
                "OK",
                $"Content-Type: {ContentTypeFor(path)}{NoStore}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Could not read {Path} for the preview.", path);
            e.Response = sender.Environment.CreateWebResourceResponse(null, 404, "Not Found", string.Empty);
        }
    }

    /// <summary>
    /// The file a marqora.document URL names, or null when it names nothing that may be
    /// served: no document folder, a path that escapes it, or a path that is not a file.
    /// </summary>
    public static string? Resolve(string? directory, string url, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(logger);

        if (directory is null || !url.StartsWith(BaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string relative = url[BaseUrl.Length..];

        int cut = relative.IndexOfAny(['?', '#']);
        if (cut >= 0)
        {
            relative = relative[..cut];
        }

        if (PathContainment.ResolveWithin(directory, Uri.UnescapeDataString(relative)) is not { } path)
        {
            logger.LogDebug("{Url} does not resolve to a file inside the document folder.", url);

            return null;
        }

        return File.Exists(path) ? path : null;
    }

    private static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".svg" => "image/svg+xml",
        ".webp" => "image/webp",
        ".avif" => "image/avif",
        ".bmp" => "image/bmp",
        ".ico" => "image/x-icon",
        ".mp4" or ".m4v" => "video/mp4",
        ".webm" => "video/webm",
        ".ogv" => "video/ogg",
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".ogg" or ".oga" => "audio/ogg",
        ".m4a" => "audio/mp4",
        ".flac" => "audio/flac",
        _ => "application/octet-stream",
    };
}
