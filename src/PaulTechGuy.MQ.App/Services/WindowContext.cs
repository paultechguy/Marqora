// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Xaml;
using PaulTechGuy.MQ.Abstractions.Ui;

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

    /// <summary>
    /// The palette windows that can raise a prompt of their own.
    ///
    /// A palette is owned by the main window and so always floats above it. A dialog anchored
    /// to the main window would open behind the palette the user just clicked in, which is a
    /// prompt nobody can answer. Registering the window here is what lets one be anchored to
    /// itself instead. See <see cref="DialogAnchor"/>.
    /// </summary>
    private readonly Dictionary<DialogAnchor, Window> _palettes = [];

    public void Register(DialogAnchor anchor, Window window) => _palettes[anchor] = window;

    public void Unregister(DialogAnchor anchor) => _palettes.Remove(anchor);

    /// <summary>
    /// What to anchor a prompt to, falling back to the main window.
    ///
    /// The fallback is not a formality: a palette that has been dismissed is still registered,
    /// and a dialog anchored to a hidden window would be as invisible as the problem this
    /// solves. An unregistered or hidden palette hands the prompt back to the main window,
    /// which is always somewhere the user can see.
    /// </summary>
    public FrameworkElement? RootFor(DialogAnchor anchor) =>
        anchor != DialogAnchor.MainWindow
            && _palettes.TryGetValue(anchor, out Window? palette)
            && palette.AppWindow?.IsVisible == true
            ? palette.Content as FrameworkElement ?? Root
            : Root;

    public XamlRoot? XamlRootFor(DialogAnchor anchor) => RootFor(anchor)?.XamlRoot;
}
