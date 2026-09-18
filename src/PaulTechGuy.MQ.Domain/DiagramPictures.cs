// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.RegularExpressions;

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// Swaps a rendered diagram for the picture of it, for markup going somewhere that cannot draw
/// an SVG.
///
/// Word does not merely ignore an inline <c>&lt;svg&gt;</c>. It discards the shapes and keeps the
/// element's text children, so a flowchart pastes as a list of its node labels in document order
/// - indistinguishable from prose the author wrote, in the middle of their document. That is
/// worse than losing the diagram, because it is wrong rather than missing.
///
/// The picture is the one the shell already draws. <c>RequestDiagramPngAsync</c> rasterizes a
/// diagram by its hash at twice its size, and the Word export and the preview's own Copy as PNG
/// have been using it all along; this is a third caller rather than a second rasterizer, so the
/// same diagram is the same picture wherever it is copied from.
///
/// When no picture comes back the diagram is removed outright. Nothing is substituted for it and
/// no note is left saying one was here: a fragment must not carry this app's commentary into
/// somebody else's document, which is the same rule that keeps the blocked-media chip drawn in
/// CSS rather than in markup.
/// </summary>
public static partial class DiagramPictures
{
    /// <summary>The shell rasterizes at twice the size; see requestDiagramPng in app.js.</summary>
    private const int RasterScale = 2;

    /// <summary>
    /// Every diagram hash in the markup, so the caller can ask for those pictures before it
    /// starts building. Fetching is a round trip per diagram and the build is synchronous, which
    /// is the same reason the Word export gathers its hashes up front.
    /// </summary>
    public static IReadOnlyCollection<string> HashesIn(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var hashes = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match diagram in RenderedDiagram().Matches(html))
        {
            string hash = diagram.Groups["hash"].Value;

            if (hash.Length > 0)
            {
                hashes.Add(hash);
            }
        }

        return hashes;
    }

    /// <summary>
    /// Replaces each rendered diagram with an image element, or removes it when there is no
    /// picture for it.
    /// </summary>
    public static string Substitute(string html, IReadOnlyDictionary<string, byte[]> pictures)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(pictures);

        if (html.Length == 0)
        {
            return html;
        }

        return RenderedDiagram().Replace(html, match =>
        {
            if (!pictures.TryGetValue(match.Groups["hash"].Value, out byte[]? png) || png.Length == 0)
            {
                return string.Empty;
            }

            string source = $"data:image/png;base64,{Convert.ToBase64String(png)}";

            // Word sizes a picture from the width and height attributes and ignores the style,
            // while a browser does the opposite; stating both means the picture is its proper
            // size in Word and still scales down in a narrow window without being squashed.
            if (!TryReadPngSize(png, out int width, out int height))
            {
                return $"<p><img src=\"{source}\" alt=\"Diagram\" style=\"max-width:100%;height:auto\" /></p>";
            }

            return string.Create(
                CultureInfo.InvariantCulture,
                $"<p><img src=\"{source}\" alt=\"Diagram\" width=\"{width / RasterScale}\" "
                + $"height=\"{height / RasterScale}\" style=\"max-width:100%;height:auto\" /></p>");
        });
    }

    /// <summary>
    /// The pixel size written in the PNG's own header.
    ///
    /// A PNG opens with an eight-byte signature and then IHDR, whose first two fields are the
    /// width and the height as big-endian four-byte integers. Reading them here avoids decoding
    /// the image to ask it how big it is, and there is no image type in this assembly to decode
    /// it with in any case.
    /// </summary>
    private static bool TryReadPngSize(ReadOnlySpan<byte> png, out int width, out int height)
    {
        width = height = 0;

        // Typed rather than written inline, or the collection expression infers a span of int
        // and the comparison does not compile.
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        // Signature, length, type, then the two fields.
        if (png.Length < 24 || !png[..8].SequenceEqual(signature))
        {
            return false;
        }

        width = BigEndian(png.Slice(16, 4));
        height = BigEndian(png.Slice(20, 4));

        // A zero would divide into a zero-sized picture, and a negative one means this is not
        // the header it looked like.
        return width >= RasterScale && height >= RasterScale;

        static int BigEndian(ReadOnlySpan<byte> four) =>
            (four[0] << 24) | (four[1] << 16) | (four[2] << 8) | four[3];
    }

    /// <summary>
    /// A diagram as the preview leaves it: the pre still carries the hash it was stamped with
    /// while its definition was still there, and holds the SVG mermaid put in its place. Written
    /// to match <c>DiagramViewer</c>'s pattern, which finds the same element for the exports.
    /// </summary>
    [GeneratedRegex(
        @"<pre\b[^>]*\bdata-mq-diagram=\x22(?<hash>[^\x22]*)\x22[^>]*>.*?</pre>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex RenderedDiagram();
}
