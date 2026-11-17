// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PaulTechGuy.MQ.App.ViewModels;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// The folder panel's list: a flat <see cref="ListView"/> of the visible rows, read by Narrator as
/// a tree (FolderPanel P1).
///
/// Flat because a <c>TreeView</c> keeps expansion in its containers, and the panel's state has to
/// live in one view model that two hosts can show (P5). What a tree gives a screen reader is put
/// back by hand: each container carries its level and its place among its siblings, set as the
/// container is realized (containers are recycled, so it cannot be set once), and the list and
/// its items report the Tree and TreeItem control types, a folder with Expand and Collapse.
///
/// Rows are drawn in code in <see cref="ListViewBase.ContainerContentChanging"/>, as Find All's
/// are, so the list still virtualizes a large folder.
/// </summary>
internal sealed partial class FolderTreeList : ListView
{
    public FolderTreeList()
    {
        SelectionMode = ListViewSelectionMode.Single;
        IsItemClickEnabled = false;
        ItemContainerStyle = MqStyles.TreeRow;

        ContainerContentChanging += OnContainerContentChanging;
    }

    /// <summary>Expand and Collapse from a screen reader, which the panel does the same way a key would.</summary>
    public Func<FolderRowViewModel, bool, Task>? SetExpanded { get; set; }

    /// <summary>A rename box's text was committed (Enter, or the box losing focus).</summary>
    public Func<FolderRowViewModel, string, Task>? CommitRename { get; set; }

    /// <summary>A rename box was abandoned (Escape).</summary>
    public Action<FolderRowViewModel>? CancelRename { get; set; }

    /// <summary>Why a name typed so far cannot be used, or null; asked on every keystroke.</summary>
    public Func<FolderRowViewModel, string, string?>? CheckRename { get; set; }

    protected override DependencyObject GetContainerForItemOverride() => new FolderTreeItem(this);

    protected override AutomationPeer OnCreateAutomationPeer() => new FolderTreeListPeer(this);

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is not ListViewItem container)
        {
            return;
        }

        if (container.Content is FolderRowPresenter old)
        {
            old.Detach();
        }

        if (args.InRecycleQueue)
        {
            container.Content = null;
            return;
        }

        container.ContentTemplate = null;

        if (args.Item is not FolderRowViewModel row)
        {
            container.Content = null;
            return;
        }

        container.Content = new FolderRowPresenter(row, container, this);
        args.Handled = true;
    }
}

/// <summary>
/// One row's content, kept in step with its view model while it is realized: the indent and
/// chevron, the name (dimmed for images, other files and what .gitignore names), the marker, and
/// the container's automation properties and Active state.
/// </summary>
internal sealed partial class FolderRowPresenter : Grid
{
    /// <summary>How far each level is indented.</summary>
    private const double IndentStep = 12;

    /// <summary>How far dimmed text fades: still readable, plainly secondary.</summary>
    private const double DimmedOpacity = 0.55;

    private readonly ListViewItem _container;
    private readonly FolderTreeList _list;
    private readonly FontIcon _chevron;
    private readonly TextBlock _name;
    private readonly Border _marker;
    private TextBox? _renameBox;
    private TextBlock? _renameError;
    private bool _renameSettled;
    private FolderRowViewModel? _row;

    public FolderRowPresenter(FolderRowViewModel row, ListViewItem container, FolderTreeList list)
    {
        _container = container;
        _list = list;
        _row = row;

        ColumnSpacing = 4;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _chevron = new FontIcon
        {
            FontSize = 9,
            Width = 12,
            Opacity = 0.7,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _name = new TextBlock
        {
            FontSize = 12.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _marker = new Border { VerticalAlignment = VerticalAlignment.Center, MinWidth = 12 };

        SetColumn(_chevron, 0);
        SetColumn(_name, 1);
        SetColumn(_marker, 2);
        Children.Add(_chevron);
        Children.Add(_name);
        Children.Add(_marker);

        row.PropertyChanged += OnRowChanged;
        Draw();
    }

    public FolderRowViewModel? Row => _row;

    /// <summary>Stops following the view model, as the container is recycled or given another row.</summary>
    public void Detach()
    {
        if (_row is not null)
        {
            _row.PropertyChanged -= OnRowChanged;
            _row = null;
        }
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e) => Draw();

    private void Draw()
    {
        if (_row is not { } row)
        {
            return;
        }

        Margin = new Thickness(row.Row.Depth * IndentStep, 0, 0, 0);

        _chevron.Glyph = !row.Row.CanExpand ? string.Empty
            : row.Row.IsExpanded ? "\uE70D"
            : "\uE76C";

        _name.Text = row.Name;
        _name.Opacity = row.Row.IsDimmed ? DimmedOpacity : 1;
        _name.FontWeight = row.IsFolder ? FontWeights.SemiBold : FontWeights.Normal;

        _marker.Child = MarkerFor(row.Marker);

        ShowRenameBox(row);

        ToolTipService.SetToolTip(_container, row.Tooltip);

        AutomationProperties.SetName(_container, row.AutomationName);
        AutomationProperties.SetLevel(_container, row.Row.Depth + 1);
        AutomationProperties.SetPositionInSet(_container, row.Row.PositionInSet);
        AutomationProperties.SetSizeOfSet(_container, row.Row.SizeOfSet);

        // A container realized for the first time has no template yet, and a state asked of it
        // then is dropped; it is asked again once the container has loaded.
        if (!VisualStateManager.GoToState(_container, row.IsActive ? "Active" : "Inactive", false))
        {
            _container.Loaded -= OnContainerLoaded;
            _container.Loaded += OnContainerLoaded;
        }
    }

    private void OnContainerLoaded(object sender, RoutedEventArgs e)
    {
        _container.Loaded -= OnContainerLoaded;

        if (_row is { } row)
        {
            VisualStateManager.GoToState(_container, row.IsActive ? "Active" : "Inactive", false);
        }
    }

    // ------------------------------------------------------------------ rename

    /// <summary>
    /// Puts a text box over the name while the row is being renamed, and takes it away after.
    ///
    /// In the row itself rather than a popup over the list, so it scrolls with its row and is
    /// read by Narrator where the name was. The name is selected without its extension, as
    /// Explorer does, because the stem is what is nearly always being changed. While it is open
    /// the reason a name cannot be used is shown under it, as it is typed (WP-7).
    /// </summary>
    private void ShowRenameBox(FolderRowViewModel row)
    {
        if (!row.IsRenaming)
        {
            if (_renameBox is not null)
            {
                Children.Remove(_renameBox);
                Children.Remove(_renameError);
                _renameBox = null;
                _renameError = null;
                _name.Visibility = Visibility.Visible;
            }

            return;
        }

        if (_renameBox is null)
        {
            _renameSettled = false;

            _renameBox = new TextBox
            {
                Text = row.Name,
                FontSize = 12.5,
                MinHeight = 0,
                Padding = new Thickness(4, 1, 4, 1),
                VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetName(_renameBox, "New name for " + row.Name);

            _renameError = new TextBlock
            {
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8,
                Margin = new Thickness(0, 2, 0, 2),
            };

            if (RowDefinitions.Count == 0)
            {
                RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            SetColumn(_renameBox, 1);
            SetColumn(_renameError, 1);
            SetRow(_renameError, 1);
            Children.Add(_renameBox);
            Children.Add(_renameError);
            _name.Visibility = Visibility.Collapsed;

            _renameBox.KeyDown += OnRenameKeyDown;
            _renameBox.TextChanged += (_, _) => CheckRenameText();
            _renameBox.LostFocus += async (_, _) => await SettleRenameAsync(commit: true);
            _renameBox.Loaded += (_, _) =>
            {
                _renameBox?.Focus(FocusState.Programmatic);
                SelectStem();
            };
        }

        _renameError!.Text = row.RenameError ?? string.Empty;
        _renameError.Visibility = string.IsNullOrEmpty(row.RenameError) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SelectStem()
    {
        if (_renameBox is not { } box || _row is not { } row)
        {
            return;
        }

        int dot = row.IsFolder ? -1 : box.Text.LastIndexOf('.');
        box.Select(0, dot > 0 ? dot : box.Text.Length);
    }

    private void CheckRenameText()
    {
        if (_row is { } row && _renameBox is { } box)
        {
            row.RenameError = _list.CheckRename?.Invoke(row, box.Text);
        }
    }

    private async void OnRenameKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Enter:
                e.Handled = true;
                await SettleRenameAsync(commit: true);
                break;

            case Windows.System.VirtualKey.Escape:
                e.Handled = true;
                await SettleRenameAsync(commit: false);
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Ends the rename once, however it ends: Enter or the box losing focus commits, Escape
    /// abandons. A name that cannot be used keeps the box open on Enter, with the reason under
    /// it; losing focus with such a name abandons it rather than leave a box nobody is looking at.
    /// </summary>
    private async Task SettleRenameAsync(bool commit)
    {
        if (_renameSettled || _row is not { } row || _renameBox is not { } box)
        {
            return;
        }

        string text = box.Text;

        if (commit && _list.CheckRename?.Invoke(row, text) is { } problem)
        {
            if (box.FocusState != FocusState.Unfocused)
            {
                row.RenameError = problem;
                return;
            }

            commit = false;
        }

        _renameSettled = true;

        if (commit && _list.CommitRename is { } apply)
        {
            await apply(row, text);
        }
        else
        {
            _list.CancelRename?.Invoke(row);
        }
    }
    /// <summary>
    /// Segoe Fluent glyphs, with the tab strip's own <c>⟳</c> and <c>!</c> for the two disk states
    /// (P20): an emoji draws in color and cannot follow the theme.
    /// </summary>
    private static FrameworkElement? MarkerFor(FolderRowMarker marker) => marker switch
    {
        FolderRowMarker.Open => Glyph("\uEA3B", 6),
        FolderRowMarker.ReadOnly => Glyph("\uE72E", 11),
        FolderRowMarker.Unsaved => Glyph("\uE70F", 11),
        FolderRowMarker.Changed => Text("\u27F3"),
        FolderRowMarker.Missing => Text("!"),
        _ => null,
    };

    private static FontIcon Glyph(string glyph, double size) => new()
    {
        Glyph = glyph,
        FontSize = size,
        Opacity = 0.75,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    private static TextBlock Text(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        Opacity = 0.8,
        HorizontalAlignment = HorizontalAlignment.Center,
    };
}

/// <summary>A row container whose automation peer says TreeItem, and offers Expand and Collapse for a folder.</summary>
internal sealed partial class FolderTreeItem(FolderTreeList list) : ListViewItem
{
    public FolderTreeList List { get; } = list;

    protected override AutomationPeer OnCreateAutomationPeer() => new FolderTreeItemPeer(this);
}

internal sealed partial class FolderTreeListPeer(FolderTreeList owner) : ListViewAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Tree;
}

internal sealed partial class FolderTreeItemPeer(FolderTreeItem owner) : ListViewItemAutomationPeer(owner), IExpandCollapseProvider
{
    private FolderRowViewModel? Row => (owner.Content as FolderRowPresenter)?.Row;

    public ExpandCollapseState ExpandCollapseState => Row switch
    {
        { Row.CanExpand: false } or null => ExpandCollapseState.LeafNode,
        { Row.IsExpanded: true } => ExpandCollapseState.Expanded,
        _ => ExpandCollapseState.Collapsed,
    };

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.TreeItem;

    protected override object GetPatternCore(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.ExpandCollapse && Row is { IsFolder: true }
            ? this
            : base.GetPatternCore(patternInterface);

    public void Expand()
    {
        if (Row is { } row && owner.List.SetExpanded is { } set)
        {
            _ = set(row, true);
        }
    }

    public void Collapse()
    {
        if (Row is { } row && owner.List.SetExpanded is { } set)
        {
            _ = set(row, false);
        }
    }
}
