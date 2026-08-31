// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// What the user chose in the system print dialog.
///
/// Marqora puts that dialog up itself rather than letting the WebView do it. The WebView's
/// own dialogs are the browser's: one is Edge's print preview, which frames the pages in
/// browser furniture and prints a band of it too, and the other never appears at all from a
/// WinUI window. Calling the Windows dialog directly is also what this app already does for
/// Open and Save, for much the same reason.
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

    /// <summary>Print the page background colours, which diagram and code surfaces rely on.</summary>
    public bool IncludeBackgrounds { get; init; } = true;

    /// <summary>
    /// The pages to print, in the form the print API takes - "2-5", or "1,4-6". Null prints
    /// the lot, which is what the dialog reports unless the user asked for a range.
    /// </summary>
    public string? PageRanges { get; init; }
}
