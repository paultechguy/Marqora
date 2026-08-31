// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Abstractions.Editing;

/// <summary>
/// Turns a Format-menu command into the edits that carry it out.
///
/// Implementations must be pure: same command and context in, same edits out, no I/O and
/// no state between calls. Nothing here knows about Monaco or the bridge — the caller
/// applies whatever comes back — which is what keeps the interesting half of the authoring
/// commands testable by handing it a few strings.
/// </summary>
public interface IMarkdownEditor
{
    /// <summary>
    /// Works out what <paramref name="command"/> should do to the selection described by
    /// <paramref name="context"/>. Returns <see cref="EditResult.None"/> when the command
    /// has nothing to do, which callers should treat as success rather than failure.
    /// </summary>
    EditResult Apply(MarkdownEditCommand command, EditContext context);

    /// <summary>
    /// What the formatting toolbar should show for this selection.
    ///
    /// Every flag says what <see cref="Apply"/> would do rather than what the text is, and
    /// both come from the same code, so a lit button and the edit it performs cannot
    /// disagree. See <see cref="MarkdownMarkState"/> for why that distinction matters.
    /// </summary>
    MarkdownMarkState Describe(EditContext context);

    /// <summary>
    /// Puts a snippet in at the caret, replacing any selection.
    ///
    /// The body is plain markdown. An optional <c>$0</c> says where the caret should end up
    /// and is removed on the way in; <c>$$0</c> escapes a literal one. Without a marker the
    /// caret lands after what was inserted.
    /// </summary>
    EditResult Insert(string snippetBody, EditContext context);
}
