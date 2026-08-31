// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Xaml;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// Carries the main window to services that need a parent for dialogs and pickers.
///
/// Win32 pickers require an owner handle and ContentDialog requires a XamlRoot, neither of
/// which exists when the container is built. Registering this holder lets those services be
/// ordinary singletons instead of reaching for a static Current window.
/// </summary>
public sealed class WindowContext
{
    public Window? Window { get; set; }

    /// <summary>Owner handle for Win32-backed pickers. Zero until the window exists.</summary>
    public IntPtr WindowHandle =>
        Window is null ? IntPtr.Zero : WinRT.Interop.WindowNative.GetWindowHandle(Window);

    /// <summary>Root for ContentDialog placement. Null until the window has loaded content.</summary>
    public XamlRoot? XamlRoot => (Window?.Content as FrameworkElement)?.XamlRoot;

    /// <summary>
    /// The window's content, for anchoring a dialog to it. Carries both the XamlRoot a
    /// dialog needs and the theme it has to be told about, since a dialog is hosted outside
    /// the element tree the theme is set on.
    /// </summary>
    public FrameworkElement? Root => Window?.Content as FrameworkElement;
}
