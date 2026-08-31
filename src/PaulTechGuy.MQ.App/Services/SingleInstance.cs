// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.InteropServices;
using Microsoft.Windows.AppLifecycle;
using PaulTechGuy.MQ.Abstractions.Services;
using Serilog;
using Windows.ApplicationModel.Activation;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// Keeps Marqora to one process, so that double-clicking a second markdown file adds a tab
/// to the window that is already open rather than starting another copy of the app.
///
/// The Windows App SDK does the work: the first launch registers a key, later launches find
/// that the key belongs to someone else and redirect their activation to it. None of this
/// requires the app to be packaged.
///
/// It all runs before <c>Application.Start</c>, on the main STA thread and before there is a
/// message pump, which is where the deadlock lives - see <see cref="Redirect"/>.
/// </summary>
internal static class SingleInstance
{
    /// <summary>Command-line switch that opts a launch out of redirection entirely.</summary>
    public const string NewInstanceSwitch = "--new-instance";

    private const string NewInstanceShortSwitch = "-n";

    /// <summary>
    /// Identifies the instance that owns the window. Any string works as long as it is stable
    /// across launches; the platform scopes it to the user and the application.
    /// </summary>
    private const string InstanceKey = "PaulTechGuy.Marqora.Main";

    /// <summary>CWMO_DEFAULT: keep dispatching COM calls while waiting, and nothing else.</summary>
    private const uint CoWaitDefault = 0;

    /// <summary>
    /// Long enough for a busy instance to answer, short enough that a wedged one does not
    /// leave the user looking at an empty desktop. On expiry this launch opens its own window,
    /// which is the behaviour Marqora had before any of this existed.
    /// </summary>
    private const uint RedirectTimeoutMs = 10_000;

    /// <summary>True when the command line asks for a separate process.</summary>
    public static bool WantsNewInstance(IEnumerable<string> args) =>
        args.Any(arg =>
            string.Equals(arg, NewInstanceSwitch, StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, NewInstanceShortSwitch, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Claims the instance key, or hands this launch over to whoever already holds it.
    ///
    /// Returns true when the files were passed on and this process should exit. Returns false
    /// when this process is the one instance, in which case later launches arrive on
    /// <paramref name="router"/>.
    /// </summary>
    public static bool TryHandOff(ActivationRouter router)
    {
        // Read the activation before registering: registering is what allows a later launch
        // to redirect here, and there is nothing left to hand over once that has happened.
        AppActivationArguments? activation = CurrentActivation();

        AppInstance keyed = AppInstance.FindOrRegisterForKey(InstanceKey);

        if (keyed.IsCurrent)
        {
            AppInstance.GetCurrent().Activated += (_, args) => Deliver(router, args);
            return false;
        }

        if (activation is null)
        {
            // With no arguments there is nothing to redirect. A second window is a poor
            // outcome, but a better one than swallowing the file the user asked for.
            Log.Warning("Another instance holds the key, but this launch has no activation to hand over.");
            return false;
        }

        Log.Information("Marqora is already running as process {ProcessId}; handing over.", keyed.ProcessId);

        return Redirect(keyed, activation);
    }

    /// <summary>
    /// Arguments that name a file this process can open.
    ///
    /// Switches are skipped, and so is the executable: an activation carries the whole
    /// command line, argv[0] included, and that is a file that exists.
    ///
    /// Only markdown extensions pass, matching the window's drop handler: dropping a
    /// selection of desktop icons onto a Marqora shortcut launches the app with every
    /// selected file as an argument, and without the filter each one became a tab.
    /// </summary>
    public static IReadOnlyList<string> FilesFrom(IEnumerable<string> args) =>
        [.. args.Where(IsOpenable)];

    // ----------------------------------------------------------------- receiving

    private static void Deliver(ActivationRouter router, AppActivationArguments args)
    {
        IReadOnlyList<string> paths = PathsFrom(args);

        Log.Information(
            "A second launch redirected here ({Kind}) with {Count} file(s).",
            args.Kind,
            paths.Count);

        // Posted even when empty: the user clicked something, so the window should come
        // forward whether or not the payload turned out to name a file.
        router.Post(paths);
    }

    private static IReadOnlyList<string> PathsFrom(AppActivationArguments args)
    {
        switch (args.Data)
        {
            // Registered file-type activation hands the items over directly.
            case IFileActivatedEventArgs file:
                return [.. file.Files.Select(item => item.Path).Where(IsOpenable)];

            // A shell association on an unpackaged app produces a plain launch instead, and
            // its payload is the raw command line.
            case ILaunchActivatedEventArgs launch:
                Log.Debug("Redirected command line: {CommandLine}", launch.Arguments);
                return FilesFrom(SplitCommandLine(launch.Arguments));

            default:
                Log.Warning("Redirected activation carried no arguments this app understands.");
                return [];
        }
    }

    private static AppActivationArguments? CurrentActivation()
    {
        try
        {
            return AppInstance.GetCurrent().GetActivatedEventArgs();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            Log.Warning(ex, "Could not read the activation arguments for this launch.");
            return null;
        }
    }

    // --------------------------------------------------------------- redirecting

    private static bool Redirect(AppInstance target, AppActivationArguments activation)
    {
        // Foreground rights belong to the process the user last interacted with, which is
        // this one. The instance being redirected to cannot raise its window until they have
        // been handed over.
        _ = AllowSetForegroundWindow(target.ProcessId);

        Task<bool> redirect = Task.Run(async () =>
        {
            try
            {
                await target.RedirectActivationToAsync(activation);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Handing the activation to process {ProcessId} failed.", target.ProcessId);
                return false;
            }
        });

        // The redirect is a cross-process COM call, and this is an STA thread with no message
        // pump yet: a plain Wait() would block the apartment that call has to return through,
        // hanging the launch. CoWaitForMultipleObjects keeps COM dispatching while it waits.
        WaitHandle finished = ((IAsyncResult)redirect).AsyncWaitHandle;

        int hr = CoWaitForMultipleObjects(
            CoWaitDefault,
            RedirectTimeoutMs,
            1,
            [finished.SafeWaitHandle.DangerousGetHandle()],
            out _);

        // The raw handle above is only valid while the wait handle is alive, and nothing else
        // keeps it rooted for the duration of the call.
        GC.KeepAlive(finished);

        if (hr != 0)
        {
            Log.Warning("Waiting for the redirect ended with 0x{Result:X8}.", hr);
        }

        return redirect.IsCompletedSuccessfully && redirect.Result;
    }

    // ----------------------------------------------------------------- arguments

    private static bool IsOpenable(string arg) =>
        !string.IsNullOrWhiteSpace(arg)
        && !arg.StartsWith('-')
        && !arg.StartsWith('/')
        && MarkdownFileTypes.IsSupported(arg)
        && File.Exists(arg)
        && !IsThisExecutable(arg);

    private static bool IsThisExecutable(string arg) =>
        Environment.ProcessPath is { } exe
        && string.Equals(Path.GetFullPath(arg), exe, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Splits a command line the way the shell built it. Naive splitting on spaces would
    /// break every path that contains one, which on Windows is most of them.
    /// </summary>
    private static List<string> SplitCommandLine(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return [];
        }

        IntPtr argv = CommandLineToArgv(commandLine, out int count);

        if (argv == IntPtr.Zero)
        {
            Log.Warning("Could not parse the redirected command line.");
            return [];
        }

        try
        {
            var arguments = new List<string>(count);

            for (int i = 0; i < count; i++)
            {
                if (Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)) is { } argument)
                {
                    arguments.Add(argument);
                }
            }

            return arguments;
        }
        finally
        {
            _ = LocalFree(argv);
        }
    }

    // ------------------------------------------------------------------- interop

    /// <summary>
    /// DllImport rather than the source-generated LibraryImport, matching the rest of the app:
    /// the generator emits unsafe marshalling code, which would mean enabling AllowUnsafeBlocks
    /// across the project for calls that pass nothing but handles and a string.
    /// </summary>
    [DllImport("ole32.dll")]
    private static extern int CoWaitForMultipleObjects(
        uint dwFlags,
        uint dwTimeout,
        uint cHandles,
        IntPtr[] pHandles,
        out uint lpdwindex);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "CommandLineToArgvW")]
    private static extern IntPtr CommandLineToArgv(string lpCmdLine, out int pNumArgs);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
