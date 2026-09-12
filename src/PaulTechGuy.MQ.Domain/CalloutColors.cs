// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace PaulTechGuy.MQ.Domain;

/// <summary>Which of the five GitHub callouts a block is.</summary>
public enum CalloutKind
{
    Note,
    Tip,
    Important,
    Warning,
    Caution,
}

/// <summary>
/// The colors the five callouts wear, and the one duplication in the app that is checked
/// rather than eliminated.
///
/// The preview gets these from <c>webshell/app.css</c>, which declares them as custom
/// properties. The Word export cannot: it renders without a browser, and it has to keep
/// working when the preview has not even been asked - which is the property that makes a Word
/// export degrade rather than fail. So the values are written here too.
///
/// That is a second copy, and a second copy drifts. <c>build/Test-DocumentColors.ps1</c> is
/// what stops it: it reads both files and fails when they disagree. The same bargain
/// <c>Test-ButtonStandards.ps1</c> already strikes for the compact button metrics, and for the
/// same reason - pushing five colors across the bridge would cost more than it saves, but a
/// test costs nothing and keeps the copy honest.
///
/// The bar color is what the preview draws down the left edge. The fill is that same color at
/// a low alpha over the page, which Word cannot express - a shading fill is opaque - so it is
/// composited here onto white instead.
/// </summary>
public static class CalloutColors
{
    // A note is drawn in Marqora's own accent rather than a color of its own, which is why it
    // is not listed here: it reads from DocumentAccent, the one place that teal is written.
    public const string TipHex = "#1a7f37";
    public const string ImportantHex = "#8250df";
    public const string WarningHex = "#9a6700";
    public const string CautionHex = "#b3261e";

    /// <summary>
    /// What a == highlight == is drawn on. Not a callout, and here because it is the same
    /// bargain: the preview reads it from app.css and the Word export cannot, so it is
    /// written twice and checked.
    /// </summary>
    public const string MarkHex = "#fff3a3";

    /// <summary>The highlight as six hex digits, which is how Word writes a color.</summary>
    public static string MarkRgb => Rgb(MarkHex);

    /// <summary>
    /// How much of the bar color shows through the fill. A note is tinted more heavily than
    /// the rest because the teal is the palest of the five and needs the help.
    /// </summary>
    private const double NoteAlpha = 0.14;
    private const double WarningAlpha = 0.10;
    private const double DefaultAlpha = 0.08;

    /// <summary>The label the preview writes above the body, and Word writes in bold.</summary>
    public static string TitleOf(CalloutKind kind) => kind switch
    {
        CalloutKind.Tip => "Tip",
        CalloutKind.Important => "Important",
        CalloutKind.Warning => "Warning",
        CalloutKind.Caution => "Caution",
        _ => "Note",
    };

    /// <summary>The bar down the left edge, as six hex digits with no leading hash.</summary>
    public static string BarOf(CalloutKind kind) => Rgb(kind switch
    {
        CalloutKind.Tip => TipHex,
        CalloutKind.Important => ImportantHex,
        CalloutKind.Warning => WarningHex,
        CalloutKind.Caution => CautionHex,
        _ => DocumentAccent.LightHex,
    });

    /// <summary>
    /// The panel behind the text: the bar color at its alpha, flattened onto white.
    ///
    /// Computed rather than written down, so that changing a callout's color changes its fill
    /// with it. Two constants that have to be kept in step are two chances to get it wrong.
    /// </summary>
    public static string FillOf(CalloutKind kind)
    {
        double alpha = kind switch
        {
            CalloutKind.Note => NoteAlpha,
            CalloutKind.Warning => WarningAlpha,
            _ => DefaultAlpha,
        };

        return OverWhite(BarOf(kind), alpha);
    }

    /// <summary>
    /// The name Markdig gives the kind, as it appears after the exclamation mark in the
    /// source. Unknown text is a note, which is what the preview does with it too.
    /// </summary>
    public static CalloutKind Parse(string? kind) =>
        kind?.Trim().ToUpperInvariant() switch
        {
            "TIP" => CalloutKind.Tip,
            "IMPORTANT" => CalloutKind.Important,
            "WARNING" => CalloutKind.Warning,
            "CAUTION" => CalloutKind.Caution,
            _ => CalloutKind.Note,
        };

    private static string Rgb(string hex) => hex.TrimStart('#').ToUpperInvariant();

    private static string OverWhite(string rgb, double alpha)
    {
        int r = Channel(rgb, 0);
        int g = Channel(rgb, 2);
        int b = Channel(rgb, 4);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Blend(r, alpha):X2}{Blend(g, alpha):X2}{Blend(b, alpha):X2}");
    }

    private static int Channel(string rgb, int at) =>
        int.Parse(rgb.AsSpan(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    /// <summary>One channel of the color over a white page.</summary>
    private static int Blend(int channel, double alpha) =>
        (int)Math.Round(255 + (alpha * (channel - 255)));
}
