// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using PaulTechGuy.MQ.App.ViewModels;
using PaulTechGuy.MQ.Domain;
using Windows.Foundation;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// The right-click menu on a document tab.
///
/// A sibling of the two pane menus rather than part of them: those are raised by the web
/// panes and arrive over the bridge as messages, while this one is an ordinary XAML event
/// on the strip, and it is fitted to the tab that was clicked rather than to whatever the
/// pointer was over.
///
/// It carries the File menu's per-document half, in the File menu's own order, so the two
/// never read as different applications. What it leaves out is anything about the workspace
/// as a whole - New, Open, Save All - because the gesture named one document and the menu
/// should answer about that one.
///
/// Two things about it are less obvious than they look, and both are about focus. It selects
/// the clicked tab before showing anything, so every item can use the same active-document
/// command the File menu uses; Close Other Tabs in particular keeps ActiveTab, and would
/// otherwise keep whichever tab happened to be in front. And the keyboard is handed back on
/// Closed, except for the items that can put a dialog on screen - see RunTabActionAsync.
/// </summary>
public sealed partial class MainWindow
{
    private MenuFlyout? _tabMenu;

    // The items whose text or enabled state depends on which tab was clicked. The rest are
    // added once and never touched again.
    private MenuFlyoutItem? _tabSaveItem;
    private MenuFlyoutItem? _tabReloadItem;
    private MenuFlyoutItem? _tabCloseOthersItem;
    private MenuFlyoutItem? _tabRevealItem;
    private MenuFlyoutItem? _tabCopyPathItem;
    private MenuFlyoutItem? _tabPrintItem;

    /// <summary>
    /// Set while an item that hands the keyboard back itself is running. See
    /// <see cref="RunTabActionAsync"/>.
    /// </summary>
    private bool _tabMenuActionOwnsFocus;

    /// <summary>
    /// Whether the tab menu is on screen. Read by the strip's pointer-released handler,
    /// which would otherwise take focus off a menu opened by a press-and-hold.
    /// </summary>
    private bool IsTabMenuOpen => _tabMenu?.IsOpen ?? false;

    /// <summary>
    /// A right-click, Menu key or press-and-hold reached the tab strip.
    ///
    /// ContextRequested rather than RightTapped: all three of those raise it, so the
    /// keyboard and touch routes come free rather than needing handlers of their own.
    /// </summary>
    private void OnTabContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        // The passthrough regions are deliberately left stale for the length of a reorder,
        // so a menu opened during one would be placed against bounds the tabs have already
        // moved off. Nothing is lost by sitting the gesture out - the drag has the pointer.
        if (_isDraggingTab)
        {
            return;
        }

        // A pointer request carries a position. The Menu key and Shift+F10 do not, and mean
        // the tab that is already active.
        bool fromPointer = e.TryGetPosition(RootGrid, out Point point);

        // Both routes when there is a point, the idiom OnTabStripPointerPressed uses:
        // several parts of a TabViewItem report a source outside that item's own visual
        // tree, so the walk up and the hit test each catch what the other misses.
        DocumentTabViewModel? tab = fromPointer
            ? FindTabItem(e.OriginalSource as DependencyObject)?.DataContext as DocumentTabViewModel
                ?? TabAt(point)
            : ViewModel.ActiveTab;

        // Not on a tab. The rest of the strip is still caption, and the window menu Windows
        // puts up there is the right answer over it.
        if (tab is null)
        {
            return;
        }

        e.Handled = true;

        ViewModel.OnTabSelectedByUser(tab);

        MenuFlyout menu = BuildTabMenu();
        RefreshTabMenu(tab);

        // Standard rather than Transient, so the menu takes focus and can be walked with the
        // arrow keys the way the header and pane menus can.
        if (fromPointer && e.TryGetPosition(DocumentTabs, out Point onStrip))
        {
            menu.ShowAt(DocumentTabs, new FlyoutShowOptions
            {
                // In the strip's own coordinates, which is what ShowAt places against.
                Position = onStrip,
                Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
                ShowMode = FlyoutShowMode.Standard,
            });

            return;
        }

        // Keyboard, or a position the strip could not resolve: hang the menu off the tab
        // itself. A tab with no container is one the fit pass has hidden, which the active
        // tab never is, but fall back to the strip rather than show nothing.
        FrameworkElement anchor =
            DocumentTabs.ContainerFromItem(tab) as TabViewItem ?? (FrameworkElement)DocumentTabs;

        menu.ShowAt(anchor, new FlyoutShowOptions
        {
            Placement = FlyoutPlacementMode.Bottom,
            ShowMode = FlyoutShowMode.Standard,
        });
    }

    /// <summary>
    /// Builds the menu once and keeps it. A context menu is opened often, and rebuilding one
    /// every time would throw away the flyout the framework has already measured - the reason
    /// the pane menus give for caching theirs. Only the parts that vary by tab are rewritten,
    /// in <see cref="RefreshTabMenu"/>.
    ///
    /// No style is named anywhere: the implicit styles in Themes/Menus.xaml reach a menu built
    /// in code just as they reach a declared one, which is what keeps this looking like the
    /// File menu without being told to.
    /// </summary>
    private MenuFlyout BuildTabMenu()
    {
        if (_tabMenu is not null)
        {
            return _tabMenu;
        }

        var menu = new MenuFlyout();

        // The text is filled in per tab. "Save" alone is what it would say with no tab, and
        // it is never shown that way.
        _tabSaveItem = Item("Save", "Ctrl+S", () => ViewModel.SaveCommand.ExecuteAsync(null));

        // Deliberately not named after the file. Save As is about the name being left behind,
        // so carrying the old one points the wrong way.
        MenuFlyoutItem saveAs = Item(
            "Save As...",
            "Ctrl+Alt+S",
            () => ViewModel.SaveAsCommand.ExecuteAsync(null));

        menu.Items.Add(_tabSaveItem);
        menu.Items.Add(saveAs);
        menu.Items.Add(new MenuFlyoutSeparator());

        _tabReloadItem = Item(
            "Reload from Disk",
            null,
            () => ViewModel.ReloadFromDiskCommand.ExecuteAsync(null));

        menu.Items.Add(_tabReloadItem);
        menu.Items.Add(new MenuFlyoutSeparator());

        // Its own group: the document acted on, rather than the file on disk above or the
        // tab itself below. Routed through Item like the rest, so the print dialog counts as
        // an action that hands the keyboard back itself.
        _tabPrintItem = Item("Print...", "Ctrl+P", () => ViewModel.PrintCommand.ExecuteAsync(null));
        menu.Items.Add(_tabPrintItem);
        menu.Items.Add(new MenuFlyoutSeparator());

        _tabCloseOthersItem = Item(
            "Close Other Tabs",
            null,
            () => ViewModel.CloseOtherTabsCommand.ExecuteAsync(null));

        menu.Items.Add(Item("Close Tab", "Ctrl+W", () => ViewModel.CloseTabCommand.ExecuteAsync(null)));
        menu.Items.Add(_tabCloseOthersItem);
        menu.Items.Add(Item(
            "Close All Tabs",
            "Ctrl+Shift+W",
            () => ViewModel.CloseAllTabsCommand.ExecuteAsync(null)));
        menu.Items.Add(new MenuFlyoutSeparator());

        // The last two raise nothing, so they take no part in the focus dance below: the
        // flyout's Closed hands the keyboard back for them, as it does for a dismissal.
        _tabRevealItem = new MenuFlyoutItem { Text = "Open in File Explorer" };
        _tabRevealItem.Click += (_, _) => ViewModel.RevealInFolderCommand.Execute(null);

        _tabCopyPathItem = new MenuFlyoutItem { Text = "Copy Full Path" };
        _tabCopyPathItem.Click += (_, _) => ViewModel.CopyPathCommand.Execute(null);

        menu.Items.Add(_tabRevealItem);
        menu.Items.Add(_tabCopyPathItem);

        // Picking from the menu, or dismissing it, ends with the keyboard back in the
        // document. On Closed rather than on an item's Click, for the reason the document
        // list gives: a MenuFlyout holds focus while it is open and hands it back as it
        // closes, which would undo a restore done any earlier.
        menu.Closed += (_, _) =>
        {
            // An item that raises a dialog restores focus itself, once that dialog has been
            // answered. Doing it here as well would put the keyboard in the document while
            // the prompt is still on screen, which is the one thing this must not do.
            if (_tabMenuActionOwnsFocus)
            {
                return;
            }

            ViewModel.RestoreDocumentFocus();
        };

        _tabMenu = menu;
        return menu;

        MenuFlyoutItem Item(string text, string? accelerator, Func<Task> action)
        {
            var item = new MenuFlyoutItem { Text = text };

            if (accelerator is not null)
            {
                // The accelerator text is a label, not a binding: the real accelerators are
                // registered once on the root, and declaring them again here would fire them
                // twice. The same reason the header and pane menus give.
                item.KeyboardAcceleratorTextOverride = accelerator;
            }

            item.Click += (_, _) => _ = RunTabActionAsync(action);

            return item;
        }
    }

    /// <summary>
    /// Fits the menu to the tab it was opened on.
    ///
    /// Enabled state is read off the tab's own snapshot rather than left to each command's
    /// CanExecute. Selecting a tab is applied through the workspace queue, and while that
    /// queue is normally drained by the time this runs, it is not guaranteed to be - a menu
    /// built from the view model could then describe the tab being left rather than the one
    /// clicked. A tab cannot be out of date about itself. The conditions below mirror
    /// CanSave, CanReloadFromDisk, CanCloseOthers and CanActOnFile.
    ///
    /// The commands behind the items do still act on the active document, which is safe for
    /// a different reason: the selection is applied on the click, and the soonest an item can
    /// be chosen is a pointer-move later.
    /// </summary>
    private void RefreshTabMenu(DocumentTabViewModel tab)
    {
        if (_tabSaveItem is null
            || _tabReloadItem is null
            || _tabCloseOthersItem is null
            || _tabRevealItem is null
            || _tabCopyPathItem is null
            || _tabPrintItem is null)
        {
            return;
        }

        // Named, because this menu belongs to one tab rather than to "the document". The full
        // name, never the tab's shortened one - the shortening exists because a tab is a fixed
        // width, and a menu is not.
        _tabSaveItem.Text = $"Save \"{tab.Title}\"";
        _tabSaveItem.IsEnabled = tab.IsDirty;

        _tabReloadItem.IsEnabled =
            !tab.IsUntitled
            && tab.Document.External != ExternalState.Missing
            && (tab.IsDirty || tab.Document.External == ExternalState.Changed);

        _tabCloseOthersItem.IsEnabled = ViewModel.Tabs.Count > 1;

        // Nothing to reveal or copy until the document has been written somewhere. Greyed
        // rather than dropped, so the menu keeps one shape whichever tab it is opened on and
        // its items do not move under the pointer.
        _tabRevealItem.IsEnabled = !tab.IsUntitled;
        _tabCopyPathItem.IsEnabled = !tab.IsUntitled;

        // Blank paper is not worth a sheet. The tab's own text rather than the view model's
        // HasContent, for the reason the rest of this method gives, and the same test
        // PrintCommand.CanExecute makes.
        _tabPrintItem.IsEnabled = !string.IsNullOrWhiteSpace(tab.Document.Text);
    }

    /// <summary>
    /// Runs a menu action and hands the keyboard back when it has finished.
    ///
    /// Every action reached through here can put a dialog on screen - a save prompt, a discard
    /// prompt, the Save As file dialog - and each of those wants the keyboard for as long as
    /// it is up. The flyout's Closed fires as soon as the item is picked, which is while the
    /// dialog is still being answered, so these items take the restore into their own hands
    /// and Closed stands back for them.
    ///
    /// The flag is cleared before the restore rather than after, so an action that finishes
    /// without ever awaiting - closing a tab with nothing unsaved in it - is restored by
    /// Closed as well, a moment later. That second restore is the one that counts: the first
    /// landed while the flyout still held focus, and a flyout hands focus back as it goes.
    /// </summary>
    private async Task RunTabActionAsync(Func<Task> action)
    {
        _tabMenuActionOwnsFocus = true;

        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // Nothing above this catches: the click is fire-and-forget, so a failure would
            // otherwise reach the global handler with no useful context.
            _logger.LogWarning(ex, "A tab menu action failed.");
        }
        finally
        {
            _tabMenuActionOwnsFocus = false;

            ViewModel.RestoreDocumentFocus();
        }
    }
}
