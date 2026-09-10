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
/// A Replace All the user has asked for and not yet had.
///
/// Carries what each document would become rather than an instruction to go and work it out,
/// because working it out is the window's half of the job and applying it is the view model's.
/// Nothing here has been written anywhere yet: the handler still has to ask.
/// </summary>
public sealed class ReplaceAllRequestedEventArgs(
    IReadOnlyList<ReplaceDocumentResult> documents,
    int totalMatches,
    bool isDeletion) : EventArgs
{
    public IReadOnlyList<ReplaceDocumentResult> Documents { get; } = documents;

    public int TotalMatches { get; } = totalMatches;

    /// <summary>
    /// True when the replacement is empty, so every match is being removed rather than changed.
    ///
    /// Carried because it is the one outcome the confirmation cannot describe from counts alone,
    /// and the shortest path to it takes no typing at all: select a word, Ctrl+Shift+H, Replace
    /// All. Whoever asks the question should be able to use the right verb.
    /// </summary>
    public bool IsDeletion { get; } = isDeletion;

    /// <summary>
    /// Completed by the handler once the whole thing is over, confirmation and all.
    ///
    /// The window keeps its Replace All button disabled until this settles. Without it the
    /// button would come back the instant the handler hit its first await - which is the
    /// confirmation opening - and a second press would queue a second confirmation behind the
    /// first, replacing twice.
    /// </summary>
    public TaskCompletionSource<bool> Completion { get; } = new();
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
    /// <param name="replaceMode">
    /// True to open with the replace row showing. Only ever turns it on: arriving by Find All
    /// while the window is already open on a replace should not fold away a row the user is
    /// working in.
    /// </param>
    void Show(string? seedTerm, bool replaceMode = false);

    /// <summary>Raised when the user picks a result.</summary>
    event EventHandler<FindMatchActivatedEventArgs>? MatchActivated;

    /// <summary>
    /// Raised when the user asks to replace every match on screen. Nothing has been changed
    /// yet; the handler confirms, applies, and completes the request's Completion.
    /// </summary>
    event EventHandler<ReplaceAllRequestedEventArgs>? ReplaceAllRequested;

    /// <summary>
    /// Closes the window as the application exits. A hidden window is still an open one, and
    /// WinUI keeps the process alive until every window has gone.
    /// </summary>
    void Shutdown();
}
