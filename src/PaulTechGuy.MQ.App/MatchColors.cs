// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Windows.UI;

namespace PaulTechGuy.MQ.App;

/// <summary>
/// The two colors a match is drawn in, and the only place either one is written down.
///
/// Marqora points at a match in three places - the source pane's selection, a result picked
/// in the Find All window, and the source pane's own Find - and two of those live in a
/// WebView while the third is WinUI. Rather than the same color written twice in two
/// notations, it is written once here, as hex, and each side converts: WinUI through
/// <see cref="Background"/> and <see cref="Foreground"/>, the web side by
/// <c>WebViewPreviewHost.SetThemeAsync</c>, which posts these strings to the shell. app.css
/// does not name them at all; app.js puts what it is given into --mq-selection and
/// --mq-selection-text, so stylesheet rules can use them too.
///
/// Dark only. Light mode's tint is a translucent accent that has never needed lifting and is
/// used in one place, where it is written.
///
/// To change the colors, change the two constants below and nothing else.
/// </summary>
internal static class MatchColors
{
    /// <summary>The color behind a match. #rrggbb, or #rrggbbaa to let the text show through.</summary>
    public const string BackgroundHex = "#75b1ff";

    /// <summary>
    /// The text on top of it. An opaque background needs one: the editor and the results
    /// list both draw their ordinary near-white, which on a light tint is barely there.
    /// </summary>
    public const string ForegroundHex = "#000000";

    public static Color Background => HexColor.Parse(BackgroundHex, nameof(MatchColors));

    public static Color Foreground => HexColor.Parse(ForegroundHex, nameof(MatchColors));
}
