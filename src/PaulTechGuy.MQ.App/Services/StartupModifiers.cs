// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.InteropServices;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// Modifier keys held at the moment the process started.
///
/// Read through GetAsyncKeyState rather than anything in WinUI: this is asked before
/// Application.Start, when there is no window, no dispatcher and no input source to ask.
/// </summary>
internal static class StartupModifiers
{
    private const int VirtualKeyShift = 0x10;

    /// <summary>The high bit of GetAsyncKeyState's result: the key is down right now.</summary>
    private const int KeyDown = 0x8000;

    /// <summary>
    /// True when either Shift key is held. The gesture is the old, well-understood one -
    /// hold Shift while starting an application to ask it for something different - and here
    /// it asks for the welcome document, whether or not this version has already shown it.
    /// </summary>
    public static bool IsShiftHeld() => (GetAsyncKeyState(VirtualKeyShift) & KeyDown) != 0;

    /// <summary>
    /// DllImport rather than the source-generated LibraryImport, matching SingleInstance:
    /// the generator emits unsafe marshalling code for what is one call taking an int.
    /// </summary>
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
