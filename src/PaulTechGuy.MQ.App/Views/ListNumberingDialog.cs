// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// Asks how Renumber List should number the list at the caret: all 0s, all 1s, or 1, 2, 3.
///
/// It opens on the choice that would change something - the shorthand for a list that counts,
/// counting for one in the shorthand - so going there and back is Ctrl+Shift+9, Enter each way.
///
/// All 0s is grayed out rather than hidden when the list sits straight under a line of text,
/// with the reason beside it. CommonMark only lets a list interrupt a paragraph when it starts
/// at one, so there the choice would quietly turn the list into paragraph text.
///
/// Built in code rather than XAML, matching <see cref="HeadingNumberDialog"/>: the buttons come
/// from the ContentDialog template, which owns their order and emphasis.
/// </summary>
internal sealed class ListNumberingDialog : ContentDialog
{
    private static readonly (ListNumbering Numbering, string Label, string Sample)[] Choices =
    [
        (ListNumbering.Zeros, "All 0s", "0.  0.  0."),
        (ListNumbering.Ones, "All 1s", "1.  1.  1."),
        (ListNumbering.Sequential, "Sequential", "1.  2.  3."),
    ];

    private readonly RadioButton[] _choices = new RadioButton[Choices.Length];

    public ListNumberingDialog(OrderedListSummary list)
    {
        ArgumentNullException.ThrowIfNull(list);

        Title = "Renumber List";
        PrimaryButtonText = "Renumber";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        for (int i = 0; i < Choices.Length; i++)
        {
            var sample = new TextBlock
            {
                Text = Choices[i].Sample,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                Opacity = 0.7,
            };

            var content = new Grid { ColumnSpacing = 12 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.Children.Add(new TextBlock { Text = Choices[i].Label });
            Grid.SetColumn(sample, 1);
            content.Children.Add(sample);

            _choices[i] = new RadioButton
            {
                Content = content,
                GroupName = "ListNumbering",
                IsChecked = Choices[i].Numbering == list.Suggested,
                IsEnabled = Choices[i].Numbering != ListNumbering.Zeros || list.CanStartAtZero,
            };
        }

        Content = BuildContent(list);
    }

    /// <summary>The numbering chosen. Only meaningful when the dialog returned Primary.</summary>
    public ListNumbering Numbering
    {
        get
        {
            for (int i = 0; i < _choices.Length; i++)
            {
                if (_choices[i].IsChecked ?? false)
                {
                    return Choices[i].Numbering;
                }
            }

            return ListNumbering.Ones;
        }
    }

    private StackPanel BuildContent(OrderedListSummary list)
    {
        var panel = new StackPanel { Spacing = 14, MinWidth = 380 };

        panel.Children.Add(new TextBlock
        {
            Text = "NUMBER EVERY ITEM",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            CharacterSpacing = 80,
            Opacity = 0.7,
        });

        var choices = new StackPanel { Spacing = 4 };

        foreach (RadioButton choice in _choices)
        {
            choices.Children.Add(choice);
        }

        panel.Children.Add(choices);

        panel.Children.Add(new TextBlock
        {
            Text = list.Items == 1 ? "1 item will be renumbered." : $"{list.Items} items will be renumbered.",
            TextWrapping = TextWrapping.Wrap,
        });

        string note = "Only this list's own level changes; a list nested inside it keeps its numbers. "
            + "The preview counts up from the first number, so a list of 0s shows as 0, 1, 2.";

        if (!list.CanStartAtZero)
        {
            note += "\n\nAll 0s is not available here: the list sits directly under a line of text, "
                + "and Markdown only starts a list there when its first number is 1. Put a blank line "
                + "above it to allow 0s.";
        }

        panel.Children.Add(new TextBlock
        {
            Text = note + "\n\nCtrl+Z takes the whole thing back in one step.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
            FontSize = 12,
        });

        return panel;
    }
}
