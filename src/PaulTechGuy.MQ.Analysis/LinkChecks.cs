// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Net;
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

            MediaTargetKind kind = MediaTarget.Classify(url);

            // On the web. Nothing is fetched and nothing is verified - that would mean going to
            // the network, which this app never does - but a picture is now told about rather
            // than left as a blank box with no explanation. A link still says nothing: it is a
            // navigation, and clicking one works.
            if (kind == MediaTargetKind.Remote)
            {
                if (request.CheckBlockedImages && link.IsImage)
                {
                    Report(link, LinkFindingKind.RemoteMedia, RemoteMessage(), into);
                }

                continue;
            }

            // mailto:, data:, blob: and the rest. Somebody else's problem, as before.
            if (kind == MediaTargetKind.OtherScheme)
            {
                continue;
            }

            // Relative paths need a folder to be relative to, and an unsaved document has
            // none. Nothing is reported rather than everything being reported.
            if (folder is null || !Directory.Exists(folder))
            {
                continue;
            }

            // "C:\pics\shot.png" and "file:///C:/pics/shot.png". These used to match the scheme
            // test and be waved through, so a picture named this way was blank and silent. Only
            // media is reported: a link written this way stays as quiet as it has always been,
            // because clicking it is a different question from whether a picture appears.
            if (kind == MediaTargetKind.LocalAbsolute)
            {
                if (request.CheckBlockedImages && link.IsImage)
                {
                    ReportLocalAbsolute(link, url, folder, into);
                }

                continue;
            }

            string target = StripSuffix(url);

            if (target.Length == 0)
            {
                continue;
            }

            if (!Exists(folder, target))
            {
                // The file may be perfectly well there and simply not beside the document -
                // "../shared/logo.png" is the everyday shape. Saying "no image at" about a file
                // sitting on the disk sent people looking for something that was never lost.
                if (request.CheckBlockedImages
                    && link.IsImage
                    && OutsideButPresent(folder, target) is { } outside)
                {
                    Report(link, LinkFindingKind.OutsideFolder, OutsideMessage(outside), into);

                    continue;
                }

                Report(
                    link,
                    link.IsImage ? LinkFindingKind.MissingImage : LinkFindingKind.BrokenLink,
                    link.IsImage ? $"No image at \"{url}\"." : $"Nothing at \"{url}\".",
                    into);
            }
        }
    }

    /// <summary>
    /// What to say about a picture addressed on the web.
    ///
    /// The second sentence is not padding. A README carrying build badges gets one of these per
    /// badge, and without it each mark reads as an accusation about a document that is, in fact,
    /// correct - it renders everywhere its readers will see it. Saying so is the difference
    /// between a mark somebody reads and a rule somebody switches off.
    ///
    /// The host is not named, and neither is an example of somewhere it works. Both were there
    /// at first and both were noise: the address is on the line the pointer is resting on, and a
    /// hover that has explained itself in two sentences should stop.
    /// </summary>
    /// <remarks>
    /// Worded without naming the element. The same check covers an iframe and a video, and
    /// "this image will not appear" is simply wrong about those - a small wrongness, but in the
    /// one sentence whose whole job is to be believed.
    /// </remarks>
    private static string RemoteMessage() =>
        "Marqora does not load content from the web, so this will not appear in the preview. "
        + "It will still work anywhere that does.";

    /// <summary>What to say about a file kept somewhere else on this machine.</summary>
    private static string OutsideMessage(string path) =>
        $"This is at \"{path}\", which is outside the document's folder. Marqora only loads files "
        + "kept beside the document.";

    /// <summary>
    /// Reports an absolutely-named local picture, saying which of the two things is true.
    ///
    /// A share is never probed. <c>File.Exists</c> on a UNC path blocks until the other machine
    /// answers, and this runs while somebody is typing - see <see cref="MediaTarget.IsNetworkShare"/>.
    /// What can be said without asking the disk is that it is not beside the document, which is
    /// the part that decides whether it appears.
    /// </summary>
    private static void ReportLocalAbsolute(
        LinkReference link,
        string url,
        string folder,
        List<LinkFinding> into)
    {
        string? path = MediaTarget.LocalPathOf(StripSuffix(url));

        if (path is null)
        {
            Report(link, LinkFindingKind.MissingImage, $"No image at \"{url}\".", into);

            return;
        }

        if (MediaTarget.IsNetworkShare(path))
        {
            Report(link, LinkFindingKind.OutsideFolder, OutsideMessage(path), into);

            return;
        }

        // An absolute path can still land inside the document's own folder, and then there is
        // nothing wrong with it at all beyond the spelling.
        if (PathContainment.Contains(folder, path) && File.Exists(path))
        {
            return;
        }

        if (File.Exists(path))
        {
            Report(link, LinkFindingKind.OutsideFolder, OutsideMessage(path), into);

            return;
        }

        Report(link, LinkFindingKind.MissingImage, $"No image at \"{url}\".", into);
    }

    /// <summary>
    /// The full path of a relative reference that escapes the document's folder but is really
    /// there, or null when it is simply missing.
    /// </summary>
    private static string? OutsideButPresent(string folder, string relative)
    {
        try
        {
            string full = Path.GetFullPath(
                Path.Combine(folder, WebUtility.UrlDecode(relative).Replace('/', Path.DirectorySeparatorChar)));

            if (MediaTarget.IsNetworkShare(full))
            {
                return full;
            }

            return !PathContainment.Contains(folder, full) && File.Exists(full) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a relative reference resolves to a file that is really there, refusing to
    /// look outside the document's own folder the way the exporter does.
    /// </summary>
    private static bool Exists(string folder, string relative)
    {
        // Null when it does not stay inside the document's folder, or is not a path this
        // filesystem can express - which is itself a broken link, but saying so precisely is
        // more use than crashing over it. See PathContainment for why the test is shaped as it is.
        if (PathContainment.ResolveWithin(folder, WebUtility.UrlDecode(relative)) is not { } full)
        {
            return false;
        }

        return File.Exists(full) || Directory.Exists(full);
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
}
