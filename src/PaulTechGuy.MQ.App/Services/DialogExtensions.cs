// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>Shared placement for every dialog in the app.</summary>
internal static class DialogExtensions
{
    /// <summary>
    /// Points a dialog at the window and gives it the theme that window is actually using.
    ///
    /// The theme half is the part that is easy to miss. A ContentDialog is hosted in the
    /// popup root, a sibling of the window's content rather than a child of it, so the
    /// RequestedTheme the theme service sets on Window.Content never reaches it — the dialog
    /// inherits nothing and falls back to the framework default, which is dark. A light app
    /// therefore opened dark dialogs, and did so however the theme was set.
    ///
    /// ActualTheme rather than the requested one, so a theme of System resolves to whatever
    /// Windows is doing at the moment the dialog opens.
    ///
    /// Every dialog goes through here so a new one cannot quietly miss it.
    /// </summary>
    public static T AnchorTo<T>(this T dialog, FrameworkElement? anchor)
        where T : ContentDialog
    {
        ArgumentNullException.ThrowIfNull(dialog);

        if (anchor is not null)
        {
            dialog.XamlRoot = anchor.XamlRoot;
            dialog.RequestedTheme = anchor.ActualTheme;
        }

        return dialog;
    }
}
