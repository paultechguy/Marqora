// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using Windows.ApplicationModel.DataTransfer;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// Puts the rendered document on the clipboard as rich text.
///
/// Two flavours go on together. Applications that understand formatting — Word, Outlook,
/// Confluence, a mail composer — take the HTML and keep the headings, tables, images and
/// diagrams. Anything that only wants characters gets the markdown source instead, which is
/// what a terminal or a code editor should receive rather than a wall of markup.
///
/// The HTML flavour has to be wrapped in the CF_HTML envelope, with its byte offsets, before
/// Windows will accept it. <see cref="HtmlFormatHelper"/> builds that, so there is no need to
/// count bytes here.
/// </summary>
internal static class ClipboardHtml
{
    /// <summary>
    /// Writes both flavours. Returns false when the clipboard refused the write, which is
    /// not exceptional: it is a shared resource and another process can have it open.
    /// </summary>
    public static bool Set(string? html, string? plainText, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (string.IsNullOrWhiteSpace(html))
        {
            return false;
        }

        try
        {
            var package = new DataPackage();

            package.SetHtmlFormat(HtmlFormatHelper.CreateHtmlFormat(html));

            if (!string.IsNullOrEmpty(plainText))
            {
                package.SetText(plainText);
            }

            Clipboard.SetContent(package);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not write rich text to the clipboard.");

            return false;
        }
    }
}
