// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Binary;
using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

/// <summary>
/// Sizes read out of headers the tests build byte by byte.
///
/// Hand-built rather than loaded from fixture files, because what is being tested is the reading
/// of specific offsets - and a fixture would prove only that one particular encoder writes them
/// where this expects. Every case here says which bytes it is asserting about.
/// </summary>
public sealed class ImageDimensionsTests
{
    [Fact]
    public void A_png_reports_its_ihdr_size()
    {
        byte[] png = [.. new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, .. new byte[16]];

        "IHDR"u8.CopyTo(png.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), 1920);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20), 1080);

        ImageDimensions.Read(png).ShouldBe((1920u, 1080u));
    }

    [Fact]
    public void A_gif_reports_its_logical_screen_size()
    {
        byte[] gif = new byte[16];

        "GIF89a"u8.CopyTo(gif);
        BinaryPrimitives.WriteUInt16LittleEndian(gif.AsSpan(6), 640);
        BinaryPrimitives.WriteUInt16LittleEndian(gif.AsSpan(8), 480);

        ImageDimensions.Read(gif).ShouldBe((640u, 480u));
    }

    /// <summary>A negative height means top-down rows, not a negative picture.</summary>
    [Fact]
    public void A_bmp_with_top_down_rows_reports_a_positive_height()
    {
        byte[] bmp = new byte[32];

        "BM"u8.CopyTo(bmp);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18), 800);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), -600);

        ImageDimensions.Read(bmp).ShouldBe((800u, 600u));
    }

    [Fact]
    public void A_lossy_webp_reports_its_fourteen_bit_size()
    {
        byte[] webp = new byte[32];

        "RIFF"u8.CopyTo(webp);
        "WEBP"u8.CopyTo(webp.AsSpan(8));
        "VP8 "u8.CopyTo(webp.AsSpan(12));
        BinaryPrimitives.WriteUInt16LittleEndian(webp.AsSpan(26), 1024);
        BinaryPrimitives.WriteUInt16LittleEndian(webp.AsSpan(28), 768);

        ImageDimensions.Read(webp).ShouldBe((1024u, 768u));
    }

    /// <summary>The extended container stores the canvas one less than it is.</summary>
    [Fact]
    public void An_extended_webp_reports_its_canvas_size()
    {
        byte[] webp = new byte[32];

        "RIFF"u8.CopyTo(webp);
        "WEBP"u8.CopyTo(webp.AsSpan(8));
        "VP8X"u8.CopyTo(webp.AsSpan(12));

        webp[24] = 0xFF;
        webp[25] = 0x03;
        webp[27] = 0x7F;
        webp[28] = 0x02;

        ImageDimensions.Read(webp).ShouldBe((1024u, 640u));
    }

    /// <summary>
    /// The one that is a scan rather than an offset: a JPEG can carry any amount of metadata
    /// before the frame header that actually says how big it is.
    /// </summary>
    [Fact]
    public void A_jpeg_is_found_past_its_metadata()
    {
        byte[] jpeg = [
            0xFF, 0xD8,
            0xFF, 0xE0, 0x00, 0x10, .. new byte[14],   // APP0, 16 bytes
            0xFF, 0xFE, 0x00, 0x08, .. new byte[6],    // a comment, 8 bytes
            0xFF, 0xC0, 0x00, 0x11, 0x08, 0x04, 0x38, 0x07, 0x80, .. new byte[8],
        ];

        ImageDimensions.Read(jpeg).ShouldBe((1920u, 1080u));
    }

    [Fact]
    public void A_progressive_jpeg_is_read_the_same_way()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xC2, 0x00, 0x11, 0x08, 0x01, 0x2C, 0x01, 0x90, .. new byte[8]];

        ImageDimensions.Read(jpeg).ShouldBe((400u, 300u));
    }

    /// <summary>
    /// Unknown is a real answer, and every caller reads it as "leave this one alone" - an image
    /// whose size cannot be established is not one to start re-encoding.
    /// </summary>
    [Theory]
    [InlineData("not an image at all, just text")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'></svg>")]
    public void Something_with_no_readable_size_is_unknown(string content) =>
        ImageDimensions.Read(System.Text.Encoding.UTF8.GetBytes(content)).ShouldBeNull();

    [Fact]
    public void A_truncated_header_is_unknown_rather_than_read_past()
    {
        ImageDimensions.Read([0x89, 0x50, 0x4E, 0x47]).ShouldBeNull();
        ImageDimensions.Read([0xFF, 0xD8, 0xFF, 0xC0]).ShouldBeNull();
        ImageDimensions.Read([]).ShouldBeNull();
    }

    [Fact]
    public void A_jpeg_that_never_reaches_a_frame_header_is_unknown()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, .. new byte[14]];

        ImageDimensions.Read(jpeg).ShouldBeNull();
    }
}
