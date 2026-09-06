// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// Which clipboard flavor an image came off, best first.
///
/// The order is the fidelity order, and it is why the reader tries them in turn rather than
/// taking the first thing it finds.
/// </summary>
public enum PastedImageSource
{
    /// <summary>
    /// A file copied in Explorer. Copied byte for byte with no re-encoding at all, which is the
    /// only tier that cannot lose anything.
    /// </summary>
    File = 0,

    /// <summary>
    /// The raw PNG that Chromium, Edge and Firefox put on the clipboard beside the bitmap.
    /// Taken unchanged, so alpha survives - which is what a Windows 11 window capture, with its
    /// rounded transparent corners, actually needs.
    /// </summary>
    Png = 1,

    /// <summary>
    /// The device-independent bitmap every app agrees on. Decoded and re-encoded as PNG, which
    /// normalizes whatever the source app put there at the cost of whatever the DIB dropped.
    /// </summary>
    Bitmap = 2,
}

/// <summary>
/// One image taken off the clipboard, ready to be written.
///
/// The bytes are already in their final form: the reader has done any decoding, downscaling and
/// re-encoding, so the store only has to name the file and write it.
/// </summary>
public sealed record PastedImage
{
    public required ReadOnlyMemory<byte> Bytes { get; init; }

    public required PastedImageSource Source { get; init; }

    /// <summary>
    /// What the file was called where it came from, or null for a bitmap, which has no name and
    /// gets a numbered one.
    /// </summary>
    public string? SuggestedName { get; init; }

    /// <summary>
    /// Where the file was copied from, for <see cref="PastedImageSource.File"/> only.
    ///
    /// Carried so the paste can notice that a file is already sitting where it would be put, and
    /// reference it rather than making a second copy of it beside the first. Null for anything
    /// that came off the clipboard as pixels, which was never anywhere.
    /// </summary>
    public string? SourcePath { get; init; }
}
