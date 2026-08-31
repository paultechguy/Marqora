// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// Resolves the branding artwork that ships beside the executable.
///
/// Paths are built from the base directory rather than ms-appx: URIs. Marqora is deployed
/// unpackaged, so there is no package to resolve ms-appx against, and an Image whose Source
/// fails to resolve simply draws nothing rather than raising anything a log would catch.
/// </summary>
internal static class AppImages
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Assets");

    /// <summary>
    /// The multi-size icon, used for the window and the taskbar. Windows takes an .ico here
    /// and nothing else, which is why one is built from the logo by build\New-AppIcon.ps1.
    /// </summary>
    public static string IconPath { get; } = Path.Combine(Root, "MarqoraLogo.ico");

    private static readonly string LogoPath = Path.Combine(Root, "MarqoraLogo.png");

    public static bool HasIcon => File.Exists(IconPath);

    /// <summary>
    /// The logo, decoded for the size it will actually be drawn at.
    ///
    /// A bitmap rather than the SVG that used to be here. SvgImageSource is backed by
    /// Direct2D, whose SVG support covers neither CSS class selectors nor text elements, so
    /// an export using either draws the wrong thing with nothing written to the log. The
    /// SVG stays in the repository as the artwork master and the PNG is what ships.
    ///
    /// The size is logical, so the decode follows display scaling: at 200% a 192 here
    /// decodes 384 real pixels rather than stretching 192 across them.
    ///
    /// Returns null when the file is missing, which callers treat as "draw nothing" rather
    /// than failing to start.
    /// </summary>
    public static ImageSource? Logo(int size)
    {
        if (!File.Exists(LogoPath))
        {
            return null;
        }

        // Assigned before the source: decoding begins the moment UriSource is set, and these
        // are only read on the way in.
        var logo = new BitmapImage
        {
            DecodePixelType = DecodePixelType.Logical,
            DecodePixelWidth = size,
            DecodePixelHeight = size,
        };

        logo.UriSource = new Uri(LogoPath);

        return logo;
    }
}
