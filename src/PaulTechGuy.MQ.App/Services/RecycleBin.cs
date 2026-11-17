// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Folders;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// Sends files to the Recycle Bin through the shell (P7).
///
/// <c>SHFileOperationW</c> rather than <c>IFileOperation</c>: it is one call, it needs no COM
/// object kept alive, and the flags say everything that matters. It runs on a thread of its own
/// in a single-threaded apartment, which the shell's file operations require, so the UI thread
/// never waits on a slow drive.
///
/// The flags ask for no confirmation and no error dialog: the panel has already asked, and it
/// reports a failure on its own notice line. <c>FOF_WANTNUKEWARNING</c> is the exception, and
/// on purpose: if the shell finds it cannot recycle after all, when <see cref="Forecast"/>
/// thought it could, it asks before destroying anything rather than doing it silently.
/// </summary>
public sealed class RecycleBin(ILogger<RecycleBin> logger) : IRecycleBin
{
    private const uint FoDelete = 0x0003;
    private const ushort FofSilent = 0x0004;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofAllowUndo = 0x0040;
    private const ushort FofNoErrorUi = 0x0400;
    private const ushort FofWantNukeWarning = 0x4000;

    private const string BitBucketVolumes = @"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume";

    public RecycleForecast Forecast(string path)
    {
        (int count, long bytes, int readOnly) = Measure(path);
        VolumeFacts facts = FactsFor(path);

        return new RecycleForecast(RecyclePolicy.WillRecycle(facts, bytes), count, bytes, readOnly);
    }

    public Task<RecycleResult> RecycleAsync(string path, CancellationToken cancellationToken = default)
    {
        bool permanent = !Forecast(path).WillRecycle;
        var done = new TaskCompletionSource<RecycleResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                done.TrySetResult(Delete(path, permanent));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The shell failed while deleting {Path}.", path);
                done.TrySetResult(RecycleResult.Failed($"\"{Path.GetFileName(path)}\" could not be deleted."));
            }
        })
        {
            IsBackground = true,
            Name = "Recycle Bin",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return done.Task;
    }

    private RecycleResult Delete(string path, bool permanent)
    {
        var operation = new ShFileOpStruct
        {
            hwnd = IntPtr.Zero,
            wFunc = FoDelete,

            // The shell reads a list of paths, each ended by a null, the list ended by another.
            pFrom = path + "\0\0",
            pTo = null,
            fFlags = (ushort)(FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi | FofWantNukeWarning),
        };

        int result = SHFileOperation(ref operation);

        if (operation.fAnyOperationsAborted)
        {
            logger.LogInformation("The delete of {Path} was canceled at the shell's warning.", path);
            return RecycleResult.Failed($"\"{Path.GetFileName(path)}\" was not deleted.");
        }

        if (result != 0)
        {
            logger.LogWarning("SHFileOperation returned 0x{Result:X} deleting {Path}.", result, path);
            return RecycleResult.Failed($"\"{Path.GetFileName(path)}\" could not be deleted (it may be open in another program).");
        }

        return new RecycleResult(true, permanent, null);
    }

    /// <summary>
    /// How many files are going, how big they are, and how many carry the read-only box from
    /// their Windows properties, for the confirmation and the size test. One walk answers all three.
    /// </summary>
    private static (int Count, long Bytes, int ReadOnly) Measure(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var single = new FileInfo(path);
                return (1, single.Length, single.IsReadOnly ? 1 : 0);
            }

            int count = 0;
            long bytes = 0;
            int readOnly = 0;

            foreach (FileInfo file in new DirectoryInfo(path).EnumerateFiles(
                "*",
                new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 }))
            {
                count++;
                bytes += file.Length;

                if (file.IsReadOnly)
                {
                    readOnly++;
                }
            }

            return (count, bytes, readOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (0, 0, 0);
        }
    }

    /// <summary>
    /// The drive's kind, and its Recycle Bin settings from the per-volume BitBucket key. A setting
    /// that cannot be read is left null, which the policy reads as the shell's default.
    /// </summary>
    private VolumeFacts FactsFor(string path)
    {
        string? root = Path.GetPathRoot(Path.GetFullPath(path));

        if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return new VolumeFacts(DriveType.Network, null, null);
        }

        DriveType drive;

        try
        {
            drive = new DriveInfo(root).DriveType;
        }
        catch (ArgumentException)
        {
            return new VolumeFacts(DriveType.Unknown, null, null);
        }

        if (drive != DriveType.Fixed || VolumeGuid(root) is not { } guid)
        {
            return new VolumeFacts(drive, null, null);
        }

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey($@"{BitBucketVolumes}\{guid}");

            bool? nuke = key?.GetValue("NukeOnDelete") is int n ? n != 0 : null;
            long? capacity = key?.GetValue("MaxCapacity") is int c ? c : null;

            return new VolumeFacts(drive, nuke, capacity);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            logger.LogDebug(ex, "Could not read the Recycle Bin settings for {Root}.", root);
            return new VolumeFacts(drive, null, null);
        }
    }

    /// <summary>The volume's GUID, braces included, as the BitBucket key names it.</summary>
    private static string? VolumeGuid(string root)
    {
        char[] name = new char[64];

        if (!GetVolumeNameForVolumeMountPoint(root, name, (uint)name.Length))
        {
            return null;
        }

        // \\?\Volume{xxxxxxxx-...}\
        string text = new string(name).TrimEnd('\0');
        int open = text.IndexOf('{', StringComparison.Ordinal);
        int close = text.IndexOf('}', StringComparison.Ordinal);

        return open >= 0 && close > open ? text[open..(close + 1)] : null;
    }

    // ------------------------------------------------------------------- interop

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;

        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;

        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    /// <summary>
    /// DllImport rather than the source-generated LibraryImport, matching the rest of the app:
    /// the generator emits unsafe marshalling code, which would mean enabling AllowUnsafeBlocks
    /// across the project for calls that pass nothing but a struct and strings.
    /// </summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation(ref ShFileOpStruct lpFileOp);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetVolumeNameForVolumeMountPointW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPoint(
        string lpszVolumeMountPoint,
        [Out] char[] lpszVolumeName,
        uint cchBufferLength);
}
