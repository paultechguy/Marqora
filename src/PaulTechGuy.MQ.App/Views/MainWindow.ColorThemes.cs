// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using PaulTechGuy.MQ.Themes;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// The two ways to choose a color theme from the window: View > Color Theme, and the gallery
/// on the toolbar.
///
/// Both are built from the catalog when the window is, so a theme added as a file shows up in
/// each with nothing to edit here. The menu is for the keyboard and for anyone who knows the
/// name they want; the gallery is for looking. Pointing at a card shows that theme on the real
/// preview without choosing it - the moment the feature exists for - and a click keeps it.
/// The previewed theme holds while the gallery is open, and the document under it still scrolls,
/// so more of it can be read in that theme. Closing the gallery any way but a click on a card -
/// Escape, a click elsewhere - puts back the theme that was in force.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The colors a card shows below its sample heading - one row that reads as the theme.</summary>
    private static readonly string[] CardSwatches =
        ["link", "callout-tip-bar", "callout-important-bar", "callout-warning-bar", "syntax-keyword"];

    /// <summary>The theme a gallery card is showing on the preview, or null when none is.</summary>
    private string? _galleryPreview;

    /// <summary>Set by a click on a card, so the flyout closing behind it does not put the old theme back.</summary>
    private bool _galleryKept;

    /// <summary>The gallery row's own scroll viewer, which the chevrons drive. Null until the flyout has first opened.</summary>
    private ScrollViewer? _galleryScroller;

    /// <summary>Each card's frame and the color its ring takes when it is the one being previewed.</summary>
    private readonly Dictionary<string, (Border Frame, Brush Ring)> _galleryRings = new(StringComparer.Ordinal);

    /// <summary>
    /// The page behind the preview, in the theme the window is wearing - the same values as
    /// --mq-bg in app.css. Read by the WebView's own background and by the gallery's cards,
    /// which sit a theme's colors on the page they will be read against.
    /// </summary>
    private static Windows.UI.Color PageColor(bool dark) =>
        dark
            ? Windows.UI.Color.FromArgb(0xFF, 0x1F, 0x1F, 0x1F)
            : Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

    /// <summary>Fills the menu and wires the gallery. Once: the themes are compiled in.</summary>
    private void BuildColorThemeChoices()
    {
        foreach (ColorTheme theme in ViewModel.ColorThemes)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = theme.Name,
                Tag = theme.Id,
                Command = ViewModel.SetColorThemeCommand,
                CommandParameter = theme.Id,
            };

            ToolTipService.SetToolTip(item, theme.Description);

            // A toggle flips itself when clicked, which would leave the theme already in force
            // unchecked when it is picked again; putting the marks right here keeps exactly one.
            item.Click += (_, _) => ShowChosenColorTheme(theme.Id);

            ColorThemeMenu.Items.Add(item);
        }

        ShowChosenColorTheme(ViewModel.ColorThemeId);

        // Built as the gallery opens rather than here, because a card shows the theme in the
        // mode the window is wearing, and that can change between one opening and the next.
        ColorThemeFlyout.Opening += (_, _) => FillGallery();
        ColorThemeFlyout.Closed += (_, _) =>
        {
            if (!_galleryKept)
            {
                EndGalleryPreview();
            }
        };

        ColorThemeGallery.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is FrameworkElement { Tag: string id })
            {
                _galleryKept = true;
                _galleryPreview = null;
                ColorThemeFlyout.Hide();
                ViewModel.SetColorThemeCommand.Execute(id);
            }
        };

        // The arrow keys preview as the pointer does. Keyboard focus only: the flyout puts
        // focus on the first card as it opens, and that is not the user pointing at Default.
        ColorThemeGallery.GotFocus += (_, e) =>
        {
            if (e.OriginalSource is GridViewItem { FocusState: FocusState.Keyboard, Content: FrameworkElement { Tag: string id } })
            {
                PreviewFromGallery(id);
            }
        };

        // The document stays live under the open gallery, so the previewed theme can be read
        // further down than the screen showed: the wheel scrolls either pane, through the
        // flyout's light-dismiss overlay, which otherwise swallows it. The source and the
        // preview are one browser surface, so passing input to it covers both. A click there
        // still closes the gallery, and closing it without a click puts the old theme back.
        //
        // Which is also why leaving the cards does not end the preview: the pointer has to
        // cross onto the document to scroll it, and arriving to find the old theme back would
        // defeat the point. The preview holds until another card is pointed at, or the gallery
        // closes.
        ColorThemeFlyout.OverlayInputPassThroughElement = PreviewSurface;

        // The chevrons either side of the row. The scroll viewer is the GridView's own, which
        // exists only once its template has been applied - so it is found when the flyout has
        // opened, and the chevrons follow it from there.
        ColorThemeGalleryBack.Click += (_, _) => ScrollGallery(-1);
        ColorThemeGalleryForward.Click += (_, _) => ScrollGallery(1);
        ColorThemeGallery.SizeChanged += (_, _) => UpdateGalleryChevrons();
        ColorThemeFlyout.Opened += (_, _) =>
        {
            HookGalleryScroller();

            // The theme in force in view, so a theme late in the list is not hidden off the end.
            if (ColorThemeGallery.Items.OfType<FrameworkElement>()
                    .FirstOrDefault(card => card.Tag as string == ViewModel.ColorThemeId) is { } chosen)
            {
                ColorThemeGallery.ScrollIntoView(chosen);
            }
        };
    }

    /// <summary>The check marks: on the menu now, and on the gallery the next time it opens.</summary>
    private void ShowChosenColorTheme(string themeId)
    {
        foreach (MenuFlyoutItemBase entry in ColorThemeMenu.Items)
        {
            if (entry is ToggleMenuFlyoutItem item)
            {
                item.IsChecked = string.Equals(item.Tag as string, themeId, StringComparison.Ordinal);
            }
        }
    }

    private void FillGallery()
    {
        _galleryKept = false;
        _galleryPreview = null;
        _galleryRings.Clear();

        bool dark = RootGrid.ActualTheme == ElementTheme.Dark;

        // Exports in a theme of their own: say so, or the gallery would be promising that what is
        // previewed here is what a PDF will look like.
        if (ViewModel.ExportColorThemeChoice is { } export)
        {
            string name = ViewModel.ColorThemes.First(t => t.Id == export).Name;

            ColorThemeExportNote.Text = $"Exports use {name}, as chosen in Preferences, whatever is picked here.";
            ColorThemeExportNote.Visibility = Visibility.Visible;
        }
        else
        {
            ColorThemeExportNote.Visibility = Visibility.Collapsed;
        }

        ColorThemeGallery.Items.Clear();

        foreach (ColorTheme theme in ViewModel.ColorThemes)
        {
            ColorThemeGallery.Items.Add(BuildCard(theme, dark, theme.Id == ViewModel.ColorThemeId));
        }

        HighlightCard(null);
    }

    /// <summary>
    /// One card: the theme's heading color and a row of its accents on the page it will be read
    /// against, and its name beneath, checked when it is the theme in force.
    ///
    /// Drawn from the palette rather than from a picture, so a new theme needs no artwork. The
    /// brushes are plain colors from the theme file, not theme resources looked up in code -
    /// those resolve against Windows' theme rather than Marqora's, which is the trap
    /// Button-App-Standards.md records.
    /// </summary>
    private Border BuildCard(ColorTheme theme, bool dark, bool chosen)
    {
        ThemePalette palette = theme.PaletteFor(dark ? PaletteMode.Dark : PaletteMode.Light);

        SolidColorBrush Brush(string slot) => new(HexColor.Parse(palette[slot], theme.Id));

        var swatches = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(10, 0, 10, 10),
        };

        foreach (string slot in CardSwatches)
        {
            swatches.Children.Add(new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                Background = Brush(slot),
            });
        }

        var sample = new Grid
        {
            Width = 136,
            Height = 76,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush("table-border"),
            Background = new SolidColorBrush(PageColor(dark)),
        };

        sample.Children.Add(new TextBlock
        {
            Text = "Aa",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("heading-1"),
            Margin = new Thickness(10, 4, 0, 0),
        });

        sample.Children.Add(swatches);

        var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

        name.Children.Add(new TextBlock { Text = theme.Name, VerticalAlignment = VerticalAlignment.Center });

        if (chosen)
        {
            name.Children.Add(new FontIcon { Glyph = "", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        }

        var content = new StackPanel { Spacing = 6 };

        content.Children.Add(sample);
        content.Children.Add(name);

        // The ring the previewed card wears - see HighlightCard. Always two pixels and simply
        // transparent until then, so lighting it moves nothing. Its color is the card's own link
        // color, so the ring is part of the theme being shown rather than the app's chrome.
        var card = new Border
        {
            Child = content,
            Tag = theme.Id,
            Padding = new Thickness(3),
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(2),
            BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };

        _galleryRings[theme.Id] = (card, Brush("link"));

        ToolTipService.SetToolTip(card, theme.Description);
        AutomationProperties.SetName(card, chosen ? $"{theme.Name}, current theme" : theme.Name);

        card.PointerEntered += (_, _) => PreviewFromGallery(theme.Id);

        return card;
    }

    /// <summary>
    /// Shows a card's theme on the preview. The theme in force is put back rather than
    /// previewed, so pointing at its own card ends the preview instead of starting one.
    /// </summary>
    private void PreviewFromGallery(string themeId)
    {
        HighlightCard(themeId);

        if (themeId == _galleryPreview)
        {
            return;
        }

        if (themeId == ViewModel.ColorThemeId)
        {
            EndGalleryPreview();
            return;
        }

        _galleryPreview = themeId;
        _ = ViewModel.PreviewColorThemeAsync(themeId);
    }

    /// <summary>
    /// Rings the card being pointed at or reached with the keyboard, a step stronger than the
    /// hover tint every card already gets, and takes the ring off the rest. Null rings none.
    /// </summary>
    private void HighlightCard(string? themeId)
    {
        var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        foreach ((string id, (Border frame, Brush ring)) in _galleryRings)
        {
            frame.BorderBrush = id == themeId ? ring : clear;
        }

        string? name = themeId is null ? null : ViewModel.ColorThemes.FirstOrDefault(t => t.Id == themeId)?.Name;

        ColorThemePreviewLabel.Text = name switch
        {
            null => string.Empty,
            _ when themeId == ViewModel.ColorThemeId => $"{name}, the current theme",
            _ => $"Previewing {name}",
        };
    }

    /// <summary>Finds the row's scroll viewer, once per instance, and follows it.</summary>
    private void HookGalleryScroller()
    {
        if (FindDescendant<ScrollViewer>(ColorThemeGallery) is { } scroller && scroller != _galleryScroller)
        {
            _galleryScroller = scroller;
            scroller.ViewChanged += (_, _) => UpdateGalleryChevrons();

            // Land on a card's left edge when the wheel or the scroll bar lets go, as the
            // chevrons do, so the card at the start of the row is never cut in half. Set here
            // rather than in XAML because snap points are the scroll viewer's own properties, not
            // attachable ones.
            scroller.HorizontalSnapPointsType = SnapPointsType.Mandatory;
            scroller.HorizontalSnapPointsAlignment = SnapPointsAlignment.Near;

            // ViewChanged reports scrolling only. Whether there is anywhere to scroll is settled
            // by layout, after the flyout has opened and the cards have been measured, so the
            // scrollable width is followed directly as well.
            scroller.RegisterPropertyChangedCallback(
                ScrollViewer.ScrollableWidthProperty,
                (_, _) => UpdateGalleryChevrons());
        }

        UpdateGalleryChevrons();
    }

    /// <summary>
    /// Scrolls the row by as many whole cards as fit, and lands on a card's left edge - so the
    /// card at the start of the row is always whole, name and all, and the one that was cut off
    /// at the far edge is the first one shown whole rather than skipped past. The snap points on
    /// the GridView do the same for the wheel and the scroll bar.
    /// </summary>
    private void ScrollGallery(int direction)
    {
        if (_galleryScroller is not { } scroller)
        {
            return;
        }

        double card = ColorThemeGallery.ContainerFromIndex(0) is FrameworkElement first
            ? first.ActualWidth + first.Margin.Left + first.Margin.Right
            : 0;

        double target;

        if (card > 1)
        {
            int perView = Math.Max(1, (int)Math.Floor(scroller.ViewportWidth / card));

            target = (Math.Round(scroller.HorizontalOffset / card) + (direction * perView)) * card;
        }
        else
        {
            target = scroller.HorizontalOffset + (direction * Math.Max(scroller.ViewportWidth - 48, 48));
        }

        target = Math.Clamp(target, 0, scroller.ScrollableWidth);

        scroller.ChangeView(target, null, null);
    }

    /// <summary>
    /// Both chevrons when the row is wider than the flyout, each grayed at its own end; neither
    /// when every card fits, because a chevron that can never do anything is just clutter.
    /// </summary>
    private void UpdateGalleryChevrons()
    {
        AlignGalleryEnd();

        ScrollViewer? scroller = _galleryScroller;
        bool scrolls = scroller is { ScrollableWidth: > 0.5 };
        Visibility shown = scrolls ? Visibility.Visible : Visibility.Collapsed;

        ColorThemeGalleryBack.Visibility = shown;
        ColorThemeGalleryForward.Visibility = shown;

        if (scrolls)
        {
            ColorThemeGalleryBack.IsEnabled = scroller!.HorizontalOffset > 0.5;
            ColorThemeGalleryForward.IsEnabled = scroller.HorizontalOffset < scroller.ScrollableWidth - 0.5;
        }
    }

    /// <summary>
    /// Pads the end of the row so the scrollable width is a whole number of cards.
    ///
    /// Snapping and the chevrons both land on a card's left edge, but the last stop is wherever
    /// the row runs out, and with ten cards in a flyout of a given width that is rarely on an
    /// edge - so the card at the start of the row was cut in half there, name and all. Less than
    /// one card of space after the last one moves that final stop onto an edge as well; the
    /// space only shows at the very end. Recomputed as the row's size changes, and only written
    /// when it differs, so setting it does not set it off again.
    /// </summary>
    private void AlignGalleryEnd()
    {
        if (_galleryScroller is not { ViewportWidth: > 0 } scroller
            || ColorThemeGallery.ContainerFromIndex(0) is not FrameworkElement first)
        {
            return;
        }

        double card = first.ActualWidth + first.Margin.Left + first.Margin.Right;

        if (card < 1)
        {
            return;
        }

        double padded = ColorThemeGallery.Padding.Right;
        double content = scroller.ExtentWidth - padded;
        double overflow = content - scroller.ViewportWidth;

        double needed = 0;

        if (overflow > 0.5)
        {
            double remainder = overflow % card;

            needed = remainder < 0.5 || card - remainder < 0.5 ? 0 : card - remainder;
        }

        if (Math.Abs(needed - padded) > 0.5)
        {
            ColorThemeGallery.Padding = new Thickness(0, 0, needed, 0);
        }
    }

    private void EndGalleryPreview()
    {
        if (_galleryPreview is null)
        {
            return;
        }

        _galleryPreview = null;
        _ = ViewModel.EndColorThemePreviewAsync();
    }
}
