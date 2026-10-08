// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using DocumentFormat.OpenXml.Wordprocessing;

namespace PaulTechGuy.MQ.Docx;

/// <summary>One side of a raw HTML block's border: CSS pixels, a CSS line style, #rrggbb.</summary>
internal sealed record HtmlBorder(double WidthPixels, string Style, string Color);

/// <summary>
/// The box the preview drew round a raw HTML block, as the shell measured it: up to four
/// borders, a fill, and the padding between the left border and the words. Every color is
/// opaque, blended over white by the shell, because Word's shading has no alpha.
///
/// This is how a styled block - the fixture's callout, a div with a colored rule down its side
/// and a tinted fill - keeps its look in Word. Word has no element for a block of HTML, but a
/// paragraph's borders and shading draw the same box, and consecutive paragraphs with the same
/// borders are drawn as one.
/// </summary>
internal sealed record HtmlBlockBox(
    HtmlBorder? Top,
    HtmlBorder? Right,
    HtmlBorder? Bottom,
    HtmlBorder? Left,
    string? Fill,
    double PaddingPixels)
{
    /// <summary>Reads the shell's "border-left:4px solid #6366f1;background:#eef2ff;padding-left:16px".</summary>
    public static HtmlBlockBox? Parse(string? stamp)
    {
        if (string.IsNullOrWhiteSpace(stamp))
        {
            return null;
        }

        HtmlBorder? top = null, right = null, bottom = null, left = null;
        string? fill = null;
        double padding = 0;

        foreach (string declaration in stamp.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = declaration.IndexOf(':', StringComparison.Ordinal);

            if (colon <= 0)
            {
                continue;
            }

            string name = declaration[..colon].Trim();
            string value = declaration[(colon + 1)..].Trim();

            switch (name)
            {
                case "border-top": top = Border(value); break;
                case "border-right": right = Border(value); break;
                case "border-bottom": bottom = Border(value); break;
                case "border-left": left = Border(value); break;
                case "background": fill = Hex(value); break;
                case "padding-left": padding = Pixels(value) ?? 0; break;
            }
        }

        return top is null && right is null && bottom is null && left is null && fill is null
            ? null
            : new HtmlBlockBox(top, right, bottom, left, fill, padding);
    }

    /// <summary>The box as paragraph borders, or null when it has no border.</summary>
    public ParagraphBorders? Borders()
    {
        if (Top is null && Right is null && Bottom is null && Left is null)
        {
            return null;
        }

        // The schema's order: top, left, bottom, right.
        var borders = new ParagraphBorders();

        if (Top is { } top)
        {
            borders.AppendChild(Side<TopBorder>(top, 1));
        }

        if (Left is { } left)
        {
            borders.AppendChild(Side<LeftBorder>(left, PaddingPixels));
        }

        if (Bottom is { } bottom)
        {
            borders.AppendChild(Side<BottomBorder>(bottom, 1));
        }

        if (Right is { } right)
        {
            borders.AppendChild(Side<RightBorder>(right, 4));
        }

        return borders;
    }

    /// <summary>The fill as paragraph shading, or null when the box has none.</summary>
    public Shading? Shading() =>
        Fill is { } fill ? new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = fill } : null;

    /// <summary>
    /// One border. Word measures a border in eighths of a point and its distance from the text
    /// in whole points, at most 31; a CSS pixel is three quarters of a point.
    /// </summary>
    private static T Side<T>(HtmlBorder border, double spacePixels)
        where T : BorderType, new() => new()
        {
            Val = border.Style switch
            {
                "dashed" => BorderValues.Dashed,
                "dotted" => BorderValues.Dotted,
                "double" => BorderValues.Double,
                _ => BorderValues.Single,
            },
            Size = (uint)Math.Clamp(Math.Round(border.WidthPixels * 0.75 * 8), 2, 96),
            Space = (uint)Math.Clamp(Math.Round(spacePixels * 0.75), 0, 31),
            Color = border.Color,
        };

    private static HtmlBorder? Border(string value)
    {
        string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length == 3 && Pixels(parts[0]) is { } width && Hex(parts[2]) is { } color
            ? new HtmlBorder(width, parts[1], color)
            : null;
    }

    private static double? Pixels(string value) =>
        double.TryParse(value.Replace("px", string.Empty, StringComparison.Ordinal), NumberStyles.Float, CultureInfo.InvariantCulture, out double pixels)
            ? pixels
            : null;

    private static string? Hex(string value) =>
        value.Length == 7 && value[0] == '#' && value[1..].All(Uri.IsHexDigit)
            ? value[1..].ToUpperInvariant()
            : null;
}
