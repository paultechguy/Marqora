// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// The markdown constructs the Format menu can apply. Each value names an intent rather
/// than a keystroke, so the menu, the accelerators and the editor bridge all agree on one
/// vocabulary.
/// </summary>
public enum MarkdownEditCommand
{
    // Inline markers, all of which toggle: applying one to text that already carries it
    // takes it back off.
    Bold,
    Italic,
    Strikethrough,
    InlineCode,

    Link,

    // Line prefixes. These apply to every line the selection touches.
    Blockquote,
    BulletList,
    NumberedList,
    TaskList,

    // Headings replace whatever level a line already has rather than stacking onto it.
    Heading1,
    Heading2,
    Heading3,
    Heading4,
    Heading5,
    Heading6,
    HeadingIncrease,
    HeadingDecrease,

    // Whole blocks inserted at the caret, with the blank lines they need around them.
    CodeBlock,
    Table,
    HorizontalRule,
}
