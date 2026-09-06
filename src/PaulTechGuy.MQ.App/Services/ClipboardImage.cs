// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Domain;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// Reads images off the Windows clipboard.
///
/// The one place WinRT imaging appears, sitting beside <see cref="ClipboardText"/> and
/// <see cref="ClipboardHtml"/> for the same reason: everything that talks to the clipboard does
/// it through one small class per flavor, so the rest of the app never holds a DataPackage.
///
/// Deliberately thin. Naming, containment and whether something is an image at all are decided
/// in Domain, where they can be tested; nothing here can be, because there is no clipboard in a
/// test run.
/// </summary>
internal static class ClipboardImage
{
    /// <summary>
    /// The registered clipboard format browsers use for a raw PNG.
    ///
    /// Not one of the StandardDataFormats, because it is a Win32 registered format rather than
    /// a WinRT one - but a DataPackageView will hand it over by name, and what comes back is the
    /// original file's bytes rather than a re-encode of a bitmap.
    /// </summary>
    private const string PngFormat = "PNG";

    /// <summary>Whether the clipboard is carrying anything this could read, without reading it.</summary>
    public static bool IsAvailable(DataPackageView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return view.Contains(StandardDataFormats.Bitmap)
            || view.Contains(PngFormat)
            || view.Contains(StandardDataFormats.StorageItems);
    }

    /// <summary>
    /// Everything on the clipboard that is an image, in the order it should be written.
    ///
    /// Empty when there is nothing to take, which the caller treats as "fall through to text"
    /// rather than as a failure.
    /// </summary>
    /// <param name="maxWidth">
    /// Cap for an image this method encodes itself, or null for none. Never applied to a file:
    /// see <paramref name="downscaleFiles"/>.
    /// </param>
    /// <param name="downscaleFiles">
    /// Whether a file copied in is held to the cap too. Off by default in settings, because a
    /// file the user chose is copied byte for byte rather than re-encoded.
    /// </param>
    public static async Task<IReadOnlyList<PastedImage>> ReadAsync(
        DataPackageView view,
        int? maxWidth,
        bool downscaleFiles,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(logger);

        // Which flavor wins is decided in Domain, beside the rule about which of them has to be
        // re-encoded. The two belong together: reading the bitmap while believing its bytes are
        // keepable is exactly how screenshots ended up on disk as .bmp.
        ClipboardImageTier tier = ClipboardImageTiers.Choose(
            view.Contains(StandardDataFormats.StorageItems),
            view.Contains(PngFormat),
            view.Contains(StandardDataFormats.Bitmap));

        // Each tier can still come up empty - a clipboard advertises formats it cannot always
        // produce - so a miss falls through to the next rather than ending the attempt.
        if (tier is ClipboardImageTier.Files
            && await ReadFilesAsync(view, maxWidth, downscaleFiles, logger).ConfigureAwait(true)
                is { Count: > 0 } files)
        {
            return files;
        }

        if (tier is ClipboardImageTier.Files or ClipboardImageTier.Png
            && view.Contains(PngFormat)
            && await ReadPngAsync(view, maxWidth, logger).ConfigureAwait(true) is { } png)
        {
            return [png];
        }

        if (view.Contains(StandardDataFormats.Bitmap)
            && await ReadBitmapAsync(view, maxWidth, logger).ConfigureAwait(true) is { } bitmap)
        {
            return [bitmap];
        }

        return [];
    }

    private static async Task<IReadOnlyList<PastedImage>> ReadFilesAsync(
        DataPackageView view,
        int? maxWidth,
        bool downscaleFiles,
        ILogger logger)
    {
        List<PastedImage> images = [];

        try
        {
            IReadOnlyList<IStorageItem> items = await view.GetStorageItemsAsync();

            foreach (IStorageItem item in items)
            {
                if (item is not StorageFile file || !ImageFileTypes.IsAllowedExtension(file.Name))
                {
                    continue;
                }

                byte[] bytes = File.ReadAllBytes(file.Path);

                // The extension got it this far; the bytes decide whether it is really an image.
                // The store checks again, which is the check that matters, but refusing here
                // keeps a folder of mixed files from producing half a paste.
                if (ImageFileTypes.ExtensionFor(bytes) is null)
                {
                    logger.LogWarning("Skipped {Name}: the bytes are not a recognized image.", file.Name);

                    continue;
                }

                if (downscaleFiles && maxWidth is { } limit)
                {
                    bytes = await EncodeAsync(bytes, limit, ClipboardImageTiers.MustReencode(ClipboardImageTier.Files), logger).ConfigureAwait(true) ?? bytes;
                }

                images.Add(new PastedImage
                {
                    Bytes = bytes,
                    Source = PastedImageSource.File,
                    SuggestedName = file.Name,
                    SourcePath = file.Path,
                });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException)
        {
            logger.LogWarning(ex, "Could not read the files on the clipboard.");
        }

        return images;
    }

    private static async Task<PastedImage?> ReadPngAsync(DataPackageView view, int? maxWidth, ILogger logger)
    {
        try
        {
            object raw = await view.GetDataAsync(PngFormat);

            // Apps are inconsistent about which of these they put there, and the difference is
            // not worth caring about, so both are unwrapped to the same stream.
            IRandomAccessStream? stream = raw switch
            {
                IRandomAccessStream direct => direct,
                RandomAccessStreamReference reference => await reference.OpenReadAsync(),
                _ => null,
            };

            if (stream is null)
            {
                return null;
            }

            byte[] bytes = await ToArrayAsync(stream).ConfigureAwait(true);

            // Whatever it claimed, it is only taken if it really is a PNG. Otherwise the tier
            // below re-encodes the bitmap, which is the safer answer anyway.
            if (ImageFileTypes.ExtensionFor(bytes) != ".png")
            {
                return null;
            }

            if (maxWidth is { } limit)
            {
                bytes = await EncodeAsync(bytes, limit, ClipboardImageTiers.MustReencode(ClipboardImageTier.Png), logger).ConfigureAwait(true) ?? bytes;
            }

            return new PastedImage { Bytes = bytes, Source = PastedImageSource.Png };
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or NotSupportedException)
        {
            // A format the clipboard advertised and then could not produce. The bitmap tier is
            // still to come, so this is a fall-through rather than a failure.
            logger.LogDebug(ex, "The clipboard's PNG flavor could not be read.");

            return null;
        }
    }

    private static async Task<PastedImage?> ReadBitmapAsync(DataPackageView view, int? maxWidth, ILogger logger)
    {
        try
        {
            RandomAccessStreamReference reference = await view.GetBitmapAsync();

            using IRandomAccessStream source = await reference.OpenReadAsync();

            byte[] bytes = await ToArrayAsync(source).ConfigureAwait(true);
            byte[]? encoded = await EncodeAsync(bytes, maxWidth, ClipboardImageTiers.MustReencode(ClipboardImageTier.Bitmap), logger).ConfigureAwait(true);

            return encoded is null
                ? null
                : new PastedImage { Bytes = encoded, Source = PastedImageSource.Bitmap };
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or NotSupportedException)
        {
            logger.LogWarning(ex, "Could not read the bitmap on the clipboard.");

            return null;
        }
    }

    /// <summary>
    /// Holds a file's bytes to the same width cap the clipboard path uses, so the preference
    /// means one thing wherever an image came from. Null when the bytes will not decode, which
    /// the caller treats as "use the original".
    /// </summary>
    public static Task<byte[]?> ResizeForFileAsync(byte[] bytes, int maxWidth, ILogger logger) =>
        EncodeAsync(bytes, maxWidth, ClipboardImageTiers.MustReencode(ClipboardImageTier.Files), logger);

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
    private static async Task<byte[]?> EncodeAsync(
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

    private static async Task<byte[]> ToArrayAsync(IRandomAccessStream stream)
    {
        stream.Seek(0);

        var buffer = new Windows.Storage.Streams.Buffer((uint)stream.Size);

        await stream.ReadAsync(buffer, (uint)stream.Size, InputStreamOptions.None);

        // Named rather than called as an extension: IBuffer.ToArray and Linq's ToArray are both
        // in scope here and the compiler picks the wrong one.
        return WindowsRuntimeBufferExtensions.ToArray(buffer);
    }
}
