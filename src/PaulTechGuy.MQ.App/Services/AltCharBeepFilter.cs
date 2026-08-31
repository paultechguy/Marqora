// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.InteropServices;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// Silences the system beep that Alt accelerators otherwise cause.
///
/// XAML invokes a KeyboardAccelerator on the key-down, but the message loop still runs
/// that key-down through TranslateMessage, which emits a WM_SYSCHAR for the Alt+character
/// pair. Nothing handles it - the accelerator's Handled flag never reaches this layer -
/// so it falls to DefWindowProc, which answers any Alt+character it does not recognize
/// with MessageBeep. That is the beep heard on Alt+1/2/3 even as the view switches.
///
/// The filter is a WH_GETMESSAGE hook on the UI thread: it blanks WM_SYSCHAR to WM_NULL
/// before dispatch, for every window this thread owns, whichever child of the XAML tree
/// holds the keyboard. Alt+Space is exempt - that character is how the system menu opens.
/// </summary>
internal sealed class AltCharBeepFilter : IDisposable
{
    private const int WhGetMessage = 3;
    private const int WmSysChar = 0x0106;
    private const int WmNull = 0x0000;
    private const int PmRemove = 0x0001;
    private const int VkSpace = 0x20;

    // The delegate is held in a field for as long as the hook lives: the native side keeps
    // only the raw pointer, and a collected delegate would be a crash on the next keypress.
    private readonly HookProc _hookProc;
    private IntPtr _hook;

    private AltCharBeepFilter()
    {
        _hookProc = OnMessageRetrieved;
        _hook = SetWindowsHookExW(WhGetMessage, _hookProc, IntPtr.Zero, GetCurrentThreadId());
    }

    /// <summary>Installs the filter on the calling thread, which must be the UI thread.</summary>
    public static AltCharBeepFilter Install() => new();

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            _ = UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr OnMessageRetrieved(int code, IntPtr wParam, IntPtr lParam)
    {
        // Peeked messages (wParam != PM_REMOVE) are seen again on the real retrieval, so
        // only the retrieval that will actually dispatch is rewritten.
        if (code >= 0 && wParam == PmRemove)
        {
            Msg message = Marshal.PtrToStructure<Msg>(lParam);

            if (message.Message == WmSysChar && message.WParam != VkSpace)
            {
                message.Message = WmNull;
                Marshal.StructureToPtr(message, lParam, fDeleteOld: false);
            }
        }

        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int PointX;
        public int PointY;
    }

    // DllImport rather than LibraryImport, matching StartupModifiers and SingleInstance:
    // the source generator cannot marshal the hook delegate, and these are four calls.
    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowsHookExW(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
