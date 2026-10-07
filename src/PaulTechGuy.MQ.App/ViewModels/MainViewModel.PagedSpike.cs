// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

#if DEBUG
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.App.Services;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Rendering;

namespace PaulTechGuy.MQ.App.ViewModels;

/// <summary>
/// The paged PDF engine's week-one spike, in Debug builds only (docs/Export-Alignment-Plan.md,
/// §4). Export to PDF is untouched: this is a second way to write the same document, there to
/// answer whether Paged.js lays pages out in a WebView2 nobody sees, how fast, at what scale and
/// with which faces. The answers go to the status line and the log.
///
/// Removed when the engine replaces the old path in phase 2, or when the spike says it cannot.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The paper furniture for a document: the Word export's own three choices, as last saved,
    /// so a paged PDF and a .docx of the same document can be laid side by side. The shared
    /// record both dialogs will offer is phase 2 (docs/Export-Alignment-Plan.md, §6.4); until
    /// then the Word setup is the one place those choices live.
    ///
    /// The cover reads the same front matter and the same date rule as Word's cover; the
    /// contents lists the same heading levels, from this document's own numbering.
    /// </summary>
    private PagedFurniture FurnitureFor(MarkdownDocument document)
    {
        DocxExportSetup layout = _settings.Current.DocxDefaults;
        FrontMatter front = FrontMatter.Read(document.Text);

        PagedCover? cover = layout.IncludeCoverPage
            ? new PagedCover(front.Title ?? document.DisplayName, front.Subject, front.CoverDate(), front.Version, front.Author)
            : null;

        PagedContents? contents = null;

        if (layout.IncludeTableOfContents)
        {
            (int first, int last) = ContentsListing.Levels(NumberingFor(document.Id));

            contents = new PagedContents(ContentsListing.Title, first, last);
        }

        return new PagedFurniture(cover, contents, layout.IncludeHeaderAndFooter);
    }

    /// <param name="export">The host's paged export, bound by the window to its own handle.</param>
    /// <param name="variant">How the print view is hidden, for the status line.</param>
    internal async Task ExportPagedPdfSpikeAsync(
        Func<string, PdfPageSetup, string, PagedFurniture, Task<PagedPrintResult>> export,
        string variant)
    {
        ArgumentNullException.ThrowIfNull(export);

        if (_workspace.Active is not { } document)
        {
            return;
        }

        PdfPageSetup setup = _settings.Current.PdfDefaults;

        string? path = await _fileDialogs
            .PickExportFileAsync(
                Path.GetFileNameWithoutExtension(SuggestedExportName(document, ".pdf")) + " (paged).pdf",
                "PDF document",
                [".pdf"])
            .ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = $"Paged PDF ({variant})...";

            PagedPrintResult result = await export(path, setup, PrintTitleOf(document), FurnitureFor(document)).ConfigureAwait(true);

            string faces = string.Join(
                ", ",
                result.Faces.Select(f => f.Key + (f.Value ? " yes" : " no")));

            StatusText = string.Create(
                CultureInfo.InvariantCulture,
                $"Paged PDF ({variant}): {result.Pages} pages, layout {result.Layout.TotalSeconds:0.0} s, "
                + $"print {result.Output.TotalSeconds:0.0} s, {result.Bytes / 1024} KB, "
                + $"tagged {(result.Tagged ? "yes" : "no")}, outline {(result.Outline ? "yes" : "no")}, "
                + $"page {result.Visibility}; footnotes {result.NotesPlaced} of {result.NotesMoved} at the foot; "
                + $"contents {result.ContentsEntries} entries; {faces}");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException
            or COMException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Paged PDF ({Variant}) failed.", variant);
            StatusText = $"Paged PDF ({variant}) failed";
            await _dialogs.ShowMessageAsync("Paged PDF failed", ex.Message).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Spike S6: every diagram's light drawing, as the SVG Word would be given, written beside
    /// a chosen index file - so Word itself can be asked which of them it draws
    /// (docs/Export-Alignment-Plan.md, §7.2). Named by source line and diagram type; the index
    /// says which drawings carry their labels as HTML in foreignObject, which Word is expected
    /// not to draw.
    /// </summary>
    internal async Task SaveDiagramSvgsSpikeAsync()
    {
        if (_workspace.Active is not { } document || _host is null)
        {
            return;
        }

        string? index = await _fileDialogs
            .PickExportFileAsync("diagram-svgs.txt", "Text file", [".txt"])
            .ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(index))
        {
            return;
        }

        string folder = Path.GetDirectoryName(index)!;
        string html = await _host.GetRenderedHtmlAsync().ConfigureAwait(true);
        var report = new System.Text.StringBuilder();
        int saved = 0;

        foreach (System.Text.RegularExpressions.Match diagram in System.Text.RegularExpressions.Regex.Matches(
            html,
            "<pre[^>]*\\bdata-src-line=\"(\\d+)\"[^>]*\\bdata-mq-diagram=\"([^\"]+)\"|<pre[^>]*\\bdata-mq-diagram=\"([^\"]+)\"[^>]*\\bdata-src-line=\"(\\d+)\""))
        {
            int line = int.Parse(diagram.Groups[1].Success ? diagram.Groups[1].Value : diagram.Groups[4].Value, CultureInfo.InvariantCulture);
            string hash = diagram.Groups[2].Success ? diagram.Groups[2].Value : diagram.Groups[3].Value;

            string? svg = await _host.RequestDiagramSvgAsync(hash).ConfigureAwait(true);

            if (string.IsNullOrEmpty(svg))
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"{line + 1}\tno drawing");
                continue;
            }

            // The drawing's own name for its type, from mermaid's aria-roledescription, kept to
            // letters, digits and hyphens. Not the first word of the source: that was
            // "%%{init:" for a diagram opening with a directive, and its colon made Windows
            // write an alternate data stream instead of a file.
            System.Text.RegularExpressions.Match role = System.Text.RegularExpressions.Regex.Match(
                svg, "aria-roledescription=\"([^\"]+)\"");
            string type = System.Text.RegularExpressions.Regex.Replace(
                role.Success ? role.Groups[1].Value : "unknown", "[^A-Za-z0-9-]", "-");

            string name = string.Create(CultureInfo.InvariantCulture, $"line-{line + 1:D4}-{type}.svg");

            await File.WriteAllTextAsync(Path.Combine(folder, name), svg).ConfigureAwait(true);

            bool foreignObject = svg.Contains("<foreignObject", StringComparison.OrdinalIgnoreCase);

            report.AppendLine(CultureInfo.InvariantCulture, $"{line + 1}\t{type}\t{name}\tforeignObject {(foreignObject ? "yes" : "no")}\t{svg.Length} chars");
            saved++;
        }

        await File.WriteAllTextAsync(index, report.ToString()).ConfigureAwait(true);

        StatusText = $"Saved {saved} diagram SVGs beside {Path.GetFileName(index)}";
        _logger.LogInformation("Saved {Count} diagram SVGs to {Folder}.", saved, folder);
    }

    /// <summary>
    /// The paged engine to a printer - spike S5. The printer is chosen in the same Windows
    /// print dialog Print uses, on the same page setup, so the two printouts can be laid side
    /// by side; the question is what the printer's unprintable edge and driver do to pages that
    /// already carry their own margins.
    /// </summary>
    /// <param name="print">The host's paged print, bound by the window to its own handle.</param>
    internal async Task PrintPagedSpikeAsync(Func<PrintJob, string, PagedFurniture, Task<PagedPrintResult>> print)
    {
        ArgumentNullException.ThrowIfNull(print);

        if (_workspace.Active is not { } document)
        {
            return;
        }

        PrintJob? job = await _printDialogs
            .PickPrinterAsync(_settings.Current.PdfDefaults)
            .ConfigureAwait(true);

        if (job is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = $"Paged print to {job.PrinterName}...";

            PagedPrintResult result = await print(job, PrintTitleOf(document), FurnitureFor(document)).ConfigureAwait(true);

            StatusText = string.Create(
                CultureInfo.InvariantCulture,
                $"Paged print to {job.PrinterName}: {result.Pages} pages, layout {result.Layout.TotalSeconds:0.0} s, "
                + $"sent in {result.Output.TotalSeconds:0.0} s");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException
            or COMException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Paged print to {Printer} failed.", job.PrinterName);
            StatusText = "Paged print failed";
            await _dialogs.ShowMessageAsync("Paged print failed", ex.Message).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
#endif
