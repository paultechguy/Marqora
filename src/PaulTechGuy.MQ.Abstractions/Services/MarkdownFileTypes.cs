// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Abstractions.Services;

/// <summary>The file extensions Marqora treats as markdown, shared by the dialog and drop handlers.</summary>
public static class MarkdownFileTypes
{
    public static IReadOnlyList<string> Extensions { get; } =
        [".md", ".markdown", ".mdown", ".mkd", ".mdx", ".txt"];

    public static bool IsSupported(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Extensions treated as markdown when sweeping a folder.
    ///
    /// Deliberately narrower than <see cref="Extensions"/>: .txt is fine when a file is
    /// chosen explicitly, but a folder full of unrelated text files should not all be
    /// opened because Marqora happens to accept that extension.
    /// </summary>
    public static IReadOnlyList<string> FolderExtensions { get; } =
        [".md", ".markdown", ".mdown", ".mkd", ".mdx"];

    public static bool IsFolderCandidate(string path) =>
        FolderExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Markdown files directly inside a folder, sorted by name.
    ///
    /// Not recursive on purpose: a repository picked by mistake would otherwise produce
    /// hundreds of tabs, and each tab costs an editor model.
    /// </summary>
    public static IReadOnlyList<string> EnumerateInFolder(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        return
        [
            .. Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly)
                .Where(IsFolderCandidate)
                .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
        ];
    }
}
