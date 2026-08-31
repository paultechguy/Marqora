// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Abstractions.Ui;

/// <summary>
/// A result the user picked out of the Find All window.
///
/// <see cref="FocusEditor"/> separates looking from going: stepping through the list with the
/// arrow keys shows each match in the source pane while the keyboard stays in the results,
/// and pressing Enter or double-clicking hands the keyboard to the text.
/// </summary>
public sealed class FindMatchActivatedEventArgs(Guid documentId, FindMatch match, bool focusEditor) : EventArgs
{
    public Guid DocumentId { get; } = documentId;

    public FindMatch Match { get; } = match;

    /// <summary>True when the user asked to be taken to the match rather than shown it.</summary>
    public bool FocusEditor { get; } = focusEditor;
}

/// <summary>
/// Owns the Find All window.
///
/// One window, reused. Closing it hides it, so the results, the term and the scroll position
/// are all still there the next time it is called up — which is what makes it usable as a
/// list to work through rather than a dialog to dismiss.
///
/// The window reads the workspace itself and searches it. All that comes back out is which
/// match the user picked; moving the editor there belongs to whoever owns the editor.
/// </summary>
public interface IFindAllWindowService
{
    /// <summary>
    /// Shows the window and puts the keyboard in the search box, or raises the one already
    /// open. A non-empty <paramref name="seedTerm"/> replaces whatever the box held.
    /// </summary>
    void Show(string? seedTerm);

    /// <summary>Raised when the user picks a result.</summary>
    event EventHandler<FindMatchActivatedEventArgs>? MatchActivated;

    /// <summary>
    /// Closes the window as the application exits. A hidden window is still an open one, and
    /// WinUI keeps the process alive until every window has gone.
    /// </summary>
    void Shutdown();
}
