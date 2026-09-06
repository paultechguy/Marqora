// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// Where a document's images live, and what they are called.
///
/// Pure path arithmetic - nothing here touches the disk. The store next door does the writing;
/// this decides the names, so the naming can be tested by handing it strings.
/// </summary>
public static class DocumentAssets
{
    /// <summary>
    /// The longest a minted file name may be before its extension.
    ///
    /// Nothing to do with MAX_PATH, which the folder has already spent most of. It is the point
    /// past which a name has stopped being readable in a link, and a pasted screenshot's name is
    /// only ever read inside one.
    /// </summary>
    private const int MaximumStemLength = 60;

    /// <summary>The stem used when a name slugs away to nothing at all.</summary>
    private const string FallbackStem = "image";

    /// <summary>
    /// Names Windows will not give a file whatever the extension, so a document called "con.md"
    /// does not produce an unwritable folder and a pasted "nul.png" does not vanish.
    /// </summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "con", "prn", "aux", "nul",
        "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
    };

    /// <summary>
    /// The folder pasted images go in, relative to the document's own folder, or the empty
    /// string when they sit directly beside it.
    /// </summary>
    public static string FolderNameFor(string documentPath, ImageFolderMode mode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);

        return mode switch
        {
            ImageFolderMode.SharedImages => "images",
            ImageFolderMode.BesideDocument => string.Empty,
            _ => Slug(Path.GetFileNameWithoutExtension(documentPath)) + ".assets",
        };
    }

    /// <summary>
    /// The reference to write into the markdown: the folder and file joined with forward
    /// slashes and percent-encoded.
    ///
    /// Encoding matters for the folder half, which is named after the user's document and can
    /// contain spaces. A destination in CommonMark ends at the first space, so
    /// "![](My Guide.assets/shot.png)" is not an image at all. The file half is minted by
    /// <see cref="Slug"/> and has no spaces to encode.
    /// </summary>
    public static string RelativeReference(string folderName, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        return string.IsNullOrEmpty(folderName)
            ? Encode(fileName)
            : $"{Encode(folderName)}/{Encode(fileName)}";
    }

    /// <summary>
    /// The reference to use when a file is already sitting where the paste would put it, or null
    /// when it is somewhere else and has to be copied.
    ///
    /// Copying a file into the folder it is already in produces a byte-identical twin under a
    /// numbered name and points the document at the twin - so a picture that was already part of
    /// the project becomes two pictures, and the one the document uses is the copy. Referencing
    /// it where it lies is what the user meant by pasting it into a document that already keeps
    /// its images there.
    ///
    /// Deliberately narrow: the file has to be in that exact folder, not merely somewhere under
    /// the document's tree. A file one level up would need a "../" reference, which the preview
    /// refuses to serve and the link check reports - so those are still copied in.
    ///
    /// The name is used exactly as it is rather than slugged. It belongs to a file that already
    /// exists and renaming is not on offer; encoding handles anything awkward in it.
    /// </summary>
    public static string? ExistingReferenceFor(string documentPath, ImageFolderMode mode, string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return null;
        }

        if (Path.GetDirectoryName(Path.GetFullPath(documentPath)) is not { } documentFolder)
        {
            return null;
        }

        string folderName = FolderNameFor(documentPath, mode);
        string target = folderName.Length == 0
            ? documentFolder
            : Path.Combine(documentFolder, folderName);

        if (Path.GetDirectoryName(Path.GetFullPath(sourcePath)) is not { } sourceFolder
            || !string.Equals(
                Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar),
                sourceFolder.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return RelativeReference(folderName, Path.GetFileName(sourcePath));
    }

    /// <summary>
    /// A file name that is safe on this filesystem, legal in a markdown destination, and still
    /// recognizable as what it came from.
    ///
    /// Lower-cased because a pasted name is not prose and mixed case in a path is a source of
    /// the very case-mismatch the link checker has to suggest corrections for.
    /// </summary>
    public static string Slug(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var builder = new StringBuilder(name.Length);
        bool lastWasDash = false;

        foreach (char c in name)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '.' or '_')
            {
                builder.Append(char.ToLowerInvariant(c));
                lastWasDash = false;
            }
            else if (!lastWasDash && builder.Length > 0)
            {
                // Any run of anything else becomes one dash - spaces, punctuation, an em dash, a
                // letter this filesystem would take but a URL would have to escape.
                //
                // A hyphen counts as one of them rather than as a character to keep, which looks
                // like a distinction without a difference and is not. It used to be kept, and a
                // kept hyphen said "the last thing I wrote was not a dash" - so a run containing
                // one stopped collapsing around it. Windows names a duplicated file
                // "test - Copy.jpg", and that space-hyphen-space came out as "test---copy".
                builder.Append('-');
                lastWasDash = true;
            }
        }

        string slug = builder.ToString().Trim('-', '.');

        if (slug.Length > MaximumStemLength)
        {
            slug = slug[..MaximumStemLength].TrimEnd('-', '.');
        }

        // All-dots is the traversal case: "." and ".." survive the character filter above and
        // are not names at all.
        return slug.Length == 0 || ReservedNames.Contains(slug) ? FallbackStem : slug;
    }

    /// <summary>
    /// The next free name in a folder that already holds <paramref name="existing"/>.
    ///
    /// Numbering runs from the highest already there rather than filling the first gap. Reusing
    /// a deleted file's name would point an old export, another document, or a reader's git
    /// history at a different picture under a name they already know.
    /// </summary>
    public static string NextFreeName(
        string stem,
        string extension,
        IReadOnlyCollection<string> existing)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stem);
        ArgumentNullException.ThrowIfNull(existing);

        var taken = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        string dotted = extension.StartsWith('.') ? extension : "." + extension;

        // A name the user brought with them is used as it stands when nothing else has it, so a
        // file copied in keeps the name it had in Explorer.
        if (!taken.Contains(stem + dotted))
        {
            return stem + dotted;
        }

        int highest = 0;

        foreach (string name in taken)
        {
            if (!name.EndsWith(dotted, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string bare = name[..^dotted.Length];

            if (!bare.StartsWith(stem + "-", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (int.TryParse(bare[(stem.Length + 1)..], out int number) && number > highest)
            {
                highest = number;
            }
        }

        return $"{stem}-{highest + 1}{dotted}";
    }

    /// <summary>
    /// Percent-encodes one path segment, then puts back the characters a markdown destination
    /// reads perfectly well and which are far more legible left alone.
    /// </summary>
    private static string Encode(string segment) =>
        Uri.EscapeDataString(segment)
            .Replace("%2E", ".", StringComparison.Ordinal)
            .Replace("%2D", "-", StringComparison.Ordinal)
            .Replace("%5F", "_", StringComparison.Ordinal);
}
