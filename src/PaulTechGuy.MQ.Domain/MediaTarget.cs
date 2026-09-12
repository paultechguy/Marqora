// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;

namespace PaulTechGuy.MQ.Domain;

/// <summary>What kind of place a reference points at.</summary>
public enum MediaTargetKind
{
    /// <summary>A path resolved against the document's own folder. Includes "../x" and "/x".</summary>
    Relative = 0,

    /// <summary>An address on the web: http, https, or protocol-relative "//host/path".</summary>
    Remote = 1,

    /// <summary>
    /// A local file named absolutely: "C:\pics\x.png", "C:/pics/x.png" or a "file:" URL.
    ///
    /// Its own kind because it is the one shape nothing used to see. A drive letter is a single
    /// character followed by a colon, so the old scheme test read "C:" as somebody else's URL and
    /// waved it through - no check, no mark, and a blank picture with nothing to explain it.
    /// </summary>
    LocalAbsolute = 2,

    /// <summary>mailto:, data:, blob: and the rest. Not this app's business.</summary>
    OtherScheme = 3,

    /// <summary>"#section" - a place in this document.</summary>
    Fragment = 4,
}

/// <summary>
/// Where a reference points, decided from the text alone.
///
/// Pure and disk-free on purpose. The question "what kind of target is this" has one answer
/// whether or not the document has ever been saved, and separating it from "is the file there"
/// is what lets the answer be tested by handing it strings.
///
/// <see cref="Classify"/> deliberately keeps "/x.png" and "../x.png" as
/// <see cref="MediaTargetKind.Relative"/>. Both are rooted or climbing, and neither resolves
/// inside the document's folder - but both have always gone down the relative path and been
/// reported as broken, and moving them would change what an existing document says for reasons
/// that have nothing to do with this rule.
/// </summary>
public static partial class MediaTarget
{
    public static MediaTargetKind Classify(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return MediaTargetKind.Relative;
        }

        string trimmed = url.Trim();

        if (trimmed[0] == '#')
        {
            return MediaTargetKind.Fragment;
        }

        // "//host/path" borrows whichever scheme the page was served over, which for the preview
        // would be the shell's own. It is a web address by any other name.
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return MediaTargetKind.Remote;
        }

        if (trimmed.StartsWith("http:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
        {
            return MediaTargetKind.Remote;
        }

        if (trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return MediaTargetKind.LocalAbsolute;
        }

        if (DriveLetter().IsMatch(trimmed))
        {
            return MediaTargetKind.LocalAbsolute;
        }

        // Two characters at least before the colon, which is what tells a scheme from a drive
        // letter. The same reasoning, and the same regex, as FolioPlanner: no scheme worth
        // honoring is a single letter.
        return Scheme().IsMatch(trimmed) ? MediaTargetKind.OtherScheme : MediaTargetKind.Relative;
    }

    /// <summary>
    /// The filesystem path a <see cref="MediaTargetKind.LocalAbsolute"/> reference names, or null
    /// when it is not one or cannot be expressed as a path.
    /// </summary>
    public static string? LocalPathOf(string? url)
    {
        if (Classify(url) != MediaTargetKind.LocalAbsolute)
        {
            return null;
        }

        string trimmed = url!.Trim();

        try
        {
            if (trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) && uri.IsFile
                    ? uri.LocalPath
                    : null;
            }

            return trimmed;
        }
        catch (Exception ex) when (ex is ArgumentException or UriFormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a path names a network share.
    ///
    /// Worth knowing because the check that would otherwise follow is <c>File.Exists</c>, and that
    /// call blocks until the share answers - which for a disconnected VPN or a machine that is
    /// off is measured in seconds. This one runs while somebody is typing, so a share is reported
    /// on what can be known for certain (it is not beside the document) and never probed.
    /// </summary>
    public static bool IsNetworkShare(string? path) =>
        !string.IsNullOrEmpty(path)
        && (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal));

    /// <summary>A Windows drive letter: one letter, a colon, then a separator or nothing.</summary>
    [GeneratedRegex(@"^[a-zA-Z]:([\\/]|$)", RegexOptions.CultureInvariant)]
    private static partial Regex DriveLetter();

    /// <summary>A scheme of two characters or more, followed by a colon.</summary>
    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.\-]+:", RegexOptions.CultureInvariant)]
    private static partial Regex Scheme();
}
