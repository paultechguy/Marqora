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
/// Debug builds only: every diagram's light drawing saved as an SVG, for Word itself to be
/// tried on (docs/Export-Alignment-Plan.md, §7.2). What is left of the week-one engine spike
/// (§4): its PDF and print items went when Export to PDF and Print moved to the paged engine,
/// and this one stays for phase 3's diagram work.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// Spike S6: every diagram's light drawing, as the SVG Word would be given, written beside
    /// a chosen index file - so Word itself can be asked which of them it draws
    /// (docs/Export-Alignment-Plan.md, §7.2). Named by source line and diagram type; the index
    /// says which drawings carry their labels as HTML in foreignObject, which Word is expected
    /// not to draw.
    /// </summary>
    internal async Task SaveDiagramSvgsAsync()
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
}
#endif
