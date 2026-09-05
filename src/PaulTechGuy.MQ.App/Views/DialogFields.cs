// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// The shapes a code-built dialog's content is made of: a labelled control, a drop-down of
/// plain strings, and the icon beside a caption that explains what the control will take.
///
/// Shared by <see cref="PdfExportDialog"/> and <see cref="PrintDialog"/>, which ask closely
/// related questions and should not answer them in two different-looking ways. Small enough
/// to have been copied instead; copied is how the label above one control ends up a different
/// size from the label above the next.
/// </summary>
internal static class DialogFields
{
    /// <summary>
    /// The caption's metrics, stated once because the hint icon beside it has to match them.
    /// An icon at full strength next to a 0.75 label reads as a warning about the field
    /// rather than an offer to explain it.
    /// </summary>
    private const double LabelFontSize = 12.5;

    private const double LabelOpacity = 0.75;

    /// <summary>
    /// Segoe Fluent Icons' outline "Info". The filled variant reads as a notice about
    /// something that has happened; this is an aside about a field nobody has filled in yet.
    /// </summary>
    private const string InfoGlyph = "\uE946";

    /// <summary>
    /// A half-point under the caption's 12.5 rather than over it: the icon annotates the
    /// label and should not outweigh it. The toolbar's 15 is for a control row.
    /// </summary>
    private const double HintGlyphSize = 12;

    /// <summary>The gap between a caption and the hint icon belonging to it.</summary>
    private const double HintGap = 6;

    /// <summary>
    /// A control with its caption above it, stretched to the dialog's width.
    /// </summary>
    /// <param name="hint">
    /// Optional, from <see cref="Hint"/>: an icon set beside the caption for a field whose
    /// answer has a syntax rather than a value. Most fields do not need one - a drop-down
    /// cannot be filled in wrongly - so it stays off by default.
    /// </param>
    public static StackPanel Labelled(string label, FrameworkElement control, FrameworkElement? hint = null)
    {
        ArgumentNullException.ThrowIfNull(control);

        var group = new StackPanel { Spacing = 4 };

        var caption = new TextBlock
        {
            Text = label,
            FontSize = LabelFontSize,
            Opacity = LabelOpacity,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (hint is null)
        {
            group.Children.Add(caption);
        }
        else
        {
            // The icon travels with the caption rather than being pushed out to the field's
            // right-hand edge, so it stays beside the word it explains however wide the
            // dialog is and however long the label gets.
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = HintGap,
            };

            row.Children.Add(caption);
            row.Children.Add(hint);
            group.Children.Add(row);
        }

        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        group.Children.Add(control);

        return group;
    }

    /// <summary>
    /// The info icon that sits beside a caption and explains the field under it.
    ///
    /// Deliberately not a Button. It performs no action, so a button's shape would promise a
    /// click that goes nowhere, and the smallest tier in docs/Button-App-Standards.md is a
    /// 34-square icon button that would tower over a 12.5pt label. A ContentControl is the
    /// smallest thing that can hold a glyph and still take focus, and taking focus is the
    /// point: a tooltip only a pointer can reach is invisible to anyone driving the dialog
    /// from the keyboard.
    /// </summary>
    /// <param name="content">
    /// What the tooltip shows. Any object a ToolTip will host, so a caller with a table to
    /// show can pass a panel rather than flattening it into a sentence.
    /// </param>
    /// <param name="name">What the icon is, for Narrator. A short noun phrase.</param>
    /// <param name="helpText">
    /// The same explanation as prose. A Grid of examples says nothing to a screen reader, so
    /// the seen and the spoken forms are passed separately - and the call site builds both
    /// from one list, so they cannot come to disagree.
    /// </param>
    public static ContentControl Hint(object content, string name, string helpText)
    {
        ArgumentNullException.ThrowIfNull(content);

        var host = new ContentControl
        {
            Content = new FontIcon { Glyph = InfoGlyph, FontSize = HintGlyphSize },
            Opacity = LabelOpacity,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,

            // Room for the focus ring to sit around the glyph rather than across it.
            Padding = new Thickness(2),
            CornerRadius = new CornerRadius(4),

            // The two halves of "reachable without a mouse": a place in the tab order, and
            // the system's own focus ring once focus arrives. A ContentControl is neither a
            // tab stop nor focus-visual-drawing by default, so both are asked for.
            IsTabStop = true,
            UseSystemFocusVisuals = true,

            // A literal transparent - not a colour choice, and not a ThemeResource lookup,
            // which would resolve against the application's theme rather than this element's
            // and is the trap PrintDialog exists because of. An unset background is not
            // hit-tested at all, so without this the pointer has to find the glyph's own
            // strokes and the tooltip appears only intermittently.
            Background = new SolidColorBrush(Colors.Transparent),
        };

        AutomationProperties.SetName(host, name);
        AutomationProperties.SetHelpText(host, helpText);

        var tip = new ToolTip { Content = content };

        ToolTipService.SetToolTip(host, tip);

        /*
            A ToolTip is popup-hosted, the same as a ContentDialog and a Flyout: a sibling of
            the window's content rather than a child of it, so nothing reaches it down the
            tree. DialogExtensions.AnchorTo and PreferencesWindow.Themed each exist to hand a
            theme across that gap, and this is the same move for the third kind of popup.

            Taken from the icon's own ActualTheme, which is right because the icon is inside
            the dialog and the dialog has already been themed by the time this runs. Set
            before the tooltip can open rather than as it opens, so there is no flash. If the
            framework does carry the theme across on its own, this assigns the value that was
            already in force and costs nothing.
        */
        host.Loaded += (_, _) => tip.RequestedTheme = host.ActualTheme;
        host.ActualThemeChanged += (_, _) => tip.RequestedTheme = host.ActualTheme;

        return host;
    }

    /// <summary>
    /// A drop-down of strings, with one of them chosen.
    ///
    /// The selection is clamped rather than trusted: it arrives from settings or from a
    /// driver, and an index that has since gone out of range would otherwise leave the
    /// drop-down blank.
    /// </summary>
    public static ComboBox Combo(IReadOnlyList<string> options, int selected)
    {
        ArgumentNullException.ThrowIfNull(options);

        var combo = new ComboBox();

        foreach (string option in options)
        {
            combo.Items.Add(new ComboBoxItem { Content = option });
        }

        combo.SelectedIndex = options.Count == 0 ? -1 : Math.Clamp(selected, 0, options.Count - 1);

        return combo;
    }
}
