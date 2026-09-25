// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Net;
using System.Text;
using PaulTechGuy.MQ.App.Views;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// Writes a shared review: the preview as the reviewer saw it, with the comments in the margin,
/// as one self-contained HTML file.
///
/// The same assembly <see cref="HtmlExporter"/> does - the export's stylesheets, its images
/// inlined, its diagrams made click-to-view, its layout rules - with three things added from
/// <see cref="ReviewPage"/>: the margin-note stylesheet, the stamp saying which version was
/// reviewed, and the reviewed source with the comments written into it. Kept apart from the
/// exporter rather than folded in as options on it: an export is the document, a review is
/// something said about the document, and the exporter's callers should not have to know
/// that the second exists.
///
/// Light, like every export, for the reason <see cref="HtmlExporter"/> gives.
/// </summary>
internal static class ReviewPageWriter
{
    /// <summary>Writes the page and returns the images that could not be carried inside it.</summary>
    /// <param name="body">The review page's body, from the shell: marks, numbers and notes already placed.</param>
    /// <param name="sourceMarkdown">The reviewed source with its comments in CriticMarkup.</param>
    public static async Task<IReadOnlyList<string>> WriteAsync(
        RenderedHtmlPackager packager,
        string outputPath,
        string title,
        string body,
        string? sourceDocumentPath,
        int measurePixels,
        string stamp,
        int commentCount,
        string sourceMarkdown,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        string styles = packager.ReadStyles(body);
        string content = packager.EmbedLocalImages(body, sourceDocumentPath, out IReadOnlyList<string> skipped);

        content = DiagramViewer.Wrap(content);

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
        builder.AppendLine(ExportLayout.PageCss(measurePixels));
        builder.AppendLine(DiagramViewer.CssFor(content));
        builder.AppendLine(ReviewPage.Css(measurePixels));
        builder.AppendLine(ReviewPage.HoverCss(commentCount));
        builder.AppendLine("</style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body class=\"mq-review\">");
        builder.AppendLine(stamp);
        builder.AppendLine("<article class=\"mq-preview\">");
        builder.AppendLine(content);
        builder.AppendLine("</article>");
        builder.AppendLine(ReviewPage.SourceBlock(sourceMarkdown));
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");

        await File.WriteAllTextAsync(outputPath, builder.ToString(), new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);

        return skipped;
    }

    /// <summary>
    /// The logo for the page's header, as a data URI so the page stays one file. The PNG rather
    /// than the SVG master, for the reason <see cref="AppImages"/> gives: the SVG draws its letter
    /// with a font the reader may not have. Null when the file is missing, and the header then
    /// simply has no logo.
    /// </summary>
    public static string? LogoDataUri()
    {
        try
        {
            return "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(AppImages.LogoPath));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
