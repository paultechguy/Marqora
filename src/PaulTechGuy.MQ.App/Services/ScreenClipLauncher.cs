// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Windows.System;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// Opens Windows' own screen-clipping overlay and waits for the result.
///
/// It uses the Snipping Tool rather than drawing a capture UI, which is a smaller app and a
/// better overlay than this one would build: it already does window, region and freeform, it
/// already knows about multiple monitors and scaling, and it is the one the user has muscle
/// memory for.
///
/// There is no input automation anywhere in here. The obvious wrong implementation is to
/// synthesize Win+Shift+S with keybd_event, which would put keystrokes on the desktop and land
/// them in whatever happened to have focus. This launches a protocol and then watches a counter.
/// </summary>
internal static class ScreenClipLauncher
{
    private const string ScreenClipUri = "ms-screenclip:";

    /// <summary>
    /// How long to wait for a clip before giving up.
    ///
    /// Generous, because framing a capture is a human activity and a person who has gone to find
    /// the right window should not come back to a timeout. Nothing is spent while waiting - it
    /// is a counter read on a timer.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Windows' own clipboard change counter.
    ///
    /// The reason this feature can tell a taken clip from a cancelled one at all. LaunchUriAsync
    /// returns as soon as the overlay starts, not when the user finishes with it, and there is no
    /// completion callback to await; window activation is no help either, because on Windows 11
    /// the Snipping Tool usually keeps the foreground after a capture rather than handing it
    /// back. The counter moves when, and only when, something new reaches the clipboard.
    ///
    /// Needs no clipboard open, so it cannot fail the way reading the clipboard can.
    ///
    /// DllImport rather than the source-generated LibraryImport, matching the rest of the app:
    /// the generator emits unsafe marshalling code, which would mean turning AllowUnsafeBlocks
    /// on across the project for a call that takes nothing and returns a number.
    /// </summary>
    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    /// <summary>
    /// Whether this machine can clip at all, asked once so the menu item can be gated.
    ///
    /// Worth asking rather than just launching: the Snipping Tool can be removed, and launching a
    /// protocol nothing handles puts up Windows' own "How do you want to open this?" chooser,
    /// which is a confusing answer to a menu item.
    /// </summary>
    public static async Task<bool> IsAvailableAsync()
    {
        try
        {
            LaunchQuerySupportStatus status = await Launcher.QueryUriSupportAsync(
                new Uri(ScreenClipUri), LaunchQuerySupportType.Uri);

            return status == LaunchQuerySupportStatus.Available;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UriFormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Opens the overlay and waits for a new image to reach the clipboard.
    ///
    /// True when one arrived, so the caller can read it by the ordinary paste path. False for
    /// every other ending: the overlay would not start, the user pressed Escape, the wait ran
    /// out, or what turned up was not an image.
    /// </summary>
    /// <param name="superseded">
    /// Asked on every tick. True ends the wait without a result, which is how a second Screen
    /// Clip retires the first rather than leaving two waits watching one counter.
    ///
    /// A callback rather than a CancellationToken so the caller needs no disposable field for
    /// something that is only ever a flag.
    /// </param>
    public static async Task<bool> CaptureAsync(ILogger logger, Func<bool> superseded)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(superseded);

        // Read before launching. This is the whole defense against the stale-clipboard bug: a
        // cancelled clip leaves whatever was on the clipboard an hour ago, and without a
        // before-and-after the app would cheerfully paste it as though it were the capture.
        uint before = GetClipboardSequenceNumber();

        if (!await Launcher.LaunchUriAsync(new Uri(ScreenClipUri)))
        {
            logger.LogWarning("Windows would not start {Uri}.", ScreenClipUri);

            return false;
        }

        DateTimeOffset deadline = DateTimeOffset.UtcNow + Patience;

        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(PollInterval).ConfigureAwait(true);

            if (superseded())
            {
                return false;
            }

            if (GetClipboardSequenceNumber() == before)
            {
                continue;
            }

            // Something arrived. Whether it is the clip or a URL the user copied while the
            // overlay was up, this attempt is over: waiting on would make a later, unrelated
            // copy look like the capture.
            return true;
        }

        logger.LogDebug("No screen clip arrived within {Seconds} seconds.", Patience.TotalSeconds);

        return false;
    }
}
