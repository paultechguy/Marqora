// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// The slice of a document an editing command needs to do its work.
///
/// How much is carried depends on what the command asked for — see
/// <see cref="EditContextScope"/>. Usually it is the lines the selection touches plus one either
/// side, because block commands need to know whether they are already sitting next to a blank
/// line; a command that reads structure rather than text gets the whole document instead.
/// Everything is addressed in absolute document coordinates:
/// <see cref="FirstLine"/> is the document line number of <c>Lines[0]</c>, so a command can
/// place its edits without knowing the window exists.
///
/// The lines travel with the selection rather than being read from the app's own copy of
/// the document, which lags the editor by a debounce interval. Typing a word and
/// immediately pressing Ctrl+B would otherwise compute against text that is one keystroke
/// out of date.
/// </summary>
/// <param name="Version">
/// What the editor called this revision of the document when it answered. Handed straight back
/// when the edits are applied, so a batch computed against text the user has since typed over is
/// dropped rather than written at offsets that have stopped meaning anything. Zero when the
/// editor did not say, which asks for no check.
/// </param>
public sealed record EditContext(
    IReadOnlyList<string> Lines,
    int FirstLine,
    TextRange Selection,
    int Version = 0)
{
    /// <summary>The document line number one past the last line carried.</summary>
    public int EndLine => FirstLine + Lines.Count;

    /// <summary>
    /// The text of a line by its document line number, or null when it falls outside the
    /// window — which, for the lines just past either edge, means the document ends there.
    /// </summary>
    public string? LineAt(int line) =>
        line >= FirstLine && line < EndLine ? Lines[line - FirstLine] : null;
}
