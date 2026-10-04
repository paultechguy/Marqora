// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using PaulTechGuy.MQ.Themes;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// The shapes a color theme leaves the app in: the palettes the webshell's color-theme.js
/// turns into a stylesheet, and the light-only CSS block a page that has left the app carries
/// instead.
///
/// Slot ids go across as the dictionary keys they already are. The serializer's camel-case
/// policy names properties, not keys, so "heading-1" arrives as "heading-1".
/// </summary>
internal static class ColorThemePayloads
{
    /// <summary>One theme's two palettes, as color-theme.js takes them.</summary>
    public static object Palettes(ColorTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        return new { light = theme.Light.Colors, dark = theme.Dark.Colors };
    }

    /// <summary>Every theme, the id of the one on screen, and the id of the one exports are drawn in.</summary>
    public static object Catalog(ThemeCatalog catalog, string currentId, string exportId)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return new
        {
            currentId,
            exportId,
            themes = catalog.Themes
                .Select(theme => new { id = theme.Id, light = theme.Light.Colors, dark = theme.Dark.Colors })
                .ToArray(),
        };
    }

    /// <summary>
    /// The light palette as a bare <c>:root</c> block, for an exported page, a Folio, a review
    /// page and the clipboard - each of which is white and has no host to post it a theme.
    ///
    /// At column zero, unindented and unqualified, because that is the only kind of block
    /// RenderedHtmlPackager.FlattenCustomProperties reads when it folds the variables into a
    /// clipboard fragment for Word and Outlook. Every value is opaque hex, so nothing here needs
    /// compositing on the way.
    /// </summary>
    public static string LightDeclarations(ColorTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        StringBuilder css = new();

        css.AppendLine(":root {");

        foreach ((string slot, string hex) in theme.Light.Colors)
        {
            css.AppendLine(CultureInfo.InvariantCulture, $"  --mq-theme-{slot}: {hex};");
        }

        css.AppendLine("}");

        return css.ToString();
    }
}
