// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// The arithmetic of a window's first opening, kept apart from the window so it can be tested.
///
/// A window's stated default is a design size: what it needs at 100% display scale. Windows
/// positions windows in physical pixels while their content grows with the scale, so a default
/// used as it stands leaves the content a third less room at 150%. Everything here is in physical
/// pixels except that design size.
/// </summary>
public static class WindowSizing
{
    /// <summary>The size a design size becomes on a display, in physical pixels.</summary>
    /// <param name="designWidth">The width the window wants at 100%.</param>
    /// <param name="designHeight">The height the window wants at 100%.</param>
    /// <param name="minimumWidth">The narrowest the window may be, in physical pixels.</param>
    /// <param name="minimumHeight">The shortest the window may be, in physical pixels.</param>
    /// <param name="scale">The display's scale, where 1.5 is 150%.</param>
    /// <param name="workWidth">The usable width of the display.</param>
    /// <param name="workHeight">The usable height of the display.</param>
    /// <returns>
    /// The scaled size, held to the minimum and then to the display. The display wins: a window
    /// larger than its screen has controls nobody can reach.
    /// </returns>
    public static (int Width, int Height) FirstOpen(
        int designWidth,
        int designHeight,
        int minimumWidth,
        int minimumHeight,
        double scale,
        int workWidth,
        int workHeight) =>
        (Fit(designWidth, minimumWidth, scale, workWidth), Fit(designHeight, minimumHeight, scale, workHeight));

    /// <summary>
    /// Where a window of <paramref name="size"/> starts along one axis so that it stays inside
    /// the area, moved no further than it has to be. A window larger than the area keeps its
    /// start on screen, because that is where its caption is.
    /// </summary>
    public static int KeepInside(int position, int size, int areaStart, int areaSize) =>
        Math.Max(areaStart, Math.Min(position, areaStart + areaSize - size));

    private static int Fit(int design, int minimum, double scale, int available) =>
        Math.Min(Math.Max((int)Math.Round(design * scale), minimum), available);
}
