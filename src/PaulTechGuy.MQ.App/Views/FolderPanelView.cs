// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PaulTechGuy.MQ.App.ViewModels;
using PaulTechGuy.MQ.Folders;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// The folder panel's face: the header naming the root, the filter box, the tree, the empty
/// states and the foot line.
///
/// Built in code rather than declared in MainWindow.xaml, because it has two hosts: the main
/// window's column now, and its own floating window in WP-9. Each host builds one of these over
/// the one <see cref="FolderPanelViewModel"/> (P5). Nothing here reads a theme brush in code
/// (C25): text is dimmed by opacity, and the teal bar lives in the row style's template.
///
/// The interaction follows the outline's: arrows move without leaving the panel, a click or Enter
/// acts, Escape goes back to the document. A markdown file opened from here hands the keyboard to
/// the editor, which is the main view model's to arrange.
/// </summary>
internal sealed partial class FolderPanelView : Grid
{
    /// <summary>
    /// The drag format an image row carries its path in. The web view does not take a drag that
    /// starts inside the app, so the main window takes the drop and looks for this (WP-8).
    /// </summary>
    public const string ImageDragFormat = "Marqora.FolderImage";

    private readonly FolderPanelViewModel _panel;
    private readonly MainViewModel _main;

    private readonly Button _collapse;
    private readonly FontIcon _collapseGlyph;
    private readonly TextBlock _title;
    private readonly Button _pin;
    private readonly FontIcon _pinGlyph = new() { Style = MqStyles.PanelIcon, Opacity = 0.7 };
    private readonly ToggleMenuFlyoutItem _revealItem;
    private readonly ToggleMenuFlyoutItem _showAllItem;
    private readonly ToggleMenuFlyoutItem _sortItem;
    private readonly InfoBar _notice;
    private readonly Button _undo;
    private readonly FolderPanelMenus _menus;
    private readonly TextBox _filter;
    private readonly FolderTreeList _list;
    private readonly StackPanel _noRoot;
    private readonly TextBlock _noRootMessage;
    private readonly Button _openFolder;
    private readonly Button _useDocumentFolder;
    private readonly StackPanel _empty;
    private readonly TextBlock _emptyMessage;
    private readonly HyperlinkButton _showAllLink;
    private readonly TextBlock _incomplete;
    private readonly StackPanel _outside;
    private readonly Grid _body;

    /// <summary>Set while the view moves the selection itself, so it is not taken for the user's.</summary>
    private bool _selectingInCode;

    private string _typeAhead = string.Empty;
    private DateTime _typeAheadAt;

    public FolderPanelView(FolderPanelViewModel panel, MainViewModel main)
    {
        _panel = panel;
        _main = main;

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ------------------------------------------------------------ header

        var header = new Grid { Margin = new Thickness(4, 6, 6, 2) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _collapseGlyph = new FontIcon { Glyph = "\uE70D", FontSize = 10, Opacity = 0.7 };
        _collapse = new Button
        {
            Style = MqStyles.PanelButton,
            Content = _collapseGlyph,
        };
        _collapse.Click += (_, _) => IsSectionCollapsed = !IsSectionCollapsed;
        AutomationProperties.SetName(_collapse, "Collapse the folder section");
        ToolTipService.SetToolTip(_collapse, "Collapse");

        _title = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            CharacterSpacing = 40,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 4, 0),
        };
        Microsoft.UI.Xaml.Documents.Typography.SetCapitals(_title, FontCapitals.SmallCaps);

        _revealItem = new ToggleMenuFlyoutItem { Text = "Reveal Active Document" };
        _revealItem.Click += (_, _) => _panel.SetRevealActive(_revealItem.IsChecked);
        _showAllItem = new ToggleMenuFlyoutItem { Text = "Show All Files" };
        _showAllItem.Click += (_, _) => _panel.SetShowAllFiles(_showAllItem.IsChecked);
        _sortItem = new ToggleMenuFlyoutItem { Text = "Sort by Modified Date" };
        _sortItem.Click += (_, _) => _panel.SetSortByModified(_sortItem.IsChecked);

        var refresh = new MenuFlyoutItem { Text = "Refresh" };
        refresh.Click += async (_, _) => await _panel.RefreshAsync();
        var collapseAll = new MenuFlyoutItem { Text = "Collapse All" };
        collapseAll.Click += (_, _) => _panel.CollapseAll();

        // Recent Folders, newest first, leaving out the folder already shown. Filled as the menu
        // opens; a folder that has gone is said so when picked, and dropped from the list.
        var recent = new MenuFlyoutSubItem { Text = "Recent Folders" };

        var closeFolder = new MenuFlyoutItem { Text = "Close Folder", Command = _main.CloseFolderCommand };

        // The root folder's own two (WP-7): what a folder row's menu offers, for the folder shown.
        var newHere = new MenuFlyoutItem { Text = "New Document Here" };
        newHere.Click += (_, _) =>
        {
            if (_panel.Root is { } folder)
            {
                _main.NewDocumentHere(folder);
            }
        };

        var openAll = new MenuFlyoutItem { Text = "Open All in Tabs" };
        openAll.Click += async (_, _) =>
        {
            if (_panel.Root is { } folder)
            {
                await _main.OpenAllInTabsAsync(folder);
            }
        };

        // The panel's one menu (P26). The header has no right-click of its own: two menus on one
        // header, sharing half their items, was one more thing to learn.
        var menu = new MenuFlyout();
        menu.Items.Add(newHere);
        menu.Items.Add(openAll);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(_revealItem);
        menu.Items.Add(_showAllItem);
        menu.Items.Add(_sortItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(refresh);
        menu.Items.Add(collapseAll);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(recent);
        menu.Items.Add(closeFolder);
        menu.Opening += (_, _) =>
        {
            _revealItem.IsChecked = _panel.RevealActive;
            _showAllItem.IsChecked = _panel.ShowAllFiles;
            _sortItem.IsChecked = _panel.SortByModified;
            refresh.IsEnabled = _panel.HasRoot;
            collapseAll.IsEnabled = _panel.HasRoot;
            newHere.IsEnabled = _panel.HasRoot;
            openAll.IsEnabled = _panel.HasRoot;
            FillRecentFolders(recent);
        };

        var more = new Button
        {
            Style = MqStyles.PanelButton,
            Content = new FontIcon { Glyph = "\uE712", Style = MqStyles.PanelIcon, Opacity = 0.7 },
            Flyout = menu,
        };
        AutomationProperties.SetName(more, "Folder panel menu");
        ToolTipService.SetToolTip(more, "Folder panel menu");

        // Pin and unpin (P29): docked in the column, or a tab on the window's edge that slides the
        // panel out over the text. The glyph shows what a click will do. There is no X: a panel is
        // always somewhere, docked or on the edge, so it can never be lost.
        _pin = new Button
        {
            Style = MqStyles.PanelButton,
            Content = _pinGlyph,
            Command = _main.ToggleFolderPanelPinnedCommand,
        };

        SetColumn(_collapse, 0);
        SetColumn(_title, 1);
        SetColumn(more, 2);
        SetColumn(_pin, 3);
        header.Children.Add(_collapse);
        header.Children.Add(_title);
        header.Children.Add(more);
        header.Children.Add(_pin);

        SetRow(header, 0);
        Children.Add(header);

        // ------------------------------------------------------------ notice

        // What a rename or delete did, with Undo after a rename (WP-7). It stays until the next
        // operation replaces it or its X dismisses it; never on a timer.
        _undo = new Button { Content = "Undo", Style = MqStyles.CommandButton };
        _undo.Click += async (_, _) => await _panel.UndoNoticeAsync();

        _notice = new InfoBar
        {
            IsClosable = true,
            Margin = new Thickness(6, 2, 6, 4),
            ActionButton = _undo,
        };
        _notice.CloseButtonClick += (_, _) => _panel.ClearNotice();

        SetRow(_notice, 1);
        Children.Add(_notice);

        _menus = new FolderPanelMenus(_main, BeginRename);

        // ------------------------------------------------------------ filter

        _filter = new TextBox
        {
            Margin = new Thickness(8, 2, 8, 6),
            PlaceholderText = "Filter files",
        };
        AutomationProperties.SetName(_filter, "Filter the folder");
        _filter.TextChanged += (_, _) =>
        {
            if (_filter.Text != _panel.Filter)
            {
                _panel.Filter = _filter.Text;
            }
        };
        _filter.KeyDown += OnFilterKeyDown;
        _filter.GotFocus += (_, _) => _main.NotifyFolderPanelFocused();

        SetRow(_filter, 2);
        Children.Add(_filter);

        // -------------------------------------------------------------- body

        _body = new Grid();

        _list = new FolderTreeList
        {
            ItemsSource = _panel.Rows,
            Padding = new Thickness(0, 0, 0, 8),
            SetExpanded = SetExpandedAsync,
            CommitRename = CommitRenameAsync,
            CancelRename = row => EndRename(row, keepFocus: true),
            CheckRename = CheckRename,
        };
        AutomationProperties.SetName(_list, "Folder");
        _list.SelectionChanged += OnSelectionChanged;
        _list.KeyDown += OnListKeyDown;
        _list.CharacterReceived += OnCharacterReceived;
        _list.GotFocus += (_, _) => _main.NotifyFolderPanelFocused();
        _list.AddHandler(UIElement.TappedEvent, new TappedEventHandler(OnTapped), handledEventsToo: true);
        _list.AddHandler(UIElement.DoubleTappedEvent, new DoubleTappedEventHandler(OnDoubleTapped), handledEventsToo: true);

        // An image row drags its link as plain text (WP-8, P24), for the source pane to take at the
        // drop point. Text only, never the file: a file dropped on the WebView is navigated to.
        _list.CanDragItems = true;
        _list.DragItemsStarting += OnDragItemsStarting;

        // A row's own menu (WP-7). ContextRequested rather than RightTapped, so the Menu key and a
        // press-and-hold raise it too; handled events, because a ListViewItem marks them on its way.
        _list.AddHandler(
            UIElement.ContextRequestedEvent,
            new Windows.Foundation.TypedEventHandler<UIElement, ContextRequestedEventArgs>(OnRowContextRequested),
            handledEventsToo: true);

        _emptyMessage = Caption();
        _showAllLink = new HyperlinkButton
        {
            Content = "Show all files",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _showAllLink.Click += (_, _) => _panel.SetShowAllFiles(true);
        _empty = new StackPanel
        {
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _emptyMessage, _showAllLink },
        };

        _noRootMessage = Caption();
        _noRootMessage.Opacity = 0.8;

        _openFolder = new Button
        {
            Style = MqStyles.PrimaryCommandButton,
            Content = "Open Folder",
            Command = _main.OpenFolderCommand,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _useDocumentFolder = new Button
        {
            Style = MqStyles.CommandButton,
            Content = "Use this document's folder",
            Command = _main.UseActiveDocumentFolderCommand,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        _noRoot = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(12, 16, 12, 16),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _noRootMessage, _openFolder, _useDocumentFolder },
        };

        _body.Children.Add(_empty);
        _body.Children.Add(_list);
        _body.Children.Add(_noRoot);

        SetRow(_body, 3);
        Children.Add(_body);

        // -------------------------------------------------------------- foot

        _incomplete = Caption();
        _incomplete.Text = "Not every folder was searched.";
        _incomplete.TextAlignment = TextAlignment.Left;

        var showFolder = new HyperlinkButton { Content = "Show its folder", Padding = new Thickness(0) };
        showFolder.Click += (_, _) => _main.UseActiveDocumentFolderCommand.Execute(null);

        var outsideText = Caption();
        outsideText.Text = "This document is in another folder.";
        outsideText.TextAlignment = TextAlignment.Left;

        _outside = new StackPanel { Spacing = 0, Children = { outsideText, showFolder } };

        var foot = new StackPanel
        {
            Margin = new Thickness(12, 2, 12, 8),
            Spacing = 4,
            Children = { _incomplete, _outside },
        };

        SetRow(foot, 4);
        Children.Add(foot);

        _panel.PropertyChanged += OnPanelChanged;
        _panel.ScrollIntoViewRequested += OnScrollIntoViewRequested;
        _main.PropertyChanged += OnMainChanged;

        Refresh();
    }

    /// <summary>The section folded to its header, giving its height to the outline. The host lays the column out.</summary>
    public bool IsSectionCollapsed
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            Refresh();
            SectionCollapsedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? SectionCollapsedChanged;

    /// <summary>
    /// Whether the fold chevron is offered: only while the panel is docked above the outline,
    /// where folding gives the outline the height. Set by the host as it lays the column out.
    /// </summary>
    public bool CanFold
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            Refresh();
        }
    }

    /// <summary>
    /// Puts the keyboard in the panel: the Open Folder button with no root, otherwise the list, on
    /// the selected row, or the active document's.
    /// </summary>
    public bool FocusPanel()
    {
        IsSectionCollapsed = false;

        if (!_panel.HasRoot)
        {
            return _openFolder.Focus(FocusState.Programmatic);
        }

        if (_list.SelectedItem is null && _panel.Rows.FirstOrDefault(r => r.IsActive) is { } active)
        {
            Select(active);
        }

        if (_panel.Rows.Count == 0)
        {
            return _filter.Focus(FocusState.Programmatic);
        }

        if (_list.SelectedItem is null)
        {
            Select(_panel.Rows[0]);
        }

        return _list.Focus(FocusState.Programmatic);
    }

    /// <summary>Stops following the view models, when the host is done with this view.</summary>
    public void Detach()
    {
        _panel.PropertyChanged -= OnPanelChanged;
        _panel.ScrollIntoViewRequested -= OnScrollIntoViewRequested;
        _main.PropertyChanged -= OnMainChanged;
        _list.ItemsSource = null;
    }

    /// <summary>Lists the recent folders other than the one shown, or says there are none.</summary>
    private void FillRecentFolders(MenuFlyoutSubItem recent)
    {
        recent.Items.Clear();

        foreach (string folder in _panel.RecentFolders)
        {
            if (_panel.Root is { } root && Folders.FolderPaths.Equal(root, folder))
            {
                continue;
            }

            string name = Path.GetFileName(Folders.FolderPaths.TrimEnd(folder));
            var item = new MenuFlyoutItem { Text = name.Length == 0 ? folder : name };
            ToolTipService.SetToolTip(item, folder);
            item.Click += async (_, _) => await _main.OpenRecentFolderAsync(folder);
            recent.Items.Add(item);
        }

        if (recent.Items.Count == 0)
        {
            recent.Items.Add(new MenuFlyoutItem { Text = "No other recent folders", IsEnabled = false });
        }
    }
    private static TextBlock Caption() => new()
    {
        Opacity = 0.6,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center,
        FontSize = 12,
    };

    // ------------------------------------------------------------ state

    private void OnPanelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FolderPanelViewModel.Filter) && _filter.Text != _panel.Filter)
        {
            _filter.Text = _panel.Filter;
        }

        Refresh();
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.FolderPanelHasFocus) or nameof(MainViewModel.FolderPanelPinned))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        bool hasRoot = _panel.HasRoot;

        // Folded only while it can be: slid out, or alone in the column, it is always the whole panel.
        bool collapsed = IsSectionCollapsed && CanFold;

        _title.Text = hasRoot ? _panel.RootName : "Folder";
        _collapse.Visibility = Show(CanFold);
        bool pinned = _main.FolderPanelPinned;
        _pinGlyph.Glyph = pinned ? "\uE77A" : "\uE718";
        ToolTipService.SetToolTip(_pin, pinned ? "Unpin: move the folder panel to the window edge" : "Pin: dock the folder panel in the column");
        AutomationProperties.SetName(_pin, pinned ? "Unpin the folder panel" : "Pin the folder panel");
        ToolTipService.SetToolTip(_title, hasRoot ? _panel.RootTooltip : null);

        _collapseGlyph.Glyph = collapsed ? "\uE76C" : "\uE70D";
        AutomationProperties.SetName(_collapse, collapsed ? "Expand the folder section" : "Collapse the folder section");
        ToolTipService.SetToolTip(_collapse, collapsed ? "Expand" : "Collapse");

        _filter.Visibility = Show(hasRoot && !collapsed);
        _body.Visibility = Show(!collapsed);

        _noRoot.Visibility = Show(!hasRoot);
        _noRootMessage.Text = _panel.NoRootMessage;
        _useDocumentFolder.Visibility = Show(_panel.CanUseActiveFolder);

        _list.Visibility = Show(hasRoot);
        _empty.Visibility = Show(_panel.ShowsEmptyMessage);
        _emptyMessage.Text = _panel.EmptyMessage;
        _showAllLink.Visibility = Show(_panel.ShowsShowAllFilesLink);

        _notice.IsOpen = _panel.HasNotice && !collapsed;
        _notice.Message = _panel.NoticeText ?? string.Empty;
        _notice.Severity = _panel.NoticeIsError ? InfoBarSeverity.Error : InfoBarSeverity.Informational;
        _undo.Visibility = Show(_panel.CanUndoNotice);

        _incomplete.Visibility = Show(!collapsed && _panel.IsFilterIncomplete);
        _outside.Visibility = Show(!collapsed && hasRoot && _panel.IsActiveOutsideRoot);
    }

    private static Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    // ------------------------------------------------------------ the list

    private void Select(FolderRowViewModel row)
    {
        _selectingInCode = true;

        try
        {
            _list.SelectedItem = row;
        }
        finally
        {
            _selectingInCode = false;
        }

        _panel.SelectedPath = row.FullPath;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_selectingInCode)
        {
            _panel.SelectedPath = (_list.SelectedItem as FolderRowViewModel)?.FullPath;
        }
    }

    private void OnScrollIntoViewRequested(object? sender, FolderRowViewModel row)
    {
        // Never scrolled out from under someone browsing the tree.
        if (!_main.FolderPanelHasFocus)
        {
            _list.ScrollIntoView(row);
        }
    }

    private async void OnTapped(object sender, TappedRoutedEventArgs e)
    {
        if (RowAt(e.OriginalSource) is not { } row)
        {
            return;
        }

        await ActAsync(row, launchImage: false);
    }

    /// <summary>A double-click opens an image in whatever Windows opens it with (WP-8).</summary>
    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (RowAt(e.OriginalSource) is { IsImage: true } row)
        {
            e.Handled = true;
            _main.LaunchImageFromPanel(row.FullPath);
        }
    }

    /// <summary>
    /// Starts an image's drag with its link as text, or refuses it with the reason on the status
    /// line: an untitled, read-only or reviewed document, Preview view, or an image the preview
    /// would not show (P24). Anything that is not an image does not drag.
    /// </summary>
    private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items is not [FolderRowViewModel { IsImage: true } row] || _renaming is not null)
        {
            e.Cancel = true;
            return;
        }

        if (_main.ImageLinkFor(row.FullPath) is not { } link)
        {
            e.Cancel = true;
            _main.StatusText = _main.WhyCannotLinkImage(row.FullPath) ?? "That image cannot be linked";
            return;
        }

        // The path for the main window, which inserts at the drop point; the link as text for
        // anywhere else that takes text.
        e.Data.SetData(ImageDragFormat, row.FullPath);
        e.Data.SetText(link);
        e.Data.RequestedOperation = DataPackageOperation.Copy;
    }

    /// <summary>A click opens a markdown file or turns a folder; Enter also opens an image, as a double-click does.</summary>
    private async Task ActAsync(FolderRowViewModel row, bool launchImage)
    {
        if (row.IsMarkdown)
        {
            await _main.OpenFromFolderPanelAsync(row.FullPath);
        }
        else if (row.IsImage)
        {
            if (launchImage)
            {
                _main.LaunchImageFromPanel(row.FullPath);
            }
        }
        else
        {
            await _panel.ActivateAsync(row);
        }
    }

    private async Task SetExpandedAsync(FolderRowViewModel row, bool expand)
    {
        if (expand && !row.Row.IsExpanded)
        {
            await _panel.ExpandAsync(row);
        }
        else if (!expand && row.Row.IsExpanded)
        {
            _panel.Collapse(row);
        }
    }

    private async void OnListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Keys typed into a rename box are the box's, not the list's.
        if (e.OriginalSource is TextBox)
        {
            return;
        }

        if (_list.SelectedItem is not FolderRowViewModel row)
        {
            if (e.Key == VirtualKey.Escape)
            {
                e.Handled = true;
                await _main.LeaveFolderPanelAsync();
            }

            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Enter:
                e.Handled = true;
                await ActAsync(row, launchImage: true);
                break;

            case VirtualKey.Right when row.IsFolder && row.Row.CanExpand:
                e.Handled = true;

                if (!row.Row.IsExpanded)
                {
                    await _panel.ExpandAsync(row);
                }
                else if (IndexOf(row) is int at && at + 1 < _panel.Rows.Count && _panel.Rows[at + 1].Row.Depth > row.Row.Depth)
                {
                    MoveTo(_panel.Rows[at + 1]);
                }

                break;

            case VirtualKey.Left:
                e.Handled = true;

                if (row.IsFolder && row.Row.IsExpanded)
                {
                    _panel.Collapse(row);
                }
                else if (_panel.ParentOf(row) is { } parent)
                {
                    MoveTo(parent);
                }

                break;

            // The way back to the document, for anyone who arrived by keyboard.
            case VirtualKey.Escape:
                e.Handled = true;
                await _main.LeaveFolderPanelAsync();
                break;

            case VirtualKey.F2 when row.IsFolder || row.IsMarkdown || row.IsImage:
                e.Handled = true;
                BeginRename(row);
                break;

            // Grayed in the menu for a marked or reviewed document; the key says why on the
            // status line, which DeleteFromPanelAsync does before asking anything.
            case VirtualKey.Delete when row.IsFolder || row.IsMarkdown || row.IsImage:
                e.Handled = true;
                await _main.DeleteFromPanelAsync(row.FullPath);
                break;

            default:
                break;
        }
    }

    // ------------------------------------------------------------- rename

    private FolderRowViewModel? _renaming;

    /// <summary>
    /// Opens the name for editing in place. The tree holds still while it is open: changes on
    /// disk wait until the box closes, so no row moves out from under it (WP-1).
    /// </summary>
    private void BeginRename(FolderRowViewModel row)
    {
        if (_renaming is { } open && !ReferenceEquals(open, row))
        {
            EndRename(open, keepFocus: false);
        }

        if (row.IsRenaming)
        {
            return;
        }

        _panel.ClearNotice();
        _panel.HoldTree();
        _renaming = row;

        Select(row);
        _list.ScrollIntoView(row);
        row.RenameError = null;
        row.IsRenaming = true;
    }

    private void EndRename(FolderRowViewModel row, bool keepFocus)
    {
        if (!row.IsRenaming)
        {
            return;
        }

        row.IsRenaming = false;
        row.RenameError = null;

        if (ReferenceEquals(_renaming, row))
        {
            _renaming = null;
        }

        _panel.ReleaseTree();

        if (keepFocus && _panel.Rows.Contains(row))
        {
            MoveTo(row);
        }
    }

    /// <summary>Why a name cannot be used, as it is typed. The same name it had is not a problem, only nothing to do.</summary>
    private string? CheckRename(FolderRowViewModel row, string text)
    {
        NameCheck check = Check(row, text);

        return check.Problem is NameProblem.None or NameProblem.Unchanged ? null : check.Message;
    }

    private NameCheck Check(FolderRowViewModel row, string text) =>
        FileNameRules.Check(
            Path.GetDirectoryName(row.FullPath) ?? string.Empty,
            row.Name,
            text.Trim(),
            row.IsFolder,
            _panel.ShowAllFiles);

    private async Task CommitRenameAsync(FolderRowViewModel row, string text)
    {
        string from = row.FullPath;
        string name = text.Trim();
        NameCheck check = Check(row, name);

        EndRename(row, keepFocus: true);

        if (!check.IsAcceptable)
        {
            return;
        }

        // A markdown file given another extension leaves the tree unless Show All Files is on.
        if (check.LeavesTree && !await _main.ConfirmRenameLeavesTreeAsync(row.Name, name))
        {
            return;
        }

        string destination = Path.Combine(Path.GetDirectoryName(from) ?? string.Empty, name);

        if (await _main.RenameFromPanelAsync(from, destination)
            && _panel.Rows.FirstOrDefault(r => Folders.FolderPaths.Equal(r.FullPath, destination)) is { } renamed)
        {
            MoveTo(renamed);
        }
    }

    // ------------------------------------------------------------ the menus

    private void OnRowContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (RowAt(e.OriginalSource) is not { } row
            || _list.ContainerFromItem(row) is not ListViewItem container)
        {
            return;
        }

        e.Handled = true;
        Select(row);

        MenuFlyout menu = _menus.For(row);

        if (e.TryGetPosition(container, out Windows.Foundation.Point point))
        {
            menu.ShowAt(container, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = point });
        }
        else
        {
            menu.ShowAt(container);
        }
    }

    /// <summary>Typing jumps to the next row whose name starts with what was typed; a pause starts again.</summary>
    private void OnCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox || char.IsControl(e.Character) || _panel.Rows.Count == 0)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;

        _typeAhead = now - _typeAheadAt > TimeSpan.FromSeconds(1)
            ? e.Character.ToString()
            : _typeAhead + e.Character;
        _typeAheadAt = now;

        int start = _list.SelectedIndex < 0 ? 0 : _list.SelectedIndex;

        // A single letter steps to the next match; a longer run stays on the current one if it still fits.
        int first = _typeAhead.Length == 1 ? 1 : 0;

        for (int k = first; k < _panel.Rows.Count + first; k++)
        {
            FolderRowViewModel candidate = _panel.Rows[(start + k) % _panel.Rows.Count];

            if (candidate.Name.StartsWith(_typeAhead, StringComparison.CurrentCultureIgnoreCase))
            {
                MoveTo(candidate);
                e.Handled = true;
                return;
            }
        }
    }

    private void MoveTo(FolderRowViewModel row)
    {
        _list.SelectedItem = row;
        _list.ScrollIntoView(row);

        if (_list.ContainerFromItem(row) is ListViewItem container)
        {
            container.Focus(FocusState.Keyboard);
        }
    }

    private int? IndexOf(FolderRowViewModel row)
    {
        int index = _panel.Rows.IndexOf(row);

        return index < 0 ? null : index;
    }

    private async void OnFilterKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape when !string.IsNullOrEmpty(_panel.Filter):
                e.Handled = true;
                _panel.Filter = string.Empty;
                break;

            case VirtualKey.Escape:
                e.Handled = true;
                await _main.LeaveFolderPanelAsync();
                break;

            case VirtualKey.Down when _panel.Rows.Count > 0:
                e.Handled = true;

                if (_list.SelectedItem is null)
                {
                    Select(_panel.Rows[0]);
                }

                _list.Focus(FocusState.Keyboard);
                break;

            default:
                break;
        }
    }

    /// <summary>The row a pointer event started in, walking up from what was hit.</summary>
    private static FolderRowViewModel? RowAt(object? source)
    {
        for (DependencyObject? node = source as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            // The scroll bar is not a row, and a click in a rename box is the box's.
            if (node is Microsoft.UI.Xaml.Controls.Primitives.ScrollBar or TextBox)
            {
                return null;
            }

            if (node is ListViewItem { Content: FolderRowPresenter presenter })
            {
                return presenter.Row;
            }
        }

        return null;
    }
}
