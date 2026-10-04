// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.RegularExpressions;

namespace PaulTechGuy.MQ.Themes;

/// <summary>
/// What reading one theme file produced: the theme, if the file was readable as one at all,
/// and everything wrong with it.
///
/// The tests require <see cref="Problems"/> to be empty for every theme that ships. The app is
/// more forgiving, because a theme with one bad color is still worth showing: the catalog fills
/// the gaps from Default and logs the problems rather than dropping the theme.
/// </summary>
public sealed record ThemeReadResult(ColorTheme? Theme, IReadOnlyList<string> Problems);

/// <summary>
/// Reads a theme file. Strict about what it reports and lenient about what it builds.
///
/// A file whose JSON does not parse, or that has no id or name, is no theme, and comes back
/// with a null <see cref="ThemeReadResult.Theme"/>. Anything less - a missing slot, a color
/// that is not six hex digits, a slot nobody has heard of - is reported, and the theme is built
/// from whatever was good. Hex is read in either case and kept lowercase, because a theme
/// drafted by an AI will sometimes answer in capitals and that is not worth a rejection.
/// </summary>
public static partial class ThemeReader
{
    /// <summary>The only schema there is. A file naming another is reported, and read anyway.</summary>
    public const int Schema = 1;

    /// <summary>The value of <c>"diagrams"</c> that keeps mermaid's own themes.</summary>
    public const string StockDiagrams = "stock";

    /// <param name="json">The file's text.</param>
    /// <param name="expectedId">The id its file name gives it, which the id inside must match.</param>
    public static ThemeReadResult Read(string json, string expectedId)
    {
        ArgumentNullException.ThrowIfNull(json);

        List<string> problems = [];

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            problems.Add($"{expectedId}: not valid JSON ({ex.Message})");
            return new ThemeReadResult(null, problems);
        }

        using (document)
        {
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{expectedId}: the file is not a JSON object.");
                return new ThemeReadResult(null, problems);
            }

            if (!TryGetInt(root, "schema", out int schema) || schema != Schema)
            {
                problems.Add($"{expectedId}: \"schema\" should be {Schema}.");
            }

            string? id = GetString(root, "id");
            string? name = GetString(root, "name");

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            {
                problems.Add($"{expectedId}: a theme needs an \"id\" and a \"name\".");
                return new ThemeReadResult(null, problems);
            }

            if (!string.Equals(id, expectedId, StringComparison.Ordinal))
            {
                problems.Add($"{expectedId}: the id inside is \"{id}\"; it must match the file name.");
            }

            string? diagrams = GetString(root, "diagrams");
            bool stock = string.Equals(diagrams, StockDiagrams, StringComparison.Ordinal);

            if (diagrams is not null && !stock)
            {
                problems.Add($"{expectedId}: \"diagrams\" may only be \"{StockDiagrams}\", or left out.");
            }

            ThemePalette light = ReadPalette(root, "light", expectedId, stock, problems);
            ThemePalette dark = ReadPalette(root, "dark", expectedId, stock, problems);

            ColorTheme theme = new(expectedId, name, GetString(root, "description") ?? string.Empty, stock, light, dark);

            return new ThemeReadResult(theme, problems);
        }
    }

    private static ThemePalette ReadPalette(JsonElement root, string mode, string themeId, bool stock, List<string> problems)
    {
        Dictionary<string, string> colors = new(StringComparer.Ordinal);

        if (!root.TryGetProperty(mode, out JsonElement palette) || palette.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{themeId}: there is no \"{mode}\" palette.");
            return new ThemePalette(colors);
        }

        foreach (JsonProperty entry in palette.EnumerateObject())
        {
            ThemeSlot? slot = ThemeSlots.Find(entry.Name);

            if (slot is null)
            {
                problems.Add($"{themeId}.{mode}: \"{entry.Name}\" is not a slot.");
                continue;
            }

            if (stock && slot.Group == ThemeSlots.DiagramsGroup)
            {
                problems.Add($"{themeId}.{mode}: \"{entry.Name}\" is a diagram slot, and this theme keeps stock diagrams.");
                continue;
            }

            string? value = entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString() : null;

            if (value is null || !HexColor().IsMatch(value))
            {
                problems.Add($"{themeId}.{mode}: \"{entry.Name}\" is \"{value}\", which is not #rrggbb.");
                continue;
            }

            colors[entry.Name] = value.ToLowerInvariant();
        }

        foreach (ThemeSlot slot in ThemeSlots.RequiredFor(stock))
        {
            if (!colors.ContainsKey(slot.Id))
            {
                problems.Add($"{themeId}.{mode}: \"{slot.Id}\" is missing.");
            }
        }

        return new ThemePalette(colors);
    }

    private static string? GetString(JsonElement root, string property) =>
        root.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryGetInt(JsonElement root, string property, out int value)
    {
        value = 0;

        return root.TryGetProperty(property, out JsonElement element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out value);
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();
}
