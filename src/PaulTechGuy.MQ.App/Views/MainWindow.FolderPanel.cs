// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using PaulTechGuy.MQ.App.ViewModels;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// The two side panels in the main window: the left column where they dock, the edge strip where
/// they live when unpinned, and the slide-out that draws one over the text (FolderPanel P29).
///
/// The column's rows are laid out here rather than bound, because their heights depend on four
/// things at once: which panels are docked, which are folded to their headers, and the
/// remembered split, which is the outline's share of the column rather than a pixel height. A
/// proportion grows with the window and with a bigger screen; pixels would leave one section
/// the same size and give the other everything. The splitter never moves on its own.
///
/// A panel's content is one element, moved between its place in the column and the slide-out
/// host as it slides out and back. Moving an element within one window is safe; both places are
/// in the same XAML tree, so its bindings and the list's state come with it.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>How long the slide takes each way. Short: it is getting out of the way, not performing.</summary>
    private static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(160);

    private FolderPanelView? _folderView;

    /// <summary>The outline folded to its header, which only means anything while both sections are stacked.</summary>
    private bool _outlineSectionCollapsed;

    /// <summary>Which panel's content is in the slide-out host now, which can lag the view model while a slide plays.</summary>
    private SidePanel _inSlideOut = SidePanel.None;

    private Storyboard? _slide;

    private void InitializeFolderPanel()
    {
        _folderView = new FolderPanelView(ViewModel.FolderPanel, ViewModel);
        _folderView.SectionCollapsedChanged += (_, _) => LayOutLeftColumn();
        FolderSectionHost.Child = _folderView;

        // An image row dropped on the source pane (WP-8). Handled events too: the web view may
        // mark the drag its own on the way past, while not taking it.
        RootGrid.AddHandler(UIElement.DragOverEvent, new DragEventHandler(OnFolderImageDragOver), handledEventsToo: true);
        RootGrid.AddHandler(UIElement.DropEvent, new DragEventHandler(OnFolderImageDrop), handledEventsToo: true);

        OutlineSectionCollapse.Click += (_, _) =>
        {
            _outlineSectionCollapsed = !_outlineSectionCollapsed;
            LayOutLeftColumn();
        };

        SectionSplitter.CurrentHeight = () => OutlineSectionRow.ActualHeight;
        SectionSplitter.HeightChanged += OnSectionSplitterDragged;
        SectionSplitter.ResetRequested += (_, _) => ViewModel.SetFolderSplit(AppSettings.DefaultFolderSplit);

        ViewModel.PropertyChanged += OnFolderLayoutPropertyChanged;
        ViewModel.FolderPanelFocusRequested += OnFolderPanelFocusRequested;

        // A press anywhere in the window outside the slid-out panel is a click away from it. The
        // text is the one place this does not hear, because the WebView keeps its pointer to
        // itself; the view model hears that one as the pane taking the keyboard. Handled events
        // too, because buttons and list rows mark their presses handled on the way past.
        RootGrid.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPointerPressedAnywhere), handledEventsToo: true);

        // Each edge tab's word is drawn on its side, so the Canvas holding it is sized to the
        // word's measured width: a fixed height left a gap between the glyph and the word that
        // grew with how short the word was.
        FitEdgeTabWord(FolderEdgeTabCanvas, FolderEdgeTabText);
        FitEdgeTabWord(OutlineEdgeTabCanvas, OutlineEdgeTabText);

        LayOutLeftColumn();
        ShowPinState();
    }

    private static void FitEdgeTabWord(Canvas canvas, TextBlock word)
    {
        void Fit()
        {
            double length = Math.Ceiling(word.ActualWidth);

            if (length > 0)
            {
                canvas.Height = length;
                Canvas.SetTop(word, length);
            }
        }

        word.SizeChanged += (_, _) => Fit();
        Fit();
    }

    private void OnFolderLayoutPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsFolderSectionVisible):
            case nameof(MainViewModel.IsOutlineVisible):
            case nameof(MainViewModel.AreSectionsStacked):
            case nameof(MainViewModel.FolderSplit):
                LayOutLeftColumn();
                break;

            case nameof(MainViewModel.OutlinePinned):
                ShowPinState();
                break;

            case nameof(MainViewModel.SlidOutPanel):
                ApplySlideOut();
                break;

            default:
                break;
        }
    }

    /// <summary>The outline header's pin button: pin when it lives on the edge, unpin when docked.</summary>
    private void ShowPinState()
    {
        bool pinned = ViewModel.OutlinePinned;

        OutlinePinGlyph.Glyph = pinned ? "\uE77A" : "\uE718";

        string tip = pinned ? "Unpin: move the outline to the window edge" : "Pin: dock the outline in the column";
        ToolTipService.SetToolTip(OutlinePinButton, tip);
        AutomationProperties.SetName(OutlinePinButton, pinned ? "Unpin the outline" : "Pin the outline");
    }

    // ------------------------------------------------------------ the column

    /// <summary>Sets the column's row heights from what is docked, what is folded, and the split.</summary>
    private void LayOutLeftColumn()
    {
        bool folder = ViewModel.IsFolderSectionVisible;
        bool outline = ViewModel.IsOutlineVisible;
        bool stacked = folder && outline;

        // Folding only means giving the height to the other section, so it is only offered,
        // and only honored, while both are docked.
        if (_folderView is not null)
        {
            _folderView.CanFold = stacked;
        }

        bool folderFolded = stacked && (_folderView?.IsSectionCollapsed ?? false);
        bool outlineFolded = stacked && _outlineSectionCollapsed;

        // A folded section gives its height to the other, and the split it had is kept for when
        // it opens again. Both folded leaves the two headers at the top.
        GridLength star = new(1, GridUnitType.Star);

        (GridLength top, GridLength bottom) = (folder, outline) switch
        {
            (true, false) => (star, GridLength.Auto),
            (false, true) => (GridLength.Auto, star),
            (true, true) when folderFolded && outlineFolded => (GridLength.Auto, GridLength.Auto),
            (true, true) when folderFolded => (GridLength.Auto, star),
            (true, true) when outlineFolded => (star, GridLength.Auto),
            (true, true) => (
                new GridLength(1 - ViewModel.FolderSplit, GridUnitType.Star),
                new GridLength(ViewModel.FolderSplit, GridUnitType.Star)),
            _ => (GridLength.Auto, GridLength.Auto),
        };

        FolderSectionRow.Height = top;
        OutlineSectionRow.Height = bottom;

        Visibility outlineBody = outlineFolded ? Visibility.Collapsed : Visibility.Visible;
        OutlineFilterRow.Visibility = outlineBody;
        OutlineBody.Visibility = outlineBody;

        OutlineSectionGlyph.Glyph = outlineFolded ? "\uE76C" : "\uE70D";
        ToolTipService.SetToolTip(OutlineSectionCollapse, outlineFolded ? "Expand" : "Collapse");
        AutomationProperties.SetName(
            OutlineSectionCollapse,
            outlineFolded ? "Expand the outline section" : "Collapse the outline section");

        // The splitter only means something between two open sections.
        SectionSplitter.IsHitTestVisible = stacked && !folderFolded && !outlineFolded;
    }

    /// <summary>A drag reports the outline's new height; the split is that share of what the two sections have.</summary>
    private void OnSectionSplitterDragged(object? sender, double outlineHeight)
    {
        double total = FolderSectionRow.ActualHeight + OutlineSectionRow.ActualHeight;

        if (total > 0)
        {
            ViewModel.SetFolderSplit(outlineHeight / total);
        }
    }

    // ------------------------------------------------------------- the slide

    /// <summary>
    /// Brings the slide-out host in line with the view model: slides the panel that is out back
    /// to its edge and returns its content to the column, then slides the new one out.
    /// </summary>
    private void ApplySlideOut()
    {
        SidePanel wanted = ViewModel.SlidOutPanel;

        if (wanted == _inSlideOut)
        {
            return;
        }

        if (_inSlideOut != SidePanel.None)
        {
            SidePanel leaving = _inSlideOut;

            // Pinned while out: it is docked now, so it goes straight to the column. Sliding
            // back to an edge it no longer lives on would show the wrong thing.
            bool docked = leaving == SidePanel.Folder ? ViewModel.IsFolderSectionVisible : ViewModel.IsOutlineVisible;

            if (docked)
            {
                _slide?.Stop();
                ReturnHome(leaving);

                if (wanted != SidePanel.None)
                {
                    ApplySlideOut();
                }

                return;
            }

            Slide(show: false, () =>
            {
                ReturnHome(leaving);

                // Something else may have been asked for while this one slid back.
                if (ViewModel.SlidOutPanel != SidePanel.None && _inSlideOut == SidePanel.None)
                {
                    ApplySlideOut();
                }
            });

            return;
        }

        MoveIntoSlideOut(wanted);
        Slide(show: true, onDone: null);
    }

    private void MoveIntoSlideOut(SidePanel panel)
    {
        UIElement content = panel == SidePanel.Folder ? TakeFolderView() : TakeOutlinePanel();

        SlideOutHost.Child = content;
        SlideOutHost.Margin = new Thickness(SlideOutLeft(), 0, 0, 0);
        _inSlideOut = panel;
    }

    /// <summary>
    /// Where a slid-out panel starts: where the text starts, past the edge strip at the window's
    /// far left, the docked column and its handle. So it covers only the text, and a docked panel
    /// stays in view.
    /// </summary>
    private double SlideOutLeft()
    {
        if (VisualTreeHelper.GetParent(SlideOutHost) is not UIElement row)
        {
            return 0;
        }

        return ColumnHandle.TransformToVisual(row)
            .TransformPoint(new Windows.Foundation.Point(ColumnHandle.ActualWidth, 0))
            .X;
    }

    private void ReturnHome(SidePanel panel)
    {
        SlideOutHost.Child = null;
        SlideOutHost.Visibility = Visibility.Collapsed;
        _inSlideOut = SidePanel.None;

        if (panel == SidePanel.Folder && _folderView is not null)
        {
            FolderSectionHost.Child = _folderView;
        }
        else if (panel == SidePanel.Outline && !LeftColumn.Children.Contains(OutlinePanel))
        {
            LeftColumn.Children.Add(OutlinePanel);
        }

        LayOutLeftColumn();
    }

    private FolderPanelView TakeFolderView()
    {
        FolderSectionHost.Child = null;

        return _folderView!;
    }

    private Border TakeOutlinePanel()
    {
        LeftColumn.Children.Remove(OutlinePanel);

        // Slid out it is the whole panel, whatever it was folded to in the column.
        OutlineFilterRow.Visibility = Visibility.Visible;
        OutlineBody.Visibility = Visibility.Visible;

        return OutlinePanel;
    }

    /// <summary>Plays the slide, out from the edge or back to it, and runs <paramref name="onDone"/> after.</summary>
    private void Slide(bool show, Action? onDone)
    {
        _slide?.Stop();

        double width = Math.Max(1, ViewModel.OutlineWidth);
        SlideOutHost.Visibility = Visibility.Visible;

        if (!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        {
            SlideOutShift.X = 0;
            onDone?.Invoke();
            return;
        }

        var animation = new DoubleAnimation
        {
            From = show ? -width : SlideOutShift.X,
            To = show ? 0 : -width,
            Duration = new Duration(SlideDuration),
            EasingFunction = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn },
        };

        Storyboard.SetTarget(animation, SlideOutShift);
        Storyboard.SetTargetProperty(animation, nameof(TranslateTransform.X));

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) =>
        {
            if (ReferenceEquals(_slide, storyboard))
            {
                _slide = null;
            }

            SlideOutShift.X = show ? 0 : -width;
            onDone?.Invoke();
        };

        _slide = storyboard;
        storyboard.Begin();
    }

    /// <summary>A press outside the slid-out panel, its edge tab and the menu bar: a click away from it.</summary>
    private void OnPointerPressedAnywhere(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel.SlidOutPanel == SidePanel.None)
        {
            return;
        }

        for (DependencyObject? node = e.OriginalSource as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, SlideOutHost) || ReferenceEquals(node, EdgeStrip) || node is MenuBar)
            {
                return;
            }
        }

        ViewModel.NotifyClickedOutsideSlidOutPanel();
    }

    /// <summary>Alt+Shift+6 and Alt+6's show: the keyboard goes into the panel, docked or slid out.</summary>
    private void OnFolderPanelFocusRequested(object? sender, EventArgs e)
    {
        if (_folderView is null || !ViewModel.IsFolderPanelOnScreen)
        {
            return;
        }

        if (_folderView.FocusPanel())
        {
            ViewModel.NotifyFolderPanelFocused();
        }
    }

    // ------------------------------------------------- an image dropped on the source

    /// <summary>
    /// The point over the web view a drag is at, or null when it is somewhere else. Only a drag
    /// from a folder panel image row is asked about.
    /// </summary>
    private Windows.Foundation.Point? FolderImageDropPoint(DragEventArgs e)
    {
        if (_previewWebView is not { } web || !e.DataView.Contains(FolderPanelView.ImageDragFormat))
        {
            return null;
        }

        Windows.Foundation.Point point = e.GetPosition(web);

        return point.X >= 0 && point.Y >= 0 && point.X < web.ActualWidth && point.Y < web.ActualHeight
            ? point
            : null;
    }

    /// <summary>
    /// Accepts an image row's drag over the source pane, and refuses it over the splitter and
    /// the preview, so the cursor shows the no-drop sign there: the preview never changes the
    /// source. Windows asks on every move and wants the answer at once, which is why the pane is
    /// worked out here rather than asked of the page (<see cref="MainViewModel.IsSourcePaneAt"/>).
    /// </summary>
    private void OnFolderImageDragOver(object sender, DragEventArgs e)
    {
        if (FolderImageDropPoint(e) is not { } point || _previewWebView is not { } web)
        {
            return;
        }

        bool overSource = ViewModel.IsSourcePaneAt(point.X, web.ActualWidth);

        e.AcceptedOperation = overSource
            ? Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy
            : Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
        e.Handled = true;

        if (e.DragUIOverride is { } overrides)
        {
            overrides.Caption = overSource ? "Insert link" : "Drop on the source pane";
            overrides.IsCaptionVisible = true;
            overrides.IsGlyphVisible = false;
        }
    }

    private async void OnFolderImageDrop(object sender, DragEventArgs e)
    {
        if (FolderImageDropPoint(e) is not { } point
            || _previewWebView is not { } web
            || !ViewModel.IsSourcePaneAt(point.X, web.ActualWidth))
        {
            return;
        }

        e.Handled = true;

        DragOperationDeferral deferral = e.GetDeferral();

        try
        {
            if (await e.DataView.GetDataAsync(FolderPanelView.ImageDragFormat) is string path)
            {
                await ViewModel.DropImageLinkAsync(path, point.X, point.Y);
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException)
        {
            ViewModel.StatusText = "The link could not be inserted";
        }
        finally
        {
            deferral.Complete();
        }
    }
}
