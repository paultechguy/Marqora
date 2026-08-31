// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Abstractions.Editing;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Editing;

/// <summary>
/// Routes each Format-menu command to the handler that knows how to carry it out. The
/// dispatch is the whole of this class; the interesting work lives in the helpers, one per
/// family of command, so each can be tested on its own.
/// </summary>
public sealed class MarkdownEditor : IMarkdownEditor
{
    public EditResult Apply(MarkdownEditCommand command, EditContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Lines.Count == 0)
        {
            return EditResult.None;
        }

        return command switch
        {
            MarkdownEditCommand.Bold => EmphasisToggle.Apply(context, "**"),
            MarkdownEditCommand.Italic => EmphasisToggle.Apply(context, "*"),
            MarkdownEditCommand.Strikethrough => EmphasisToggle.Apply(context, "~~"),
            MarkdownEditCommand.InlineCode => EmphasisToggle.Apply(context, "`"),

            MarkdownEditCommand.Link => LinkInsert.Apply(context),

            MarkdownEditCommand.Blockquote => LinePrefixToggle.Apply(context, LinePrefixKind.Blockquote),
            MarkdownEditCommand.BulletList => LinePrefixToggle.Apply(context, LinePrefixKind.Bullet),
            MarkdownEditCommand.NumberedList => LinePrefixToggle.Apply(context, LinePrefixKind.Numbered),
            MarkdownEditCommand.TaskList => LinePrefixToggle.Apply(context, LinePrefixKind.Task),

            MarkdownEditCommand.Heading1 => LinePrefixToggle.Apply(context, LinePrefixKind.Heading, 1),
            MarkdownEditCommand.Heading2 => LinePrefixToggle.Apply(context, LinePrefixKind.Heading, 2),
            MarkdownEditCommand.Heading3 => LinePrefixToggle.Apply(context, LinePrefixKind.Heading, 3),
            MarkdownEditCommand.Heading4 => LinePrefixToggle.Apply(context, LinePrefixKind.Heading, 4),
            MarkdownEditCommand.Heading5 => LinePrefixToggle.Apply(context, LinePrefixKind.Heading, 5),
            MarkdownEditCommand.Heading6 => LinePrefixToggle.Apply(context, LinePrefixKind.Heading, 6),
            MarkdownEditCommand.HeadingIncrease => LinePrefixToggle.Shift(context, 1),
            MarkdownEditCommand.HeadingDecrease => LinePrefixToggle.Shift(context, -1),

            MarkdownEditCommand.CodeBlock => BlockInsert.CodeBlock(context),
            MarkdownEditCommand.Table => BlockInsert.Table(context),
            MarkdownEditCommand.HorizontalRule => BlockInsert.HorizontalRule(context),

            _ => EditResult.None,
        };
    }

    public MarkdownMarkState Describe(EditContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Lines.Count == 0)
        {
            return MarkdownMarkState.None;
        }

        return new MarkdownMarkState
        {
            Bold = EmphasisToggle.WouldRemove(context, "**"),
            Italic = EmphasisToggle.WouldRemove(context, "*"),
            Strikethrough = EmphasisToggle.WouldRemove(context, "~~"),
            InlineCode = EmphasisToggle.WouldRemove(context, "`"),

            Blockquote = LinePrefixToggle.WouldRemove(context, LinePrefixKind.Blockquote),
            BulletList = LinePrefixToggle.WouldRemove(context, LinePrefixKind.Bullet),
            NumberedList = LinePrefixToggle.WouldRemove(context, LinePrefixKind.Numbered),
            TaskList = LinePrefixToggle.WouldRemove(context, LinePrefixKind.Task),

            HeadingLevel = LinePrefixToggle.HeadingLevelOf(context),
        };
    }

    public EditResult Insert(string snippetBody, EditContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snippetBody);

        return context.Lines.Count == 0 ? EditResult.None : SnippetInsert.Apply(context, snippetBody);
    }
}
