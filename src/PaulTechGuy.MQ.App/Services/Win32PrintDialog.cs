// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.InteropServices;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// The Windows print dialog, called through comdlg32.
///
/// Marqora shows this itself rather than letting the WebView show one of its own. The
/// WebView offers two and neither will do: Edge's print preview is a browser window that
/// prints a browser's header and footer, and the "system" one it claims to offer never
/// appears from a WinUI window - the call returns, the renderer blocks, and nothing is on
/// screen. Calling comdlg32 directly is what this app already does for Open and Save, and
/// for the same reason: it is the dialog every desktop app has always used, and it behaves
/// the same packaged or not.
///
/// What comes back is fed to the WebView's own print call, which is the only route on which
/// the browser's header and footer can be switched off.
/// </summary>
internal static class Win32PrintDialog
{
    private const uint PD_PAGENUMS = 0x00000002;
    private const uint PD_NOSELECTION = 0x00000004;
    private const uint PD_COLLATE = 0x00000010;
    private const uint PD_HIDEPRINTTOFILE = 0x00100000;

    private const uint DM_ORIENTATION = 0x00000001;
    private const uint DM_PAPERSIZE = 0x00000002;
    private const uint DM_PAPERLENGTH = 0x00000004;
    private const uint DM_PAPERWIDTH = 0x00000008;

    private const short DMORIENT_LANDSCAPE = 2;

    private const short DMPAPER_LETTER = 1;
    private const short DMPAPER_TABLOID = 3;
    private const short DMPAPER_LEGAL = 5;
    private const short DMPAPER_STATEMENT = 6;
    private const short DMPAPER_EXECUTIVE = 7;
    private const short DMPAPER_A3 = 8;
    private const short DMPAPER_A4 = 9;
    private const short DMPAPER_A5 = 11;

    /// <summary>The paper a driver can name that Marqora can size, as portrait inches.</summary>
    private static readonly Dictionary<short, (double Width, double Height)> PaperSizes = new()
    {
        [DMPAPER_LETTER] = (8.5, 11.0),
        [DMPAPER_TABLOID] = (11.0, 17.0),
        [DMPAPER_LEGAL] = (8.5, 14.0),
        [DMPAPER_STATEMENT] = (5.5, 8.5),
        [DMPAPER_EXECUTIVE] = (7.25, 10.5),
        [DMPAPER_A3] = (11.69, 16.54),
        [DMPAPER_A4] = (8.27, 11.69),
        [DMPAPER_A5] = (5.83, 8.27),
    };

    /// <summary>
    /// Shows the print dialog. Returns null when the user cancels, and when the machine has
    /// no printer at all - comdlg32 says so itself, and there is nothing to add to it.
    ///
    /// Margins and backgrounds are not in this dialog, which has no field for either. They
    /// come from <paramref name="defaults"/>, so paper and PDF start from the same idea of
    /// what a Marqora page looks like.
    /// </summary>
    public static PrintJob? Show(IntPtr owner, PdfPageSetup defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);

        var dialog = new PRINTDLGW
        {
            lStructSize = (uint)Marshal.SizeOf<PRINTDLGW>(),
            hwndOwner = owner,

            // No PD_RETURNDC: nothing here draws through GDI. The WebView prints its own
            // pages, and all this dialog is asked for is which printer and how many.
            Flags = PD_NOSELECTION | PD_HIDEPRINTTOFILE,

            // The page count is not known until the pages are laid out, which happens inside
            // the print call itself. A range is still offered, bounded by a number no
            // document will reach, because whatever the user asks for is passed straight on.
            nMinPage = 1,
            nMaxPage = 9999,
            nFromPage = 1,
            nToPage = 9999,
            nCopies = 1,
        };

        if (!PrintDlgW(ref dialog))
        {
            // Cancel and failure look alike here, and are treated alike: CommDlgExtendedError
            // tells them apart, but either way there is nothing to print, and a dialog that
            // failed has already said so on screen.
            Free(ref dialog);
            return null;
        }

        try
        {
            string? printer = ReadPrinterName(dialog.hDevNames);

            if (string.IsNullOrWhiteSpace(printer))
            {
                return null;
            }

            (double width, double height, PageOrientation orientation) = ReadPaper(dialog.hDevMode, defaults);

            return new PrintJob
            {
                PrinterName = printer,
                // Cast before the comparison: nCopies is a ushort, and the literal would
                // otherwise leave Math.Max ambiguous between its int and ushort overloads.
                Copies = Math.Max(1, (int)dialog.nCopies),
                Collate = (dialog.Flags & PD_COLLATE) != 0,
                Orientation = orientation,
                WidthInches = width,
                HeightInches = height,
                MarginInches = defaults.MarginInches,
                IncludeBackgrounds = defaults.IncludeBackgrounds,
                PageRanges = (dialog.Flags & PD_PAGENUMS) != 0
                    ? $"{dialog.nFromPage}-{dialog.nToPage}"
                    : null,
            };
        }
        finally
        {
            Free(ref dialog);
        }
    }

    /// <summary>
    /// The chosen printer's name, out of the DEVNAMES block.
    ///
    /// The block is a head of four offsets followed by the three strings they point into,
    /// each offset counted in characters from the start of the block rather than in bytes.
    /// </summary>
    private static string? ReadPrinterName(IntPtr hDevNames)
    {
        if (hDevNames == IntPtr.Zero)
        {
            return null;
        }

        IntPtr block = GlobalLock(hDevNames);

        if (block == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            DEVNAMES names = Marshal.PtrToStructure<DEVNAMES>(block);

            return Marshal.PtrToStringUni(block + (names.wDeviceOffset * sizeof(char)));
        }
        finally
        {
            GlobalUnlock(hDevNames);
        }
    }

    /// <summary>
    /// Paper size and orientation, out of the DEVMODE block, in inches.
    ///
    /// A driver fills in only the fields it declares in dmFields, so each one is read only
    /// when it is claimed and anything unclaimed falls back to the page setup passed in.
    /// dmPaperSize names a standard size; a driver with a custom size gives dmPaperWidth and
    /// dmPaperLength instead, in tenths of a millimetre.
    /// </summary>
    private static (double Width, double Height, PageOrientation Orientation) ReadPaper(
        IntPtr hDevMode,
        PdfPageSetup defaults)
    {
        double shortEdge = Math.Min(defaults.WidthInches, defaults.HeightInches);
        double longEdge = Math.Max(defaults.WidthInches, defaults.HeightInches);
        PageOrientation orientation = defaults.Orientation;

        IntPtr block = hDevMode == IntPtr.Zero ? IntPtr.Zero : GlobalLock(hDevMode);

        if (block == IntPtr.Zero)
        {
            return Apply(shortEdge, longEdge, orientation);
        }

        try
        {
            DEVMODEW mode = Marshal.PtrToStructure<DEVMODEW>(block);

            if ((mode.dmFields & DM_ORIENTATION) != 0)
            {
                orientation = mode.dmOrientation == DMORIENT_LANDSCAPE
                    ? PageOrientation.Landscape
                    : PageOrientation.Portrait;
            }

            if ((mode.dmFields & DM_PAPERSIZE) != 0
                && PaperSizes.TryGetValue(mode.dmPaperSize, out (double Width, double Height) paper))
            {
                shortEdge = paper.Width;
                longEdge = paper.Height;
            }
            else if ((mode.dmFields & DM_PAPERWIDTH) != 0
                && (mode.dmFields & DM_PAPERLENGTH) != 0
                && mode.dmPaperWidth > 0
                && mode.dmPaperLength > 0)
            {
                shortEdge = mode.dmPaperWidth / 254.0;
                longEdge = mode.dmPaperLength / 254.0;
            }
        }
        finally
        {
            GlobalUnlock(hDevMode);
        }

        return Apply(shortEdge, longEdge, orientation);

        // The edges are held short-first above, whatever the driver called them, so that
        // orientation is applied once here rather than reasoned about at every branch.
        static (double, double, PageOrientation) Apply(
            double shortEdge,
            double longEdge,
            PageOrientation orientation) =>
            orientation == PageOrientation.Portrait
                ? (shortEdge, longEdge, orientation)
                : (longEdge, shortEdge, orientation);
    }

    /// <summary>
    /// Releases the two blocks the dialog allocated.
    ///
    /// They belong to the caller once the dialog returns, whether it was accepted or
    /// cancelled - a cancelled dialog can still have allocated them.
    /// </summary>
    private static void Free(ref PRINTDLGW dialog)
    {
        if (dialog.hDevMode != IntPtr.Zero)
        {
            GlobalFree(dialog.hDevMode);
            dialog.hDevMode = IntPtr.Zero;
        }

        if (dialog.hDevNames != IntPtr.Zero)
        {
            GlobalFree(dialog.hDevNames);
            dialog.hDevNames = IntPtr.Zero;
        }
    }

    // DllImport rather than LibraryImport, matching AltCharBeepFilter and the rest of the
    // interop here: this is a handful of calls, and generated marshalling would buy nothing
    // the runtime does not already do for them.
    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintDlgW(ref PRINTDLGW lppd);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    /// <summary>
    /// PRINTDLGW. Declared in full even though most of it is never set: the struct is passed
    /// by reference and its size is checked against the dialog's own, so every field has to
    /// be here for the ones that matter to land at the right offset.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PRINTDLGW
    {
        public uint lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hDevMode;
        public IntPtr hDevNames;
        public IntPtr hDC;
        public uint Flags;
        public ushort nFromPage;
        public ushort nToPage;
        public ushort nMinPage;
        public ushort nMaxPage;
        public ushort nCopies;
        public IntPtr hInstance;
        public IntPtr lCustData;
        public IntPtr lpfnPrintHook;
        public IntPtr lpfnSetupHook;
        public IntPtr lpPrintTemplateName;
        public IntPtr lpSetupTemplateName;
        public IntPtr hPrintTemplate;
        public IntPtr hSetupTemplate;
    }

    /// <summary>
    /// The head of the DEVNAMES block. The strings it points at follow it in the same
    /// allocation, so only the offsets are declared here.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct DEVNAMES
    {
        public ushort wDriverOffset;
        public ushort wDeviceOffset;
        public ushort wOutputOffset;
        public ushort wDefault;
    }

    /// <summary>
    /// DEVMODEW, as far as the fields Marqora reads.
    ///
    /// The real structure runs on past this, and drivers append their own data after that,
    /// which is why the block is only ever read and never written back. Everything up to
    /// dmCollate is fixed by the API, so a prefix is safe to lay over it.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODEW
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;

        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public short dmOrientation;
        public short dmPaperSize;
        public short dmPaperLength;
        public short dmPaperWidth;
        public short dmScale;
        public short dmCopies;
        public short dmDefaultSource;
        public short dmPrintQuality;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
    }
}
