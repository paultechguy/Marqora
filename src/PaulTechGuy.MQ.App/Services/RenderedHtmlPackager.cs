// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// Turns the live preview's markup into something that survives leaving the app.
///
/// The markup itself comes from the preview pane, so mermaid diagrams are already inline
/// SVG, maths is already laid out by KaTeX and code is already highlighted. What is missing
/// is everything the page was relying on the app to provide: stylesheets that were linked,
/// fonts that resolved against the app's own folders, and images that pointed at a virtual
/// origin only WebView2 knows about. This class supplies all three.
///
/// Both destinations that need this — the HTML export and the clipboard — go through here,
/// so a document exported to a file and one pasted into Word come from the same pipeline.
///
/// Output is always light. A dark background is rarely wanted in something printed or
/// dropped into someone else's document.
/// </summary>
public sealed partial class RenderedHtmlPackager(IAppPaths paths, ILogger<RenderedHtmlPackager> logger)
{
    /// <summary>Images above this size stay as links; base64 would bloat the output absurdly.</summary>
    private const long MaxEmbeddedImageBytes = 8 * 1024 * 1024;

    /// <summary>
    /// A self-contained fragment: a style block followed by the document, with no surrounding
    /// html or head element.
    ///
    /// This is the shape the clipboard wants. Custom properties are resolved on the way out,
    /// because a fragment has no root element to declare them on and the applications this is
    /// aimed at — Word, Outlook — do not implement them in any case.
    /// </summary>
    public string BuildFragment(string renderedHtml, string? sourceDocumentPath)
    {
        var builder = new StringBuilder();

        builder.AppendLine("<style>");
        builder.AppendLine(FlattenCustomProperties(ReadStyles(renderedHtml)));
        builder.AppendLine(FragmentOverrides);
        builder.AppendLine("</style>");
        builder.AppendLine("<article class=\"mq-preview\">");
        builder.AppendLine(EmbedLocalImages(renderedHtml, sourceDocumentPath));
        builder.AppendLine("</article>");

        return builder.ToString();
    }

    /// <summary>
    /// The stylesheets this particular document needs, concatenated.
    ///
    /// The app's own stylesheet is reused rather than maintaining a second one, which keeps
    /// output looking like the preview and stops the two drifting apart. It carries some
    /// rules for panes and the splitter that a standalone document has no use for, a fair
    /// trade for not having a parallel stylesheet to keep in step.
    ///
    /// The maths and highlighting themes are included only when the document actually
    /// contains them. KaTeX's stylesheet alone is around 25 KB, which is most of the file
    /// for a document with no equations in it.
    /// </summary>
    public string ReadStyles(string renderedHtml)
    {
        ArgumentNullException.ThrowIfNull(renderedHtml);

        var builder = new StringBuilder();

        builder.AppendLine(ReadAsset("app.css"));

        if (renderedHtml.Contains("hljs", StringComparison.Ordinal))
        {
            builder.AppendLine(ReadAsset(Path.Combine("vendor", "highlight", "github.min.css")));
        }

        if (renderedHtml.Contains("katex", StringComparison.Ordinal))
        {
            builder.AppendLine(EmbedKatexFonts(ReadAsset(Path.Combine("vendor", "katex", "katex.min.css"))));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Rewrites images that point at the document's folder into data URIs.
    ///
    /// In the preview those resolve through the marqora.document virtual origin, which
    /// exists only inside the app. Reading the bytes here rather than in the page avoids the
    /// content-security policy and the cross-origin rules entirely: the host already knows
    /// where the document lives and can simply open the file.
    /// </summary>
    public string EmbedLocalImages(string html, string? sourceDocumentPath)
    {
        ArgumentNullException.ThrowIfNull(html);

        string? folder = string.IsNullOrWhiteSpace(sourceDocumentPath)
            ? null
            : Path.GetDirectoryName(Path.GetFullPath(sourceDocumentPath));

        if (folder is null || !Directory.Exists(folder))
        {
            return html;
        }

        return DocumentAssetReference().Replace(html, match =>
        {
            string relative = WebUtility.UrlDecode(match.Groups["path"].Value);
            string full = Path.GetFullPath(Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar)));

            // Refuse to walk outside the document's folder.
            if (!full.StartsWith(folder, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            {
                logger.LogDebug("Leaving {Reference} as-is; no readable file behind it.", match.Value);
                return match.Value;
            }

            try
            {
                var info = new FileInfo(full);

                if (info.Length > MaxEmbeddedImageBytes)
                {
                    logger.LogInformation(
                        "{Path} is {Size:N0} bytes, too large to embed; left as a link.", full, info.Length);
                    return match.Value;
                }

                string data = Convert.ToBase64String(File.ReadAllBytes(full));

                return $"{match.Groups["attr"].Value}=\"data:{MediaTypeFor(full)};base64,{data}\"";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not embed {Path}.", full);
                return match.Value;
            }
        });
    }

    /// <summary>
    /// Substitutes the stylesheet's custom properties for their light-theme values.
    ///
    /// Every colour in the app's stylesheet is a var(--mq-...) declared on :root and
    /// re-pointed for dark mode. That works in a whole page, but a clipboard fragment has no
    /// root element to carry the declarations, and Word and Outlook do not implement custom
    /// properties regardless. Folding the values in leaves plain CSS they understand, and
    /// taking them from the bare :root block — never the dark one — is what keeps a pasted
    /// document light even when the app is not.
    /// </summary>
    private static string FlattenCustomProperties(string css)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);

        // Anchored at column zero, which is the light :root and only the light one. The dark
        // overrides are written as :root[data-theme="dark"], and the print block indents.
        foreach (Match block in LightRootBlock().Matches(css))
        {
            foreach (Match declaration in CustomProperty().Matches(block.Groups["body"].Value))
            {
                values[declaration.Groups["name"].Value] = declaration.Groups["value"].Value.Trim();
            }
        }

        if (values.Count == 0)
        {
            return css;
        }

        // A value can itself be a var() reference, so this runs more than once. Three passes
        // is deeper than the stylesheet nests and cannot spin.
        for (int pass = 0; pass < 3; pass++)
        {
            string previous = css;

            css = VarReference().Replace(css, match => values.TryGetValue(match.Groups["name"].Value, out string? value)
                ? value
                : match.Groups["fallback"].Success
                    ? match.Groups["fallback"].Value.Trim()
                    : match.Value);

            if (css == previous)
            {
                break;
            }
        }

        return css;
    }

    private string ReadAsset(string relativePath)
    {
        string full = Path.Combine(paths.WebAssetsDirectory, relativePath);

        if (File.Exists(full))
        {
            return File.ReadAllText(full);
        }

        logger.LogWarning("Stylesheet {Path} is missing; the output will be missing its styling.", full);

        return string.Empty;
    }

    /// <summary>
    /// Inlines the KaTeX web fonts as data URIs.
    ///
    /// The stylesheet refers to them as fonts/KaTeX_Main-Regular.woff2 and similar, relative
    /// paths that resolve inside the app but nowhere else. Left alone, every equation would
    /// silently fall back to a serif face and look wrong. The woff and truetype sources are
    /// dropped at the same time: the restore script keeps only woff2, so those entries point
    /// at files that were never shipped.
    /// </summary>
    private string EmbedKatexFonts(string css)
    {
        if (css.Length == 0)
        {
            return css;
        }

        string fontFolder = Path.Combine(paths.WebAssetsDirectory, "vendor", "katex", "fonts");

        // Drop the sources whose files are not shipped, before embedding what is.
        css = UnshippedFontSource().Replace(css, string.Empty);

        return KatexFontReference().Replace(css, match =>
        {
            string file = match.Groups["file"].Value;
            string full = Path.Combine(fontFolder, file);

            if (!File.Exists(full))
            {
                logger.LogDebug("KaTeX font {File} is not present; leaving the reference alone.", file);
                return match.Value;
            }

            try
            {
                return $"url(data:font/woff2;base64,{Convert.ToBase64String(File.ReadAllBytes(full))})";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not embed KaTeX font {File}.", file);
                return match.Value;
            }
        });
    }

    private static string MediaTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".avif" => "image/avif",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream",
    };

    /// <summary>Matches a woff2 reference in the KaTeX stylesheet.</summary>
    [GeneratedRegex(@"url\(fonts/(?<file>[A-Za-z0-9_\-]+\.woff2)\)", RegexOptions.IgnoreCase)]
    private static partial Regex KatexFontReference();

    /// <summary>Matches the woff and truetype sources, which the restore script does not ship.</summary>
    [GeneratedRegex(
        @",\s*url\(fonts/[A-Za-z0-9_\-]+\.(?:woff|ttf)\)\s*format\(\x22[^\x22]+\x22\)",
        RegexOptions.IgnoreCase)]
    private static partial Regex UnshippedFontSource();

    /// <summary>
    /// Matches src or poster attributes pointing at the document virtual origin. Quotes are
    /// written as \x22 so the pattern itself contains none, which keeps it readable in a
    /// C# string without escaping games.
    /// </summary>
    [GeneratedRegex(
        @"(?<attr>src|poster)\s*=\s*\x22https://marqora\.document/(?<path>[^\x22]*)\x22",
        RegexOptions.IgnoreCase)]
    private static partial Regex DocumentAssetReference();

    /// <summary>The light :root block, and nothing indented or qualified.</summary>
    [GeneratedRegex(@"^:root[ \t]*\{(?<body>[^}]*)\}", RegexOptions.Multiline)]
    private static partial Regex LightRootBlock();

    [GeneratedRegex(@"--(?<name>[A-Za-z0-9_\-]+)\s*:\s*(?<value>[^;]+);")]
    private static partial Regex CustomProperty();

    [GeneratedRegex(@"var\(\s*--(?<name>[A-Za-z0-9_\-]+)\s*(?:,(?<fallback>[^()]*))?\)")]
    private static partial Regex VarReference();

    /// <summary>
    /// Undoes the app's layout for a fragment that is about to be dropped into someone
    /// else's document, where it is content rather than a pane in a split view.
    /// </summary>
    private const string FragmentOverrides = """
        .mq-preview {
          max-width: none;
          margin: 0;
          padding: 0;
          height: auto;
          overflow: visible;
          font-size: 16px;
        }
        """;
}
