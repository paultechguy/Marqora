// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Abstractions.Ui;

/// <summary>
/// Owns the diagram pop-out windows.
///
/// A window has an id of its own and follows one diagram through the document it came from,
/// so editing the definition redraws the window and deleting it says so. Double-clicking a
/// diagram that already has a window raises that one.
///
/// The windows are deliberately transient. They are not restored at startup and their
/// positions are not saved: a diagram is popped out to look at something, and the next
/// session starts from the document rather than from the windows the last one left behind.
/// </summary>
public interface IDiagramWindowService
{
    /// <summary>How many pop-out windows are open, for the menu item that closes them.</summary>
    int OpenCount { get; }

    /// <summary>
    /// What each open window is following, which is exactly what the preview needs in order
    /// to keep reporting changes.
    /// </summary>
    IReadOnlyCollection<DiagramWatch> Watched { get; }

    /// <summary>Raised whenever a window opens or closes, however it was dismissed.</summary>
    event EventHandler<int>? OpenCountChanged;

    /// <summary>Raised when <see cref="Watched"/> has changed and the preview should be retold.</summary>
    event EventHandler? WatchedChanged;

    /// <summary>
    /// Shows <paramref name="svg"/> in a window of its own, or raises and refreshes the
    /// window already following that diagram.
    ///
    /// <paramref name="documentName"/> names the window. It is taken once, when the window
    /// opens, because it still has to answer "which file was this?" after that document has
    /// been closed - which is exactly when the question gets asked.
    /// </summary>
    Task ShowAsync(Guid documentId, int index, string hash, string svg, string documentName, string documentPath);

    /// <summary>
    /// Redraws the window with this id, if one is open, and records the definition the
    /// preview is now tracking for it. Does nothing otherwise, so a stale report costs
    /// nothing.
    /// </summary>
    void Update(Guid diagramId, string hash, int index, string svg);

    /// <summary>
    /// Tells the window with this id that its diagram has gone, so it stops presenting a
    /// stale render as though it were current.
    /// </summary>
    void MarkRemoved(Guid diagramId);

    /// <summary>
    /// Tells the window with this id that its definition no longer parses, so it stops
    /// presenting a render that has stopped matching the source as though it were current.
    /// </summary>
    void MarkInvalid(Guid diagramId, string message);

    /// <summary>Closes every open pop-out. Does nothing when none are open.</summary>
    void CloseAll();

    /// <summary>
    /// Closes them all as the application exits. An open window keeps WinUI from ending the
    /// process, so this runs even though the user is on their way out.
    /// </summary>
    void Shutdown();
}
