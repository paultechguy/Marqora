// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Domain;
using Windows.Foundation;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// A PDF or a printout through the paged engine, falling back to the classic one, for every
/// window that prints: the preview, the cheatsheet and a diagram pop-out
/// (docs/Export-Alignment-Plan.md, §6.6). One copy of the rules, so the three cannot disagree
/// about when a job may fall back.
///
/// <list type="bullet">
/// <item>The classic engine when the settings ask for it.</item>
/// <item>The classic engine when the page did not hand over its print markup, or the paged
/// engine failed before anything left it - a PDF export never refuses because a page was busy,
/// and does not start now.</item>
/// <item>Never the classic engine after a printout may have reached the printer: that would
/// print it twice. The failure is reported instead.</item>
/// <item>Never the classic engine for a PDF file that cannot be written: it could not write it
/// either.</item>
/// </list>
/// </summary>
internal static class PagedJobs
{
    /// <summary>A PDF file. Answers which engine wrote it.</summary>
    /// <param name="requestMarkup">The page's print markup, or null when it did not answer.</param>
    /// <param name="exportClassic">The page printed as it stands, through its print stylesheet.</param>
    public static async Task<PrintEngine> ExportPdfAsync(
        IntPtr parentWindow,
        IWebAssetProvider assets,
        Func<Task<string?>> requestMarkup,
        string path,
        PdfPageSetup setup,
        string title,
        PaperFurniture furniture,
        TypedEventHandler<CoreWebView2, CoreWebView2WebResourceRequestedEventArgs>? documentAssets,
        ILogger logger,
        Func<Task> exportClassic)
    {
        ArgumentNullException.ThrowIfNull(requestMarkup);
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(exportClassic);

        if (!setup.UseClassicEngine)
        {
            string? markup = await requestMarkup().ConfigureAwait(true);

            if (markup is null)
            {
                logger.LogWarning("The page did not hand over its print markup; {Path} goes to the classic engine.", path);
            }
            else
            {
                try
                {
                    await PagedPrintHost.ExportAsync(
                        parentWindow, assets, markup, setup, title, furniture, path,
                        PagedHostMode.OffScreen, documentAssets, logger).ConfigureAwait(true);

                    return PrintEngine.Paged;
                }
                catch (PagedOutputException ex) when (ex.InnerException is IOException or UnauthorizedAccessException)
                {
                    ExceptionDispatchInfo.Throw(ex.InnerException);
                    throw;
                }
                catch (Exception ex) when (IsEngineFailure(ex))
                {
                    logger.LogWarning(ex, "The paged engine could not write {Path}; it goes to the classic engine.", path);
                }
            }
        }

        await exportClassic().ConfigureAwait(true);

        return PrintEngine.Classic;
    }

    /// <summary>A printout. Answers which engine printed it.</summary>
    /// <param name="requestMarkup">The page's print markup, or null when it did not answer.</param>
    /// <param name="printClassic">The page printed as it stands, through its print stylesheet.</param>
    public static async Task<PrintEngine> PrintAsync(
        IntPtr parentWindow,
        IWebAssetProvider assets,
        Func<Task<string?>> requestMarkup,
        PrintJob job,
        string title,
        PaperFurniture furniture,
        bool useClassicEngine,
        TypedEventHandler<CoreWebView2, CoreWebView2WebResourceRequestedEventArgs>? documentAssets,
        ILogger logger,
        Func<Task> printClassic)
    {
        ArgumentNullException.ThrowIfNull(requestMarkup);
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(printClassic);

        if (!useClassicEngine)
        {
            string? markup = await requestMarkup().ConfigureAwait(true);

            if (markup is null)
            {
                logger.LogWarning("The page did not hand over its print markup; the print goes to the classic engine.");
            }
            else
            {
                try
                {
                    await PagedPrintHost.PrintAsync(
                        parentWindow, assets, markup, job, title, furniture,
                        PagedHostMode.OffScreen, documentAssets, logger).ConfigureAwait(true);

                    return PrintEngine.Paged;
                }
                catch (PagedOutputException ex)
                {
                    logger.LogError(ex, "The paged engine failed sending to {Printer}; not retried, to avoid a second copy.", job.PrinterName);
                    throw new InvalidOperationException(
                        $"The pages could not be sent to {job.PrinterName}. Check the print queue before printing again: some may have arrived.",
                        ex);
                }
                catch (Exception ex) when (IsEngineFailure(ex))
                {
                    logger.LogWarning(ex, "The paged engine could not lay out the print; it goes to the classic engine.");
                }
            }
        }

        await printClassic().ConfigureAwait(true);

        return PrintEngine.Classic;
    }

    /// <summary>
    /// A failure of the paged engine itself - a timeout, the print page misbehaving, the
    /// runtime refusing a call - as opposed to one the classic engine would meet as well.
    /// </summary>
    private static bool IsEngineFailure(Exception ex) =>
        ex is PagedOutputException
            or TimeoutException
            or InvalidOperationException
            or COMException
            or JsonException;
}
