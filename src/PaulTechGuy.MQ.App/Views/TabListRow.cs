// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Text;
using Windows.UI.Text;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// The small decisions a row of the title bar's document list makes about itself, as static
/// functions an <c>x:Bind</c> in the row's template can call.
///
/// They live here rather than on <see cref="ViewModels.DocumentTabViewModel"/> because every one
/// of them answers in a UI type. A view model that hands out a <see cref="FontWeight"/> has taken
/// a dependency on the toolkit drawing it, and this list is the only thing that wants these
/// answers — the tab strip draws the same documents and asks none of them.
///
/// Static because that is what a function binding inside a DataTemplate can reach: the binding is
/// evaluated against the row's own data type, so anything else it needs has to be addressable by
/// name through an xmlns.
/// </summary>
internal static class TabListRow
{
    /// <summary>
    /// Marks the row for the document that is already open.
    ///
    /// The list is alphabetical rather than in tab order, so nothing about a row's position says
    /// where you currently are. The open document is also the one you are least likely to be
    /// reaching for, which is why it is marked rather than left out: the mark is there to say
    /// "you are here", not to offer anything.
    ///
    /// Weight rather than color. A colored row in a list whose other rows carry a dirty marker
    /// would be two color meanings in one place, and this one has to survive both themes.
    /// </summary>
    public static FontWeight WeightFor(bool active) =>
        active ? FontWeights.SemiBold : FontWeights.Normal;

    /// <summary>
    /// What the close button on a row promises, named so that it is not just an X.
    ///
    /// The document's own name rather than "Close document": the list exists to tell twelve
    /// similar files apart, and a tooltip that reads the same on every row would undo that at
    /// the moment the pointer is closest to the wrong one.
    /// </summary>
    public static string CloseTooltipFor(string title) => $"Close {title}";
}
