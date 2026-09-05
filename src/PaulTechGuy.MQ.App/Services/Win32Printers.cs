// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.InteropServices;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>A paper the chosen printer says it has, in the units the print call takes.</summary>
/// <param name="Name">The driver's own name for it - "Letter", "A4", "Env #10".</param>
internal sealed record PaperOption(string Name, double WidthInches, double HeightInches);

/// <summary>
/// What a printer can do, as its driver reports it.
///
/// Only the parts the dialog offers. A capability the driver does not claim is not shown at
/// all, rather than shown and then dropped on the way to the paper.
/// </summary>
internal sealed record PrinterCapabilities
{
    public required IReadOnlyList<PaperOption> Papers { get; init; }

    public bool SupportsColor { get; init; }

    public bool SupportsDuplex { get; init; }

    /// <summary>Most copies the driver will take in one job. At least one.</summary>
    public int MaximumCopies { get; init; } = 1;
}

/// <summary>
/// The installed printers and what each one can do, through the spooler.
///
/// Marqora shows its own print dialog, so it has to answer the questions the Windows one used
/// to: which printers exist, which is the default, and what paper each of them holds. The
/// spooler answers all three, and answers them per printer - which is the point. The dialog
/// this replaces carried a table of eight DMPAPER_ constants and could size nothing outside
/// it; a driver that names its own paper here is believed.
///
/// See docs/DialogTheming.md for why the Windows dialog is no longer used.
/// </summary>
internal static class Win32Printers
{
    private const uint PRINTER_ENUM_LOCAL = 0x00000002;
    private const uint PRINTER_ENUM_CONNECTIONS = 0x00000004;

    /// <summary>Level 4: name, server and attributes. The cheapest level that names a printer.</summary>
    private const uint PrinterInfoLevel = 4;

    private const ushort DC_PAPERS = 2;
    private const ushort DC_PAPERSIZE = 3;
    private const ushort DC_DUPLEX = 7;
    private const ushort DC_PAPERNAMES = 16;
    private const ushort DC_COPIES = 18;
    private const ushort DC_COLORDEVICE = 32;

    /// <summary>DC_PAPERNAMES writes fixed-width fields, null-padded, not a packed list.</summary>
    private const int PaperNameLength = 64;

    private const int ERROR_INSUFFICIENT_BUFFER = 122;

    /// <summary>
    /// The paper every printer is assumed to have when its driver names none.
    ///
    /// A driver that answers DC_PAPERNAMES with nothing is either unusual or asleep, and an
    /// empty paper list would leave the dialog with nothing to print on. These match the
    /// sizes PdfPageSetup uses, so paper and PDF agree when neither can ask a driver.
    /// </summary>
    private static readonly PaperOption[] FallbackPapers =
    [
        new("Letter", 8.5, 11.0),
        new("A4", 8.27, 11.69),
        new("Legal", 8.5, 14.0),
    ];

    /// <summary>
    /// Every printer this user can print to, local and connected, in the spooler's order.
    ///
    /// Empty is a real answer: a machine with no printer installed has none, and the dialog
    /// says so rather than offering an empty list.
    /// </summary>
    public static IReadOnlyList<string> Names()
    {
        const uint flags = PRINTER_ENUM_LOCAL | PRINTER_ENUM_CONNECTIONS;

        // Asked twice, as the spooler expects: once with no buffer to learn the size, then
        // again to fill it. The count comes back only from the second call.
        _ = EnumPrintersW(flags, null, PrinterInfoLevel, IntPtr.Zero, 0, out uint needed, out _);

        if (needed == 0)
        {
            return [];
        }

        IntPtr buffer = Marshal.AllocHGlobal((int)needed);

        try
        {
            if (!EnumPrintersW(flags, null, PrinterInfoLevel, buffer, needed, out _, out uint count))
            {
                return [];
            }

            List<string> names = new((int)count);

            for (int i = 0; i < count; i++)
            {
                PRINTER_INFO_4W info = Marshal.PtrToStructure<PRINTER_INFO_4W>(
                    buffer + (i * Marshal.SizeOf<PRINTER_INFO_4W>()));

                string? name = Marshal.PtrToStringUni(info.pPrinterName);

                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }

            return names;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>The user's default printer, or null when there is not one.</summary>
    public static string? Default()
    {
        uint length = 0;

        // Fails the first time on purpose: the length comes back through the parameter.
        if (GetDefaultPrinterW(null, ref length) || length == 0)
        {
            return null;
        }

        if (Marshal.GetLastWin32Error() != ERROR_INSUFFICIENT_BUFFER)
        {
            return null;
        }

        var name = new char[length];

        return GetDefaultPrinterW(name, ref length)
            ? new string(name, 0, (int)Math.Max(0, length - 1))
            : null;
    }

    /// <summary>
    /// What a printer can do. Never throws and never returns null: a driver that cannot be
    /// asked - offline, or a queue that has gone - answers as a plain printer with the
    /// fallback paper, and the dialog stays usable.
    /// </summary>
    public static PrinterCapabilities Capabilities(string printer)
    {
        if (string.IsNullOrWhiteSpace(printer))
        {
            return new PrinterCapabilities { Papers = FallbackPapers };
        }

        List<PaperOption> papers = ReadPapers(printer);

        return new PrinterCapabilities
        {
            Papers = papers.Count > 0 ? papers : FallbackPapers,
            SupportsColor = Capability(printer, DC_COLORDEVICE) == 1,

            // DC_DUPLEX answers 1 for a printer that can turn the paper over and 0 for one
            // that cannot. It is the whole answer; the two binding edges are always both
            // available on a printer that has either.
            SupportsDuplex = Capability(printer, DC_DUPLEX) == 1,
            MaximumCopies = Math.Max(1, Capability(printer, DC_COPIES)),
        };
    }

    /// <summary>
    /// The paper the driver names, sized.
    ///
    /// Names and sizes are two separate questions with one shared order, so both are asked
    /// and the answers are zipped. A driver that disagrees with itself about how many it has
    /// is not argued with - the list is dropped and the caller falls back.
    /// </summary>
    private static List<PaperOption> ReadPapers(string printer)
    {
        int count = Capability(printer, DC_PAPERNAMES);

        if (count <= 0)
        {
            return [];
        }

        IntPtr names = Marshal.AllocHGlobal(count * PaperNameLength * sizeof(char));
        IntPtr sizes = Marshal.AllocHGlobal(count * Marshal.SizeOf<POINT>());

        try
        {
            if (DeviceCapabilitiesW(printer, null, DC_PAPERNAMES, names, IntPtr.Zero) != count
                || DeviceCapabilitiesW(printer, null, DC_PAPERSIZE, sizes, IntPtr.Zero) != count)
            {
                return [];
            }

            List<PaperOption> papers = new(count);
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < count; i++)
            {
                // Fixed-width and null-padded rather than null-terminated, so the read is
                // bounded by the field and trimmed rather than run to the next zero.
                string name = Marshal
                    .PtrToStringUni(names + (i * PaperNameLength * sizeof(char)), PaperNameLength)
                    .TrimEnd('\0')
                    .Trim();

                POINT size = Marshal.PtrToStructure<POINT>(sizes + (i * Marshal.SizeOf<POINT>()));

                // Tenths of a millimetre. A zero edge is a driver entry with no size behind
                // it - "custom", usually - which nothing here can lay a page out on.
                if (name.Length == 0 || size.x <= 0 || size.y <= 0 || !seen.Add(name))
                {
                    continue;
                }

                papers.Add(new PaperOption(name, size.x / 254.0, size.y / 254.0));
            }

            return papers;
        }
        finally
        {
            Marshal.FreeHGlobal(names);
            Marshal.FreeHGlobal(sizes);
        }
    }

    /// <summary>A capability that answers with a number rather than a list. -1 when it cannot.</summary>
    private static int Capability(string printer, ushort capability) =>
        DeviceCapabilitiesW(printer, null, capability, IntPtr.Zero, IntPtr.Zero);

    // DllImport rather than LibraryImport, matching Win32Dialogs and the rest of the interop
    // here: a handful of calls, and generated marshalling would buy nothing.
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumPrintersW(
        uint flags,
        string? name,
        uint level,
        IntPtr printerEnum,
        uint bufferBytes,
        out uint bytesNeeded,
        out uint returned);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDefaultPrinterW([Out] char[]? buffer, ref uint length);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DeviceCapabilitiesW(
        string device,
        string? port,
        ushort capability,
        IntPtr output,
        IntPtr deviceMode);

    /// <summary>PRINTER_INFO_4W. Three fields, and only the first is read.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PRINTER_INFO_4W
    {
        public IntPtr pPrinterName;
        public IntPtr pServerName;
        public uint Attributes;
    }

    /// <summary>POINT, as DC_PAPERSIZE fills it: width in x, height in y.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }
}
