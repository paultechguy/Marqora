// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Domain;
using Windows.System;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// Hands a web address to whatever browser Windows is set to use.
///
/// Marqora itself still makes no network calls: this launches the shell, the same way the
/// About box launches Explorer for a folder, and nothing is fetched unless the reader asks
/// for it.
///
/// Static rather than an injected service because it holds no state and needs nothing from
/// the container. It exists so that the places offering a link agree on what a usable URL
/// is and on what happens when the launch does not take.
/// </summary>
internal static class ExternalLink
{
    /// <summary>
    /// Opens the URL, returning false when it could not be opened.
    ///
    /// The result matters. A shell that declines to launch reports it by returning false
    /// rather than by throwing, so a caller that awaits this and ignores the answer cannot
    /// tell a working link from a dead one - which is exactly what every hand-rolled launch
    /// in the app did before this.
    ///
    /// Anything that is not an absolute http(s) address is refused rather than passed on.
    /// Launching an arbitrary URI hands it to whichever application claims that scheme, and
    /// none of the links Marqora offers has any business doing that.
    /// </summary>
    public static async Task<bool> OpenAsync(string? url, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (!ProjectLinks.IsUsable(url))
        {
            logger.LogWarning("Refused to open '{Url}': not an absolute http(s) address.", url);
            return false;
        }

        try
        {
            return await Launcher.LaunchUriAsync(new Uri(url!));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not open {Url}.", url);
            return false;
        }
    }
}
