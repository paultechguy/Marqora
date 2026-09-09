// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Folio;

/// <summary>
/// Works out what a Folio will contain before anything is written.
///
/// The rule is the one every media application settled on: preserve structure where it already
/// works, relocate only what has to move. An image sitting beside its document, or in the
/// document's own assets folder, keeps the path the author wrote - renaming a reference that was
/// never broken churns the document for nothing and guarantees the copy differs from the
/// original. An image reached by an absolute path, or from above the document's folder, is the
/// one that breaks the moment the file is sent anywhere, so that is the one that is collected and
/// repointed.
///
/// References are repointed by position rather than by searching the text for them. Every edit is
/// bounded by the span Markdig reported for that construct, so a reference cannot be rewritten
/// inside a fenced code block - the parser never reported the contents of a fence as a link in
/// the first place. Replacing "](old)" throughout the text is simpler and quietly corrupts any
/// document that writes about markdown, of which this repository contains several.
///
/// Touches the filesystem only to read what is there: whether a file exists, how big it is, and
/// its bytes. Substituting that would test nothing, since whether a path resolves is the entire
/// question being asked.
/// </summary>
public static partial class FolioPlanner
{
    /// <summary>Where a reference that had to move ends up, relative to the Folio root.</summary>
    public const string RelocatedFolder = "media";

    /// <summary>
    /// Every document, every image they need, and everything the author should be told, for one
    /// Folio. Nothing is written; the destination has not been named yet.
    /// </summary>
    /// <param name="maxImageWidth">
    /// The width past which an image will be reduced on the way out, or zero to leave every
    /// picture as it is. Only reported here - the planner never touches bytes - but reported
    /// before anything is written, because shrinking is the single lossy thing a Folio does.
    /// </param>
    public static FolioPlan Plan(IReadOnlyList<FolioSource> sources, int maxImageWidth = 0)
    {
        ArgumentNullException.ThrowIfNull(sources);

        if (sources.Count == 0)
        {
            return FolioPlan.Empty;
        }

        // Every name claimed inside the Folio, documents and images alike, so that an image
        // keeping its path can never land on top of something already going there.
        HashSet<string> taken = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> documentEntries = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, FolioAsset> byContent = new(StringComparer.Ordinal);
        HashSet<string> relocatedNames = new(StringComparer.OrdinalIgnoreCase);

        List<FolioAsset> assets = [];
        List<FolioWarning> warnings = [];
        List<FolioDocumentPlan> documents = [];

        // Named first and all together, because a link from the first document to the last has to
        // know what the last one will be called.
        foreach (FolioSource source in sources)
        {
            string full = Path.GetFullPath(source.Path);

            string entry = DocumentAssets.NextFreeName(
                Path.GetFileNameWithoutExtension(full),
                Path.GetExtension(full),
                taken);

            taken.Add(entry);
            documentEntries[full] = entry;
        }

        foreach (FolioSource source in sources)
        {
            string documentFull = Path.GetFullPath(source.Path);
            string folder = Path.GetDirectoryName(documentFull) ?? string.Empty;

            List<Edit> edits = [];

            foreach (LinkReference link in source.Links)
            {
                string url = link.Url.Trim();

                if (url.Length == 0 || url[0] == '#')
                {
                    continue;
                }

                // A scheme, or protocol-relative: somebody else's file, and checking it would
                // mean going to the network, which this app never does.
                if (Scheme().IsMatch(url) || url.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                (string target, string suffix) = SplitSuffix(url);

                if (target.Length == 0)
                {
                    continue;
                }

                string? resolved = Resolve(folder, WebUtility.UrlDecode(target));

                if (resolved is null)
                {
                    continue;
                }

                if (link.IsImage)
                {
                    PlanImage(
                        link, url, suffix, resolved, folder, documentFull, maxImageWidth,
                        taken, byContent, relocatedNames, assets, warnings, edits);
                }
                else if (documentEntries.TryGetValue(resolved, out string? targetEntry))
                {
                    // A link to another document in this Folio. Both are at the root now, so a
                    // reference written from one folder to another has to be flattened with them.
                    Repoint(link, url, targetEntry + suffix, edits);
                }
                else
                {
                    warnings.Add(new FolioWarning
                    {
                        Kind = FolioWarningKind.OutsideLink,
                        DocumentPath = documentFull,
                        Line = link.SourceLine,
                        Url = url,
                        Message = $"\"{url}\" is not in this Folio and will not resolve for the reader.",
                    });
                }
            }

            string text = ApplyEdits(source.Text, edits, documentFull, warnings);

            documents.Add(new FolioDocumentPlan
            {
                SourcePath = documentFull,
                EntryName = documentEntries[documentFull],
                Text = text,
                Rewritten = !string.Equals(text, source.Text, StringComparison.Ordinal),
            });
        }

        return new FolioPlan
        {
            Documents = documents,
            Assets = assets,
            Warnings = warnings,
        };
    }

    private static void PlanImage(
        LinkReference link,
        string url,
        string suffix,
        string resolved,
        string folder,
        string documentFull,
        int maxImageWidth,
        HashSet<string> taken,
        Dictionary<string, FolioAsset> byContent,
        HashSet<string> relocatedNames,
        List<FolioAsset> assets,
        List<FolioWarning> warnings,
        List<Edit> edits)
    {
        if (!File.Exists(resolved))
        {
            warnings.Add(new FolioWarning
            {
                Kind = FolioWarningKind.MissingImage,
                DocumentPath = documentFull,
                Line = link.SourceLine,
                Url = url,
                Message = $"No image at \"{url}\".",
            });

            return;
        }

        string content;
        long size;
        uint width;

        try
        {
            var info = new FileInfo(resolved);
            size = info.Length;

            // One open for both answers. The header is read first and the stream rewound, rather
            // than opening the file twice to learn two things about it.
            using FileStream stream = File.OpenRead(resolved);

            byte[] head = new byte[(int)Math.Min(ImageDimensions.HeaderBytes, Math.Max(size, 0))];
            int read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);

            width = ImageDimensions.Read(head.AsSpan(0, read))?.Width ?? 0;

            stream.Position = 0;
            content = Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add(new FolioWarning
            {
                Kind = FolioWarningKind.MissingImage,
                DocumentPath = documentFull,
                Line = link.SourceLine,
                Url = url,
                Message = $"\"{url}\" could not be read.",
            });

            return;
        }

        // The same picture used by five documents is collected once. Matching on content rather
        // than on path is what catches the copy of a logo that lives in two folders.
        if (byContent.TryGetValue(content, out FolioAsset? already))
        {
            Repoint(link, url, already.EntryName + suffix, edits);
            return;
        }

        string? kept = null;

        if (PathContainment.Contains(folder, resolved))
        {
            string relative = Path.GetRelativePath(folder, resolved).Replace('\\', '/');

            // Free, or it would be overwriting something else that is already going there.
            if (!taken.Contains(relative))
            {
                kept = relative;
            }
        }

        string entry;
        bool relocated;

        if (kept is not null)
        {
            entry = kept;
            relocated = false;
        }
        else
        {
            string name = DocumentAssets.NextFreeName(
                DocumentAssets.Slug(Path.GetFileNameWithoutExtension(resolved)),
                Path.GetExtension(resolved),
                relocatedNames);

            relocatedNames.Add(name);
            entry = RelocatedFolder + "/" + name;
            relocated = true;
        }

        taken.Add(entry);

        var asset = new FolioAsset
        {
            SourcePath = resolved,
            EntryName = entry,
            Bytes = size,
            Relocated = relocated,
            PixelWidth = width,
        };

        assets.Add(asset);
        byContent[content] = asset;

        // Once per image rather than once per reference: this arrives after the content check
        // above, so a picture used by five documents is reported once, as one picture.
        if (maxImageWidth > 0 && width > maxImageWidth)
        {
            warnings.Add(new FolioWarning
            {
                Kind = FolioWarningKind.WillBeShrunk,
                DocumentPath = documentFull,
                Line = link.SourceLine,
                Url = url,
                Message = $"\"{url}\" is {width} px wide and will be reduced to {maxImageWidth} px.",
            });
        }

        if (relocated)
        {
            Repoint(link, url, entry + suffix, edits);
        }
    }

    /// <summary>Queues an edit, unless the reference already says what it needs to say.</summary>
    private static void Repoint(LinkReference link, string url, string replacement, List<Edit> edits)
    {
        if (!string.Equals(url, replacement, StringComparison.Ordinal))
        {
            edits.Add(new Edit(link.SourceLine, link.SourceColumn, link.Length, url, replacement));
        }
    }

    /// <summary>
    /// The document with every repointed reference applied.
    ///
    /// Each edit is looked for only inside the span Markdig reported for its own construct, and
    /// they are applied last-first so that an earlier replacement cannot move a later one.
    /// </summary>
    private static string ApplyEdits(
        string text,
        List<Edit> edits,
        string documentPath,
        List<FolioWarning> warnings)
    {
        if (edits.Count == 0)
        {
            return text;
        }

        int[] lineStarts = LineStarts(text);
        List<(int Start, int Length, string Replacement)> spans = [];

        foreach (Edit edit in edits)
        {
            int found = -1;
            int start = 0;

            if (edit.Line >= 0 && edit.Line < lineStarts.Length)
            {
                start = lineStarts[edit.Line] + edit.Column;

                if (start >= 0 && start <= text.Length)
                {
                    int length = Math.Min(Math.Max(edit.Length, 0), text.Length - start);
                    found = text.AsSpan(start, length).IndexOf(edit.OldUrl.AsSpan(), StringComparison.Ordinal);
                }
            }

            if (found < 0)
            {
                // A reference-style image is the usual cause: Markdig reports the target it
                // resolved to, and that text is in the definition rather than at the link.
                warnings.Add(new FolioWarning
                {
                    Kind = FolioWarningKind.NotRewritable,
                    DocumentPath = documentPath,
                    Line = edit.Line,
                    Url = edit.OldUrl,
                    Message =
                        $"\"{edit.OldUrl}\" has to move, and could not be repointed automatically. "
                        + "The image travels; the reference will need fixing by hand.",
                });

                continue;
            }

            spans.Add((start + found, edit.OldUrl.Length, edit.NewUrl));
        }

        spans.Sort((a, b) => b.Start.CompareTo(a.Start));

        var builder = new StringBuilder(text);
        int lowestApplied = int.MaxValue;

        foreach ((int start, int length, string replacement) in spans)
        {
            // Two constructs cannot overlap, but a malformed span could still claim they do.
            if (start + length > lowestApplied)
            {
                continue;
            }

            builder.Remove(start, length).Insert(start, replacement);
            lowestApplied = start;
        }

        return builder.ToString();
    }

    /// <summary>
    /// The offset each line begins at. CRLF, LF and a bare CR each end one, matching how the
    /// editor counts them, so a file with mixed endings still lands on the line Markdig meant.
    /// </summary>
    private static int[] LineStarts(string text)
    {
        List<int> starts = [0];

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                starts.Add(i + 1);
            }
            else if (c == '\n')
            {
                starts.Add(i + 1);
            }
        }

        return [.. starts];
    }

    /// <summary>Splits a fragment or query off a reference, so it can be put back afterwards.</summary>
    private static (string Target, string Suffix) SplitSuffix(string url)
    {
        int cut = url.IndexOfAny(['#', '?']);

        return cut < 0 ? (url, string.Empty) : (url[..cut], url[cut..]);
    }

    /// <summary>
    /// Where a reference points, or null for a path this filesystem cannot express.
    ///
    /// An absolute reference wins over the folder, which is exactly what is wanted: that is how
    /// an image pasted from somewhere else on the disk is recognized as needing to travel.
    /// </summary>
    private static string? Resolve(string folder, string relative)
    {
        try
        {
            return Path.GetFullPath(
                Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private readonly record struct Edit(int Line, int Column, int Length, string OldUrl, string NewUrl);

    /// <summary>
    /// A scheme followed by a colon: http:, https:, mailto:, data: and so on.
    ///
    /// Two characters at least before the colon, which is the difference between this and the
    /// otherwise identical test in <see cref="AssetRelocation"/>. On Windows a drive letter is a
    /// single character followed by a colon, so "C:/photos/chart.png" matches a one-or-more
    /// pattern and would be waved through as somebody else's URL - and an image referenced by
    /// absolute path is precisely the one a Folio exists to collect. No scheme worth honoring is
    /// a single letter.
    /// </summary>
    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.\-]+:", RegexOptions.CultureInvariant)]
    private static partial Regex Scheme();
}
