// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.App.ViewModels;
using PaulTechGuy.MQ.Domain;
using Windows.Foundation;
using Windows.System;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// Insert Reference: writes a link to one of the document's own headings. Reached three ways -
/// the source pane's right-click menu, Insert > Reference to Heading, and Ctrl+R - and all three
/// open the same picker, at the click or at the caret.
///
/// The picker is a flyout declared on PreviewSurface in MainWindow.xaml - a filter box, a
/// Name / Number switch, and the outline's rows. Picking a row writes <c>[words](#slug)</c> or
/// <c>[2.3](#slug)</c> at the caret. With text selected, the selection stays and becomes the
/// link's text, so the switch is hidden: there is nothing for it to choose.
///
/// A heading with no number - numbering off, or a heading above where the count starts - is
/// grayed while Number is chosen rather than quietly written by name. A document with no
/// numbers at all disables Number outright.
///
/// What is written is a plain anchor link, and a number in it is a snapshot: the numbers are the
/// renderer's, not the source's. See <see cref="HeadingReference"/>.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// The Name / Number choice, kept between openings for the session. Someone numbering their
    /// references usually wants every one of them numbered.
    /// </summary>
    private bool _referenceByNumber;

    /// <summary>Whether the editor had a selection at the right-click that raised the menu.</summary>
    private bool _referenceWrapsSelection;

    /// <summary>Whether any heading in the document has a number, worked out once per opening.</summary>
    private bool _referenceHasNumbers;

    /// <summary>Where the right-click was, so the picker opens where the menu did.</summary>
    private Point _referencePoint;

    /// <summary>
    /// Number in force right now: chosen, possible in this document, and meaningful - with a
    /// selection the selected words are the link's text and neither choice applies.
    /// </summary>
    private bool ReferenceByNumber => _referenceByNumber && _referenceHasNumbers && !_referenceWrapsSelection;

    /// <summary>
    /// Captured at each right-click on the source pane, before the menu goes up. The item's
    /// handler runs after the menu has gone, and by then the event that carried these is gone too.
    /// </summary>
    private void NoteReferenceContext(PaneContextMenuEventArgs e)
    {
        _referencePoint = new Point(e.X, e.Y);
        _referenceWrapsSelection = e.HasSelection;
    }

    /// <summary>
    /// The other two ways in - Ctrl+R in the editor, and Insert > Reference to Heading - which
    /// have no right-click point, so the shell answers with the caret's instead.
    ///
    /// Asked again here rather than trusted: Ctrl+R comes straight from the editor, which knows
    /// nothing of a read-only tab or a document with no headings.
    /// </summary>
    private void OnReferenceRequested(object? sender, ReferenceRequestedEventArgs e)
    {
        if (!ViewModel.CanInsertReference)
        {
            return;
        }

        _referencePoint = new Point(e.X, e.Y);
        _referenceWrapsSelection = e.HasSelection;

        OpenInsertReference();
    }

    /// <summary>
    /// Opens the picker where the menu was.
    ///
    /// Queued rather than shown from inside the menu item's Click: the menu is still closing at
    /// that point, and a flyout opened while another is being dismissed can be taken down with
    /// it.
    /// </summary>
    private void OpenInsertReference()
    {
        IReadOnlyList<OutlineHeading> outline = ViewModel.ActiveOutline;

        if (outline.Count == 0)
        {
            return;
        }

        _referenceHasNumbers = outline.Any(heading => heading.Number.Length > 0);

        ReferenceFilterBox.Text = string.Empty;
        ApplyReferenceMode();
        FillReferenceList();

        DispatcherQueue.TryEnqueue(() => InsertReferenceFlyout.ShowAt(PreviewSurface, new FlyoutShowOptions
        {
            // In the WebView's own coordinates, as the context menu's position is.
            Position = _referencePoint,
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
            ShowMode = FlyoutShowMode.Standard,
        }));
    }

    /// <summary>Shows the switch or the selection note, and sets the switch to what is in force.</summary>
    private void ApplyReferenceMode()
    {
        ReferenceModeSwitcher.Visibility = _referenceWrapsSelection ? Visibility.Collapsed : Visibility.Visible;
        ReferenceSelectionNote.Visibility = _referenceWrapsSelection ? Visibility.Visible : Visibility.Collapsed;

        ReferenceByNumberSegment.IsEnabled = _referenceHasNumbers;

        bool byNumber = ReferenceByNumber;

        ReferenceByNameSegment.IsChecked = !byNumber;
        ReferenceByNumberSegment.IsChecked = byNumber;
    }

    /// <summary>
    /// Rebuilds the rows from the whole outline and the filter box.
    ///
    /// The selection lands on the first row that can be picked, so Enter in the filter box does
    /// something useful straight away and never lands on a grayed row.
    /// </summary>
    private void FillReferenceList()
    {
        List<OutlineRowViewModel> rows = [.. OutlineNavigation
            .Filter(ViewModel.ActiveOutline, ReferenceFilterBox.Text, OutlineNavigation.UnlimitedDepth)
            .Select(heading => new OutlineRowViewModel(heading))];

        ReferenceList.ItemsSource = rows;

        bool any = rows.Count > 0;

        ReferenceList.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        ReferenceEmptyText.Visibility = any ? Visibility.Collapsed : Visibility.Visible;

        ReferenceList.SelectedIndex = rows.FindIndex(IsPickable);
    }

    private bool IsPickable(OutlineRowViewModel row) => !ReferenceByNumber || row.Number.Length > 0;

    /// <summary>
    /// Grays and disables a row that cannot be picked under Number.
    ///
    /// Opacity rather than a disabled brush: MqListRowStyle has no disabled state, and a brush
    /// fetched in code resolves against the operating system's theme rather than the app's.
    /// A disabled container cannot be clicked or arrowed onto, which is the rest of it.
    /// </summary>
    private void OnReferenceContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is OutlineRowViewModel row)
        {
            bool pickable = IsPickable(row);

            args.ItemContainer.IsEnabled = pickable;
            args.ItemContainer.Opacity = pickable ? 1.0 : 0.4;
        }
    }

    private void OnReferenceModeClick(object sender, RoutedEventArgs e)
    {
        _referenceByNumber = ReferenceEquals(sender, ReferenceByNumberSegment);

        // Reapplied even when the choice did not change: a click on the segment already down
        // has just toggled it up, and it has to be put back.
        ApplyReferenceMode();
        FillReferenceList();
    }

    private void OnReferenceFilterChanged(object sender, TextChangedEventArgs e) => FillReferenceList();

    /// <summary>
    /// Down steps into the list and Enter takes the highlighted row, as in the outline panel's
    /// filter box. Escape is left to the flyout, which closes on it.
    /// </summary>
    private void OnReferenceFilterKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Down when ReferenceList.SelectedIndex >= 0:
                e.Handled = true;
                ReferenceList.Focus(FocusState.Keyboard);
                break;

            case VirtualKey.Enter when ReferenceList.SelectedItem is OutlineRowViewModel row:
                e.Handled = true;
                CommitReference(row);
                break;

            default:
                break;
        }
    }

    private void OnReferenceListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ReferenceList.SelectedItem is OutlineRowViewModel row)
        {
            e.Handled = true;
            CommitReference(row);
        }
    }

    private void OnReferenceItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is OutlineRowViewModel row)
        {
            CommitReference(row);
        }
    }

    private void CommitReference(OutlineRowViewModel row)
    {
        if (!IsPickable(row))
        {
            return;
        }

        bool byNumber = ReferenceByNumber;

        InsertReferenceFlyout.Hide();

        _ = ViewModel.InsertReferenceAsync(row.Heading, byNumber);
    }

    private void OnInsertReferenceOpened(object? sender, object e) =>
        ReferenceFilterBox.Focus(FocusState.Programmatic);

    /// <summary>Back to the editor however the picker closed - a pick, Escape, or a click away.</summary>
    private void OnInsertReferenceClosed(object? sender, object e) =>
        _ = ViewModel.ReturnToSourceAsync();
}
