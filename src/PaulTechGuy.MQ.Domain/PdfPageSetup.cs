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

/// <summary>
/// Word's margin presets, which both exports use.
///
/// There were two of these, one per export, and they shared four member names and agreed on
/// none of the measurements: Normal meant half an inch to the PDF dialog and a whole one to
/// Word, and Wide meant an inch to one and two inches at the sides to the other. The same
/// document exported both ways came out on two different measures under one word, and a
/// setup carried from one to the other silently changed what it meant.
/// </summary>
public enum PageMargin
{
    Normal,
    Narrow,
    Moderate,
    Wide,
    None,
}

/// <summary>
/// What each preset measures, and what the dialogs call it.
///
/// Word's own numbers. Two of the five are not square - Moderate and Wide change the measure
/// without changing the page - which is why this is a pair of figures rather than one.
/// </summary>
public static class PageMargins
{
    /// <summary>
    /// What the dialogs call each preset, in the enum's own order.
    ///
    /// Beside the measurements on purpose. A label states the numbers, so a label kept
    /// anywhere else is a second copy of them waiting to disagree - and one did, reading
    /// "Wide (1 in, 2 sides)", which says one inch on two sides at least as readily as it
    /// says two inches at the sides. Every combo is indexed by the enum, so the order here
    /// is the order there.
    /// </summary>
    public static IReadOnlyList<string> Labels { get; } =
    [
        "Normal - 1 in all sides",
        "Narrow - 0.5 in all sides",
        "Moderate - 1 in top, 0.75 in sides",
        "Wide - 1 in top, 2 in sides",
        "None - no margins",
    ];

    public static double VerticalInches(PageMargin margin) => margin switch
    {
        PageMargin.Narrow => 0.5,
        PageMargin.None => 0.0,
        _ => 1.0,
    };

    public static double HorizontalInches(PageMargin margin) => margin switch
    {
        PageMargin.Narrow => 0.5,
        PageMargin.Moderate => 0.75,
        PageMargin.Wide => 2.0,
        PageMargin.None => 0.0,
        _ => 1.0,
    };
}

/// <summary>
/// Page setup for a PDF export, chosen in the export dialog.
///
/// Dimensions are inches because that is what the print API takes; the enum values exist so
/// the dialog and the settings file deal in names rather than numbers.
/// </summary>
public sealed record PdfPageSetup
{
    public PaperSize Paper { get; set; } = PaperSize.Letter;

    public PageOrientation Orientation { get; set; } = PageOrientation.Portrait;

    public PageMargin Margin { get; set; } = PageMargin.Normal;

    /// <summary>Print the page background colors, which diagram and code surfaces rely on.</summary>
    public bool IncludeBackgrounds { get; set; } = true;

    public static PdfPageSetup Default => new();

    /// <summary>Page width in inches, after orientation is applied.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double WidthInches => Orientation == PageOrientation.Portrait ? ShortEdge : LongEdge;

    /// <summary>Page height in inches, after orientation is applied.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double HeightInches => Orientation == PageOrientation.Portrait ? LongEdge : ShortEdge;

    /// <summary>Top and bottom margin in inches.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double VerticalMarginInches => PageMargins.VerticalInches(Margin);

    /// <summary>Left and right margin in inches, which two presets set apart from the top.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double HorizontalMarginInches => PageMargins.HorizontalInches(Margin);

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
