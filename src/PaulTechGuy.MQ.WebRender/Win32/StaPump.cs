// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.InteropServices;

namespace PaulTechGuy.MQ.WebRender.Win32;

/// <summary>
/// A Win32 message loop on the calling thread, and a <see cref="SynchronizationContext"/> that
/// runs posted work inside it.
///
/// WebView2 belongs to the thread that created it, and that thread has to pump messages: the
/// browser talks to its controller through window messages, and an await on one of its
/// operations resumes through the context that was current when it started. A console process
/// has neither, so this supplies both. Work arrives as a message to a window of the pump's own,
/// rather than as a thread message, because a thread message is dropped whenever anything on the
/// thread runs a modal loop of its own.
/// </summary>
public sealed class StaPump : SynchronizationContext
{
    private const string ClassName = "MarqoraCliPump";

    private static ushort _atom;

    private readonly nint _window;

    private StaPump(nint window) => _window = window;

    /// <summary>
    /// The first error thrown by posted work. Work is mostly await continuations, which put
    /// their exceptions in their task; anything that escapes to here ends the loop rather than
    /// the process, and is rethrown from <see cref="Run{T}"/>.
    /// </summary>
    private static Exception? _escaped;

    /// <summary>
    /// Runs <paramref name="work"/> on this thread with the pump as its context, pumping messages
    /// until it finishes, and returns its result. The thread must be STA; a console entry point
    /// marked <c>[STAThread]</c> is.
    /// </summary>
    public static T Run<T>(Func<Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException("The WebView2 pump needs an STA thread.");
        }

        RegisterClass();

        nint window = NativeMethods.CreateWindowEx(
            0, ClassName, string.Empty, 0, 0, 0, 0, 0, NativeMethods.HWND_MESSAGE, 0, NativeMethods.GetModuleHandle(null), 0);

        if (window == 0)
        {
            throw new InvalidOperationException($"Could not create the message window ({Marshal.GetLastPInvokeError()}).");
        }

        SynchronizationContext? previous = Current;
        var pump = new StaPump(window);

        SetSynchronizationContext(pump);

        try
        {
            Task<T> running = work();

            // Whichever way it ends, the loop is told to stop from inside itself.
            running.ContinueWith(_ => NativeMethods.PostQuitMessage(0), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());

            while (NativeMethods.GetMessage(out NativeMethods.Msg message, 0, 0, 0) > 0)
            {
                NativeMethods.TranslateMessage(message);
                NativeMethods.DispatchMessage(message);
            }

            if (_escaped is { } escaped)
            {
                throw new InvalidOperationException("Work posted to the WebView2 pump failed.", escaped);
            }

            return running.GetAwaiter().GetResult();
        }
        finally
        {
            SetSynchronizationContext(previous);
            NativeMethods.DestroyWindow(window);
        }
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        GCHandle handle = GCHandle.Alloc(new WorkItem(d, state));

        // Refused once the loop has ended and its window is gone. What arrives then is the tail
        // of work the run gave up on - a print abandoned at Ctrl+C finishing in its browser's
        // last moments - and there is nowhere left to run it. Throwing here would throw on the
        // thread that completed that work, which is a thread-pool thread, and end the process.
        if (!NativeMethods.PostMessage(_window, NativeMethods.WM_APP_RUN, 0, GCHandle.ToIntPtr(handle)))
        {
            handle.Free();
        }
    }

    public override void Send(SendOrPostCallback d, object? state) =>
        throw new NotSupportedException("The WebView2 pump only takes posted work.");

    public override SynchronizationContext CreateCopy() => this;

    private static unsafe void RegisterClass()
    {
        if (_atom != 0)
        {
            return;
        }

        var windowClass = new NativeMethods.WndClassEx
        {
            Size = (uint)sizeof(NativeMethods.WndClassEx),
            WndProc = &WindowProcedure,
            Instance = NativeMethods.GetModuleHandle(null),

            // Kept for the life of the process: the class is registered once and never removed.
            ClassName = Marshal.StringToHGlobalUni(ClassName),
        };

        _atom = NativeMethods.RegisterClassEx(windowClass);

        if (_atom == 0)
        {
            throw new InvalidOperationException($"Could not register the pump's window class ({Marshal.GetLastPInvokeError()}).");
        }
    }

    /// <summary>The window class shared by the pump's own window and the parent windows.</summary>
    internal static string WindowClass
    {
        get
        {
            RegisterClass();
            return ClassName;
        }
    }

    [UnmanagedCallersOnly]
    private static nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        if (message != NativeMethods.WM_APP_RUN)
        {
            return NativeMethods.DefWindowProc(window, message, wParam, lParam);
        }

        var handle = GCHandle.FromIntPtr(lParam);
        var item = (WorkItem)handle.Target!;

        handle.Free();

        // Nothing may be thrown across this boundary: it would end the process with no word
        // about why. Kept, and the loop ended, so Run can say what happened.
        try
        {
            item.Callback(item.State);
        }
        catch (Exception ex)
        {
            _escaped ??= ex;
            NativeMethods.PostQuitMessage(1);
        }

        return 0;
    }

    private sealed record WorkItem(SendOrPostCallback Callback, object? State);
}
