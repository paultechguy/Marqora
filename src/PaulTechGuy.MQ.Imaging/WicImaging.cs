// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Domain;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace PaulTechGuy.MQ.Imaging;

/// <summary>
/// Decoding, scaling and encoding a picture with Windows Imaging, for every caller that does it:
/// the app's clipboard paste, a picture inserted from a file, and the Folio's "Shrink images wider
/// than", in the app and in marqora-cli alike.
///
/// Moved out of the app's ClipboardImage, unchanged, so that a library and a console program can
/// reach it (docs/CLI-Implementation-Plan.md, WP-M1-15). Windows Imaging needs the Windows SDK and
/// nothing of WinUI, so this project carries no Windows App SDK reference.
/// </summary>
public static class WicImaging
{
    /// <summary>
    /// Decodes, optionally scales, and encodes as PNG.
    /// </summary>
    /// <param name="mustReencode">
    /// True when the bytes are not a format worth keeping and have to come out as PNG whatever
    /// their size.
    ///
    /// This is the whole difference between the two callers, and getting it wrong is not subtle:
    /// what <c>GetBitmapAsync</c> hands back for a clipboard DIB is a <em>BMP</em>, so skipping
    /// the encode for an image that needed no resizing wrote screenshots to disk as
    /// uncompressed .bmp files - which is most screenshots, since most are narrower than the
    /// default cap and never reached the scaling path at all.
    ///
    /// False for a PNG or a picked file, where the bytes already are what they should be and
    /// re-encoding one that needs no resizing would throw away the original for nothing.
    /// </param>
    /// <returns>Null when the bytes will not decode.</returns>
    public static async Task<byte[]?> EncodeAsync(
        byte[] bytes,
        int? maxWidth,
        bool mustReencode,
        ILogger logger)
    {
        try
        {
            using var source = new InMemoryRandomAccessStream();

            await source.WriteAsync(bytes.AsBuffer());
            source.Seek(0);

            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(source);

            // The decision is made in Domain, where a test can reach it. Both of this file's
            // bugs were in these few lines of arithmetic, and none of it needs an imaging stack
            // to be right - so what is left here is only the decoding and encoding, which do.
            ImageEncodeStep step = ImageScaling.Plan(
                decoder.PixelWidth, decoder.PixelHeight, maxWidth, mustReencode);

            if (!step.Encode)
            {
                return bytes;
            }

            var transform = new BitmapTransform
            {
                ScaledWidth = step.Width,
                ScaledHeight = step.Height,

                // Fant is the slow one and the only one that does not turn text in a screenshot
                // into fringes, which is most of what gets pasted here.
                InterpolationMode = BitmapInterpolationMode.Fant,
            };

            using SoftwareBitmap scaled = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb);

            using var destination = new InMemoryRandomAccessStream();

            BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, destination);

            // SetSoftwareBitmap wants straight alpha; the decode above asked for premultiplied
            // because that is what the scaler works in.
            encoder.SetSoftwareBitmap(SoftwareBitmap.Convert(
                scaled, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight));

            await encoder.FlushAsync();

            destination.Seek(0);

            return await ToArrayAsync(destination).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or NotSupportedException)
        {
            logger.LogWarning(ex, "Could not decode the pasted image.");

            return null;
        }
    }

    public static async Task<byte[]> ToArrayAsync(IRandomAccessStream stream)
    {
        stream.Seek(0);

        var buffer = new Windows.Storage.Streams.Buffer((uint)stream.Size);

        await stream.ReadAsync(buffer, (uint)stream.Size, InputStreamOptions.None);

        // Named rather than called as an extension: IBuffer.ToArray and Linq's ToArray are both
        // in scope here and the compiler picks the wrong one.
        return WindowsRuntimeBufferExtensions.ToArray(buffer);
    }
}
