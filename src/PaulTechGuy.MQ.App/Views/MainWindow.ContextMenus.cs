// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.App.Services;
using PaulTechGuy.MQ.Domain;
using Windows.Foundation;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// The right-click menus for the two panes.
///
/// Both are ordinary WinUI MenuFlyouts, which is the whole point of them. The panes are web
/// content, and each used to bring its own menu: Monaco drew one in the DOM for the source
/// pane and Chromium drew a native one for the preview. Three menus from three toolkits
/// could never be made to match, and the preview's followed Edge's dark mode rather than
/// the app's theme, so it came up dark in a light window. Both are switched off now. The
/// panes report the click across the bridge and these go up instead, styled from
/// Themes/Menus.xaml along with the header menu bar.
///
/// Built in code rather than declared in MainWindow.xaml because a WinUI resource
/// dictionary will not take x:Name, so a declared menu's items could not be reached to
/// enable and hide them per click - which is most of what the code below does. The
/// recent-files submenu is built the same way, a few hundred lines further up.
///
/// They are built once and kept. A context menu is opened often, and rebuilding one every
/// time would throw away the flyout the framework has already measured.
///
/// The third right-click menu in the window is the tab strip's, in
/// MainWindow.TabContextMenu.cs. It is kept apart because it is not one of these: it is
/// raised by a XAML event rather than reported over the bridge, and it is fitted to the tab
/// that was clicked rather than to what the pointer was over.
/// </summary>
public sealed partial class MainWindow
{
    private MenuFlyout? _sourceMenu;
    private MenuFlyout? _previewMenu;

    // Preview items that appear only when the pointer was over something they apply to.
    private MenuFlyoutItem? _copyLinkItem;
    private MenuFlyoutItem? _copyImageItem;
    private MenuFlyoutSeparator? _targetSeparator;

    // Items that need a selection to mean anything. The two Copy items are separate
    // because they copy from different places: the editor's selection and the preview's.
    private MenuFlyoutItem? _cutItem;
    private MenuFlyoutItem? _sourceCopyItem;
    private MenuFlyoutItem? _previewCopyItem;

    // Items that need a document with something in it.
    private readonly List<MenuFlyoutItem> _contentItems = [];

    /// <summary>What the pointer was over, captured at the click and used by the two Copy items.</summary>
    private string? _clickedLinkUrl;
    private string? _clickedImageUrl;

    /// <summary>
    /// A right-click arrived from one of the panes. Fit the menu to what was under the
    /// pointer, then put it where the pointer is.
    /// </summary>
    private void OnContextMenuRequested(object? sender, PaneContextMenuEventArgs e)
    {
        // Zoom and scroll commands act on the pane you were last in, and right-clicking a
        // pane is being in it.
        ViewModel.SetActivePane(e.Pane);

        _clickedLinkUrl = e.LinkUrl;
        _clickedImageUrl = e.ImageUrl;

        MenuFlyout menu = e.Pane == EditorPane.Source
            ? BuildSourceMenu()
            : BuildPreviewMenu();

        foreach (MenuFlyoutItem item in _contentItems)
        {
            item.IsEnabled = ViewModel.HasContent;
        }

        if (e.Pane == EditorPane.Source)
        {
            if (_cutItem is not null) { _cutItem.IsEnabled = e.HasSelection; }
            if (_sourceCopyItem is not null) { _sourceCopyItem.IsEnabled = e.HasSelection; }
        }
        else
        {
            // Collapsed rather than disabled: a menu that always carries Copy Link is
            // mostly a menu about links, and the preview's is not.
            Show(_copyLinkItem, e.LinkUrl is not null);
            Show(_copyImageItem, e.ImageUrl is not null);
            Show(_targetSeparator, e.LinkUrl is not null || e.ImageUrl is not null);

            if (_previewCopyItem is not null) { _previewCopyItem.IsEnabled = e.HasSelection; }
        }

        // Anchored on the panel rather than the WebView inside it. They occupy the same
        // rectangle, so the coordinates are the same either way, and the panel is the one
        // that survives the WebView being replaced after a crash.
        menu.ShowAt(PreviewSurface, new FlyoutShowOptions
        {
            // In the WebView's own coordinates, which is what the pane reported.
            Position = new Point(e.X, e.Y),
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,

            // Standard rather than Transient, so the menu takes focus and can be walked
            // with the arrow keys the way the header menus can.
            ShowMode = FlyoutShowMode.Standard,
        });

        static void Show(MenuFlyoutItemBase? item, bool visible)
        {
            if (item is not null)
            {
                item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    /// <summary>
    /// The source pane's menu: the Edit menu, minus the entries that only make sense with
    /// the whole document in view. Every item routes to the same commands the header menu
    /// uses, so there is one implementation of Copy and one of Find.
    /// </summary>
    private MenuFlyout BuildSourceMenu()
    {
        if (_sourceMenu is not null)
        {
            return _sourceMenu;
        }

        var menu = new MenuFlyout();

        menu.Items.Add(Edit("Undo", "undo", "Ctrl+Z"));
        menu.Items.Add(Edit("Redo", "redo", "Ctrl+Y"));
        menu.Items.Add(new MenuFlyoutSeparator());

        _cutItem = Edit("Cut", "cut", "Ctrl+X");
        _sourceCopyItem = Edit("Copy", "copy", "Ctrl+C");

        menu.Items.Add(_cutItem);
        menu.Items.Add(_sourceCopyItem);
        menu.Items.Add(Edit("Paste", "paste", "Ctrl+V"));
        menu.Items.Add(NeedsContent(Edit("Select All", "selectAll", "Ctrl+A")));
        menu.Items.Add(new MenuFlyoutSeparator());

        menu.Items.Add(NeedsContent(Edit("Find...", "find", "Ctrl+F")));
        menu.Items.Add(NeedsContent(Edit("Replace...", "replace", "Ctrl+H")));
        menu.Items.Add(NeedsContent(Edit("Go to Line...", "gotoLine", "Ctrl+G")));
        menu.Items.Add(new MenuFlyoutSeparator());

        var format = new MenuFlyoutItem
        {
            Text = "Format Document",
            KeyboardAcceleratorTextOverride = "Shift+Alt+F",
        };

        format.Click += (_, _) => ViewModel.FormatDocumentCommand.Execute(null);
        menu.Items.Add(NeedsContent(format));

        _sourceMenu = menu;
        return menu;

        MenuFlyoutItem Edit(string text, string command, string accelerator)
        {
            var item = new MenuFlyoutItem
            {
                Text = text,
                KeyboardAcceleratorTextOverride = accelerator,
            };

            // The accelerator text is a label, not a binding: the real accelerators are
            // registered once on the root, and declaring them again here would fire them
            // twice. Same reason the header menu gives for its own overrides.
            item.Click += (_, _) => ViewModel.EditActionCommand.Execute(command);

            return item;
        }
    }

    /// <summary>
    /// The preview's menu: reading and getting the document out, which is what the pane is
    /// for. Nothing that edits, because nothing in the preview is editable.
    /// </summary>
    private MenuFlyout BuildPreviewMenu()
    {
        if (_previewMenu is not null)
        {
            return _previewMenu;
        }

        var menu = new MenuFlyout();

        // Deliberately not the Edit menu's Copy: that one copies the editor's selection and
        // pulls the source pane into view to do it. The preview holds a selection of its own.
        _previewCopyItem = new MenuFlyoutItem { Text = "Copy", KeyboardAcceleratorTextOverride = "Ctrl+C" };
        _previewCopyItem.Click += (_, _) => ViewModel.CopyPreviewSelectionCommand.Execute(null);

        var selectAll = new MenuFlyoutItem { Text = "Select All", KeyboardAcceleratorTextOverride = "Ctrl+A" };
        selectAll.Click += (_, _) => ViewModel.SelectAllInPreviewCommand.Execute(null);

        menu.Items.Add(_previewCopyItem);
        menu.Items.Add(NeedsContent(selectAll));

        _targetSeparator = new MenuFlyoutSeparator();
        menu.Items.Add(_targetSeparator);

        _copyLinkItem = new MenuFlyoutItem { Text = "Copy Link" };
        _copyLinkItem.Click += (_, _) => CopyClicked(_clickedLinkUrl, "Link copied");

        _copyImageItem = new MenuFlyoutItem { Text = "Copy Image Address" };
        _copyImageItem.Click += (_, _) => CopyClicked(_clickedImageUrl, "Image address copied");

        menu.Items.Add(_copyLinkItem);
        menu.Items.Add(_copyImageItem);
        menu.Items.Add(new MenuFlyoutSeparator());

        // Copies whatever is selected in the preview, or the whole document when nothing
        // is, which is why it sits with the copy items rather than the exports.
        var richText = new MenuFlyoutItem
        {
            Text = "Copy as Rich Text",
            KeyboardAcceleratorTextOverride = "Ctrl+Shift+C",
        };
        richText.Click += (_, _) => ViewModel.CopyAsRichTextCommand.Execute(null);

        menu.Items.Add(NeedsContent(richText));
        menu.Items.Add(new MenuFlyoutSeparator());

        var pdf = new MenuFlyoutItem { Text = "Export to PDF..." };
        pdf.Click += (_, _) => ViewModel.ExportPdfCommand.Execute(null);

        var html = new MenuFlyoutItem { Text = "Export to HTML..." };
        html.Click += (_, _) => ViewModel.ExportHtmlCommand.Execute(null);

        var print = new MenuFlyoutItem { Text = "Print...", KeyboardAcceleratorTextOverride = "Ctrl+P" };
        print.Click += (_, _) => ViewModel.PrintCommand.Execute(null);

        menu.Items.Add(NeedsContent(pdf));
        menu.Items.Add(NeedsContent(html));
        menu.Items.Add(NeedsContent(print));

        _previewMenu = menu;
        return menu;
    }

    /// <summary>Registers an item as one that needs a document with something in it.</summary>
    private MenuFlyoutItem NeedsContent(MenuFlyoutItem item)
    {
        _contentItems.Add(item);
        return item;
    }

    private void CopyClicked(string? value, string announcement)
    {
        if (ClipboardText.Set(value, _logger))
        {
            ViewModel.StatusText = announcement;
        }
    }
}
