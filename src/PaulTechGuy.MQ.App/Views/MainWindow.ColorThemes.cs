// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
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

        bool dark = RootGrid.ActualTheme == ElementTheme.Dark;

        ColorThemeGallery.Items.Clear();

        foreach (ColorTheme theme in ViewModel.ColorThemes)
        {
            ColorThemeGallery.Items.Add(BuildCard(theme, dark, theme.Id == ViewModel.ColorThemeId));
        }
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
    private StackPanel BuildCard(ColorTheme theme, bool dark, bool chosen)
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

        var card = new StackPanel { Spacing = 6, Tag = theme.Id, Margin = new Thickness(2) };

        card.Children.Add(sample);
        card.Children.Add(name);

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
