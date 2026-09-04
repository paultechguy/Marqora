// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// The outline panel's behaviour.
///
/// Split out for the same reason the context menus are: it is a self-contained piece of the
/// window with a story of its own, and MainWindow.xaml.cs is long enough already.
///
/// The interaction is the one Find All settled on - arrowing the list shows each section
/// with the keyboard still in the panel, Enter or a double-click hands it to the text -
/// because they are the same gesture asked of the same kind of list, and answering it two
/// different ways in one app would be the surprise.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Set while the panel's selection is being moved to follow the document, so the
    /// SelectionChanged that results is not mistaken for the user picking a row.
    ///
    /// Without it the two feed each other: following the caret would select a row, selecting
    /// a row would scroll the editor to it, and a document being read would drag itself
    /// heading by heading. ListView raises SelectionChanged synchronously from the setter,
    /// so the flag is only ever true for the length of one assignment.
    /// </summary>
    private bool _followingOutline;

    private void InitializeOutline()
    {
        OutlineList.SelectionChanged += OnOutlineSelectionChanged;
        OutlineList.KeyDown += OnOutlineKeyDown;
        OutlineList.DoubleTapped += OnOutlineDoubleTapped;
        OutlineList.GotFocus += OnOutlineGotFocus;

        OutlineFilterBox.GotFocus += OnOutlineGotFocus;
        OutlineFilterBox.KeyDown += OnOutlineFilterKeyDown;

        OutlineSplitter.CurrentWidth = () => ViewModel.OutlineWidth;
        OutlineSplitter.WidthChanged += OnOutlineSplitterDragged;

        ViewModel.OutlineSelectionChanged += OnOutlineFollowRequested;
        ViewModel.OutlineFocusRequested += OnOutlineFocusRequested;
    }

    // ------------------------------------------------------------------- focus

    /// <summary>
    /// Both the list and the filter box report focus, because both are the panel as far as
    /// the rest of the app is concerned: what matters to the Format menu is that the
    /// keyboard is not in a document pane, not which half of the panel holds it.
    /// </summary>
    private void OnOutlineGotFocus(object sender, RoutedEventArgs e) =>
        ViewModel.NotifyOutlineFocused();

    /// <summary>
    /// Puts the keyboard in the panel, on the followed row when there is one.
    ///
    /// Focusing the list rather than the filter box: the panel is for getting somewhere, and
    /// arriving on the section you are already in means the arrow keys work from where you
    /// are. The filter is a Tab away for anyone who wants it.
    /// </summary>
    private void OnOutlineFocusRequested(object? sender, EventArgs e)
    {
        if (!ViewModel.IsOutlineVisible)
        {
            return;
        }

        // Asked of the rows rather than of ListView.Items, here and below. Items and
        // ItemsSource are two different collections, and which one answers for the other
        // once ItemsSource is bound is a detail of the control; the collection this window
        // filled is not in any doubt.
        //
        // Nothing listed: an empty outline still has a filter box, which is the only thing
        // in the panel worth landing on.
        bool focused = ViewModel.OutlineRows.Count == 0
            ? OutlineFilterBox.Focus(FocusState.Programmatic)

            // No row is pre-selected when none is already followed, which happens only where
            // no heading owns the caret's line - above the first one. Selecting the first
            // heading to fill the gap would scroll the document somewhere nobody asked to
            // go; the first Down does that, and says so.
            : OutlineList.Focus(FocusState.Programmatic);

        // Said rather than waited for. GotFocus reports the same thing a moment later and
        // the flag is idempotent, but the whole of the panel's behaviour hangs off it - what
        // Escape does, and whether the Format menu is available - so it is not left resting
        // on a routed event arriving from a control that has only just been shown.
        if (focused)
        {
            ViewModel.NotifyOutlineFocused();
        }
    }

    // -------------------------------------------------------------- navigation

    /// <summary>
    /// Moving through the list shows each heading without taking the keyboard away from it,
    /// so the arrow keys keep working - the same bargain the Find All results make.
    /// </summary>
    private async void OnOutlineSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_followingOutline)
        {
            return;
        }

        await ViewModel.GoToOutlineRowAsync(OutlineList.SelectedIndex, focusEditor: false);
    }

    private async void OnOutlineKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                e.Handled = true;
                await ViewModel.GoToOutlineRowAsync(OutlineList.SelectedIndex, focusEditor: true);
                break;

            // The way back out for anyone who arrived by keyboard. Without it the panel is
            // a room with the door on the other side of a Tab cycle.
            case VirtualKey.Escape:
                e.Handled = true;
                await ViewModel.LeaveOutlineAsync();
                break;

            default:
                break;
        }
    }

    private async void OnOutlineDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;

        await ViewModel.GoToOutlineRowAsync(OutlineList.SelectedIndex, focusEditor: true);
    }

    /// <summary>
    /// Escape clears the filter before it leaves the panel, and Down steps into the list.
    ///
    /// Clearing first because that is what the box in front of you is for: a filter typed by
    /// mistake should cost one Escape to undo, not a trip back to the panel to empty it by
    /// hand. A second Escape, with nothing left to clear, leaves.
    /// </summary>
    private async void OnOutlineFilterKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape when !string.IsNullOrEmpty(ViewModel.OutlineFilter):
                e.Handled = true;
                ViewModel.OutlineFilter = string.Empty;
                break;

            case VirtualKey.Escape:
                e.Handled = true;
                await ViewModel.LeaveOutlineAsync();
                break;

            case VirtualKey.Down when ViewModel.OutlineRows.Count > 0:
                e.Handled = true;
                OutlineList.Focus(FocusState.Programmatic);
                break;

            case VirtualKey.Enter when ViewModel.OutlineRows.Count > 0:
                e.Handled = true;
                await ViewModel.GoToOutlineRowAsync(
                    Math.Max(0, OutlineList.SelectedIndex), focusEditor: true);
                break;

            default:
                break;
        }
    }

    // ---------------------------------------------------------------- following

    /// <summary>
    /// Moves the highlight to the section the document is now showing.
    ///
    /// The row is brought into view only when the panel does not hold the keyboard. Scrolling
    /// a list out from under someone who is reading it is the one thing an automatic
    /// selection must not do, and while they are in the panel they can see where they are
    /// anyway.
    /// </summary>
    private void OnOutlineFollowRequested(object? sender, int index)
    {
        if (OutlineList.SelectedIndex == index)
        {
            return;
        }

        _followingOutline = true;

        try
        {
            OutlineList.SelectedIndex = index;
        }
        finally
        {
            _followingOutline = false;
        }

        if (!ViewModel.OutlineHasFocus && index >= 0 && index < ViewModel.OutlineRows.Count)
        {
            // The row object itself, which is what ScrollIntoView wants and what avoids
            // going back through ListView.Items for it.
            OutlineList.ScrollIntoView(ViewModel.OutlineRows[index]);
        }
    }

    // ------------------------------------------------------------------ resize

    /// <summary>
    /// The window width is passed in so the panel can be stopped from taking the document's
    /// room. The handle knows how far it has been dragged and nothing else.
    /// </summary>
    private void OnOutlineSplitterDragged(object? sender, double width) =>
        ViewModel.SetOutlineWidth(width, RootGrid.ActualWidth);

    /// <summary>
    /// Pulls the panel back inside a window that has been made narrower.
    ///
    /// A width remembered at 1900 pixels is most of an 800-pixel window, and a saved layout
    /// from a docked monitor is exactly how that happens.
    /// </summary>
    private void ClampOutlineWidth()
    {
        if (RootGrid.ActualWidth > 0)
        {
            ViewModel.SetOutlineWidth(ViewModel.OutlineWidth, RootGrid.ActualWidth);
        }
    }

    // ------------------------------------------------------------ accelerators

    /// <summary>
    /// Whether the keyboard is in an editable field of the window's own.
    ///
    /// Asked by the Edit accelerators before they run. See the note beside RunEdit in
    /// RegisterAccelerators for why this could be taken for granted until the outline
    /// panel's filter box existed.
    ///
    /// Deliberately a check for the control type rather than for the outline in particular.
    /// The next text box to be added to this window will need the same answer, and finding
    /// out that it did not get it means finding out that Ctrl+A selected a document.
    /// </summary>
    private bool IsTextInputFocused() =>
        FocusManager.GetFocusedElement(this.Content?.XamlRoot) is TextBox or RichEditBox or AutoSuggestBox;
}
