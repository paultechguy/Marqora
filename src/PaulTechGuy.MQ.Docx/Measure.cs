// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Docx;

/// <summary>
/// The units WordprocessingML measures in, and the conversions into them.
///
/// Word uses four at once and mixes them within a single element: a page is in twentieths of
/// a point, a font size in half-points, a border width in eighths of a point, the space
/// around that same border in whole points, and anything drawn is in English Metric Units.
/// Getting one wrong produces a document that opens and is quietly the wrong size, so they
/// are named here rather than written as bare arithmetic at each use.
/// </summary>
internal static class Measure
{
    public const long EmuPerInch = 914400;
    public const long EmuPerPixel = 9525;   // at 96 DPI, which is what the preview assumes
    public const long EmuPerTwip = 635;     // 914400 / 1440

    private const int TwipsPerInch = 1440;

    public static int Twips(double inches) => (int)Math.Round(inches * TwipsPerInch);

    public static long PixelsToEmu(uint pixels) => pixels * EmuPerPixel;

    public static long TwipsToEmu(int twips) => twips * EmuPerTwip;

    /// <summary>
    /// The page size in twips, after orientation.
    ///
    /// A table rather than <c>Twips(setup.WidthInches)</c>, and the reason is A4. Marqora
    /// states A4 as 8.27 x 11.69 inches, which is the right answer to three significant
    /// figures and the wrong one here: 8.27 x 1440 is 11908.8, and a page 11909 twips wide is
    /// not A4 as far as Word is concerned. It shows "Custom size" in Page Setup, and the
    /// printer driver then disagrees with the document about what is in the tray.
    ///
    /// Word's own A4 is 11906 x 16838, which is 210 x 297 millimetres converted exactly.
    /// Letter and Legal have no such problem - they are defined in inches to begin with - but
    /// they are listed here too so that one table answers the question.
    /// </summary>
    public static (int Width, int Height) PageTwips(DocxExportSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);

        (int shortEdge, int longEdge) = setup.Paper switch
        {
            PaperSize.A4 => (11906, 16838),
            PaperSize.Legal => (12240, 20160),
            _ => (12240, 15840),
        };

        return setup.Orientation == PageOrientation.Portrait
            ? (shortEdge, longEdge)
            : (longEdge, shortEdge);
    }

    /// <summary>
    /// How wide the text column is: the page less both margins.
    ///
    /// Wanted often enough to be worth naming - table grids, image caps and the table of
    /// contents' right-aligned tab stop are all measured against it.
    /// </summary>
    public static int UsableWidthTwips(DocxExportSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);

        (int width, _) = PageTwips(setup);

        return width - (2 * Twips(setup.HorizontalMarginInches));
    }

    /// <summary>
    /// How tall the text column is: the page less both margins. The title page measures its
    /// drop against this rather than against the paper, so the block lands in the same place
    /// however the margins are set.
    /// </summary>
    public static int UsableHeightTwips(DocxExportSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);

        (_, int height) = PageTwips(setup);

        return height - (2 * Twips(setup.VerticalMarginInches));
    }
}
