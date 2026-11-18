// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Folders;

/// <summary>
/// The markdown the folder panel writes for an image (P24, WP-8): Insert Link in Source and the
/// drag both take it from here.
/// </summary>
public static class ImageLinks
{
    /// <summary>
    /// <c>![name](target)</c> for an image in the document's folder or below it, or null for any
    /// other image: the preview serves nothing outside that folder, so such a link would show as
    /// broken (C21). The alt text is the file name without its extension, with its brackets
    /// escaped; the target is relative, with forward slashes, wrapped by <see cref="Target"/>.
    /// </summary>
    public static string? For(string documentFolder, string imagePath)
    {
        ArgumentNullException.ThrowIfNull(documentFolder);
        ArgumentNullException.ThrowIfNull(imagePath);

        if (!FolderPaths.IsUnder(imagePath, documentFolder))
        {
            return null;
        }

        string relative = Path.GetRelativePath(documentFolder, imagePath).Replace('\\', '/');

        string alt = Path.GetFileNameWithoutExtension(imagePath)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);

        return $"![{alt}]({Target(relative)})";
    }

    /// <summary>
    /// Wraps a link target in angle brackets when it needs them.
    ///
    /// A space inside <c>(...)</c> ends the target as far as every markdown parser is concerned,
    /// so "my notes.md" would link to "my" and leave "notes.md)" as text. CommonMark's answer is
    /// the pointy-bracket form, which is more readable than percent-encoding and is what a
    /// person editing the link afterwards would have written themselves.
    /// </summary>
    public static string Target(string? relative) =>
        relative is not null && relative.AsSpan().ContainsAny(' ', '(', ')') ? $"<{relative}>" : relative ?? string.Empty;
}
