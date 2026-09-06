// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// What counts as an image, decided by looking at the bytes.
///
/// This is a security boundary rather than a convenience. Pasting can put a file the app never
/// chose next to a document the user cares about, and a clipboard that claims "image/png" is
/// making a claim, not a promise. Nothing is written unless the bytes say what it is and the
/// answer is on the list - so a renamed .exe, a .lnk or a .ps1 is refused whatever the drop
/// said it was.
/// </summary>
public static class ImageFileTypes
{
    /// <summary>
    /// The formats the preview can actually render, which is the only reason to accept one.
    ///
    /// SVG is here and is safe: an SVG referenced from an img element is script-disabled by
    /// every browser, and the shell's own policy starts from default-src 'none'.
    /// </summary>
    public static IReadOnlySet<string> AllowedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".webp", ".avif", ".bmp", ".svg",
        };

    /// <summary>
    /// The extension these bytes really deserve, or null if they are not an image at all.
    ///
    /// The signature wins over any name or declared type it arrived with.
    /// </summary>
    public static string? ExtensionFor(ReadOnlySpan<byte> bytes)
    {
        if (Starts(bytes, [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return ".png";
        }

        if (Starts(bytes, [0xFF, 0xD8, 0xFF]))
        {
            return ".jpg";
        }

        if (Starts(bytes, "GIF87a"u8) || Starts(bytes, "GIF89a"u8))
        {
            return ".gif";
        }

        if (Starts(bytes, "BM"u8))
        {
            return ".bmp";
        }

        // RIFF....WEBP - the size sits between the two markers, so the tag is checked where it
        // actually is rather than by scanning.
        if (Starts(bytes, "RIFF"u8) && bytes.Length >= 12 && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return ".webp";
        }

        // ....ftypavif - likewise, the box length comes first.
        if (bytes.Length >= 12 && bytes[4..8].SequenceEqual("ftyp"u8)
            && (bytes[8..12].SequenceEqual("avif"u8) || bytes[8..12].SequenceEqual("avis"u8)))
        {
            return ".avif";
        }

        return IsSvg(bytes) ? ".svg" : null;
    }

    /// <summary>Whether a file name's extension is one the app will write.</summary>
    public static bool IsAllowedExtension(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName)
        && AllowedExtensions.Contains(Path.GetExtension(fileName));

    /// <summary>
    /// SVG is text, so it has no signature to match. It is recognized by finding an svg tag
    /// near the front, past whatever declaration, doctype or comment came first.
    /// </summary>
    private static bool IsSvg(ReadOnlySpan<byte> bytes)
    {
        // A byte-order mark would otherwise push the first tag out of reach of the check below.
        if (Starts(bytes, [0xEF, 0xBB, 0xBF]))
        {
            bytes = bytes[3..];
        }

        int length = Math.Min(bytes.Length, 1024);

        if (length == 0 || bytes[0] != (byte)'<')
        {
            return false;
        }

        Span<char> head = stackalloc char[length];

        for (int i = 0; i < length; i++)
        {
            head[i] = char.ToLowerInvariant((char)bytes[i]);
        }

        return head.Contains("<svg", StringComparison.Ordinal);
    }

    private static bool Starts(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> prefix) =>
        bytes.Length >= prefix.Length && bytes[..prefix.Length].SequenceEqual(prefix);
}
