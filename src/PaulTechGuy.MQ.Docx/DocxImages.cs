// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Drawing.Wordprocessing;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Domain;
using A = DocumentFormat.OpenXml.Drawing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace PaulTechGuy.MQ.Docx;

/// <summary>
/// Puts pictures into the document.
///
/// Three things about a Word picture are easy to get wrong and produce a file that opens with
/// nothing visible in it. The drawing has to state its size twice, in two different elements,
/// and the two have to agree. Every drawing needs an id that is unique across the whole
/// document and is not zero. And the image bytes live in their own part, referred to by a
/// relationship id that is scoped to the part doing the referring - so the same picture used
/// in the body and in a header needs a relationship in each.
/// </summary>
internal sealed class DocxImages
{
    private readonly MainDocumentPart _main;
    private readonly string? _documentFolder;
    private readonly ExportReport _report;
    private readonly ILogger _logger;

    /// <summary>Resolved path to relationship id, so a picture used twice is stored once.</summary>
    private readonly Dictionary<string, string> _parts =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Shared by the body, and by anything else that ever draws: a repeated drawing id is one
    /// of the reliable ways to make Word call a document damaged, and zero is not allowed.
    /// </summary>
    private uint _nextDrawingId = 1;

    public DocxImages(
        MainDocumentPart main,
        string? sourceDocumentPath,
        ExportReport report,
        ILogger logger)
    {
        _main = main;
        _report = report;
        _logger = logger;

        _documentFolder = sourceDocumentPath is { Length: > 0 }
            ? Path.GetDirectoryName(Path.GetFullPath(sourceDocumentPath))
            : null;
    }

    /// <summary>
    /// The picture as a run, or null when it could not be embedded - in which case the caller
    /// writes the alt text instead and the reason has already been recorded.
    /// </summary>
    public Run? TryBuild(string url, string altText, int maximumWidthTwips)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        // A remote image cannot be fetched: Marqora makes no network calls at runtime, which
        // is a property of the product rather than an omission. The preview cannot load one
        // either - its content policy blocks it - so nothing is lost that was ever visible,
        // but the reader should still be told the file has a hole where a picture was.
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            Skip(altText, url, "not on this machine");
            return null;
        }

        if (_documentFolder is null)
        {
            Skip(altText, url, "the document has not been saved, so its images cannot be found");
            return null;
        }

        string decoded = Uri.UnescapeDataString(url);

        // The same containment check the preview uses when it serves an image: a path that
        // resolves outside the document's own folder is refused rather than followed.
        if (PathContainment.ResolveWithin(_documentFolder, decoded) is not { } full
            || !File.Exists(full))
        {
            Skip(altText, url, "not found");
            return null;
        }

        try
        {
            return Build(full, altText, maximumWidthTwips);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "The image {Path} could not be read.", full);
            Skip(altText, url, "could not be read");

            return null;
        }
    }

    /// <summary>
    /// A picture from bytes rather than from a file: what a rendered diagram arrives as.
    ///
    /// The scale says how much bigger the bitmap is than the size it should be drawn at. The
    /// shell rasterizes a diagram at twice its natural size so that it is still crisp when
    /// somebody zooms or prints it, and drawing those pixels one-for-one would put a diagram
    /// on the page at double the size its author saw.
    /// </summary>
    public Run? TryBuildFromBytes(
        byte[] png,
        string altText,
        int maximumWidthTwips,
        int scale = 1)
    {
        ArgumentNullException.ThrowIfNull(png);

        if (png.Length == 0)
        {
            return null;
        }

        ImagePart part = _main.AddImagePart(ImagePartType.Png);

        using (var source = new MemoryStream(png))
        {
            part.FeedData(source);
        }

        long maximumWidth = Measure.TwipsToEmu(maximumWidthTwips);
        long width = maximumWidth;
        long height = maximumWidth / 2;

        if (ImageDimensions.Read(png) is { } size && size.Width > 0 && size.Height > 0)
        {
            width = Measure.PixelsToEmu(size.Width) / Math.Max(1, scale);
            height = Measure.PixelsToEmu(size.Height) / Math.Max(1, scale);

            if (width > maximumWidth)
            {
                height = (long)Math.Round(height * (maximumWidth / (double)width));
                width = maximumWidth;
            }
        }

        return BuildRun(
            _main.GetIdOfPart(part),
            altText,
            "diagram.png",
            Math.Max(width, 1),
            Math.Max(height, 1));
    }

    private Run? Build(string path, string altText, int maximumWidthTwips)
    {
        if (!_parts.TryGetValue(path, out string? relationshipId))
        {
            if (PartTypeFor(path) is not { } contentType)
            {
                Skip(altText, path, "not a picture Word can show");
                return null;
            }

            ImagePart part = _main.AddImagePart(contentType);

            using (FileStream source = File.OpenRead(path))
            {
                part.FeedData(source);
            }

            relationshipId = _main.GetIdOfPart(part);
            _parts[path] = relationshipId;
        }

        (long width, long height) = SizeOf(path, maximumWidthTwips);

        return BuildRun(relationshipId, altText, path, width, height);
    }

    /// <summary>
    /// How big to draw the picture, in English Metric Units.
    ///
    /// Pixels are read as 96 to the inch, which is what the preview assumes and what an author
    /// means by "this image is 800 wide". Anything wider than the text column is scaled down
    /// with its proportions kept, and anything taller than the page after that is scaled again
    /// - a tall narrow screenshot otherwise takes three pages to itself.
    /// </summary>
    private static (long Width, long Height) SizeOf(string path, int maximumWidthTwips)
    {
        (uint Width, uint Height)? pixels = ReadPixelSize(path);

        long maximumWidth = Measure.TwipsToEmu(maximumWidthTwips);

        if (pixels is not { } size || size.Width == 0 || size.Height == 0)
        {
            // A picture whose dimensions cannot be read - an SVG, say - is given the text
            // column and a shape Word will not stretch oddly.
            return (maximumWidth, maximumWidth / 2);
        }

        long width = Measure.PixelsToEmu(size.Width);
        long height = Measure.PixelsToEmu(size.Height);

        if (width > maximumWidth)
        {
            height = (long)Math.Round(height * (maximumWidth / (double)width));
            width = maximumWidth;
        }

        return (Math.Max(width, 1), Math.Max(height, 1));
    }

    /// <summary>
    /// The picture's own size, read from its header rather than by decoding it.
    ///
    /// The reader is the one the Folio preflight already uses, so there is one answer to "how
    /// big is this picture" in the app rather than two. It reads the first part of the file
    /// and then, for a JPEG whose dimensions sit behind a lot of metadata, a larger slice.
    /// </summary>
    private static (uint Width, uint Height)? ReadPixelSize(string path)
    {
        using FileStream file = File.OpenRead(path);

        byte[] head = new byte[(int)Math.Min(file.Length, ImageDimensions.HeaderBytes)];
        int read = file.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);

        return ImageDimensions.Read(head.AsSpan(0, read));
    }

    private Run BuildRun(
        string relationshipId,
        string altText,
        string path,
        long width,
        long height)
    {
        uint id = _nextDrawingId++;
        string name = Path.GetFileName(path);
        string description = altText.Length > 0 ? altText : name;

        var picture = new PIC.Picture(
            new PIC.NonVisualPictureProperties(
                new PIC.NonVisualDrawingProperties
                {
                    Id = 0U,
                    Name = name,
                    Description = description,
                },
                new PIC.NonVisualPictureDrawingProperties(
                    new A.PictureLocks { NoChangeAspect = true, NoChangeArrowheads = true })),
            new PIC.BlipFill(
                new A.Blip { Embed = relationshipId },
                new A.SourceRectangle(),
                new A.Stretch(new A.FillRectangle())),
            new PIC.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = 0L, Y = 0L },
                    new A.Extents { Cx = width, Cy = height }),
                new A.PresetGeometry(new A.AdjustValueList())
                {
                    Preset = A.ShapeTypeValues.Rectangle,
                }));

        var drawing = new Drawing(
            new Inline(
                new Extent { Cx = width, Cy = height },
                new EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },

                // The description is the alt text, and Word reads it out. It costs nothing
                // and a picture without it is a hole in the document for anyone using a
                // screen reader.
                new DocProperties
                {
                    Id = id,
                    Name = $"Picture {id.ToString(CultureInfo.InvariantCulture)}",
                    Description = description,
                },
                new NonVisualGraphicFrameDrawingProperties(
                    new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(picture)
                    {
                        Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture",
                    }))
            {
                DistanceFromTop = 0U,
                DistanceFromBottom = 0U,
                DistanceFromLeft = 0U,
                DistanceFromRight = 0U,
            });

        return new Run(drawing);
    }

    /// <summary>
    /// The part type for a file extension, or null for something Word will not draw.
    ///
    /// WebP is deliberately absent: Word 2021 and later read it, older versions do not, and
    /// the SDK has no part type for it. Naming it as unshowable is more honest than embedding
    /// something half the readers will see as a blank box.
    /// </summary>
    private static PartTypeInfo? PartTypeFor(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => ImagePartType.Png,
            ".jpg" or ".jpeg" => ImagePartType.Jpeg,
            ".gif" => ImagePartType.Gif,
            ".bmp" => ImagePartType.Bmp,
            ".tif" or ".tiff" => ImagePartType.Tiff,
            _ => null,
        };

    private void Skip(string altText, string url, string why)
    {
        string what = altText.Length > 0 ? altText : url;

        _report.Note($"{what} ({why})");
        _logger.LogDebug("Image {Url} was not embedded: {Why}.", url, why);
    }
}
