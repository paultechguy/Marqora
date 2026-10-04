// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// Marqora's teal, as two hex strings and nothing else.
///
/// The color itself, and everything about why there are two shades rather than one and a
/// tint, is explained on <c>AccentColors</c> in the app. It moved down here when the Word
/// export needed the same teal and could not see the app.
///
/// It is the app's color now, not the document's. A document wears its color theme - links,
/// note callouts and table headers included, in the preview and in Word alike - and the teal is
/// left to what belongs to Marqora itself: the outline row, the Find All tint, the cheatsheet,
/// and the marks the preview draws to talk to the user. Changing it still means changing two
/// constants in one file; it is just this file.
/// </summary>
public static class DocumentAccent
{
    /// <summary>The teal on a light page. Dark enough to read as text on white.</summary>
    public const string LightHex = "#3f8f98";

    /// <summary>The teal on a dark one, lifted far enough to still be a color.</summary>
    public const string DarkHex = "#7fcdd5";
}
