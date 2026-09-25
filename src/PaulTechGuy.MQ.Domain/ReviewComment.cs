// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// Where a review comment sits in the rendered preview.
///
/// The preview is the only place a reviewer selects from, so the anchor is stated in its terms:
/// the block the selection lies in, named by the zero-based source line Markdig stamped on it
/// as <c>data-src-line</c>, and the character range inside that block's text. The quote rides
/// along so a block whose text moved - heading numbers switched on mid-session, say - can still
/// be found by what was selected rather than by where it used to be.
///
/// A selection never spans two blocks. The shell refuses one that does, which is what lets a
/// single line number say where every comment belongs.
///
/// A line does not always name one element. Every cell in a table row carries the row's line,
/// and a loose list item shares its line with the paragraph inside it, so the anchor also says
/// which of the elements carrying that line it was: without it, a comment on the third "Yes" in
/// a row would come back on the first.
/// </summary>
/// <param name="Line">Zero-based source line of the innermost block holding the selection.</param>
/// <param name="Index">Which of the elements carrying that line, counting from zero in document order.</param>
/// <param name="Start">Offset of the first selected character in that block's text.</param>
/// <param name="End">Offset one past the last selected character.</param>
/// <param name="Quote">The selected text exactly as the preview showed it.</param>
public sealed record ReviewAnchor(int Line, int Index, int Start, int End, string Quote);

/// <summary>
/// One comment a reviewer attached to a passage.
///
/// Immutable: editing a note replaces the record, so the session's revision count is the only
/// thing that has to notice a change.
/// </summary>
public sealed record ReviewComment(Guid Id, ReviewAnchor Anchor, string Note);
