// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.RegularExpressions;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Folio;

/// <summary>What came out of a Folio, and anything that would not.</summary>
public sealed record FolioUnpackResult
{
    /// <summary>The documents written, in the order the Folio held them, by absolute path.</summary>
    public required IReadOnlyList<string> Documents { get; init; }

    public required int Images { get; init; }

    /// <summary>
    /// Entries that were refused or could not be written, in the author's terms. A Folio that
    /// unpacks nine documents out of ten says so rather than looking like it worked.
    /// </summary>
    public required IReadOnlyList<string> Refused { get; init; }
}

/// <summary>
/// Takes a reading copy apart again: the markdown back out of the payload, the images back out
/// of the data URIs they are already sitting in.
///
/// <b>This is the only untrusted input Marqora reads.</b> Everything else in the app comes off
/// the user's own disk; a Folio arrives by email, from someone who may not be the person opening
/// it, and every path in it was written by them. So nothing here trusts an entry name: each is
/// resolved against the target folder and refused if it does not stay inside, each image's bytes
/// decide its own extension rather than its name doing so, and nothing is ever written over
/// something already there.
///
/// The images are read out of the page rather than the payload because that is where they
/// already are - embedded once as data URIs for the reader, and carrying a
/// <c>data-mq-asset</c> attribute saying where each belongs. Repeating them in the payload would
/// have added a third again to a file that is mostly pictures.
/// </summary>
public static partial class FolioUnpacker
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Writes a Folio's documents and images into <paramref name="targetFolder"/>, which must be
    /// missing or empty.
    /// </summary>
    /// <param name="html">The Folio's markup, as read from the file.</param>
    /// <param name="payload">Its decoded payload, from <see cref="FolioPayload.TryDecode"/>.</param>
    public static FolioUnpackResult Unpack(string html, FolioPayload payload, string targetFolder)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFolder);

        string root = Path.GetFullPath(targetFolder);

        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new IOException($"{root} already has something in it.");
        }

        Directory.CreateDirectory(root);

        List<string> documents = [];
        List<string> refused = [];

        foreach (FolioPayloadDocument document in payload.Documents)
        {
            if (Destination(root, document.Entry) is not { } path)
            {
                refused.Add($"\"{document.Entry}\" does not stay inside the folder.");
                continue;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, document.Text, Utf8NoBom);

                documents.Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                refused.Add($"\"{document.Entry}\" could not be written: {ex.Message}");
            }
        }

        int images = WriteImages(html, root, refused);

        return new FolioUnpackResult
        {
            Documents = documents,
            Images = images,
            Refused = refused,
        };
    }

    /// <summary>
    /// Every embedded image written back to where its document expects it.
    ///
    /// Keyed on the attribute rather than on the payload's asset list, because the attribute is
    /// attached to the bytes: an image the writer could not embed simply has no attribute, and
    /// so is quietly absent rather than being written empty.
    /// </summary>
    private static int WriteImages(string html, string root, List<string> refused)
    {
        HashSet<string> written = new(StringComparer.OrdinalIgnoreCase);
        int count = 0;

        foreach (Match match in EmbeddedAsset().Matches(html))
        {
            string entry = match.Groups["entry"].Value;

            // The same picture can be used by several documents and so appear more than once.
            if (!written.Add(entry))
            {
                continue;
            }

            if (Destination(root, entry) is not { } path)
            {
                refused.Add($"\"{entry}\" does not stay inside the folder.");
                continue;
            }

            byte[] bytes;

            try
            {
                bytes = Convert.FromBase64String(match.Groups["data"].Value);
            }
            catch (FormatException)
            {
                refused.Add($"\"{entry}\" is not readable and was left out.");
                continue;
            }

            // The bytes decide what this is, not the name it arrived under - the same boundary
            // the asset store draws for a pasted image, and for the same reason: a Folio is a
            // file somebody sent, and a name is a claim rather than a promise.
            if (ImageFileTypes.ExtensionFor(bytes) is null)
            {
                refused.Add($"\"{entry}\" is not an image and was left out.");
                continue;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                using FileStream file = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);

                file.Write(bytes);
                count++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                refused.Add($"\"{entry}\" could not be written: {ex.Message}");
            }
        }

        return count;
    }

    /// <summary>
    /// Where an entry lands, or null when it would not stay inside the folder.
    ///
    /// Absolute paths, drive letters, "..\" and anything else that climbs out all fail the same
    /// test, because the test is on where the path resolves rather than on how it is spelled.
    /// </summary>
    private static string? Destination(string root, string entry) =>
        string.IsNullOrWhiteSpace(entry) || Path.IsPathRooted(entry)
            ? null
            : PathContainment.ResolveWithin(root, entry);

    /// <summary>
    /// An img carrying both the attribute that names where it belongs and the data URI holding
    /// its bytes, in either order - which is why the two orders are spelled out rather than
    /// matched with something that would also span from one element into the next.
    /// </summary>
    [GeneratedRegex(
        """
        <img\b[^>]*?(?:
            data-mq-asset\s*=\s*"(?<entry>[^"]+)"[^>]*?src\s*=\s*"data:[^;"]+;base64,(?<data>[A-Za-z0-9+/=]+)"
          | src\s*=\s*"data:[^;"]+;base64,(?<data>[A-Za-z0-9+/=]+)"[^>]*?data-mq-asset\s*=\s*"(?<entry>[^"]+)"
        )
        """,
        RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex EmbeddedAsset();
}
