// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Imaging;
using Shouldly;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Xunit;

namespace PaulTechGuy.MQ.Exporting.Tests;

/// <summary>
/// CLI-FO-03 and WP-M1-15: the Folio's "Shrink images wider than", moved out of the app. A picture
/// wider than the limit really comes back smaller, at the limit's width; a reduction that would
/// not shrink the file is declined; and the same picture and width always give the same bytes.
///
/// This is where a reduction that actually happens is proved: the M0-05 baseline's screenshot came
/// back no smaller, so the baseline shows only the declined path (plan §9.3).
/// </summary>
public sealed class FolioShrinkTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "marqora-shrink-tests", Guid.NewGuid().ToString("n"));

    public FolioShrinkTests() => Directory.CreateDirectory(_root);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FolioShrinker Shrinker(IImageResizer? resizer = null) =>
        new(NullLogger<FolioShrinker>.Instance, resizer ?? new WicImageResizer(NullLogger<WicImageResizer>.Instance));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder outliving its test is not a failed test.
        }
    }

    [Fact]
    public async Task A_screenshot_wider_than_the_limit_comes_back_smaller_at_the_limits_width()
    {
        FolioPlan plan = await PlanWithAsync("shot.png", await ScreenshotAsync(3200, 400));

        FolioPlan shrunk = await Shrinker().ShrinkAsync(plan, 1600, Path.Combine(_root, "scratch"), Ct);

        FolioAsset before = plan.Assets.Single();
        FolioAsset after = shrunk.Assets.Single();

        after.SourcePath.ShouldNotBe(before.SourcePath);
        after.Bytes.ShouldBeLessThan(before.Bytes);
        after.PixelWidth.ShouldBe(1600u);
        (await WidthOfAsync(File.ReadAllBytes(after.SourcePath))).ShouldBe(1600u);
        File.ReadAllBytes(before.SourcePath).Length.ShouldBe((int)before.Bytes, "the author's own picture is never written over");
    }

    [Fact]
    public async Task A_picture_within_the_limit_is_left_alone()
    {
        FolioPlan plan = await PlanWithAsync("small.png", await ScreenshotAsync(800, 200));

        FolioPlan shrunk = await Shrinker().ShrinkAsync(plan, 1600, Path.Combine(_root, "scratch"), Ct);

        shrunk.ShouldBeSameAs(plan);
    }

    /// <summary>A copy that would not be smaller - a photograph already stored as JPEG - is declined, and the original shared.</summary>
    [Fact]
    public async Task A_reduction_that_would_not_shrink_the_file_is_declined()
    {
        FolioPlan plan = await PlanWithAsync("photo.jpg", await ScreenshotAsync(3200, 400));

        FolioPlan shrunk = await Shrinker(new GrowingResizer()).ShrinkAsync(plan, 1600, Path.Combine(_root, "scratch"), Ct);

        shrunk.Assets.Single().ShouldBe(plan.Assets.Single());
    }

    [Fact]
    public async Task The_same_picture_and_width_give_the_same_bytes()
    {
        byte[] picture = await ScreenshotAsync(3000, 300);
        var resizer = new WicImageResizer(NullLogger<WicImageResizer>.Instance);

        byte[]? first = await resizer.ResizeForFileAsync(picture, 1200, Ct);
        byte[]? second = await resizer.ResizeForFileAsync(picture, 1200, Ct);

        first.ShouldNotBeNull();
        second.ShouldBe(first);
    }

    private async Task<FolioPlan> PlanWithAsync(string name, byte[] bytes)
    {
        string path = Path.Combine(_root, name);
        await File.WriteAllBytesAsync(path, bytes, Ct);

        return new FolioPlan
        {
            Documents = [],
            Warnings = [],
            Assets =
            [
                new FolioAsset
                {
                    SourcePath = path,
                    EntryName = "media/" + name,
                    Bytes = bytes.LongLength,
                    PixelWidth = await WidthOfAsync(bytes),
                    Relocated = true,
                },
            ],
        };
    }

    /// <summary>A picture the way a screenshot is: large flat areas and hard edges, which PNG compresses well.</summary>
    private static async Task<byte[]> ScreenshotAsync(int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = ((y * width) + x) * 4;
                bool band = (x / 40) % 2 == 0;

                pixels[i] = band ? (byte)0xF0 : (byte)0x30;
                pixels[i + 1] = (byte)(y < 30 ? 0x80 : 0xE0);
                pixels[i + 2] = band ? (byte)0x20 : (byte)0xC0;
                pixels[i + 3] = 0xFF;
            }
        }

        using var stream = new InMemoryRandomAccessStream();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);

        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, pixels);
        await encoder.FlushAsync();

        return await WicImaging.ToArrayAsync(stream);
    }

    private static async Task<uint> WidthOfAsync(byte[] bytes)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(bytes.AsBuffer());
        stream.Seek(0);

        return (await BitmapDecoder.CreateAsync(stream)).PixelWidth;
    }

    /// <summary>A resizer whose copy is always larger than what it was given.</summary>
    private sealed class GrowingResizer : IImageResizer
    {
        public Task<byte[]?> ResizeForFileAsync(byte[] bytes, int maxWidth, CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(new byte[bytes.Length + 1]);
    }
}
