// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PaulTechGuy.MQ.App.ViewModels;
using Windows.System;
using Windows.UI.Core;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// The comments sidebar's behavior: the cards' buttons, the keys in a card's box, and putting
/// the keyboard or the view where the view model asks.
///
/// Split out like the outline panel's, for the same reason. The decisions are all in
/// <see cref="MainViewModel"/>; this is only the part that has to touch controls.
/// </summary>
public sealed partial class MainWindow
{
    private void InitializeReview()
    {
        ViewModel.ReviewCommentFocusRequested += (_, card) => FocusReviewCard(card);
        ViewModel.ReviewCommentRevealRequested += (_, card) => BringReviewCardIntoView(card);

        // End Commenting collapses as its question appears, and the keyboard would be left on a
        // button that is no longer there. Keep Commenting is the answer that loses nothing.
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsEndWarningVisible) && ViewModel.IsEndWarningVisible)
            {
                DispatcherQueue.TryEnqueue(() => KeepCommentingButton.Focus(FocusState.Programmatic));
            }
        };
    }

    private static ReviewCommentViewModel? CardOf(object sender) =>
        (sender as FrameworkElement)?.Tag as ReviewCommentViewModel;

    private void OnReviewEditClick(object sender, RoutedEventArgs e) =>
        ViewModel.EditCommentCommand.Execute(CardOf(sender));

    private void OnReviewDeleteClick(object sender, RoutedEventArgs e) =>
        ViewModel.DeleteCommentCommand.Execute(CardOf(sender));

    private void OnReviewSaveClick(object sender, RoutedEventArgs e) =>
        ViewModel.SaveCommentCommand.Execute(CardOf(sender));

    /// <summary>
    /// Tab and Shift+Tab through the card being written: box, Save, Cancel, and back the same
    /// way. Stated here because the repeater's own tab order does not keep to it. Tab from Cancel
    /// is left alone - it already goes on to the footer - and Share Review... sends Shift+Tab
    /// back here (see OnReviewShareKeyDown), so the sequence runs the same in both directions.
    /// </summary>
    private void OnReviewSaveKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Tab
            || (sender as FrameworkElement)?.Parent is not Panel row
            || row.Parent is not Panel face)
        {
            return;
        }

        bool shift = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);

        // Box, then whichever buttons can take the keyboard - a grayed Save cannot.
        List<Control> order = [.. face.Children.OfType<TextBox>()];
        order.AddRange(row.Children.OfType<Button>().Where(b => b.IsEnabled));

        int at = order.IndexOf((Control)sender);
        int next = shift ? at - 1 : at + 1;

        if (at < 0 || next < 0 || next >= order.Count)
        {
            return;
        }

        e.Handled = true;
        order[next].Focus(FocusState.Keyboard);
    }

    /// <summary>Shift+Tab from Share Review... goes back to Cancel on the card being written, if there is one.</summary>
    private void OnReviewShareKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool shift = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);

        if (e.Key != VirtualKey.Tab || !shift
            || ViewModel.ReviewComments.FirstOrDefault(c => c.IsEditing) is not { } card
            || ReviewCards.TryGetElement(ViewModel.ReviewComments.IndexOf(card)) is not { } element
            || FindDescendant<TextBox>(element)?.Parent is not Panel face)
        {
            return;
        }

        Button? last = face.Children.OfType<Panel>()
            .SelectMany(row => row.Children.OfType<Button>())
            .LastOrDefault(b => b.IsEnabled);

        if (last is not null)
        {
            e.Handled = true;
            last.Focus(FocusState.Keyboard);
        }
    }

    private void OnReviewCancelClick(object sender, RoutedEventArgs e) =>
        ViewModel.CancelCommentCommand.Execute(CardOf(sender));

    /// <summary>
    /// A click on a card, away from its buttons and its box, finds its passage in the preview.
    /// The buttons and the box handle their own pointer, so a tap reaching here was on the card.
    /// </summary>
    private void OnReviewCardTapped(object sender, TappedRoutedEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;

        if (IsInside<ButtonBase>(source) || IsInside<TextBox>(source))
        {
            return;
        }

        ViewModel.RevealCommentCommand.Execute(CardOf(sender));
    }

    /// <summary>
    /// Ctrl+Enter saves and Escape cancels, in PreviewKeyDown because the box accepts Return:
    /// by KeyDown it has already put the line break in.
    ///
    /// Tab goes to Save, then Cancel, the two buttons under the box. Said here rather than left to
    /// tab order, which inside a repeater of cards went elsewhere; Shift+Tab is left alone.
    /// </summary>
    private void OnReviewBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (CardOf(sender) is not { } card)
        {
            return;
        }

        bool control = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);

        bool shift = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);

        // The box shares a panel with the row holding its buttons, and Save is the first of them -
        // or Cancel, while an empty box has Save grayed and a grayed button cannot take the keyboard.
        // Looked for in that row only: the box's own template has a Button in it, its clear button,
        // which a search of the whole panel would find first.
        if (e.Key == VirtualKey.Tab && !shift && !control
            && (sender as FrameworkElement)?.Parent is Panel face
            && face.Children.OfType<Panel>().SelectMany(row => row.Children.OfType<Button>()).FirstOrDefault(b => b.IsEnabled) is { } save)
        {
            e.Handled = true;
            save.Focus(FocusState.Keyboard);
            return;
        }

        if (e.Key == VirtualKey.Enter && control)
        {
            e.Handled = true;
            ViewModel.SaveCommentCommand.Execute(card);
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            ViewModel.CancelCommentCommand.Execute(card);
        }
    }

    /// <summary>Puts the keyboard in a card's box: a new draft, Edit, or a second request while one is open.</summary>
    /// <remarks>
    /// Tried several times rather than once, and checked rather than trusted. A new card comes
    /// from a click on the page's Comment button, so the keyboard is in the WebView when this
    /// runs, and the card was added a moment ago: the repeater may not have realized it, its box
    /// may still be collapsed while the edit face is bound in, and a focus request made while the
    /// WebView is still handling the click it sent is turned down. Each of those fails silently -
    /// Focus answers false and nothing else happens - which is how the first version, one
    /// attempt on the next tick, left the reader clicking into the box by hand.
    /// </remarks>
    private async void FocusReviewCard(ReviewCommentViewModel card)
    {
        try
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                await Task.Delay(attempt == 0 ? 0 : 40).ConfigureAwait(true);

                if (!card.IsEditing || ViewModel.ReviewComments.IndexOf(card) < 0)
                {
                    return;
                }

                ReviewCards.UpdateLayout();

                if (BringReviewCardIntoView(card) is not { } element
                    || FindDescendant<TextBox>(element) is not { Visibility: Visibility.Visible } box)
                {
                    continue;
                }

                box.UpdateLayout();

                if (box.ActualHeight > 0 && box.Focus(FocusState.Programmatic)
                    && ReferenceEquals(FocusManager.GetFocusedElement(box.XamlRoot), box))
                {
                    box.SelectionStart = box.Text.Length;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            // Raised from an event; nothing above this catches for it.
            _logger.LogWarning(ex, "Could not put the keyboard in a comment card.");
        }
    }

    private void OnReviewCardPointerEntered(object sender, PointerRoutedEventArgs e) =>
        ViewModel.HoverCard(CardOf(sender));

    private void OnReviewCardPointerExited(object sender, PointerRoutedEventArgs e) =>
        ViewModel.HoverCard(null);

    private UIElement? BringReviewCardIntoView(ReviewCommentViewModel card)
    {
        int index = ViewModel.ReviewComments.IndexOf(card);

        if (index < 0)
        {
            return null;
        }

        UIElement element = ReviewCards.GetOrCreateElement(index);
        element.UpdateLayout();
        element.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true, VerticalAlignmentRatio = 0.3 });

        return element;
    }

    private static bool IsInside<T>(DependencyObject? node)
        where T : DependencyObject
    {
        for (DependencyObject? current = node; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T)
            {
                return true;
            }
        }

        return false;
    }
}
