// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Xaml;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// The button vocabulary App.xaml states, reachable from the windows that are built in code.
///
/// Code cannot write {StaticResource} - that is a XAML markup extension - so a code-built window
/// has to ask the dictionary itself. Application.Current.Resources is the right dictionary to ask
/// whichever tree the control ends up in: a palette window, a flyout, a ContentDialog in the popup
/// root. The application dictionary is the last stop of every lookup and belongs to no XamlRoot in
/// particular.
///
/// That is not the trap App.xaml describes for the old teal. Those were *framework* keys reached
/// through aliases the framework resolves once inside its own dictionary, so overriding the target
/// from outside could not move an alias already resolved. Nothing aliases an Mq key.
///
/// The lookups are here rather than at each call site for one reason: a mistyped key returns null
/// through "as Style", assigning null to Button.Style is perfectly legal, and the button then
/// quietly wears the framework default. That is a hard bug to see and an easy one to ship. This
/// throws instead, naming the key.
///
/// Sharing one Style object across every window is safe: a Style parsed from XAML keeps its
/// {ThemeResource} setters live, and each element re-reads them for its own ActualTheme. Nothing
/// here hands out a Brush, which is the thing that would not survive the trip - a brush looked up
/// in code resolves against the application's theme, which is the operating system's rather than
/// the one the user chose in Marqora. See PaletteWindow.SurfaceBrush.
/// </summary>
internal static class MqStyles
{
    /// <summary>A quiet toolbar button: no chrome until the pointer arrives.</summary>
    public static Style ToolButton => Style("MqToolButtonStyle");

    /// <summary>Square, glyph only, at the chrome row height.</summary>
    public static Style IconButton => Style("MqIconButtonStyle");

    /// <summary>A toolbar button inside a palette window, one step denser.</summary>
    public static Style CompactToolButton => Style("MqCompactToolButtonStyle");

    /// <summary>A neutral action - everything in a row that is not the commit.</summary>
    public static Style CommandButton => Style("MqCommandButtonStyle");

    /// <summary>The one action that commits, wearing the user's Windows accent.</summary>
    public static Style PrimaryCommandButton => Style("MqPrimaryCommandButtonStyle");

    /// <summary>A split action standing in an action row.</summary>
    public static Style CommandDropDown => Style("MqCommandDropDownStyle");

    /// <summary>The gap between buttons in a row.</summary>
    public static double ButtonGroupSpacing => Number("MqButtonGroupSpacing");

    /// <summary>A text box, a drop-down, and the button standing beside them.</summary>
    public static double FormRowHeight => Number("MqFormRowHeight");

    /// <summary>
    /// Resolves every key once, loudly.
    ///
    /// Called from startup so a renamed or mistyped key fails at launch with the key in the
    /// message, rather than the first time some rarely-opened flyout is built. It costs a
    /// handful of dictionary lookups.
    /// </summary>
    public static void Verify()
    {
        _ = ToolButton;
        _ = IconButton;
        _ = CompactToolButton;
        _ = CommandButton;
        _ = PrimaryCommandButton;
        _ = CommandDropDown;
        _ = ButtonGroupSpacing;
        _ = FormRowHeight;
    }

    private static Style Style(string key) =>
        Lookup(key) as Style
            ?? throw new InvalidOperationException($"App.xaml has no Style with the key '{key}'.");

    private static double Number(string key) =>
        Lookup(key) is double value
            ? value
            : throw new InvalidOperationException($"App.xaml has no x:Double with the key '{key}'.");

    private static object? Lookup(string key)
    {
        try
        {
            /*
                The indexer rather than TryGetValue.

                TryGetValue and ContainsKey look only at a dictionary's own entries, while the
                indexer performs the full lookup - merged dictionaries and the active theme
                dictionary included. Every key here is an own entry of Application.Resources
                today, but the difference is a trap not worth leaving for whoever first moves
                one into a merged file.
            */
            return Application.Current.Resources[key];
        }
        catch (KeyNotFoundException)
        {
            // The indexer throws for a key that is nowhere. The callers above turn that into a
            // message naming the key, which is the only thing the caller can act on.
            return null;
        }
    }
}
