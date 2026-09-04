// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace PaulTechGuy.MQ.App.Controls;

/// <summary>
/// A drag handle that resizes the grid column to its left.
///
/// Hand-rolled rather than taken from a toolkit. The only thing needed is a vertical bar
/// that widens one column, and the package it would otherwise come from is a dependency this
/// project has gone out of its way not to take - see the note at the head of
/// Directory.Packages.props. The webshell's own splitter is written the same way and for the
/// same reason.
///
/// Deriving is what allows the resize cursor: WinUI exposes it as the protected
/// <c>UIElement.ProtectedCursor</c>, so an element can only be given one from the inside.
/// <see cref="HandCursorButton"/> exists for the same reason.
///
/// A <see cref="Grid"/> rather than a <see cref="Control"/>, which is the part that is easy
/// to get wrong: a Control with no template of its own renders nothing at all, background
/// included, so it is not there to be hit-tested and the drag never starts. A panel paints
/// its own background and so can be a transparent hit target, which is all this needs to be -
/// the visible line between the two is the outline panel's own right border.
///
/// The width is reported through <see cref="WidthChanged"/> rather than written into the
/// column directly, so that the one place deciding how wide the panel may be - and the one
/// place that persists it - is the window, not the handle being dragged.
/// </summary>
public sealed partial class ColumnSplitter : Grid
{
    private double _startWidth;

    public ColumnSplitter()
    {
        this.ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);

        // TranslateX alone: the handle only ever moves sideways, and allowing the vertical
        // axis lets a slightly diagonal drag steal the gesture from the horizontal one.
        this.ManipulationMode = ManipulationModes.TranslateX;

        this.ManipulationStarted += OnManipulationStarted;
        this.ManipulationDelta += OnManipulationDelta;
    }

    /// <summary>The width the dragged column should take, in device-independent pixels.</summary>
    public event EventHandler<double>? WidthChanged;

    /// <summary>
    /// Where the column started, set when the drag begins.
    ///
    /// The caller supplies it because this control does not know which column it resizes;
    /// the window does, and it is the window that also clamps the result.
    /// </summary>
    public Func<double>? CurrentWidth { get; set; }

    private void OnManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e) =>
        _startWidth = CurrentWidth?.Invoke() ?? 0;

    /// <summary>
    /// Measured from where the drag began rather than accumulated delta by delta.
    ///
    /// Cumulative is what the manipulation already reports, and adding each delta to a
    /// running width instead lets rounding - and any clamp the window applies - drift the
    /// handle away from the pointer over a long drag.
    /// </summary>
    private void OnManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e) =>
        WidthChanged?.Invoke(this, _startWidth + e.Cumulative.Translation.X);
}
