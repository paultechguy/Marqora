// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// The row of buttons at the foot of a window, a flyout or a notice.
///
/// A ContentDialog gets this from its template: it lays out its own footer and puts the accent on
/// whichever button DefaultButton names. Everything else in Marqora is a window or a flyout, and
/// neither has template buttons to borrow. Each of the places that needed one had arrived at a
/// different answer - different widths, different spacing, the accent on nothing at all, and in
/// one case the destructive choice first and unmarked.
///
/// What this fixes is not the pixels; those are two setters in App.xaml. It is the order and the
/// emphasis, which is the part a call site kept getting wrong.
///
/// See docs/Button-App-Standards.md.
/// </summary>
internal static class CommandFooter
{
    /// <summary>
    /// The ordinary footer: OK then Cancel, Save then Discard then Cancel.
    ///
    /// The commit comes first because that is where Windows puts it, and where the ContentDialog
    /// this pattern replaced put it. It carries the accent, and it is the only button in the row
    /// that does.
    /// </summary>
    public static StackPanel Commit(Button commit, params Button[] dismiss)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(dismiss);

        commit.Style = MqStyles.PrimaryCommandButton;

        StackPanel row = Row();
        row.Children.Add(commit);

        foreach (Button button in dismiss)
        {
            button.Style = MqStyles.CommandButton;
            row.Children.Add(button);
        }

        return row;
    }

    /// <summary>
    /// A footer that offers to throw something away.
    ///
    /// The destructive choice comes first and stays neutral; the safe one comes last, wears the
    /// accent, and takes focus as the row appears - so Enter and a reflexive click on the
    /// emphasised button both keep the user's work.
    ///
    /// No red. Red says something has failed, and nothing has happened yet. What marks this row
    /// is that the quiet button is the one that destroys, and that its label says so in a verb.
    ///
    /// The safe button is deliberately not moved to the front. Reversing the order as well as the
    /// emphasis would leave the two footers disagreeing about where the left-hand button lives,
    /// and muscle memory would find the accent where it expects the commit.
    /// </summary>
    public static StackPanel Destructive(Button destructive, Button safe)
    {
        ArgumentNullException.ThrowIfNull(destructive);
        ArgumentNullException.ThrowIfNull(safe);

        destructive.Style = MqStyles.CommandButton;
        safe.Style = MqStyles.PrimaryCommandButton;

        StackPanel row = Row();
        row.Children.Add(destructive);
        row.Children.Add(safe);

        /*
            WinUI has no Button.IsDefault - that is a WPF property - so a default button is
            assembled from the three things one is made of: the accent says which it is, focus
            makes Enter reach it, and the focus ring says so on screen.

            Queued behind Loaded rather than called from it. An element that has not been laid
            out cannot take focus, and Loaded fires before the first arrange pass completes.
        */
        safe.Loaded += (_, _) => safe.DispatcherQueue.TryEnqueue(
            () => safe.Focus(FocusState.Programmatic));

        return row;
    }

    /// <summary>
    /// Enter and Escape, for a container with no template to supply them.
    ///
    /// Attached to the container rather than to a button, and so only reached when nothing nearer
    /// took the key: a handler added this way does not see an event a control has already
    /// handled, so Enter inside a number box still commits the number rather than the window.
    /// </summary>
    public static void WireKeys(UIElement scope, Action? onEnter, Action? onEscape)
    {
        ArgumentNullException.ThrowIfNull(scope);

        scope.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case VirtualKey.Enter when onEnter is not null:
                    e.Handled = true;
                    onEnter();
                    break;

                case VirtualKey.Escape when onEscape is not null:
                    e.Handled = true;
                    onEscape();
                    break;

                default:
                    break;
            }
        };
    }

    private static StackPanel Row() => new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Right,
        Spacing = MqStyles.ButtonGroupSpacing,
    };
}
