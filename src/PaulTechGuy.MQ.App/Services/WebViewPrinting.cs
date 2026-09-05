// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Web.WebView2.Core;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// Sends a WebView's pages to a printer the user has already chosen.
///
/// Shared by the three windows that print - the preview, the cheatsheet and the diagram
/// pop-out - so all three printouts agree, which is the same promise print-header.css makes
/// about the letterhead they draw.
///
/// This is the settings-driven print rather than either dialog the WebView can raise, and
/// that is the whole point: it is the only route on which the browser's header and footer
/// can be switched off.
/// </summary>
internal static class WebViewPrinting
{
    /// <summary>
    /// Prints and waits for the printer to accept the job.
    ///
    /// Throws <see cref="IOException"/> when the job does not land, which is a real
    /// possibility here - a printer can be off, out of paper or gone from the network
    /// between the dialog closing and this call.
    /// </summary>
    public static async Task PrintAsync(CoreWebView2 core, PrintJob job)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(job);

        CoreWebView2PrintSettings settings = core.Environment.CreatePrintSettings();

        settings.PrinterName = job.PrinterName;
        settings.Copies = job.Copies;
        settings.Collation = job.Collate
            ? CoreWebView2PrintCollation.Collated
            : CoreWebView2PrintCollation.Uncollated;

        settings.Orientation = job.Orientation == PageOrientation.Landscape
            ? CoreWebView2PrintOrientation.Landscape
            : CoreWebView2PrintOrientation.Portrait;

        // Custom, so that PageWidth and PageHeight are the ones used. Left at Default the
        // printer reaches for its own default paper instead, and the size the user picked in
        // the dialog would be quietly dropped: the paper choice lives in a DEVMODE that the
        // print call has no way to accept, so it is carried across as inches or not at all.
        settings.MediaSize = CoreWebView2PrintMediaSize.Custom;
        settings.PageWidth = job.WidthInches;
        settings.PageHeight = job.HeightInches;

        settings.MarginTop = job.MarginInches;
        settings.MarginBottom = job.MarginInches;
        settings.MarginLeft = job.MarginInches;
        settings.MarginRight = job.MarginInches;

        settings.ShouldPrintBackgrounds = job.IncludeBackgrounds;

        // Color and sides, which the dialog offers only where the driver said it could do
        // them. Default means the printer was not asked, and decides for itself - which is
        // what every job did before Marqora had a dialog of its own to ask in.
        settings.ColorMode = job.ColorMode switch
        {
            PrintColorMode.Color => CoreWebView2PrintColorMode.Color,
            PrintColorMode.Grayscale => CoreWebView2PrintColorMode.Grayscale,
            _ => CoreWebView2PrintColorMode.Default,
        };

        settings.Duplex = job.Duplex switch
        {
            PrintDuplex.OneSided => CoreWebView2PrintDuplex.OneSided,
            PrintDuplex.LongEdge => CoreWebView2PrintDuplex.TwoSidedLongEdge,
            PrintDuplex.ShortEdge => CoreWebView2PrintDuplex.TwoSidedShortEdge,
            _ => CoreWebView2PrintDuplex.Default,
        };

        // The point of the whole exercise: off, and not offered to the reader as a choice.
        settings.ShouldPrintHeaderAndFooter = false;

        settings.ScaleFactor = 1.0;

        if (!string.IsNullOrWhiteSpace(job.PageRanges))
        {
            settings.PageRanges = job.PageRanges;
        }

        CoreWebView2PrintStatus status = await core.PrintAsync(settings);

        if (status != CoreWebView2PrintStatus.Succeeded)
        {
            throw new IOException(status == CoreWebView2PrintStatus.PrinterUnavailable
                ? $"{job.PrinterName} is not available."
                : $"The pages could not be sent to {job.PrinterName}.");
        }
    }
}
