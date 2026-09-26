// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Folio;

/// <summary>Where a stand-in put its document, and the pictures it would not write.</summary>
/// <param name="DocumentPath">The path the planner is given for the document. No file is written there.</param>
public sealed record FolioStandInResult(string DocumentPath, IReadOnlyList<string> Skipped);

/// <summary>
/// A review resumed from its page, set down in a Folio's temporary folder so the planner can
/// take it like any saved document: a folder of its own, and in it the pictures the page carried
/// at the paths the document names them by.
///
/// The document itself is not written. The planner never reads it - it takes the text from the
/// buffer - so putting the reviewed text into the temporary folder would buy nothing.
///
/// The pictures came from a page somebody sent, so each is written only when every one of these
/// holds: its path stays inside the stand-in's folder, its extension is what its bytes actually
/// are, and nothing is already there. A page cannot use this to leave an ".hta", a
/// "desktop.ini" or anything else that merely starts the way a PNG does. One picture that cannot
/// be written is skipped, and shows in the Folio's report as missing like any other.
/// </summary>
public static class FolioStandIn
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <param name="root">The folder stand-ins go in, inside a Folio's own temporary folder.</param>
    /// <param name="fileName">The reviewed document's name, as the page gave it.</param>
    public static FolioStandInResult Write(string root, string fileName, IReadOnlyDictionary<string, ReviewAsset> assets)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(assets);

        string folder = NewFolder(root);
        string documentPath = Path.Combine(folder, SafeName(fileName));
        List<string> skipped = [];

        foreach ((string key, ReviewAsset asset) in assets)
        {
            if (!TryWrite(folder, key, asset))
            {
                skipped.Add(key);
            }
        }

        return new FolioStandInResult(documentPath, skipped);
    }

    /// <summary>
    /// A name Windows will create as written: the page's name cleaned, with a reserved device
    /// name, or one that ends in a dot or a space - which Windows would quietly trim, so that the
    /// path the planner is given and the folder on disk disagree - replaced.
    /// </summary>
    public static string SafeName(string? fileName)
    {
        string name = ReviewState.SanitizeFileName(fileName);
        string stem = Path.GetFileNameWithoutExtension(name);

        if (ReservedNames.Contains(stem.Split('.')[0])
            || name.EndsWith('.')
            || name.EndsWith(' ')
            || stem.Length == 0)
        {
            return "review.md";
        }

        return name;
    }

    private static string NewFolder(string root)
    {
        for (int n = 1; ; n++)
        {
            string candidate = Path.Combine(root, n.ToString(System.Globalization.CultureInfo.InvariantCulture));

            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                Directory.CreateDirectory(candidate);
                return candidate;
            }
        }
    }

    private static bool TryWrite(string folder, string key, ReviewAsset asset)
    {
        if (PathContainment.ResolveWithin(folder, key) is not { } path
            || string.Equals(path.TrimEnd(Path.DirectorySeparatorChar), folder.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            || !ExtensionMatches(path, asset.Bytes))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            using FileStream file = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);

            file.Write(asset.Bytes);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // A key naming a file where another key needs a folder, a path too long, a name the
            // file system will not take: this one picture is left out, and says so as missing.
            return false;
        }
    }

    /// <summary>Whether a path's extension is what the bytes say they are. ".jpeg" and ".jpg" are one.</summary>
    private static bool ExtensionMatches(string path, byte[] bytes)
    {
        if (ImageFileTypes.ExtensionFor(bytes) is not { } actual)
        {
            return false;
        }

        string written = Path.GetExtension(path).ToLowerInvariant();

        return written == actual || (actual == ".jpg" && written == ".jpeg");
    }
}
