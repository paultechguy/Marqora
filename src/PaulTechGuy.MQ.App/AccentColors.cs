// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Windows.UI;

namespace PaulTechGuy.MQ.App;

/// <summary>
/// Marqora's teal, and the only place either shade of it is written down.
///
/// Not to be confused with the Windows accent. That one is the user's, and it is what every
/// button that commits, the active tab and the title bar wear - App.xaml says why at length.
/// This is the document's color: the one the preview draws a link, a note callout and a table
/// header in, and it is the app's own on purpose, because a page has to look the same in the
/// preview, in an exported HTML file and on paper, where nobody's Windows accent applies.
///
/// It is read in four places - the preview stylesheet, the cheatsheet, the row a navigable
/// list selects, and the tint Find All puts behind a match in light mode - and until this
/// class existed it was written out in all four, in two notations and three slightly
/// different teals. The row that started this was the odd one out: it wore the Windows accent
/// because the tab strip beside it does, and turned up purple next to a teal preview.
///
/// The shades are a pair, not one color and a tint of it: #3f8f98 is dark enough to read as
/// text on a white page, and on a dark one it is nearly invisible, so dark mode lifts it to
/// #7fcdd5. Changing one means looking at the other.
///
/// To change the color, change the two constants below and nothing else. The webshell is
/// posted these by SetThemeAsync and names no teal of its own, exactly as it does not name
/// the match colors - see <see cref="MatchColors"/>, which is the same bargain.
/// </summary>
internal static class AccentColors
{
    /// <summary>The teal on a light page. #rrggbb, or #rrggbbaa to let what is behind it through.</summary>
    public const string LightHex = "#3f8f98";

    /// <summary>The teal on a dark one, lifted far enough to still be a color rather than a shadow.</summary>
    public const string DarkHex = "#7fcdd5";

    public static Color Light => HexColor.Parse(LightHex, nameof(AccentColors));

    public static Color Dark => HexColor.Parse(DarkHex, nameof(AccentColors));

    /// <summary>The shade for a theme. The effective theme, never the requested one.</summary>
    public static Color For(AppTheme theme) => theme == AppTheme.Dark ? Dark : Light;

    /// <summary>The hex for a theme, for the side of the app that reads colors as strings.</summary>
    public static string HexFor(AppTheme theme) => theme == AppTheme.Dark ? DarkHex : LightHex;

    /// <summary>
    /// The same teal at an alpha, for the places that tint rather than paint - a match
    /// highlight with the line still legible through it.
    /// </summary>
    public static Color Tint(AppTheme theme, byte alpha)
    {
        Color color = For(theme);

        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }
}
