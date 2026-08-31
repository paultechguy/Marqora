// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// What the formatting toolbar should show for the current selection.
///
/// Every flag answers "would pressing this button take the markers off", not "is this text
/// bold". The two are not the same question, and answering the second one would produce a
/// toolbar that lies: with a selection spanning several lines, emphasis is always added and
/// never removed, so <see cref="Bold"/> is false even when the whole selection sits inside
/// a bold run. Showing it checked there would mean clicking it made the text *more* bold.
///
/// Because the flags come from the same code that performs the edits, the indicator and the
/// action cannot drift apart.
/// </summary>
public readonly record struct MarkdownMarkState
{
    public bool Bold { get; init; }

    public bool Italic { get; init; }

    public bool Strikethrough { get; init; }

    public bool InlineCode { get; init; }

    public bool Blockquote { get; init; }

    public bool BulletList { get; init; }

    public bool NumberedList { get; init; }

    public bool TaskList { get; init; }

    /// <summary>
    /// The heading level shared by every line the selection touches. Zero when none is a
    /// heading, and zero when they disagree.
    /// </summary>
    public int HeadingLevel { get; init; }

    /// <summary>Nothing active, which is what an empty or unreadable selection reports.</summary>
    public static MarkdownMarkState None { get; }
}
