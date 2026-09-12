// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// The outline panel's behavior.
///
/// Split out for the same reason the context menus are: it is a self-contained piece of the
/// window with a story of its own, and MainWindow.xaml.cs is long enough already.
///
/// The interaction is the one Find All settled on - arrowing the list shows each section
/// with the keyboard still in the panel, Enter hands it to the text - because they are the
/// same gesture asked of the same kind of list, and answering it two different ways in one
/// app would be the surprise.
///
/// A click is the exception, and deliberately so. Find All is a window of its own, where
/// taking the keyboard away would take it out of the list being worked; this panel sits
/// beside the text in the same window, and a hand already on the mouse has pointed at a
/// heading rather than stepped towards it. So a click goes: both panes scroll, and the
/// caret lands on the heading line with the source pane holding the keyboard, ready to
/// type. Without that last part the caret is placed but never drawn - Monaco hides it
/// while the editor is blurred - and the click looks like it left the cursor nowhere.
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
        OutlineList.GotFocus += OnOutlineGotFocus;

        // handledEventsToo, where the others are plain subscriptions. A tap is the one
        // event here a ListViewItem may have marked handled on its way past - that is how
        // the container claims a click for selection - and a click that selects a row is
        // exactly the click this needs to hear about.
        OutlineList.AddHandler(
            UIElement.TappedEvent, new TappedEventHandler(OnOutlineTapped), handledEventsToo: true);

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
        // the flag is idempotent, but the whole of the panel's behavior hangs off it - what
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

    /// <summary>
    /// A click on a row: shows the section, and hands the keyboard to the source pane with
    /// the caret at the start of the heading line.
    ///
    /// This runs after the SelectionChanged the same click raised, which has already done
    /// the scrolling; what is left is the focus. Asking for the jump a second time costs a
    /// repeat of a move that is already where it is going, which is nothing on screen.
    ///
    /// The second click of a double-click arrives here too, on the row the first one went
    /// to, and does that same harmless nothing - which is why there is no DoubleTapped
    /// handler beside this one any more. One gesture, one answer.
    /// </summary>
    private async void OnOutlineTapped(object sender, TappedRoutedEventArgs e)
    {
        // Only a click that landed on a row. The list is taller than its contents for any
        // document shorter than the panel, and a click in the space below the last heading
        // selects nothing - so without this, pointing at empty space would take the editor
        // to whatever row was picked last.
        if (!IsOutlineRow(e.OriginalSource))
        {
            return;
        }

        await ViewModel.GoToOutlineRowAsync(OutlineList.SelectedIndex, focusEditor: true);
    }

    /// <summary>
    /// Whether a pointer event started inside one of the list's rows.
    ///
    /// Walked up the visual tree rather than asked of the source directly: what gets hit is
    /// the TextBlock the row draws, and the row itself is a parent or three above it.
    /// </summary>
    private static bool IsOutlineRow(object? source)
    {
        DependencyObject? node = source as DependencyObject;

        while (node is not null and not ListViewItem)
        {
            node = VisualTreeHelper.GetParent(node);
        }

        return node is not null;
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
