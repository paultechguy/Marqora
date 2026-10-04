// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Themes;

/// <summary>Which of a theme's two palettes: the light page or the dark one.</summary>
public enum PaletteMode
{
    Light,
    Dark,
}

/// <summary>
/// One mode's colors: slot id to <c>#rrggbb</c>, lowercase.
///
/// Complete for every slot the theme is required to name. The catalog fills any gap from
/// Default before a palette is handed out, so a reader never has to ask whether a slot is
/// there - except the diagram slots of a stock-diagram theme, which are absent on purpose.
/// </summary>
public sealed class ThemePalette(IReadOnlyDictionary<string, string> colors)
{
    public IReadOnlyDictionary<string, string> Colors { get; } = colors;

    /// <summary>The slot's color. Throws for a slot this palette does not carry.</summary>
    public string this[string slot] =>
        Colors.TryGetValue(slot, out string? hex)
            ? hex
            : throw new KeyNotFoundException($"The palette has no color for '{slot}'.");

    public bool TryGet(string slot, out string hex)
    {
        bool found = Colors.TryGetValue(slot, out string? value);

        hex = value ?? string.Empty;

        return found;
    }
}

/// <summary>
/// A theme: what the gallery calls it, and its light and dark palettes.
///
/// Every output takes the light palette, whatever the window is wearing - a printed page, a
/// PDF, an exported file and a pasted fragment are all white.
/// </summary>
public sealed record ColorTheme(
    string Id,
    string Name,
    string Description,
    bool UsesStockDiagrams,
    ThemePalette Light,
    ThemePalette Dark)
{
    public ThemePalette PaletteFor(PaletteMode mode) => mode == PaletteMode.Dark ? Dark : Light;
}
