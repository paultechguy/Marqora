// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.InteropServices;

namespace PaulTechGuy.MQ.WebRender.Win32;

/// <summary>
/// The kinds of window a headless WebView2 can be given to belong to. The spike tries each and
/// records which one lays out (docs/CLI-Implementation-Plan.md, §9.1): Chromium stops producing
/// animation frames for a page it believes is hidden, and Paged.js steps its layout on them.
/// </summary>
public enum ParentWindowKind
{
    /// <summary>A message-only window: never visible, never enumerated, never on any screen.</summary>
    MessageOnly,

    /// <summary>An ordinary popup tool window that is created and never shown.</summary>
    HiddenPopup,

    /// <summary>
    /// The same popup, shown without activation far off the desktop and cloaked, so the
    /// compositor draws nothing and it never takes focus or a taskbar button.
    /// </summary>
    CloakedPopup,
}

/// <summary>A window for WebView2 controllers to belong to. Created and destroyed on the pump's thread.</summary>
public sealed class ParentWindow : IDisposable
{
    private ParentWindow(nint handle, ParentWindowKind kind)
    {
        Handle = handle;
        Kind = kind;
    }

    public nint Handle { get; }

    public ParentWindowKind Kind { get; }

    public static ParentWindow Create(ParentWindowKind kind, int width, int height)
    {
        nint instance = NativeMethods.GetModuleHandle(null);
        string windowClass = StaPump.WindowClass;

        nint handle = kind switch
        {
            ParentWindowKind.MessageOnly => NativeMethods.CreateWindowEx(
                0, windowClass, "Marqora CLI", 0, 0, 0, width, height, NativeMethods.HWND_MESSAGE, 0, instance, 0),

            _ => NativeMethods.CreateWindowEx(
                NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE,
                windowClass,
                "Marqora CLI",
                NativeMethods.WS_POPUP,
                -32000,
                -32000,
                width,
                height,
                0,
                0,
                instance,
                0),
        };

        if (handle == 0)
        {
            throw new InvalidOperationException($"Could not create the {kind} window ({Marshal.GetLastPInvokeError()}).");
        }

        if (kind == ParentWindowKind.CloakedPopup)
        {
            // Cloaked before it is shown, so there is never a frame in which it is drawn.
            int cloak = 1;
            NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DWMWA_CLOAK, ref cloak, sizeof(int));
            NativeMethods.ShowWindow(handle, NativeMethods.SW_SHOWNOACTIVATE);
        }

        return new ParentWindow(handle, kind);
    }

    public void Dispose() => NativeMethods.DestroyWindow(Handle);
}
