// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// The layout an exported document takes once it is a page rather than a pane.
///
/// Stated once because both exports need it and they were drifting: each carried its own copy
/// of the same rules, and both capped the text at a 46em measure that the preview itself had
/// already dropped. <c>webshell/app.css</c> records why it went - a centered measure "left a
/// wide band of empty background down both sides of the pane, so widening the preview bought
/// nothing" - and an export that ignores the reader's own width preference makes exactly that
/// complaint again on somebody else's monitor.
///
/// So the width is <see cref="AppSettings.PreviewMaxWidth"/>, the setting the preview already
/// answers to, rather than a number written a third time here. Zero is no limit, which is what
/// the app ships with.
/// </summary>
internal static class ExportLayout
{
    /// <summary>
    /// Side padding echoing the preview's own <c>3em</c>. It is what keeps text off the frame
    /// when there is no measure holding it away, which is the usual case.
    /// </summary>
    private const string BodyPadding = "2.5rem 3rem 4rem";

    /// <summary>
    /// Turns the app's layout rules back into a plain document.
    ///
    /// The preview is normally a pane inside a split view, with a viewport-sized tail for
    /// scroll synchronization; neither belongs in a file somebody opens in a browser.
    /// </summary>
    /// <param name="measurePixels">
    /// The widest the text column gets, or zero for none. Comes from the reader's own
    /// preference, so a Folio and the preview that made it agree about width.
    /// </param>
    public static string PageCss(int measurePixels)
    {
        string measure = measurePixels > 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"  max-width: {measurePixels}px;{Environment.NewLine}  margin: 0 auto;")
            : "  max-width: none;\n  margin: 0;";

        return $$"""
            html, body {
              height: auto;
              overflow: visible;
              background: var(--mq-bg);
            }

            body { padding: {{BodyPadding}}; }

            .mq-preview {
            {{measure}}
              padding: 0;
              font-size: 16px;
            }

            @media print {
              body { padding: 0; }

              .mq-preview { max-width: none; margin: 0; }
            }
            """;
    }
}
