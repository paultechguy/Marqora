// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.RegularExpressions;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Analysis;

/// <summary>
/// Checks that links and images actually lead somewhere.
///
/// The commonest defect in a README by a distance: a file renamed, a screenshot moved, a
/// heading retitled and the anchor left behind. None of it shows up in the preview, which
/// renders a dead link exactly like a live one.
///
/// Links inside fenced code blocks never reach here, because the parser does not read them
/// as links in the first place.
///
/// What comes out is a <see cref="LinkFinding"/> rather than a <see cref="Diagnostic"/>, because
/// each one has a specific repair - which heading was meant, which file was meant - and a menu
/// is where a repair belongs. The style rules next door stay diagnostics: they are advisory and
/// the formatter fixes all of them at once.
/// </summary>
internal static partial class LinkChecks
{
    public static void Run(AnalysisRequest request, List<LinkFinding> into)
    {
        string? folder = string.IsNullOrWhiteSpace(request.DocumentPath)
            ? null
            : Path.GetDirectoryName(Path.GetFullPath(request.DocumentPath));

        HashSet<string>? anchors = null;

        foreach (LinkReference link in request.Links)
        {
            string url = link.Url.Trim();

            if (url.Length == 0)
            {
                continue;
            }

            // An anchor into this same document: resolvable without touching the disk, and
            // worth checking even for a document that has never been saved. Both kinds of
            // target count - the one a heading gets for free, and the one an author wrote by
            // hand as raw HTML to name something that is not a heading.
            if (url[0] == '#')
            {
                anchors ??=
                [
                    .. request.Outline.Select(h => h.Slug),
                    .. request.Anchors,
                ];

                if (!anchors.Contains(url[1..], StringComparer.OrdinalIgnoreCase))
                {
                    Report(
                        link,
                        LinkFindingKind.DeadAnchor,
                        $"Nothing in this document is named \"{url}\".",
                        into);
                }

                continue;
            }

            // Anything with a scheme, or protocol-relative, is somebody else's problem:
            // checking it would mean going out to the network, which this app never does.
            if (AbsoluteUrl().IsMatch(url) || url.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            // Relative paths need a folder to be relative to, and an unsaved document has
            // none. Nothing is reported rather than everything being reported.
            if (folder is null || !Directory.Exists(folder))
            {
                continue;
            }

            string target = StripSuffix(url);

            if (target.Length == 0)
            {
                continue;
            }

            if (!Exists(folder, target))
            {
                Report(
                    link,
                    link.IsImage ? LinkFindingKind.MissingImage : LinkFindingKind.BrokenLink,
                    link.IsImage ? $"No image at \"{url}\"." : $"Nothing at \"{url}\".",
                    into);
            }
        }
    }

    /// <summary>
    /// Whether a relative reference resolves to a file that is really there, refusing to
    /// look outside the document's own folder the way the exporter does.
    /// </summary>
    private static bool Exists(string folder, string relative)
    {
        try
        {
            string decoded = WebUtility.UrlDecode(relative).Replace('/', Path.DirectorySeparatorChar);
            string root = Path.GetFullPath(folder + Path.DirectorySeparatorChar);
            string full = Path.GetFullPath(Path.Combine(root, decoded));

            // The trailing separator on the root is what stops a sibling folder passing the
            // prefix test: without it "C:\docs2\logo.png" starts with "C:\docs" and is read as
            // being inside it. Comparing the target with a separator appended keeps the folder
            // itself contained, which is what a link written as "." resolves to.
            if (!(full + Path.DirectorySeparatorChar).StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return File.Exists(full) || Directory.Exists(full);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Not a path this filesystem can express, which is itself a broken link, but
            // saying so precisely is more use than crashing over it.
            return false;
        }
    }

    /// <summary>Drops any query string or fragment, which are not part of the file name.</summary>
    private static string StripSuffix(string url)
    {
        int cut = url.IndexOfAny(['#', '?']);

        return cut < 0 ? url : url[..cut];
    }

    private static void Report(
        LinkReference link,
        LinkFindingKind kind,
        string message,
        List<LinkFinding> into) =>
        into.Add(new LinkFinding
        {
            Line = link.SourceLine,
            Start = link.SourceColumn,
            Length = Math.Max(1, link.Length),
            Url = link.Url.Trim(),
            Kind = kind,
            Message = message,
        });

    /// <summary>A scheme followed by a colon: http:, https:, mailto:, ftp:, data: and so on.</summary>
    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.\-]*:", RegexOptions.CultureInvariant)]
    private static partial Regex AbsoluteUrl();
}
