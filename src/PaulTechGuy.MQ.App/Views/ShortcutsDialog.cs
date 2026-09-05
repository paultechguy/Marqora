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
using Microsoft.UI.Xaml.Input;
using PaulTechGuy.MQ.App.Services;
using Windows.System;

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

    /// <summary>How many shortcuts there are altogether, for the count beside the filter.</summary>
    private static readonly int TotalShortcuts = KeyboardShortcuts.Groups.Sum(g => g.Shortcuts.Count);

    private readonly ILogger _logger;
    private readonly TextBlock _caption = new();
    private readonly TextBox _filter = new();
    private readonly Button _copy = new();
    private readonly StackPanel _list = new();

    /// <summary>What the list is showing, and therefore what the copy button copies.</summary>
    private IReadOnlyList<ShortcutGroup> _shown = KeyboardShortcuts.Groups;

    public ShortcutsDialog(ILogger logger)
    {
        _logger = logger;

        Title = "Keyboard shortcuts";
        CloseButtonText = "Close";
        DefaultButton = ContentDialogButton.Close;

        Content = BuildContent();

        // Focus the filter box, but a tick behind the Opened event rather than inside it.
        // ContentDialog moves focus to its default button as part of opening, and does so
        // after Opened has returned - so a Focus call made there, or earlier on the box's
        // own Loaded, is simply taken back. Queueing it puts it after the dialog has
        // finished with focus, which is the only point it stays put.
        //
        // Qualified for the reason MainWindow's startup focus is: this file uses
        // Windows.System for VirtualKey, and that namespace has a DispatcherQueuePriority of
        // its own that is not the one this queue takes.
        Opened += (_, _) => DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => _filter.Focus(FocusState.Programmatic));
    }

    private StackPanel BuildContent()
    {
        var panel = new StackPanel { Spacing = 12, Width = 440 };

        panel.Children.Add(BuildHeader());
        panel.Children.Add(BuildFilter());

        // A gutter for the scrollbar. WinUI draws it over the content rather than beside
        // it, so without this it lies across the right-hand column and cuts the shortcuts
        // in half.
        _list.Spacing = 18;
        _list.Padding = new Thickness(0, 0, ScrollBarGutter, 0);

        panel.Children.Add(new ScrollViewer
        {
            Content = _list,
            MaxHeight = 420,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });

        Rebuild();

        return panel;
    }

    /// <summary>
    /// The copy control, sitting with the list rather than as a dialog button. It is an
    /// action on the content, not a way out of the dialog, and the Close button is the only
    /// thing that should look like one.
    /// </summary>
    private Grid BuildHeader()
    {
        // Caption and button are fields rather than locals: the caption becomes the match
        // count while a filter is typed, and the button's tooltip has to say so.
        _caption.Opacity = 0.75;
        _caption.FontSize = 12.5;
        _caption.VerticalAlignment = VerticalAlignment.Center;

        // Through MqStyles rather than a cast off the dictionary: "as Style" hands back null for
        // a key that has been renamed, assigning null is legal, and the button would quietly
        // wear the stock style instead.
        _copy.Style = MqStyles.ToolButton;
        _copy.HorizontalAlignment = HorizontalAlignment.Right;
        _copy.Content = new FontIcon
        {
            Glyph = "",
            FontSize = 15,
        };

        AutomationProperties.SetName(_copy, "Copy shortcuts");
        _copy.Click += (_, _) => CopyShown();

        // The caption takes the slack so the button sits hard right. The button carries its
        // own padding, so the gutter is reduced by that much to line its glyph up with the
        // shortcut column below rather than overhanging it.
        var grid = new Grid { Padding = new Thickness(0, 0, ScrollBarGutter - 10, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(_caption, 0);
        Grid.SetColumn(_copy, 1);
        grid.Children.Add(_caption);
        grid.Children.Add(_copy);

        return grid;
    }

    /// <summary>
    /// The quick filter: a plain box above the list, in the same place and with the same
    /// manners as the outline panel's, that narrows the list as the letters arrive.
    /// </summary>
    private TextBox BuildFilter()
    {
        _filter.PlaceholderText = "Filter";
        _filter.Margin = new Thickness(0, 0, ScrollBarGutter, 0);

        AutomationProperties.SetName(_filter, "Filter the shortcuts");

        _filter.TextChanged += (_, _) => Rebuild();
        _filter.KeyDown += OnFilterKeyDown;

        return _filter;
    }

    /// <summary>
    /// Redraws the list for whatever is in the filter box.
    ///
    /// All of it, on every keystroke, rather than hiding rows where they stand. It is a few
    /// dozen text blocks in a dialog that is already open, and rebuilding reads far better
    /// than keeping per-row and per-heading visibility in step with a term changing
    /// underneath them.
    /// </summary>
    private void Rebuild()
    {
        _shown = KeyboardShortcuts.Filter(_filter.Text);

        int shown = _shown.Sum(g => g.Shortcuts.Count);
        bool filtered = !string.IsNullOrWhiteSpace(_filter.Text);

        _caption.Text = filtered
            ? string.Create(CultureInfo.CurrentCulture, $"{shown} of {TotalShortcuts} shortcuts")
            : "Every key Marqora answers to.";

        // The tooltip is retitled as well as re-aimed. Copy follows the filter now, so it
        // has to stop promising the whole list while it is handing over part of one.
        ToolTipService.SetToolTip(
            _copy,
            filtered ? "Copy the shortcuts shown to the clipboard" : "Copy every shortcut to the clipboard");

        _copy.IsEnabled = shown > 0;

        _list.Children.Clear();

        if (shown == 0)
        {
            _list.Children.Add(BuildEmpty());

            return;
        }

        foreach (ShortcutGroup group in _shown)
        {
            _list.Children.Add(BuildGroup(group));
        }
    }

    /// <summary>What stands in for the list when the filter matches nothing.</summary>
    private static TextBlock BuildEmpty() => new()
    {
        Text = "No shortcut answers to that.",
        Opacity = 0.55,
        FontSize = 12.5,
        TextAlignment = TextAlignment.Center,
        Margin = new Thickness(0, 12, 0, 12),
    };

    /// <summary>
    /// Escape empties the filter before it closes the dialog, and Enter with a filter typed
    /// does nothing at all.
    ///
    /// Both because while there is text in the box, the box is what the keys are aimed at.
    /// A filter typed by mistake should cost one Escape to undo rather than a reopened
    /// dialog, and Enter — which the Close button answers to, being the default — should not
    /// shut the list that has just been narrowed. An empty box hands both keys straight back
    /// to the dialog.
    /// </summary>
    private void OnFilterKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_filter.Text.Length == 0)
        {
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Escape:
                e.Handled = true;
                _filter.Text = string.Empty;
                break;

            case VirtualKey.Enter:
                e.Handled = true;
                break;

            default:
                break;
        }
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

    /// <summary>
    /// Copies what the list is showing, filter and all, so the clipboard cannot disagree
    /// with the window any more than the window can disagree with
    /// <see cref="KeyboardShortcuts"/>.
    /// </summary>
    private void CopyShown()
    {
        // Both flavours, as everywhere else the app copies: a table for anything that keeps
        // formatting, and aligned columns for anything that does not.
        bool copied = ClipboardHtml.Set(BuildHtml(_shown), BuildText(_shown), _logger);

        ToolTipService.SetToolTip(_copy, copied ? "Copied" : "The clipboard is in use");
    }

    /// <summary>
    /// Aligned columns, so it still reads as a table in a text editor with no formatting to
    /// lean on.
    /// </summary>
    private static string BuildText(IReadOnlyList<ShortcutGroup> groups)
    {
        int width = groups
            .SelectMany(g => g.Shortcuts)
            .Select(s => s.Action.Length)
            .DefaultIfEmpty(0)
            .Max();

        var builder = new StringBuilder();
        builder.AppendLine("Marqora keyboard shortcuts");

        foreach (ShortcutGroup group in groups)
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
    private static string BuildHtml(IReadOnlyList<ShortcutGroup> groups)
    {
        const string Cell = "padding:3px 24px 3px 0;font-family:Segoe UI,sans-serif;font-size:11pt";
        const string Keys = "padding:3px 0;font-family:Consolas,monospace;font-size:10.5pt;white-space:nowrap";

        var builder = new StringBuilder();

        builder.Append("<div style=\"font-family:Segoe UI,sans-serif\">");
        builder.Append("<p style=\"font-size:14pt;font-weight:600;margin:0 0 10px\">Marqora keyboard shortcuts</p>");
        builder.Append("<table style=\"border-collapse:collapse\">");

        foreach (ShortcutGroup group in groups)
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
