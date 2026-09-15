// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>How much of the document a command needs to see to do its work.</summary>
public enum EditContextScope
{
    /// <summary>
    /// The selected lines and one either side. What almost every command wants: a line prefix or
    /// an emphasis marker is decided by the lines it is applied to and nothing else.
    /// </summary>
    Selection,

    /// <summary>
    /// The whole document, from line zero.
    ///
    /// For commands that have to read structure rather than text. Nesting a list item means
    /// finding the item above it and the subtree below it, and neither is at a known distance;
    /// knowing whether a line is inside a fenced code block can only be answered from the top of
    /// the file. A larger window would not do — a scan that runs off the edge of one cannot tell
    /// that from reaching the end of the document, so it would half-move a subtree and half-
    /// renumber a list with nothing to say it had.
    /// </summary>
    Document,
}

/// <summary>
/// Which scope each command needs.
///
/// Here rather than on <c>IMarkdownEditor</c> deliberately. That interface promises to know
/// nothing about Monaco or the bridge, and "how many lines should the shell send" is exactly
/// bridge knowledge — it would be the one member of a pure text-in, edits-out contract that
/// existed to steer a message.
/// </summary>
public static class EditContextScopes
{
    /// <summary>The scope <paramref name="command"/> has to be given to work correctly.</summary>
    public static EditContextScope For(MarkdownEditCommand command) => command switch
    {
        MarkdownEditCommand.IncreaseIndent or MarkdownEditCommand.DecreaseIndent => EditContextScope.Document,
        _ => EditContextScope.Selection,
    };
}
