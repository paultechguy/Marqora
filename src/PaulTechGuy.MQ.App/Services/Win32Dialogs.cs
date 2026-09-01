// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.InteropServices;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// The Windows common file dialogs, called through their COM interfaces.
///
/// Marqora is an unpackaged desktop app, and the WinRT pickers in Windows.Storage.Pickers
/// do not work reliably in that configuration: PickSingleFolderAsync and its siblings simply
/// never complete, with no exception to catch. These are the underlying Win32 dialogs that
/// every desktop app has always used, and they behave identically packaged or not.
///
/// Only the members Marqora actually calls are given real signatures. The rest are declared
/// to keep the vtable layout correct and are never invoked.
/// </summary>
internal static class Win32Dialogs
{
    private const uint FOS_OVERWRITEPROMPT = 0x00000002;
    private const uint FOS_STRICTFILETYPES = 0x00000004;
    private const uint FOS_PICKFOLDERS = 0x00000020;
    private const uint FOS_FORCEFILESYSTEM = 0x00000040;
    private const uint FOS_PATHMUSTEXIST = 0x00000800;
    private const uint FOS_FILEMUSTEXIST = 0x00001000;

    private const int ERROR_CANCELLED = unchecked((int)0x800704C7);

    /// <summary>Display name form: the full file-system path.</summary>
    private const uint SIGDN_FILESYSPATH = 0x80058000;

    /// <summary>Shows the Open dialog. Returns null when the user cancels.</summary>
    public static string? OpenFile(
        IntPtr owner,
        string title,
        IReadOnlyList<string> extensions,
        string filterLabel = "Markdown")
    {
        var dialog = (IFileOpenDialog)new FileOpenDialogRcw();

        try
        {
            dialog.SetOptions(FOS_FORCEFILESYSTEM | FOS_FILEMUSTEXIST | FOS_PATHMUSTEXIST);
            dialog.SetTitle(title);
            SetFilters(dialog, extensions, includeAllFiles: true, filterLabel);

            return Show(dialog, owner);
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }

    /// <summary>Shows the Save As dialog. Returns null when the user cancels.</summary>
    public static string? SaveFile(
        IntPtr owner,
        string title,
        string? suggestedFileName,
        IReadOnlyList<string> extensions,
        string filterLabel = "Markdown")
    {
        var dialog = (IFileSaveDialog)new FileSaveDialogRcw();

        try
        {
            dialog.SetOptions(FOS_FORCEFILESYSTEM | FOS_OVERWRITEPROMPT | FOS_PATHMUSTEXIST | FOS_STRICTFILETYPES);
            dialog.SetTitle(title);
            SetFilters(dialog, extensions, includeAllFiles: false, filterLabel);

            if (extensions.Count > 0)
            {
                dialog.SetDefaultExtension(extensions[0].TrimStart('.'));
            }

            if (!string.IsNullOrWhiteSpace(suggestedFileName))
            {
                dialog.SetFileName(suggestedFileName);
            }

            return Show(dialog, owner);
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }

    /// <summary>
    /// Shows the folder picker. This is the Open dialog with FOS_PICKFOLDERS, which is how
    /// the modern folder chooser is produced; there is no separate folder dialog class.
    /// </summary>
    public static string? PickFolder(IntPtr owner, string title)
    {
        var dialog = (IFileOpenDialog)new FileOpenDialogRcw();

        try
        {
            dialog.SetOptions(FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
            dialog.SetTitle(title);

            return Show(dialog, owner);
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }

    private static void SetFilters(
        IFileDialog dialog,
        IReadOnlyList<string> extensions,
        bool includeAllFiles,
        string filterLabel = "Markdown")
    {
        if (extensions.Count == 0)
        {
            return;
        }

        string pattern = string.Join(";", extensions.Select(e => "*" + e));

        List<COMDLG_FILTERSPEC> filters =
        [
            new COMDLG_FILTERSPEC { pszName = filterLabel, pszSpec = pattern },
        ];

        if (includeAllFiles)
        {
            filters.Add(new COMDLG_FILTERSPEC { pszName = "All files", pszSpec = "*.*" });
        }

        dialog.SetFileTypes((uint)filters.Count, [.. filters]);
        dialog.SetFileTypeIndex(1);
    }

    /// <summary>Runs the dialog modally and reads the chosen path back.</summary>
    private static string? Show(IFileDialog dialog, IntPtr owner)
    {
        int hr = dialog.Show(owner);

        if (hr == ERROR_CANCELLED)
        {
            return null;
        }

        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        dialog.GetResult(out IShellItem item);

        try
        {
            item.GetDisplayName(SIGDN_FILESYSPATH, out IntPtr buffer);

            try
            {
                return Marshal.PtrToStringUni(buffer);
            }
            finally
            {
                Marshal.FreeCoTaskMem(buffer);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(item);
        }
    }

    // ------------------------------------------------------------------- interop

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct COMDLG_FILTERSPEC
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string pszName;
        [MarshalAs(UnmanagedType.LPWStr)] public string pszSpec;
    }

    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialogRcw;

    [ComImport, Guid("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B")]
    private class FileSaveDialogRcw;

    [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        // IModalWindow
        [PreserveSig] int Show(IntPtr parent);

        // IFileDialog
        void SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray)] COMDLG_FILTERSPEC[] rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
    }

    [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog : IFileDialog
    {
        // The base members are redeclared because COM interop does not inherit vtable slots.
        [PreserveSig] new int Show(IntPtr parent);

        new void SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray)] COMDLG_FILTERSPEC[] rgFilterSpec);
        new void SetFileTypeIndex(uint iFileType);
        new void GetFileTypeIndex(out uint piFileType);
        new void Advise(IntPtr pfde, out uint pdwCookie);
        new void Unadvise(uint dwCookie);
        new void SetOptions(uint fos);
        new void GetOptions(out uint pfos);
        new void SetDefaultFolder(IShellItem psi);
        new void SetFolder(IShellItem psi);
        new void GetFolder(out IShellItem ppsi);
        new void GetCurrentSelection(out IShellItem ppsi);
        new void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        new void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        new void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        new void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        new void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        new void GetResult(out IShellItem ppsi);
        new void AddPlace(IShellItem psi, int fdap);
        new void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        new void Close(int hr);
        new void SetClientGuid(ref Guid guid);
        new void ClearClientData();
        new void SetFilter(IntPtr pFilter);

        // IFileOpenDialog
        void GetResults(out IntPtr ppenum);
        void GetSelectedItems(out IntPtr ppsai);
    }

    [ComImport, Guid("84bccd23-5fde-4cdb-aea4-af64b83d78ab"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileSaveDialog : IFileDialog
    {
        [PreserveSig] new int Show(IntPtr parent);

        new void SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray)] COMDLG_FILTERSPEC[] rgFilterSpec);
        new void SetFileTypeIndex(uint iFileType);
        new void GetFileTypeIndex(out uint piFileType);
        new void Advise(IntPtr pfde, out uint pdwCookie);
        new void Unadvise(uint dwCookie);
        new void SetOptions(uint fos);
        new void GetOptions(out uint pfos);
        new void SetDefaultFolder(IShellItem psi);
        new void SetFolder(IShellItem psi);
        new void GetFolder(out IShellItem ppsi);
        new void GetCurrentSelection(out IShellItem ppsi);
        new void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        new void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        new void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        new void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        new void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        new void GetResult(out IShellItem ppsi);
        new void AddPlace(IShellItem psi, int fdap);
        new void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        new void Close(int hr);
        new void SetClientGuid(ref Guid guid);
        new void ClearClientData();
        new void SetFilter(IntPtr pFilter);

        // IFileSaveDialog
        void SetSaveAsItem(IShellItem psi);
        void SetProperties(IntPtr pStore);
        void SetCollectedProperties(IntPtr pList, bool fAppendDefault);
        void GetProperties(out IntPtr ppStore);
        void ApplyProperties(IShellItem psi, IntPtr pStore, IntPtr hwnd, IntPtr pSink);
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);

        /// <summary>The buffer is allocated with CoTaskMemAlloc and must be freed by the caller.</summary>
        void GetDisplayName(uint sigdnName, out IntPtr ppszName);

        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }
}
