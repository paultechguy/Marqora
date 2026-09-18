// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// Asks where the count starts and what sits after the number, before writing section numbers
/// into a document's markdown.
///
/// Two questions and no more. The level is the one the document is already showing, so the
/// common answer is Enter — the command means "make the numbers I am looking at real", and a
/// dialog that asked it from scratch would be asking the user to repeat a decision they made
/// with Alt+Shift+2 a moment ago.
///
/// What it deliberately does not offer is a numbering style — A., I., 1.a.i — or a depth limit.
/// Both are real Word features and both would put the file out of step with the screen, because
/// the preview can render neither. A number written into the markdown that the preview cannot
/// reproduce is a number the user cannot check.
///
/// Built in code rather than XAML, matching <see cref="FormatOptionsDialog"/>: the separator
/// list is generated from the enum, and the buttons come from the ContentDialog template, which
/// owns their order and emphasis.
/// </summary>
internal sealed class HeadingNumberDialog : ContentDialog
{
    /// <summary>
    /// The separators, in the order the list offers them, each shown as the thing it produces.
    /// Showing "1.2  Scope" rather than "two spaces" is the whole point: the choice is about
    /// what the line will look like.
    /// </summary>
    private static readonly (HeadingNumberStyle Style, string Sample)[] Styles =
    [
        (HeadingNumberStyle.TwoSpaces, "1.2␣␣Scope"),
        (HeadingNumberStyle.OneSpace, "1.2␣Scope"),
        (HeadingNumberStyle.DotSpace, "1.2.␣Scope"),
        (HeadingNumberStyle.ParenSpace, "1.2)␣Scope"),
        (HeadingNumberStyle.Tab, "1.2→Scope"),
    ];

    private readonly RadioButton[] _levels = new RadioButton[3];
    private readonly ComboBox _style;
    private readonly TextBlock _count = new() { TextWrapping = TextWrapping.Wrap };
    private readonly IReadOnlyList<int> _headingLevels;
    private readonly bool _renumbering;

    /// <param name="headingLevels">
    /// The level of every heading that can carry a number — one entry per heading, empty ones
    /// left out because nothing is written into those. The count on the dialog is recomputed
    /// from this whenever the level changes, so it never claims a number the chosen level would
    /// not produce.
    /// </param>
    public HeadingNumberDialog(
        HeadingNumbering suggested,
        HeadingNumberStyle style,
        IReadOnlyList<int> headingLevels,
        bool renumbering)
    {
        _headingLevels = headingLevels;
        _renumbering = renumbering;

        Title = renumbering ? "Renumber Sections" : "Number Sections";
        PrimaryButtonText = renumbering ? "Renumber" : "Number";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        for (int i = 0; i < _levels.Length; i++)
        {
            _levels[i] = new RadioButton
            {
                Content = $"Heading {i + 1}",
                GroupName = "HeadingNumberLevel",

                // Off cannot be suggested - the caller turns that into Remove before it gets
                // here - so the fallback only guards a settings file naming a level this list
                // does not have.
                IsChecked = (int)suggested == i + 1 || (suggested == HeadingNumbering.Off && i == 0),
            };

            _levels[i].Checked += (_, _) => RefreshCount();
        }

        ToolTipService.SetToolTip(_levels[0], "Count from the first heading level: 1, 1.1, 1.1.1");
        ToolTipService.SetToolTip(_levels[1], "Count from the second level, leaving the title unnumbered");
        ToolTipService.SetToolTip(_levels[2], "Count from the third level");

        _style = new ComboBox
        {
            ItemsSource = Styles.Select(s => s.Sample).ToList(),
            SelectedIndex = Math.Max(0, Array.FindIndex(Styles, s => s.Style == style)),
            MinWidth = 180,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
        };

        Content = BuildContent();
        RefreshCount();
    }

    /// <summary>Where the count starts. Only meaningful when the dialog returned Primary.</summary>
    public HeadingNumbering Start
    {
        get
        {
            for (int i = 0; i < _levels.Length; i++)
            {
                if (_levels[i].IsChecked ?? false)
                {
                    return (HeadingNumbering)(i + 1);
                }
            }

            return HeadingNumbering.FromHeading1;
        }
    }

    /// <summary>
    /// The separator to write, and to remember.
    ///
    /// Not called <c>Style</c>: a <see cref="ContentDialog"/> is a
    /// <see cref="FrameworkElement"/> and already has one, and hiding it would leave the two
    /// meanings one keystroke apart at every call site.
    /// </summary>
    public HeadingNumberStyle NumberStyle =>
        Styles[Math.Clamp(_style.SelectedIndex, 0, Styles.Length - 1)].Style;

    /// <summary>
    /// Restates how many headings the chosen level reaches.
    ///
    /// A heading takes a number exactly when it sits at or below the level the count starts
    /// from, which is why this can count rather than having to number: every heading at or
    /// below the start has its own counter incremented, so none of them ever comes back blank.
    /// </summary>
    private void RefreshCount()
    {
        int start = (int)Start;
        int numbered = _headingLevels.Count(level => level >= start);
        string verb = _renumbering ? "renumbered" : "numbered";

        _count.Text = numbered switch
        {
            0 => "No headings at this level, so nothing would be written.",
            1 => $"1 heading will be {verb}.",
            _ => $"{numbered} headings will be {verb}.",
        };
    }

    private StackPanel BuildContent()
    {
        var panel = new StackPanel { Spacing = 14, MinWidth = 380 };

        panel.Children.Add(new TextBlock
        {
            Text = "COUNT FROM",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            CharacterSpacing = 80,
            Opacity = 0.7,
        });

        var levels = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };

        foreach (RadioButton level in _levels)
        {
            levels.Children.Add(level);
        }

        panel.Children.Add(levels);

        var styleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
        };

        styleRow.Children.Add(new TextBlock
        {
            Text = "Separator",
            VerticalAlignment = VerticalAlignment.Center,
        });

        styleRow.Children.Add(_style);
        panel.Children.Add(styleRow);

        panel.Children.Add(_count);

        panel.Children.Add(new TextBlock
        {
            Text = "The numbers are written into the markdown, so they travel with the text "
                + "anywhere it is pasted. Marqora's own numbering is switched off for this "
                + "document so they are not shown twice.\n\n"
                + "Numbering a heading changes the anchor it answers to. Links inside this "
                + "document are moved to match, but a link from anywhere else - another "
                + "document, a wiki page, a bookmark - still points at the old anchor and will "
                + "stop resolving.\n\n"
                + "Ctrl+Z takes the whole thing back in one step.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
            FontSize = 12,
        });

        return panel;
    }
}
