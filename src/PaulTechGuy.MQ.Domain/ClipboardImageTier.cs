// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// Which of the clipboard's offerings to take an image from, best first.
///
/// A clipboard usually carries the same picture several ways at once - a browser puts a bitmap,
/// a PNG and some HTML there together - and which one is read decides what lands on disk. The
/// order is a fidelity order, so it is written down here where it can be read and tested rather
/// than left implicit in the shape of an if-else chain.
/// </summary>
public enum ClipboardImageTier
{
    /// <summary>Nothing on the clipboard is an image.</summary>
    None = 0,

    /// <summary>
    /// Files copied in Explorer. First because copying the original re-encodes nothing at all,
    /// and because the page cannot see this flavor - a File from a DataTransfer has a name but no
    /// path - so reading it host-side is what keeps an Explorer copy byte-exact.
    /// </summary>
    Files = 1,

    /// <summary>
    /// The raw PNG a browser puts there beside the bitmap. Taken unchanged, so whatever
    /// transparency it carries survives.
    /// </summary>
    Png = 2,

    /// <summary>
    /// The device-independent bitmap every app agrees on. Always re-encoded, because it is a BMP
    /// and a BMP is not what anyone wants written next to their document.
    /// </summary>
    Bitmap = 3,
}

/// <summary>Picks the tier from what the clipboard says it has.</summary>
public static class ClipboardImageTiers
{
    public static ClipboardImageTier Choose(bool hasFiles, bool hasPng, bool hasBitmap) =>
        hasFiles ? ClipboardImageTier.Files
        : hasPng ? ClipboardImageTier.Png
        : hasBitmap ? ClipboardImageTier.Bitmap
        : ClipboardImageTier.None;

    /// <summary>
    /// Whether a tier's bytes have to be written out again rather than used as they are.
    ///
    /// Only the bitmap does. Feeding this into <see cref="ImageScaling.Plan"/> is what keeps the
    /// two decisions - which flavor, and what to do with it - from drifting apart.
    /// </summary>
    public static bool MustReencode(ClipboardImageTier tier) => tier == ClipboardImageTier.Bitmap;
}
