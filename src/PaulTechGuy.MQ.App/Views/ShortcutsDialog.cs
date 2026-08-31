// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PaulTechGuy.MQ.App.Services;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// Help, Keyboard Shortcuts.
///
/// Built in code like the About box, and for the same reason: a fixed list with no bindings,
/// generated from <see cref="KeyboardShortcuts"/> so the window and the clipboard cannot
/// disagree about what the shortcuts are.
/// </summary>
internal sealed class ShortcutsDialog : ContentDialog
{
    /// <summary>Room kept clear on the right for the scrollbar, which is drawn over the content.</summary>
    private const double ScrollBarGutter = 16;

    private readonly ILogger _logger;

    public ShortcutsDialog(ILogger logger)
    {
        _logger = logger;

        Title = "Keyboard shortcuts";
        CloseButtonText = "Close";
        DefaultButton = ContentDialogButton.Close;

        Content = BuildContent();
    }

    private StackPanel BuildContent()
    {
        var panel = new StackPanel { Spacing = 12, Width = 440 };

        panel.Children.Add(BuildHeader());

        // A gutter for the scrollbar. WinUI draws it over the content rather than beside
        // it, so without this it lies across the right-hand column and cuts the shortcuts
        // in half.
        var list = new StackPanel { Spacing = 18, Padding = new Thickness(0, 0, ScrollBarGutter, 0) };

        foreach (ShortcutGroup group in KeyboardShortcuts.Groups)
        {
            list.Children.Add(BuildGroup(group));
        }

        panel.Children.Add(new ScrollViewer
        {
            Content = list,
            MaxHeight = 420,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });

        return panel;
    }

    /// <summary>
    /// The copy control, sitting with the list rather than as a dialog button. It is an
    /// action on the content, not a way out of the dialog, and the Close button is the only
    /// thing that should look like one.
    /// </summary>
    private Grid BuildHeader()
    {
        var caption = new TextBlock
        {
            Text = "Every key Marqora answers to.",
            Opacity = 0.75,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var copy = new Button
        {
            Style = Application.Current.Resources["MqToolButtonStyle"] as Style,
            Content = new FontIcon { Glyph = "", FontSize = 15 },
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        ToolTipService.SetToolTip(copy, "Copy every shortcut to the clipboard");
        AutomationProperties.SetName(copy, "Copy shortcuts");
        copy.Click += (_, _) => CopyAll(copy);

        // The caption takes the slack so the button sits hard right. The button carries its
        // own padding, so the gutter is reduced by that much to line its glyph up with the
        // shortcut column below rather than overhanging it.
        var grid = new Grid { Padding = new Thickness(0, 0, ScrollBarGutter - 10, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(caption, 0);
        Grid.SetColumn(copy, 1);
        grid.Children.Add(caption);
        grid.Children.Add(copy);

        return grid;
    }

    private static StackPanel BuildGroup(ShortcutGroup group)
    {
        var panel = new StackPanel { Spacing = 4 };

        panel.Children.Add(new TextBlock
        {
            Text = group.Name,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
        });

        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 3 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        for (int i = 0; i < group.Shortcuts.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var action = new TextBlock
            {
                Text = group.Shortcuts[i].Action,
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
            };

            Grid.SetRow(action, i);
            Grid.SetColumn(action, 0);
            grid.Children.Add(action);

            var keys = new TextBlock
            {
                Text = group.Shortcuts[i].Keys,
                FontSize = 12.5,
                Opacity = 0.75,
                HorizontalAlignment = HorizontalAlignment.Right,
            };

            Grid.SetRow(keys, i);
            Grid.SetColumn(keys, 1);
            grid.Children.Add(keys);
        }

        panel.Children.Add(grid);

        return panel;
    }

    private void CopyAll(Button source)
    {
        // Both flavours, as everywhere else the app copies: a table for anything that keeps
        // formatting, and aligned columns for anything that does not.
        bool copied = ClipboardHtml.Set(BuildHtml(), BuildText(), _logger);

        ToolTipService.SetToolTip(source, copied ? "Copied" : "The clipboard is in use");
    }

    /// <summary>
    /// Aligned columns, so it still reads as a table in a text editor with no formatting to
    /// lean on.
    /// </summary>
    private static string BuildText()
    {
        int width = KeyboardShortcuts.Groups
            .SelectMany(g => g.Shortcuts)
            .Max(s => s.Action.Length);

        var builder = new StringBuilder();
        builder.AppendLine("Marqora keyboard shortcuts");

        foreach (ShortcutGroup group in KeyboardShortcuts.Groups)
        {
            builder.AppendLine();
            builder.AppendLine(group.Name);

            foreach (Shortcut shortcut in group.Shortcuts)
            {
                builder.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"  {shortcut.Action.PadRight(width)}  {shortcut.Keys}");
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// A real table, styled inline.
    ///
    /// Inline rather than through a style block because Word ignores stylesheets on paste
    /// and applies only what a tag implies or an attribute states — the same reason the
    /// preview's rich-text copy writes its styles onto the elements.
    /// </summary>
    private static string BuildHtml()
    {
        const string Cell = "padding:3px 24px 3px 0;font-family:Segoe UI,sans-serif;font-size:11pt";
        const string Keys = "padding:3px 0;font-family:Consolas,monospace;font-size:10.5pt;white-space:nowrap";

        var builder = new StringBuilder();

        builder.Append("<div style=\"font-family:Segoe UI,sans-serif\">");
        builder.Append("<p style=\"font-size:14pt;font-weight:600;margin:0 0 10px\">Marqora keyboard shortcuts</p>");
        builder.Append("<table style=\"border-collapse:collapse\">");

        foreach (ShortcutGroup group in KeyboardShortcuts.Groups)
        {
            builder.Append(CultureInfo.InvariantCulture, $"""
                <tr><td colspan="2" style="padding:14px 0 4px;font-weight:600;font-size:11.5pt">{
                    WebUtility.HtmlEncode(group.Name)}</td></tr>
                """);

            foreach (Shortcut shortcut in group.Shortcuts)
            {
                builder.Append(CultureInfo.InvariantCulture, $"""
                    <tr><td style="{Cell}">{WebUtility.HtmlEncode(shortcut.Action)}</td><td style="{Keys}">{
                        WebUtility.HtmlEncode(shortcut.Keys)}</td></tr>
                    """);
            }
        }

        builder.Append("</table></div>");

        return builder.ToString();
    }
}
