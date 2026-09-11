// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// Gives App.xaml's teal brushes their color.
///
/// The teal has to be one value. It is the preview's accent, posted to the webshell by
/// SetThemeAsync, and it is now also the row a navigable list selects - and a XAML dictionary
/// cannot read a C# constant, so one side has to hand it to the other. This is that hand-off,
/// in the same direction the match colors already travel: <see cref="AccentColors"/> chooses,
/// everything else is given.
///
/// The brushes themselves are declared in App.xaml, one set per theme dictionary, carrying
/// their opacity and nothing else. That split is not arbitrary: how far a plate is tinted is
/// a question about the theme it sits in and belongs beside the other theme keys, while which
/// teal it is tinted with is the same question the stylesheet asks and has one answer. It also
/// keeps the keys where the rest of the app looks for them - Test-ButtonStandards reads every
/// Mq key out of the XAML, and a brush conjured entirely in code would be a key that resolves
/// at runtime and is missing as far as the checker is concerned.
///
/// Into the theme dictionaries rather than a brush per window, which is what leaves the theme
/// switching to the framework: the Light dictionary gets the light teal and the Default one
/// the dark, and a window changing theme re-reads its ThemeResource without anything here
/// running again. That is also why the brushes are not simply built where they are used - a
/// brush built in code resolves against the application's theme, which is the operating
/// system's rather than the one the user chose in Marqora. See PaletteWindow.SurfaceBrush.
///
/// Called once, before any window is built. A brush that never got here would be transparent
/// rather than teal - a selection nobody can see, which is the bug this whole change was
/// about - so every step of the walk below fails loudly instead.
/// </summary>
internal static class MqAccentBrushes
{
    /// <summary>
    /// A key App.xaml states in both theme dictionaries, used to recognize Marqora's own
    /// dictionary among the merged ones rather than trusting its position in the list.
    /// </summary>
    private const string Marker = "MqChromeRuleBrush";

    /// <summary>The keys that are the teal itself, or a tint of it.</summary>
    private static readonly string[] Keys =
    [
        "MqAccentBrush",
        "MqListRowSelectedBrush",
        "MqListRowSelectedPointerOverBrush",
    ];

    public static void Install()
    {
        Fill("Light", AccentColors.Light);

        // "Default" rather than "Dark" - see the note in App.xaml for why the dark dictionary
        // is keyed that way.
        Fill("Default", AccentColors.Dark);
    }

    private static void Fill(string theme, Color accent)
    {
        ResourceDictionary dictionary = ThemeDictionary(theme);

        foreach (string key in Keys)
        {
            if (dictionary[key] is not SolidColorBrush brush)
            {
                throw new InvalidOperationException(
                    $"App.xaml's '{theme}' theme dictionary has no SolidColorBrush named '{key}'.");
            }

            // The color only. Opacity is App.xaml's, and stays whatever the dictionary said.
            brush.Color = accent;
        }
    }

    private static ResourceDictionary ThemeDictionary(string theme)
    {
        foreach (ResourceDictionary merged in Application.Current.Resources.MergedDictionaries)
        {
            /*
                ContainsKey rather than the indexer, and deliberately: it looks at a
                dictionary's own entries only, which is the question being asked here - which
                of the merged dictionaries is the one App.xaml wrote. The indexer performs the
                full lookup and would answer for a key some other dictionary states.
            */
            if (merged.ThemeDictionaries.TryGetValue(theme, out object? value)
                && value is ResourceDictionary dictionary
                && dictionary.ContainsKey(Marker))
            {
                return dictionary;
            }
        }

        throw new InvalidOperationException(
            $"App.xaml has no '{theme}' theme dictionary stating '{Marker}'.");
    }
}
