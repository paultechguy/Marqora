// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// The formatter's rule list.
///
/// Every rule is a checkbox and nothing is hidden behind an "advanced" section, because the
/// only way to trust a formatter is to be able to see exactly what it will do. The rules are
/// laid out in three columns so the whole set is visible at once rather than scrolling.
///
/// Built in code rather than XAML: it is a grid of checkboxes generated from a list, which is
/// shorter and far less repetitive to express this way than as sixteen hand-written rows.
/// </summary>
internal sealed class FormatOptionsDialog : ContentDialog
{
    private const int Columns = 3;

    /// <summary>A rule: its label, how to read it, and how to write it back.</summary>
    private sealed record Rule(
        string Label,
        string Tooltip,
        Func<FormatOptions, bool> Read,
        Func<FormatOptions, bool, FormatOptions> Write);

    // Ordered so the eleven core rules come first, in the order they appear on the panel this
    // was modelled on, with the additions after them.
    private static readonly Rule[] Rules =
    [
        new("Heading space", "#Heading becomes # Heading",
            o => o.HeadingSpace, (o, v) => o with { HeadingSpace = v }),
        new("Trailing whitespace", "Strips stray spaces at the end of a line, keeping deliberate hard breaks",
            o => o.TrailingWhitespace, (o, v) => o with { TrailingWhitespace = v }),
        new("Normalize markers", "Rewrites every bullet to the same character",
            o => o.NormalizeMarkers, (o, v) => o with { NormalizeMarkers = v }),
        new("Line endings", "Makes every line ending in the file the same",
            o => o.LineEndings, (o, v) => o with { LineEndings = v }),
        new("Blank lines", "A blank line above and below headings, fences and tables",
            o => o.BlankLines, (o, v) => o with { BlankLines = v }),
        new("List marker space", "-item becomes - item",
            o => o.ListMarkerSpace, (o, v) => o with { ListMarkerSpace = v }),
        new("Link syntax", "[text] (url) becomes [text](url)",
            o => o.LinkSyntax, (o, v) => o with { LinkSyntax = v }),
        new("EOF newline", "Exactly one newline at the end of the file",
            o => o.EofNewline, (o, v) => o with { EofNewline = v }),
        new("Collapse blanks", "Runs of blank lines become a single one",
            o => o.CollapseBlanks, (o, v) => o with { CollapseBlanks = v }),
        new("Ordered numbering", "Renumbers ordered lists so they count up",
            o => o.OrderedNumbering, (o, v) => o with { OrderedNumbering = v }),
        new("Blockquote space", ">quote becomes > quote",
            o => o.BlockquoteSpace, (o, v) => o with { BlockquoteSpace = v }),
        new("Table formatting", "Pads cells so the pipes line up, keeping alignment colons",
            o => o.FormatTables, (o, v) => o with { FormatTables = v }),
        new("Code fences", "Unifies ``` and ~~~ and spaces fences out. Never alters code",
            o => o.TidyCodeFences, (o, v) => o with { TidyCodeFences = v }),
        new("Underlined headings", "Converts === and --- underlines to # and ##",
            o => o.SetextToAtx, (o, v) => o with { SetextToAtx = v }),
        new("Emphasis markers", "Settles on one of * or _ for italic and bold",
            o => o.UnifyEmphasis, (o, v) => o with { UnifyEmphasis = v }),
    ];

    private readonly CheckBox[] _boxes = new CheckBox[Rules.Length];
    private readonly CheckBox _reflow;
    private readonly NumberBox _wrapColumn;
    private readonly CheckBox _formatOnSave;
    private readonly CheckBox _selectionOnly;
    private readonly FormatOptions _initial;

    public FormatOptionsDialog(FormatOptions current, int selectedLines)
    {
        _initial = current;

        // Offered only when there is a selection, and ticked by default in that case: having
        // deliberately selected something, formatting just that is the likelier intent.
        _selectionOnly = new CheckBox
        {
            Content = selectedLines == 1
                ? "Format the selected line only"
                : $"Format the {selectedLines} selected lines only",
            IsChecked = selectedLines > 0,
            Visibility = selectedLines > 0 ? Visibility.Visible : Visibility.Collapsed,
        };

        ToolTipService.SetToolTip(
            _selectionOnly,
            "Lines outside the selection are left exactly as they are.");

        Title = "Format Markdown";
        PrimaryButtonText = "Format";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        for (int i = 0; i < Rules.Length; i++)
        {
            _boxes[i] = new CheckBox
            {
                Content = Rules[i].Label,
                IsChecked = Rules[i].Read(current),
                MinWidth = 0,
            };

            ToolTipService.SetToolTip(_boxes[i], Rules[i].Tooltip);
        }

        _reflow = new CheckBox
        {
            Content = "Re-wrap paragraphs to",
            IsChecked = current.ReflowParagraphs,
            VerticalAlignment = VerticalAlignment.Center,
        };

        ToolTipService.SetToolTip(
            _reflow,
            "Rewrites paragraphs to fit the column. Lists, quotes, tables and code are left alone.");

        _wrapColumn = new NumberBox
        {
            Value = current.WrapColumn,
            Minimum = 40,
            Maximum = 200,
            SmallChange = 5,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Width = 120,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _formatOnSave = new CheckBox
        {
            Content = "Format automatically when saving",
            IsChecked = current.FormatOnSave,
        };

        Content = BuildContent();
    }

    /// <summary>True when the user asked for only the selected lines to be formatted.</summary>
    public bool SelectionOnly => _selectionOnly.IsChecked ?? false;

    /// <summary>What the user chose. Only meaningful when the dialog returned Primary.</summary>
    public FormatOptions Options
    {
        get
        {
            FormatOptions result = _initial;

            for (int i = 0; i < Rules.Length; i++)
            {
                result = Rules[i].Write(result, _boxes[i].IsChecked ?? false);
            }

            return result with
            {
                ReflowParagraphs = _reflow.IsChecked ?? false,
                WrapColumn = double.IsNaN(_wrapColumn.Value) ? _initial.WrapColumn : (int)_wrapColumn.Value,
                FormatOnSave = _formatOnSave.IsChecked ?? false,
            };
        }
    }

    private StackPanel BuildContent()
    {
        var panel = new StackPanel { Spacing = 14, MinWidth = 620 };

        panel.Children.Add(new TextBlock
        {
            Text = "RULES",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            CharacterSpacing = 80,
            Opacity = 0.7,
        });

        panel.Children.Add(BuildRuleGrid());

        panel.Children.Add(new Border
        {
            Height = 1,
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
            Margin = new Thickness(0, 2, 0, 2),
        });

        var wrapRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
        };

        wrapRow.Children.Add(_reflow);
        wrapRow.Children.Add(_wrapColumn);
        wrapRow.Children.Add(new TextBlock { Text = "columns", VerticalAlignment = VerticalAlignment.Center });

        panel.Children.Add(wrapRow);
        panel.Children.Add(_selectionOnly);
        panel.Children.Add(_formatOnSave);

        panel.Children.Add(new TextBlock
        {
            Text = "Formatting never changes what a document renders to, and never touches the "
                + "inside of a code block or front matter. Ctrl+Z takes a whole reformat back in one step.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
            FontSize = 12,
        });

        return panel;
    }

    private Grid BuildRuleGrid()
    {
        int rows = (Rules.Length + Columns - 1) / Columns;

        var grid = new Grid { ColumnSpacing = 24, RowSpacing = 8 };

        for (int c = 0; c < Columns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (int r = 0; r < rows; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        // Filled column by column so the reading order runs down each column, matching the
        // layout this was modelled on.
        for (int i = 0; i < _boxes.Length; i++)
        {
            Grid.SetColumn(_boxes[i], i / rows);
            Grid.SetRow(_boxes[i], i % rows);
            grid.Children.Add(_boxes[i]);
        }

        return grid;
    }
}
