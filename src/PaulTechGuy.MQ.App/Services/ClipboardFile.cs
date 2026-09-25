// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// Puts a file itself on the clipboard, the way Explorer's Copy does.
///
/// Pasting it into Outlook, Teams or a chat attaches the file, which is as close to "share" as
/// Marqora comes without sending anything anywhere: the reviewer decides where it goes, in an
/// app that is not this one.
/// </summary>
internal static class ClipboardFile
{
    /// <summary>
    /// Returns false when the file is gone or the clipboard refused the write. Neither is
    /// exceptional: the file may have been moved since it was written, and the clipboard is a
    /// shared resource another process can hold open.
    /// </summary>
    public static async Task<bool> SetAsync(string path, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(path);

            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            package.SetStorageItems([file]);
            Clipboard.SetContent(package);
            Clipboard.Flush();

            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not put {Path} on the clipboard.", path);
            return false;
        }
    }
}
