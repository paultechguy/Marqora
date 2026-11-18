// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Xaml.Controls;
using PaulTechGuy.MQ.App.ViewModels;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// The folder panel's right-click menus, one per kind of row (WP-7, P12, P14).
///
/// | Row | Items |
/// |:---|:---|
/// | Markdown | Open, Open in File Explorer, Copy, Rename, Delete |
/// | Image | Insert Link in Source, Open, Open in File Explorer, Copy, Rename, Delete |
/// | Other file (Show all files) | Open in File Explorer, Copy |
/// | Folder | New Document Here, Open All in Tabs, Set as Root, Open in File Explorer, Copy, Rename, Delete |
///
/// The labels follow the tab menu's (P14): Open in File Explorer, and Copy with Name, Relative
/// Path and Full Path. Relative Path is measured from the active document, which is what someone
/// pasting a link wants, and is grayed when there is no saved document to measure from.
///
/// The read-only rule (CLAUDE.md, <i>Read-only documents</i>) has its own list here rather than
/// the main window's, because the menu is the panel's (C10): an item that writes into the active
/// document is registered with <see cref="Writes"/> and grayed while that document is read-only.
/// Insert Link in Source is the one item that writes.
/// </summary>
internal sealed class FolderPanelMenus(MainViewModel main, Action<FolderRowViewModel> beginRename)
{
    private readonly List<MenuFlyoutItem> _writes = [];

    /// <summary>The menu for one row, built as it is asked for, so every item reflects that row as it is now.</summary>
    public MenuFlyout For(FolderRowViewModel row)
    {
        _writes.Clear();

        var menu = new MenuFlyout();

        if (row.IsFolder)
        {
            menu.Items.Add(Item("New Document Here", () => main.NewDocumentHere(row.FullPath)));
            menu.Items.Add(Item("Open All in Tabs", () => _ = main.OpenAllInTabsAsync(row.FullPath)));
            menu.Items.Add(Item("Set as Root", () => _ = main.UseFolderAsRootFromPanelAsync(row.FullPath)));
            menu.Items.Add(new MenuFlyoutSeparator());
        }
        else if (row.IsMarkdown)
        {
            menu.Items.Add(Item("Open", () => _ = main.OpenFromFolderPanelAsync(row.FullPath)));
            menu.Items.Add(new MenuFlyoutSeparator());
        }
        else if (row.IsImage)
        {
            MenuFlyoutItem insert = Writes(Item("Insert Link in Source", () => _ = main.InsertImageLinkFromPanelAsync(row.FullPath)));

            // Grayed with the reason as its tooltip (P24): an untitled document, Preview view, or
            // an image the preview would not show.
            if (main.WhyCannotLinkImage(row.FullPath) is { } why)
            {
                insert.IsEnabled = false;
                ToolTipService.SetToolTip(insert, why);
            }

            menu.Items.Add(insert);
            menu.Items.Add(Item("Open", () => main.LaunchImageFromPanel(row.FullPath)));
            menu.Items.Add(new MenuFlyoutSeparator());
        }

        menu.Items.Add(Item("Open in File Explorer", () => main.RevealInExplorer(row.FullPath)));
        menu.Items.Add(CopyMenu(row));

        // A shown non-markdown file gets Explorer and Copy, and nothing that changes it.
        if (row.IsFolder || row.IsMarkdown || row.IsImage)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(Item("Rename", () => beginRename(row), "F2"));

            MenuFlyoutItem delete = Item("Delete", () => _ = main.DeleteFromPanelAsync(row.FullPath), "Delete");

            // Grayed with the reason as its tooltip (P8, P25): the mark protects against a stray
            // keystroke, and deleting its folder is the same keystroke.
            if (main.WhyCannotDelete(row.FullPath) is { } why)
            {
                delete.IsEnabled = false;
                ToolTipService.SetToolTip(delete, why);
            }

            menu.Items.Add(delete);
        }

        menu.Opening += (_, _) => GrayWritesIfReadOnly();

        return menu;
    }

    /// <summary>Registers an item that writes into the active document, so it grays while that document is read-only.</summary>
    public MenuFlyoutItem Writes(MenuFlyoutItem item)
    {
        _writes.Add(item);
        return item;
    }

    private void GrayWritesIfReadOnly()
    {
        foreach (MenuFlyoutItem item in _writes)
        {
            // Only ever grays: an item may already be grayed for a reason of its own.
            if (main.ActiveTabIsReadOnly)
            {
                item.IsEnabled = false;
            }
        }
    }

    private MenuFlyoutSubItem CopyMenu(FolderRowViewModel row)
    {
        var copy = new MenuFlyoutSubItem { Text = "Copy" };

        copy.Items.Add(Item("Name", () => main.CopyFromPanel(row.FullPath, PanelCopy.Name)));

        // A folder has no relative path anyone would paste as a link.
        if (!row.IsFolder)
        {
            MenuFlyoutItem relative = Item("Relative Path", () => main.CopyFromPanel(row.FullPath, PanelCopy.RelativePath));

            if (main.CopyTextFor(row.FullPath, PanelCopy.RelativePath) is null)
            {
                relative.IsEnabled = false;
                ToolTipService.SetToolTip(relative, "Relative to the active document, which has not been saved");
            }

            copy.Items.Add(relative);
        }

        copy.Items.Add(Item("Full Path", () => main.CopyFromPanel(row.FullPath, PanelCopy.FullPath)));

        return copy;
    }

    private static MenuFlyoutItem Item(string text, Action act, string? keyText = null)
    {
        var item = new MenuFlyoutItem { Text = text };

        if (keyText is not null)
        {
            item.KeyboardAcceleratorTextOverride = keyText;
        }

        item.Click += (_, _) => act();

        return item;
    }
}
