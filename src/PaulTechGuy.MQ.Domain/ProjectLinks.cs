// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// Where Marqora lives on the web, in one place.
///
/// Constants rather than configuration: these change when the repository moves, which is to
/// say never, and putting them in a settings file would mean shipping a file a user can
/// break and the app must then validate for no gain. The licence URL is built from the
/// repository URL so the two cannot drift apart.
///
/// Nothing here is fetched. These are handed to the shell when the reader asks for them,
/// which is the only way any URL leaves Marqora.
/// </summary>
public static class ProjectLinks
{
    public const string RepositoryUrl = "https://github.com/paultechguy/Marqora";

    public const string SponsorsUrl = "https://github.com/sponsors/paultechguy";

    public const string LicenceUrl = RepositoryUrl + "/blob/master/LICENSE";

    /// <summary>
    /// True when the string is an absolute http(s) address the shell can be handed.
    ///
    /// Two jobs. It guards the case where one of the constants above is emptied - say the
    /// Sponsors page is withdrawn - so the button that would use it disappears rather than
    /// launching nothing. And it keeps every scheme but http(s) away from the shell, which
    /// matters because handing an arbitrary URI to Launcher is handing it to whatever
    /// application claims that scheme.
    /// </summary>
    public static bool IsUsable(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)
        && parsed.Scheme is "https" or "http";
}
