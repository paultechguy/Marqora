// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Themes;

/// <summary>A theme file's id - its file name - and its text.</summary>
public sealed record ThemeSource(string Id, string Json);

/// <summary>
/// The themes Marqora ships, in gallery order: Default first, always, then the rest by name.
///
/// Alphabetical rather than numbered so that adding a theme is adding a file, with nothing to
/// renumber. Default is not merely first: it is the fallback for everything. A document whose
/// theme has been removed in a later version gets Default, and a theme missing a color gets
/// Default's color for that slot, so every palette handed out is complete. The one gap that
/// cannot be filled that way is the diagrams, because Default keeps mermaid's stock themes and
/// has no diagram colors to lend: a theme missing any of its diagram slots keeps stock diagrams
/// as well.
///
/// None of that forgiveness is for themes that ship - the tests hold every one of them to an
/// empty <see cref="Problems"/> list. It is there so that one bad value in a file degrades one
/// slot, which the log names, rather than taking the theme or the app down with it.
/// </summary>
public sealed class ThemeCatalog
{
    public const string DefaultId = "default";

    private const string ResourcePrefix = "themes/";
    private const string ResourceSuffix = ".json";

    private readonly Dictionary<string, ColorTheme> _byId;

    private ThemeCatalog(IReadOnlyList<ColorTheme> themes, IReadOnlyList<string> problems)
    {
        Themes = themes;
        Problems = problems;
        Default = themes[0];
        _byId = themes.ToDictionary(theme => theme.Id, StringComparer.Ordinal);
    }

    /// <summary>Every theme, Default first and the rest alphabetical by name.</summary>
    public IReadOnlyList<ColorTheme> Themes { get; }

    public ColorTheme Default { get; }

    /// <summary>Everything wrong with the files read, for the log. Empty for the themes that ship.</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>The theme with this id, or Default when there is none - including for a null id.</summary>
    public ColorTheme Find(string? id) =>
        id is not null && _byId.TryGetValue(id, out ColorTheme? theme) ? theme : Default;

    /// <summary>True when a theme with this id exists.</summary>
    public bool Contains(string? id) => id is not null && _byId.ContainsKey(id);

    /// <summary>The catalog of the themes embedded in this assembly.</summary>
    public static ThemeCatalog Load() => FromSources(EmbeddedSources());

    /// <summary>The embedded theme files, each named by its file name.</summary>
    public static IReadOnlyList<ThemeSource> EmbeddedSources()
    {
        System.Reflection.Assembly assembly = typeof(ThemeCatalog).Assembly;
        List<ThemeSource> sources = [];

        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                || !name.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            using Stream stream = assembly.GetManifestResourceStream(name)!;
            using StreamReader reader = new(stream);

            string id = name[ResourcePrefix.Length..^ResourceSuffix.Length];

            sources.Add(new ThemeSource(id, reader.ReadToEnd()));
        }

        return sources;
    }

    /// <summary>
    /// A catalog of these files. Throws when Default is not among them or cannot be read,
    /// because there is nothing to fall back to without it.
    /// </summary>
    public static ThemeCatalog FromSources(IEnumerable<ThemeSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        List<string> problems = [];
        Dictionary<string, ColorTheme> read = new(StringComparer.Ordinal);

        foreach (ThemeSource source in sources)
        {
            ThemeReadResult result = ThemeReader.Read(source.Json, source.Id);

            problems.AddRange(result.Problems);

            if (result.Theme is null)
            {
                continue;
            }

            if (!read.TryAdd(result.Theme.Id, result.Theme))
            {
                problems.Add($"{source.Id}: a second theme with this id was ignored.");
            }
        }

        if (!read.Remove(DefaultId, out ColorTheme? fallback))
        {
            throw new InvalidOperationException(
                $"The theme \"{DefaultId}\" is missing or unreadable, and every other theme falls back to it.");
        }

        List<ColorTheme> themes = [fallback];

        themes.AddRange(read.Values
            .Select(theme => Completed(theme, fallback))
            .OrderBy(theme => theme.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(theme => theme.Id, StringComparer.Ordinal));

        return new ThemeCatalog(themes, problems);
    }

    /// <summary>The theme with every gap filled from Default, or stock diagrams where that cannot be done.</summary>
    private static ColorTheme Completed(ColorTheme theme, ColorTheme fallback)
    {
        bool stock = theme.UsesStockDiagrams
            || !HasDiagrams(theme.Light)
            || !HasDiagrams(theme.Dark);

        return theme with
        {
            UsesStockDiagrams = stock,
            Light = Filled(theme.Light, fallback.Light, stock),
            Dark = Filled(theme.Dark, fallback.Dark, stock),
        };
    }

    private static bool HasDiagrams(ThemePalette palette) =>
        ThemeSlots.All
            .Where(slot => slot.Group == ThemeSlots.DiagramsGroup)
            .All(slot => palette.Colors.ContainsKey(slot.Id));

    private static ThemePalette Filled(ThemePalette palette, ThemePalette fallback, bool stock)
    {
        Dictionary<string, string> colors = new(StringComparer.Ordinal);

        foreach (ThemeSlot slot in ThemeSlots.RequiredFor(stock))
        {
            if (palette.TryGet(slot.Id, out string hex) || fallback.TryGet(slot.Id, out hex))
            {
                colors[slot.Id] = hex;
            }
        }

        return new ThemePalette(colors);
    }
}
