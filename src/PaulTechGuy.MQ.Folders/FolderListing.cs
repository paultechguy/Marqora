// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Folders;

/// <summary>What a row in the tree stands for.</summary>
public enum FolderEntryKind
{
    Folder,
    Markdown,
    Image,

    /// <summary>Any other file. Listed only while Show all files is on, and dimmed then.</summary>
    Other,
}

/// <summary>One thing found directly inside a folder.</summary>
/// <param name="IsReparsePoint">
/// A junction or symbolic link. It is listed, but a look below a folder never goes through one,
/// because a link back up the tree would make the look endless.
/// </param>
public sealed record FolderEntry(
    string Name,
    string FullPath,
    FolderEntryKind Kind,
    bool IsReparsePoint,
    DateTime LastWriteUtc)
{
    public bool IsFolder => Kind == FolderEntryKind.Folder;
}

/// <summary>
/// What the tree lists, one folder level at a time.
///
/// Markdown uses <see cref="MarkdownFileTypes.FolderExtensions"/>, the list Open Folder already
/// used, which leaves out .txt by that list's own reasoning: a folder of unrelated text files is
/// not a folder of documents. Images are the formats the preview can draw.
/// </summary>
public static class FolderListing
{
    private static readonly HashSet<string> HiddenFolderNames =
        new(StringComparer.OrdinalIgnoreCase) { "node_modules", "bin", "obj" };

    /// <summary>
    /// Folders that are never shown, not even with Show all files on: <c>.git</c> and every
    /// other dot-folder, and the build and package folders that fill a code repository.
    /// </summary>
    public static bool IsAlwaysHiddenFolder(string name) =>
        name.StartsWith('.') || HiddenFolderNames.Contains(name);

    public static FolderEntryKind KindOfFile(string name)
    {
        if (MarkdownFileTypes.IsFolderCandidate(name))
        {
            return FolderEntryKind.Markdown;
        }

        return ImageFileTypes.IsAllowedExtension(name) ? FolderEntryKind.Image : FolderEntryKind.Other;
    }

    /// <summary>
    /// Whether a file is listed. Show all files lifts every filter on files, dot-files included;
    /// without it, only markdown and images are shown and a dot-file is not.
    /// </summary>
    public static bool IsListedFile(string name, bool showAllFiles) =>
        showAllFiles
        || (!name.StartsWith('.') && KindOfFile(name) != FolderEntryKind.Other);

    /// <summary>
    /// The folder's own entries, unsorted, with always-hidden folders and unlisted files left out.
    ///
    /// Whether a listed folder leads to anything is a separate question, answered by
    /// <see cref="FolderProbe"/>. A folder that cannot be read lists as empty rather than
    /// throwing: the tree shows what it can, and a folder that vanished mid-read is the watcher's
    /// news to deliver.
    /// </summary>
    public static IReadOnlyList<FolderEntry> Read(
        string folder,
        bool showAllFiles,
        CancellationToken cancellationToken = default)
    {
        var entries = new List<FolderEntry>();

        IEnumerable<FileSystemInfo> found;

        try
        {
            found = new DirectoryInfo(folder).EnumerateFileSystemInfos("*", Enumeration);
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            return entries;
        }

        try
        {
            foreach (FileSystemInfo info in found)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (Classify(info, showAllFiles) is { } entry)
                {
                    entries.Add(entry);
                }
            }
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            // Keep what was read before the folder went away.
        }

        return entries;
    }

    /// <summary>The entry for one path, or null when it is not listed or no longer there.</summary>
    public static FolderEntry? Describe(string path, bool showAllFiles)
    {
        try
        {
            FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            return info.Exists ? Classify(info, showAllFiles) : null;
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            return null;
        }
    }

    internal static EnumerationOptions Enumeration { get; } = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.None,
        ReturnSpecialDirectories = false,
    };

    internal static bool IsReadFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or System.Security.SecurityException;

    private static FolderEntry? Classify(FileSystemInfo info, bool showAllFiles)
    {
        bool reparse = info.Attributes.HasFlag(FileAttributes.ReparsePoint);

        if (info is DirectoryInfo)
        {
            return IsAlwaysHiddenFolder(info.Name)
                ? null
                : new FolderEntry(info.Name, info.FullName, FolderEntryKind.Folder, reparse, info.LastWriteTimeUtc);
        }

        return IsListedFile(info.Name, showAllFiles)
            ? new FolderEntry(info.Name, info.FullName, KindOfFile(info.Name), reparse, info.LastWriteTimeUtc)
            : null;
    }
}
