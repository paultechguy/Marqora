// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Themes;

/// <summary>
/// One thing a theme colors: its id, which group it belongs to, what it paints, and - for a
/// slot that is read rather than merely seen - what it must stand out against.
///
/// The id is what a theme file names and what the stylesheet reads, as
/// <c>--mq-theme-&lt;id&gt;</c>. The meaning is written for the person, or the AI, filling in
/// a new theme: it is what the authoring document's slot table is generated from.
/// </summary>
public sealed record ThemeSlot(string Id, string Group, string Meaning, ContrastRule? Contrast = null);

/// <summary>
/// What a slot is read against, and how far it has to stand out.
///
/// <see cref="Against"/> is another slot's id, or one of the two colors no theme owns:
/// <see cref="ThemeSlots.Page"/> and <see cref="ThemeSlots.BodyText"/>. Those belong to the
/// page, which stays neutral in every theme, and they are read from app.css by the tests rather
/// than written down a second time here.
/// </summary>
public sealed record ContrastRule(string Against, double Minimum);
