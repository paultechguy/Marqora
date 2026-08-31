// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

public enum PaperSize
{
    Letter,
    A4,
    Legal,
}

public enum PageOrientation
{
    Portrait,
    Landscape,
}

public enum PageMargin
{
    Normal,
    Narrow,
    Wide,
    None,
}

/// <summary>
/// Page setup for a PDF export, chosen in the export dialog.
///
/// Dimensions are inches because that is what the print API takes; the enum values exist so
/// the dialog and the settings file deal in names rather than numbers.
/// </summary>
public sealed record PdfPageSetup
{
    public PaperSize Paper { get; init; } = PaperSize.Letter;

    public PageOrientation Orientation { get; init; } = PageOrientation.Portrait;

    public PageMargin Margin { get; init; } = PageMargin.Normal;

    /// <summary>Print the page background colours, which diagram and code surfaces rely on.</summary>
    public bool IncludeBackgrounds { get; init; } = true;

    public static PdfPageSetup Default => new();

    /// <summary>Page width in inches, after orientation is applied.</summary>
    public double WidthInches => Orientation == PageOrientation.Portrait ? ShortEdge : LongEdge;

    /// <summary>Page height in inches, after orientation is applied.</summary>
    public double HeightInches => Orientation == PageOrientation.Portrait ? LongEdge : ShortEdge;

    public double MarginInches => Margin switch
    {
        PageMargin.Narrow => 0.25,
        PageMargin.Wide => 1.0,
        PageMargin.None => 0.0,
        _ => 0.5,
    };

    private double ShortEdge => Paper switch
    {
        PaperSize.A4 => 8.27,
        _ => 8.5,
    };

    private double LongEdge => Paper switch
    {
        PaperSize.A4 => 11.69,
        PaperSize.Legal => 14.0,
        _ => 11.0,
    };
}
