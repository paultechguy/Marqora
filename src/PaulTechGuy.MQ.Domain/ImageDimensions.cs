// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Binary;

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// How wide and tall a picture is, read from its header rather than by decoding it.
///
/// This exists so that the Folio preflight can say "three of these will be shrunk" without an
/// imaging stack. Decoding every image on every tick of a dialog is not affordable, and the
/// planner that needs the answer is a plain library with no WinRT to call - while the size is
/// sitting in the first few dozen bytes of every format that matters.
///
/// Unknown is a real answer and the common way to fail: an unrecognized format, a truncated
/// header, or a vector image that has no pixel size at all. Every caller treats it as "leave
/// this one alone", which is the safe reading - an image whose size cannot be established is
/// not one to start re-encoding.
/// </summary>
public static class ImageDimensions
{
    /// <summary>Enough for every header below. JPEG is the one that may want more; see Jpeg.</summary>
    public const int HeaderBytes = 64 * 1024;

    private static ReadOnlySpan<byte> PngSignature =>
        [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// The picture's size, or null when it cannot be established from these bytes.
    /// </summary>
    public static (uint Width, uint Height)? Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 16)
        {
            return null;
        }

        if (bytes[..8].SequenceEqual(PngSignature))
        {
            return Png(bytes);
        }

        if (bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            return Jpeg(bytes);
        }

        if (bytes[..3].SequenceEqual("GIF"u8))
        {
            return Gif(bytes);
        }

        if (bytes[..2].SequenceEqual("BM"u8))
        {
            return Bmp(bytes);
        }

        if (bytes[..4].SequenceEqual("RIFF"u8) && bytes.Length >= 12 && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return Webp(bytes);
        }

        // Anything else - AVIF, SVG, something new - is unknown rather than guessed at. SVG has
        // no pixel size to report in the first place.
        return null;
    }

    /// <summary>IHDR is always the first chunk, and its width and height are big-endian.</summary>
    private static (uint, uint)? Png(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 24 && bytes[12..16].SequenceEqual("IHDR"u8)
            ? (BinaryPrimitives.ReadUInt32BigEndian(bytes[16..]),
               BinaryPrimitives.ReadUInt32BigEndian(bytes[20..]))
            : null;

    /// <summary>Logical screen width and height, little-endian, straight after the signature.</summary>
    private static (uint, uint)? Gif(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 10
            ? (BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]),
               BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]))
            : null;

    /// <summary>
    /// The DIB header's width and height, little-endian and signed - a negative height means the
    /// rows are stored top-down, which says nothing about how big the picture is.
    /// </summary>
    private static (uint, uint)? Bmp(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 26)
        {
            return null;
        }

        int width = BinaryPrimitives.ReadInt32LittleEndian(bytes[18..]);
        int height = BinaryPrimitives.ReadInt32LittleEndian(bytes[22..]);

        return width > 0 ? ((uint)width, (uint)Math.Abs(height)) : null;
    }

    /// <summary>
    /// Three shapes under one signature: the lossy bitstream, the lossless one, and the extended
    /// container that wraps either. They keep the size in three different places and two
    /// different bit widths, so each is read on its own terms.
    /// </summary>
    private static (uint, uint)? Webp(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 30)
        {
            return null;
        }

        ReadOnlySpan<byte> tag = bytes[12..16];

        if (tag.SequenceEqual("VP8X"u8))
        {
            // Canvas size, 24-bit little-endian, and stored one less than it is.
            uint width = (uint)(bytes[24] | (bytes[25] << 8) | (bytes[26] << 16)) + 1;
            uint height = (uint)(bytes[27] | (bytes[28] << 8) | (bytes[29] << 16)) + 1;

            return (width, height);
        }

        if (tag.SequenceEqual("VP8 "u8))
        {
            // 14 bits each, after the three-byte start code, past the frame tag.
            return (BinaryPrimitives.ReadUInt16LittleEndian(bytes[26..]) & 0x3FFFu,
                    BinaryPrimitives.ReadUInt16LittleEndian(bytes[28..]) & 0x3FFFu);
        }

        if (tag.SequenceEqual("VP8L"u8) && bytes.Length >= 25)
        {
            // 14 bits each again, but packed across four bytes and stored one less than it is.
            uint packed = BinaryPrimitives.ReadUInt32LittleEndian(bytes[21..]);

            return ((packed & 0x3FFF) + 1, ((packed >> 14) & 0x3FFF) + 1);
        }

        return null;
    }

    /// <summary>
    /// Walks the segments to the frame header, which is the only one carrying the size.
    ///
    /// Unlike the others this is a scan rather than a fixed offset, because a JPEG may carry any
    /// amount of metadata - an color profile, a thumbnail, EXIF - before it says how big the
    /// picture is. The walk is bounded by the bytes it was given, so a truncated or malformed
    /// file runs out and reports unknown rather than reading past the end.
    /// </summary>
    private static (uint, uint)? Jpeg(ReadOnlySpan<byte> bytes)
    {
        int at = 2;

        while (at + 3 < bytes.Length)
        {
            if (bytes[at] != 0xFF)
            {
                return null;
            }

            byte marker = bytes[at + 1];

            // Padding, and the standalone markers that carry no length to skip by.
            if (marker == 0xFF)
            {
                at++;
                continue;
            }

            if (marker is 0x01 or (>= 0xD0 and <= 0xD9))
            {
                at += 2;
                continue;
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 2)..]);

            // A frame header: precision, then height and width, big-endian.
            if (marker is (>= 0xC0 and <= 0xC3) or (>= 0xC5 and <= 0xC7)
                or (>= 0xC9 and <= 0xCB) or (>= 0xCD and <= 0xCF))
            {
                return at + 9 <= bytes.Length
                    ? (BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 7)..]),
                       BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 5)..]))
                    : null;
            }

            if (length < 2)
            {
                return null;
            }

            at += 2 + length;
        }

        return null;
    }
}
