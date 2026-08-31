// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using Windows.ApplicationModel.DataTransfer;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// Puts text on the Windows clipboard.
///
/// Every menu that copies something ends up here: the Edit menu, both context menus, and
/// the diagram and cheatsheet windows. The panes cannot do it themselves - a browser only
/// honours a copy during a trusted user gesture, and a click on a native menu is not one -
/// so the host owns the clipboard and the pages only ever hand it text.
/// </summary>
internal static class ClipboardText
{
    /// <summary>
    /// Writes text, or does nothing if there is none. Returns false when the clipboard
    /// refused the write, which is not exceptional: it is a shared resource and another
    /// process can have it open.
    /// </summary>
    public static bool Set(string? text, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not write to the clipboard.");
            return false;
        }
    }
}
