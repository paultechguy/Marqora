// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// The Windows display scale a window is at, where 1.5 is 150%. A size meant for 100% is
/// multiplied by this before it goes to AppWindow, which works in physical pixels.
/// </summary>
internal static class DisplayScale
{
    /// <summary>The DPI Windows calls 100%.</summary>
    private const double DefaultDpi = 96.0;

    /// <summary>The scale of <paramref name="handle"/>'s display, or 1 if Windows cannot say.</summary>
    public static double Of(IntPtr handle)
    {
        uint dpi = GetDpiForWindow(handle);

        return dpi > 0 ? dpi / DefaultDpi : 1.0;
    }

    /// <summary>
    /// DllImport rather than the source-generated LibraryImport: the generator emits unsafe
    /// code, and this project does not allow it.
    /// </summary>
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);
}
