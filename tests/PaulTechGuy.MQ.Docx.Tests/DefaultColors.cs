// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Themes;

namespace PaulTechGuy.MQ.Docx.Tests;

/// <summary>
/// The Default color theme's light colors, as Word writes them: six hex digits, upper case.
///
/// Read from the theme rather than written into the tests, because the theme file is where the
/// color is chosen. A test that names the hex itself breaks the moment Default is retuned, and
/// says nothing true about the export when it does.
/// </summary>
internal static class DefaultColors
{
    private static readonly ThemePalette Light = ThemeCatalog.Load().Default.Light;

    public static string Rgb(string slot) => Light[slot].TrimStart('#').ToUpperInvariant();
}
