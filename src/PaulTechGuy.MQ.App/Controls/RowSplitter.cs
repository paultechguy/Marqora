// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace PaulTechGuy.MQ.App.Controls;

/// <summary>
/// A drag handle that resizes the grid row below it.
///
/// <see cref="ColumnSplitter"/> turned on its side, and a sibling rather than one control with
/// an orientation: every reason that one is shaped the way it is applies here unchanged, and a
/// shared control would carry an axis switch through all four of them. The comment there has
/// the full account - why it is hand-rolled rather than taken from a toolkit, why it derives
/// (the resize cursor is protected, so an element can only be given one from the inside), and
/// why it is a <see cref="Grid"/> rather than a <see cref="Control"/> - a Control with no
/// template renders nothing, so it is not there to be hit-tested and the drag never starts.
///
/// The height is reported rather than written, so the one place deciding how tall the pane may
/// be, and the one place that persists it, is the window rather than the handle.
/// </summary>
public sealed partial class RowSplitter : Grid
{
    private double _startHeight;

    public RowSplitter()
    {
        this.ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);

        // TranslateY alone, for the reason its sibling takes TranslateX: allowing the other
        // axis lets a slightly diagonal drag steal the gesture from the vertical one.
        this.ManipulationMode = ManipulationModes.TranslateY;

        this.ManipulationStarted += OnManipulationStarted;
        this.ManipulationDelta += OnManipulationDelta;
    }

    /// <summary>The height the dragged row should take, in device-independent pixels.</summary>
    public event EventHandler<double>? HeightChanged;

    /// <summary>
    /// Where the row started, read when the drag begins. The caller supplies it because this
    /// control does not know which row it resizes; the window does, and clamps the result.
    /// </summary>
    public Func<double>? CurrentHeight { get; set; }

    private void OnManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e) =>
        _startHeight = CurrentHeight?.Invoke() ?? 0;

    /// <summary>
    /// Measured from where the drag began rather than accumulated delta by delta - cumulative
    /// is what the manipulation already reports, and a running total drifts away from the
    /// pointer over a long drag once rounding and the window's clamp are applied.
    ///
    /// Negated: the pane being resized is <em>below</em> the handle, so dragging up makes it
    /// taller. Its sibling resizes the column to its left, where the sign works out the other
    /// way round.
    /// </summary>
    private void OnManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e) =>
        HeightChanged?.Invoke(this, _startHeight - e.Cumulative.Translation.Y);
}
