// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// Marqora's teal, as two hex strings and nothing else.
///
/// The color itself, and everything about why there are two shades rather than one and a
/// tint, is explained on <c>AccentColors</c> in the app - which is where this lived until a
/// third reader appeared. The Word export needs the same teal for the document theme it
/// writes, and it cannot see the app: it is a library, and the app is the composition root
/// that depends on it rather than the other way round.
///
/// So the literals moved down here, where the preview, the window chrome and the exporter can
/// all read the one value, and the WinRT <c>Color</c> helpers stayed up there, where the
/// <c>Windows.UI</c> types they return belong. Changing the color still means changing two
/// constants in one file; it is just this file now.
/// </summary>
public static class DocumentAccent
{
    /// <summary>The teal on a light page. Dark enough to read as text on white.</summary>
    public const string LightHex = "#3f8f98";

    /// <summary>The teal on a dark one, lifted far enough to still be a color.</summary>
    public const string DarkHex = "#7fcdd5";

    /// <summary>
    /// The same value without the leading hash, which is how OOXML writes a color and how
    /// the shell's URL fragment carries one.
    /// </summary>
    public static string LightRgb => LightHex.TrimStart('#').ToUpperInvariant();
}
