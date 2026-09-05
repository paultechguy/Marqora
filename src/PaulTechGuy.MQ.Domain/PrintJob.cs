// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>Whether a job prints in color, and what the printer was asked for.</summary>
public enum PrintColorMode
{
    /// <summary>Whatever the printer does when it is not told. Used when it has no choice.</summary>
    Default,
    Color,
    Grayscale,
}

/// <summary>Which side of the paper, and which edge the back page turns on.</summary>
public enum PrintDuplex
{
    /// <summary>Whatever the printer does when it is not told. Used when it has no choice.</summary>
    Default,
    OneSided,

    /// <summary>Bound along the long edge - a book.</summary>
    LongEdge,

    /// <summary>Bound along the short edge - a notepad.</summary>
    ShortEdge,
}

/// <summary>
/// What the user chose in Marqora's print dialog.
///
/// The dialog is Marqora's own rather than the Windows one. The WebView's two are the
/// browser's - Edge's print preview frames the pages in browser furniture and prints a band
/// of it too, and the "system" one never appears at all from a WinUI window - and the Windows
/// one is no longer reachable either: Windows 11 substitutes its own modern print experience
/// for a PrintDlg call, themed by the system rather than by the app, and hands back a DEVMODE
/// whose color and duplex choices this app then had no way to honour. See
/// docs/DialogTheming.md.
///
/// The point of carrying the answer in a record is that the print itself is then a plain
/// call with settings on it, which is the only route on which the browser's header and
/// footer can be switched off.
/// </summary>
public sealed record PrintJob
{
    /// <summary>The chosen printer, as Windows names it.</summary>
    public required string PrinterName { get; init; }

    public int Copies { get; init; } = 1;

    /// <summary>Whether copies come out gathered into sets.</summary>
    public bool Collate { get; init; }

    public PageOrientation Orientation { get; init; } = PageOrientation.Portrait;

    /// <summary>Page width in inches, after orientation is applied.</summary>
    public double WidthInches { get; init; } = 8.5;

    /// <summary>Page height in inches, after orientation is applied.</summary>
    public double HeightInches { get; init; } = 11.0;

    public double MarginInches { get; init; } = 0.5;

    /// <summary>Print the page background colors, which diagram and code surfaces rely on.</summary>
    public bool IncludeBackgrounds { get; init; } = true;

    /// <summary>
    /// Color or grey. Default where the printer offers no choice, which is most of the
    /// time: a mono laser has one answer and the dialog does not ask.
    /// </summary>
    public PrintColorMode ColorMode { get; init; } = PrintColorMode.Default;

    /// <summary>One side or two. Default where the printer cannot turn the paper over.</summary>
    public PrintDuplex Duplex { get; init; } = PrintDuplex.Default;

    /// <summary>
    /// The pages to print, in the form the print API takes - "2-5", or "1,4-6". Null prints
    /// the lot, which is what the dialog reports unless the user asked for a range.
    /// </summary>
    public string? PageRanges { get; init; }
}
