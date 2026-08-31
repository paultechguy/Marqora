// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Ui;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// Writes the rendered preview out as one self-contained HTML file.
///
/// The markup, the stylesheets and the embedded images all come from
/// <see cref="RenderedHtmlPackager"/>, which the clipboard's rich-text copy uses as well.
/// What is left here is the part only a file needs: the document element around it all, and
/// the layout rules that turn a pane in a split view back into a page.
///
/// Exports are always light. A dark background is rarely wanted in something printed or
/// embedded in someone else's document, so the theme attribute is pinned regardless of what
/// the app is showing.
/// </summary>
public sealed class HtmlExporter(RenderedHtmlPackager packager, ILogger<HtmlExporter> logger) : IHtmlExporter
{
    public async Task WriteAsync(
        string outputPath,
        string title,
        string renderedHtml,
        string? sourceDocumentPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        // A whole page can keep the custom properties: it has a root element to declare them
        // on, and any browser opening the file understands them.
        string styles = packager.ReadStyles(renderedHtml);
        string body = packager.EmbedLocalImages(renderedHtml, sourceDocumentPath);

        var builder = new StringBuilder();

        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"en\" data-theme=\"light\">");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\" />");
        builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        builder.AppendLine(CultureInfo.InvariantCulture, $"<title>{WebUtility.HtmlEncode(title)}</title>");
        builder.AppendLine("<meta name=\"generator\" content=\"Marqora\" />");
        builder.AppendLine("<style>");
        builder.AppendLine(styles);
        builder.AppendLine(ExportOverrides);
        builder.AppendLine("</style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("<article class=\"mq-preview\">");
        builder.AppendLine(body);
        builder.AppendLine("</article>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");

        await File.WriteAllTextAsync(outputPath, builder.ToString(), new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation("Exported HTML to {Path}.", outputPath);
    }

    /// <summary>
    /// Turns the app's layout rules back into a plain document: the preview is normally a
    /// pane inside a split view, with a viewport-sized tail for scroll synchronization.
    /// </summary>
    private const string ExportOverrides = """
        html, body {
          height: auto;
          overflow: visible;
          background: var(--mq-bg);
        }

        body { padding: 2.5rem 1.5rem 4rem; }

        .mq-preview {
          max-width: 46em;
          margin: 0 auto;
          padding: 0;
          font-size: 16px;
        }

        @media print {
          body { padding: 0; }
        }
        """;
}
