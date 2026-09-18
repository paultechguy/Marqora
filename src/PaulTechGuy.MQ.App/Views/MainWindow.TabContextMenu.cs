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
/// never read as different applications. What it leaves out is anything about *other*
/// documents - New, Open, Save All - because the gesture named one document.
///
/// That line used to be drawn at "the workspace as a whole", which was never quite what the
/// menu did: Close Other Tabs and Close All Tabs were always here, and neither is about the
/// clicked tab alone. The line the items actually fall on is the clicked tab and the set of
/// tabs around it, which is why Close Tabs to the Right and Reopen Closed Tab belong here too.
/// Reopen in particular: the strip is where somebody realizes they closed the wrong thing, and
/// the strip is where their hand already is.
///
/// Open in File Explorer and the Copy submenu are no longer duplicates of a File menu item.
/// The menu bar gave them up in the same pass that moved Export onto File, so this is the only
/// route to them - which is why they now sit above the closing group rather than below it.
///
/// Pinned is the second item with no File menu twin, and deliberately so rather than by
/// omission: a pin is about where a tab sits in the strip, and the strip is the only place the
/// gesture means anything. It would read as a document property on the File menu and as a
/// position on this one, which is the kind of split that makes two menus disagree about what a
/// word means.
///
/// Two things about it are less obvious than they look, and both are about focus. It selects
/// the clicked tab before showing anything, so every item can use the same active-document
/// command the File menu uses; Close Other Tabs in particular keeps ActiveTab, and would
/// otherwise keep whichever tab happened to be in front. And the keyboard is handed back on
/// Closed, except for the items that can put a dialog on screen - see RunTabActionAsync.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// The longest a document's name may be where the Save item spells it out. Generous rather
    /// than tight: the point is to stop a pathological name drawing a flyout off the screen, not
    /// to keep the menu narrow.
    /// </summary>
    private const int MaximumNamedTitle = 44;

    private MenuFlyout? _tabMenu;

    // The items whose text or enabled state depends on which tab was clicked. The rest are
    // added once and never touched again.
    private MenuFlyoutItem? _tabSaveItem;
    private MenuFlyoutItem? _tabReloadItem;
    private MenuFlyoutItem? _tabCloseOthersItem;
    private MenuFlyoutItem? _tabCloseRightItem;
    private MenuFlyoutItem? _tabCloseAllItem;
    private MenuFlyoutItem? _tabReopenItem;
    private MenuFlyoutItem? _tabRevealItem;
    private MenuFlyoutItem? _tabPrintItem;

    /// <summary>
    /// The Copy submenu and the four rows in it.
    ///
    /// A submenu rather than four siblings: four is where one earns its hover, and a context
    /// menu that spends four of its rows on variations of copying reads as a menu about the
    /// clipboard rather than about the document.
    /// </summary>
    private MenuFlyoutSubItem? _tabCopyMenu;

    private MenuFlyoutItem? _tabCopyNameItem;
    private MenuFlyoutItem? _tabCopyRelativeItem;
    private MenuFlyoutItem? _tabCopyPathItem;
    private MenuFlyoutItem? _tabCopyLinkItem;

    /// <summary>The tab the menu was opened on, captured for the Copy items.</summary>
    private DocumentTabViewModel? _clickedTab;

    /// <summary>
    /// The folder a relative path is measured from: where the document that was in front sat
    /// when the menu opened, *before* the right-click selected the tab under the pointer.
    ///
    /// This is the whole of what makes Copy Relative Path mean anything. Right-clicking a tab
    /// selects it — that is what lets every other item here use the same active-document command
    /// the File menu uses — so by the time an item is chosen the clicked tab is the active one,
    /// and a path measured from "the active document" would be that document relative to itself.
    /// What the user wants is the path to write into the document they were already editing.
    /// </summary>
    private string? _tabMenuRelativeTo;

    /// <summary>
    /// The read-only tick. Its own field type because <see cref="ToggleMenuFlyoutItem"/> is a
    /// sibling of <see cref="MenuFlyoutItem"/> under MenuFlyoutItemBase rather than a subclass
    /// of it, so the Item helper that builds every other row here cannot make one.
    /// </summary>
    private ToggleMenuFlyoutItem? _tabReadOnlyItem;

    /// <summary>
    /// The pinned tick. Its own field type for the reason the read-only one gives, and its own
    /// guard below for a sharper version of the same reason.
    /// </summary>
    private ToggleMenuFlyoutItem? _tabPinnedItem;

    /// <summary>
    /// Set while <see cref="RefreshTabMenu"/> is writing the pinned tick.
    ///
    /// Not belt and braces the way the read-only one is. That tick and this one are both written
    /// on every right-click, but the cost of a stray Click differs: there it would toggle a mark
    /// the user would see on the next glance, and here it would move the tab to the other side of
    /// the strip while the menu was still open on it.
    /// </summary>
    private bool _settingTabPinnedTick;

    /// <summary>
    /// Set while <see cref="RefreshTabMenu"/> is writing the tick, so the Click handler can tell
    /// the user's choice from the menu's own bookkeeping.
    ///
    /// Belt and braces rather than a fix for something observed: WinUI raises Click on invoke
    /// rather than on an assignment to IsChecked, so this should never be the thing that saves
    /// it. The cost of being wrong is the reason it is here anyway - RefreshTabMenu runs every
    /// time the menu opens, so a Click raised from it would toggle the document on every
    /// right-click, which is about the worst failure this feature could have.
    /// </summary>
    private bool _settingTabReadOnlyTick;

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

        // Both captured before the selection moves. See _tabMenuRelativeTo for why the order
        // of these two lines is the feature rather than an incidental.
        _clickedTab = tab;
        _tabMenuRelativeTo = ViewModel.ActiveDocumentFolder;

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

        // With the file commands rather than in a group of its own: it is a fact about the
        // file, and it decides what Save above it will do.
        //
        // Wired directly rather than through Item, which returns a MenuFlyoutItem. This one
        // raises no dialog, so the flyout's Closed hands the keyboard back for it, exactly as
        // it does for the last two items in the menu.
        _tabReadOnlyItem = new ToggleMenuFlyoutItem { Text = "Read-Only" };

        _tabReadOnlyItem.Click += (_, _) =>
        {
            // Guards against the menu's own tick-writing being read as the user's choice. See
            // the field for why it is here when it should not be reachable.
            if (_settingTabReadOnlyTick)
            {
                return;
            }

            _ = ViewModel.ToggleReadOnlyCommand.ExecuteAsync(null);
        };

        menu.Items.Add(_tabReadOnlyItem);
        menu.Items.Add(new MenuFlyoutSeparator());

        // Its own group: the document acted on, rather than the file on disk above or the
        // tab itself below. Routed through Item like the rest, so the print dialog counts as
        // an action that hands the keyboard back itself.
        _tabPrintItem = Item("Print...", "Ctrl+P", () => ViewModel.PrintCommand.ExecuteAsync(null));
        menu.Items.Add(_tabPrintItem);
        menu.Items.Add(new MenuFlyoutSeparator());

        // Reveal and copy sit above the closing group rather than below it, which is where
        // they used to be. Two reasons, and the second is the stronger. A menu's last group is
        // the one a slipped pointer lands in, so it should not be the destructive one - which
        // is where Explorer, Chrome and VS Code all put theirs. And since the menu bar's File
        // menu gave these two up, this is the only place in the app they can be reached, so
        // burying them under four ways to close things had them furthest from the pointer at
        // exactly the moment they became load-bearing.
        //
        // Neither raises a dialog, so both take no part in the focus dance below: the flyout's
        // Closed hands the keyboard back for them, as it does for a dismissal.
        _tabRevealItem = new MenuFlyoutItem { Text = "Open in File Explorer" };
        _tabRevealItem.Click += (_, _) => ViewModel.RevealInFolderCommand.Execute(null);

        menu.Items.Add(_tabRevealItem);
        menu.Items.Add(BuildTabCopyMenu());
        menu.Items.Add(new MenuFlyoutSeparator());

        // Its own group directly above the closing one, because it guards three of the four
        // items in it. A reader who wonders why Close All Tabs left something behind finds the
        // answer on the row above rather than having to know it already.
        //
        // "Pinned" rather than "Pin Tab" / "Unpin Tab": a tick is what this menu already uses
        // for a per-tab boolean, and rewriting the text per tab would change the row's width
        // under the pointer between one right-click and the next.
        //
        // This one gets a tooltip where Read-Only does not, and the difference is deliberate.
        // Read-Only says what it does in its own name. What a pin does to the close commands is
        // not guessable from the word, and it is the half people are surprised by.
        _tabPinnedItem = new ToggleMenuFlyoutItem { Text = "Pinned" };

        ToolTipService.SetToolTip(
            _tabPinnedItem,
            "Keep this tab at the left of the strip, and out of Close Other Tabs, "
            + "Close Tabs to the Right and Close All Tabs");

        _tabPinnedItem.Click += (_, _) =>
        {
            // The same guard Read-Only takes, and here it earns its keep rather than being belt
            // and braces: RefreshTabMenu writes this tick on every right-click, and a Click
            // raised from that write would send the tab across the strip each time the menu was
            // opened on it.
            if (_settingTabPinnedTick)
            {
                return;
            }

            _ = ViewModel.ToggleTabPinCommand.ExecuteAsync(null);
        };

        menu.Items.Add(_tabPinnedItem);
        menu.Items.Add(new MenuFlyoutSeparator());

        _tabCloseOthersItem = Item(
            "Close Other Tabs",
            null,
            () => ViewModel.CloseOtherTabsCommand.ExecuteAsync(null));

        // The one people actually reach for: a chain of documents opened while following
        // something through, done with, and wanted gone without taking the tabs to the left
        // that the chain started from. Below Close Other Tabs rather than above it, so the
        // three read from blunt to precise in the order Chrome, Visual Studio and VS Code
        // have taught.
        _tabCloseRightItem = Item(
            "Close Tabs to the Right",
            null,
            () => ViewModel.CloseTabsToTheRightCommand.ExecuteAsync(null));

        menu.Items.Add(Item("Close Tab", "Ctrl+W", () => ViewModel.CloseTabCommand.ExecuteAsync(null)));
        // Kept in a field like the other two, now that a pin can make it close nothing.
        _tabCloseAllItem = Item(
            "Close All Tabs",
            "Ctrl+Shift+W",
            () => ViewModel.CloseAllTabsCommand.ExecuteAsync(null));

        menu.Items.Add(_tabCloseOthersItem);
        menu.Items.Add(_tabCloseRightItem);
        menu.Items.Add(_tabCloseAllItem);

        // The undo of the four above it, and the reason it is in their group rather than in
        // one of its own. It is also the one item here that answers about the workspace rather
        // than about the clicked tab - see the note at the top of this file, which now says so.
        // The strip is where you realize you closed the wrong thing and where your hand already
        // is, which is why every browser carries it here too.
        _tabReopenItem = Item(
            "Reopen Closed Tab",
            "Ctrl+Shift+T",
            () => ViewModel.ReopenLastClosedTabCommand.ExecuteAsync(null));

        menu.Items.Add(_tabReopenItem);

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
    /// The Copy submenu: four ways to name the clicked document, from the shortest to the most
    /// composed.
    ///
    /// The order is the order of usefulness in a markdown editor rather than of generality.
    /// Name and Relative Path are what goes inside a link between two documents in the same
    /// project, which is the common case; Full Path is what goes into a chat message or a bug
    /// report; the markdown link is all of it written out.
    ///
    /// None of these goes through <see cref="RunTabActionAsync"/>. Copying raises no dialog, so
    /// the flyout's Closed hands the keyboard back for them, exactly as it does for Open in File
    /// Explorer above.
    ///
    /// Every one reads <see cref="_clickedTab"/> at click time rather than capturing a tab here.
    /// These four items are built once and outlive every right-click - the same rule the pane
    /// menus' suggestion slots follow, and for the same reason.
    /// </summary>
    private MenuFlyoutSubItem BuildTabCopyMenu()
    {
        var copy = new MenuFlyoutSubItem { Text = "Copy" };

        _tabCopyNameItem = new MenuFlyoutItem { Text = "Name" };
        _tabCopyNameItem.Click += (_, _) => CopyClickedTab(tab =>
            tab.Document.DisplayName, "Name copied");

        _tabCopyRelativeItem = new MenuFlyoutItem { Text = "Relative Path" };
        _tabCopyRelativeItem.Click += (_, _) => CopyClickedTab(tab =>
            MainViewModel.RelativeLink(_tabMenuRelativeTo, tab.Document.DisplayPath), "Relative path copied");

        _tabCopyPathItem = new MenuFlyoutItem { Text = "Full Path" };
        _tabCopyPathItem.Click += (_, _) => CopyClickedTab(tab => tab.Document.DisplayPath, "Path copied");

        _tabCopyLinkItem = new MenuFlyoutItem { Text = "as Markdown Link" };
        _tabCopyLinkItem.Click += (_, _) => CopyClickedTab(MarkdownLinkFor, "Markdown link copied");

        copy.Items.Add(_tabCopyNameItem);
        copy.Items.Add(_tabCopyRelativeItem);
        copy.Items.Add(_tabCopyPathItem);
        copy.Items.Add(new MenuFlyoutSeparator());
        copy.Items.Add(_tabCopyLinkItem);

        _tabCopyMenu = copy;

        return copy;
    }

    /// <summary>
    /// A finished markdown link to the clicked document, relative to the one that was in front.
    ///
    /// Null when there is no relative path to be had, rather than falling back to an absolute
    /// one: a link to <c>C:/Users/...</c> works on exactly one machine, and writing one into a
    /// document that is about to be shared is worse than the command being unavailable. The row
    /// is grayed for that case anyway - this is the belt to that braces.
    /// </summary>
    private string? MarkdownLinkFor(DocumentTabViewModel tab) =>
        MainViewModel.RelativeLink(_tabMenuRelativeTo, tab.Document.DisplayPath) is { } relative
            ? $"[{MainViewModel.LinkTitleFor(tab)}]({MainViewModel.LinkTarget(relative)})"
            : null;

    /// <summary>
    /// Runs one of the Copy items against whatever tab the menu was opened on.
    ///
    /// The tab is read from the field here rather than captured when the item was built, for the
    /// reason <see cref="BuildTabCopyMenu"/> gives.
    /// </summary>
    private void CopyClickedTab(Func<DocumentTabViewModel, string?> value, string announcement)
    {
        if (_clickedTab is { } tab)
        {
            ViewModel.CopyForTab(value(tab), announcement);
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
            || _tabReadOnlyItem is null
            || _tabPinnedItem is null
            || _tabCloseOthersItem is null
            || _tabCloseRightItem is null
            || _tabCloseAllItem is null
            || _tabReopenItem is null
            || _tabRevealItem is null
            || _tabCopyMenu is null
            || _tabCopyNameItem is null
            || _tabCopyRelativeItem is null
            || _tabCopyPathItem is null
            || _tabCopyLinkItem is null
            || _tabPrintItem is null)
        {
            return;
        }

        // Named, because this menu belongs to one tab rather than to "the document". The full
        // name rather than the tab's shortened one - the shortening exists because a tab is a
        // fixed width - but capped all the same, because a menu is not unbounded either and a
        // hundred-character file name would draw a flyout wider than the display. Cut through
        // the middle by the same helper the tabs use, so a shortened name reads alike in both.
        _tabSaveItem.Text = $"Save \"{TabTitleFitter.Clamp(tab.Title, MaximumNamedTitle)}\"";

        // The read-only half is not decoration here: this item does not go through the Save
        // command's CanExecute, so without it a marked document would offer a Save that the
        // workspace turns away.
        _tabSaveItem.IsEnabled = tab.IsDirty && !tab.IsReadOnly;

        // The tick follows the mark rather than IsReadOnly, which also answers false for a
        // document whose file has been deleted - saving is how that one comes back, and the
        // user's mark should still be showing where they put it.
        //
        // Guarded because assigning IsChecked raises Click, which would read as the user
        // toggling it the moment the menu opened.
        _settingTabReadOnlyTick = true;
        _tabReadOnlyItem.IsChecked = tab.IsLocked;
        _settingTabReadOnlyTick = false;

        // An untitled document has no file for a mark to protect, and the mark is remembered
        // by path. Grayed rather than dropped, like Open in File Explorer below.
        _tabReadOnlyItem.IsEnabled = !tab.IsUntitled;

        // The pinned tick, read off the clicked tab rather than the view model for the reason
        // this whole method exists: the selection is applied through the workspace queue, and a
        // tab cannot be out of date about itself.
        _settingTabPinnedTick = true;
        _tabPinnedItem.IsChecked = tab.IsPinned;
        _settingTabPinnedTick = false;

        // Same rule as the mark above, and the same reason: a pin is remembered by path.
        _tabPinnedItem.IsEnabled = !tab.IsUntitled;

        _tabReloadItem.IsEnabled =
            !tab.IsUntitled
            && tab.Document.External != ExternalState.Missing
            && (tab.IsDirty || tab.Document.External == ExternalState.Changed);

        // Read off the strip rather than from the commands, for the reason this method exists:
        // the clicked tab knows where it is sooner than the view model does. These three mirror
        // CanCloseOthers, CanCloseTabsToTheRight and CanCloseAllTabs, and have to be changed with
        // them - a predicate written in one place and not the other gives either a gray item
        // that would have worked or a live one that silently closes nothing.
        //
        // All three now count what a command would actually take rather than how many tabs
        // there are, because a pinned tab is not one of them.
        int position = ViewModel.Tabs.IndexOf(tab);

        _tabCloseOthersItem.IsEnabled =
            ViewModel.Tabs.Any(other => other.Id != tab.Id && !other.IsPinned);

        _tabCloseRightItem.IsEnabled =
            position >= 0 && ViewModel.Tabs.Skip(position + 1).Any(right => !right.IsPinned);

        _tabCloseAllItem.IsEnabled = ViewModel.Tabs.Any(any => !any.IsPinned);

        // The only item here that is not about the clicked tab, so it is the only one whose
        // enabled state comes straight off its command.
        _tabReopenItem.IsEnabled = ViewModel.ReopenLastClosedTabCommand.CanExecute(null);

        // Nothing to reveal or copy until the document has been written somewhere. Grayed
        // rather than dropped, so the menu keeps one shape whichever tab it is opened on and
        // its items do not move under the pointer.
        _tabRevealItem.IsEnabled = !tab.IsUntitled;
        _tabCopyMenu.IsEnabled = !tab.IsUntitled;
        _tabCopyNameItem.IsEnabled = !tab.IsUntitled;
        _tabCopyPathItem.IsEnabled = !tab.IsUntitled;

        // The two relative rows need a second thing the other two do not: somewhere to be
        // relative *to*. There is no relative path from an unsaved document, and none at all
        // between two files on different drives - so rather than quietly handing back an
        // absolute path from a command that promised a relative one, both rows go gray and say
        // why. See MainViewModel.RelativeLink.
        bool relative = !tab.IsUntitled
            && MainViewModel.RelativeLink(_tabMenuRelativeTo, tab.Document.DisplayPath) is not null;

        _tabCopyRelativeItem.IsEnabled = relative;
        _tabCopyLinkItem.IsEnabled = relative;

        string? why = relative
            ? null
            : _tabMenuRelativeTo is null
                ? "The document you were in has not been saved, so there is nowhere to be relative to"
                : "This document is on another drive, so there is no relative path between the two";

        ToolTipService.SetToolTip(_tabCopyRelativeItem, why);
        ToolTipService.SetToolTip(_tabCopyLinkItem, why);

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
