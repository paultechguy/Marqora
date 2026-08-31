// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Abstractions.Ui;

/// <summary>Text the editor reports back after the user types, tagged with its tab.</summary>
public sealed class EditorTextChangedEventArgs(Guid documentId, string text) : EventArgs
{
    public Guid DocumentId { get; } = documentId;

    public string Text { get; } = text;
}

/// <summary>Identifies a pane and its new zoom percentage.</summary>
public sealed class ZoomChangedEventArgs(EditorPane pane, int percent) : EventArgs
{
    public EditorPane Pane { get; } = pane;

    public int Percent { get; } = percent;
}

/// <summary>
/// A right-click inside one of the panes, and everything the host needs to put a menu up
/// for it.
///
/// The panes draw no menu of their own. Monaco's is switched off and so is Chromium's, so
/// that both context menus are ordinary WinUI flyouts styled from Themes/Menus.xaml along
/// with the header menu bar - rather than three menus from three toolkits, one of which
/// followed Edge's dark mode instead of the app's theme.
///
/// The position is in the WebView's own coordinates, measured from its top-left corner in
/// device-independent pixels, which is what a flyout wants. Pane zoom does not enter into
/// it: the source pane zooms by changing font size and the preview by scaling its content,
/// so neither moves the pointer relative to the surface it was clicked on.
///
/// What was under the pointer travels with the event rather than being fetched afterwards.
/// A menu is only worth showing at the moment of the click, and asking the page about it
/// later would race with the user typing.
/// </summary>
public sealed class PaneContextMenuEventArgs(
    EditorPane pane,
    double x,
    double y,
    bool hasSelection,
    string? linkUrl,
    string? imageUrl) : EventArgs
{
    public EditorPane Pane { get; } = pane;

    public double X { get; } = x;

    public double Y { get; } = y;

    /// <summary>Whether anything is selected in that pane, which is what enables Copy and Cut.</summary>
    public bool HasSelection { get; } = hasSelection;

    /// <summary>The link that was right-clicked, already absolute, or null.</summary>
    public string? LinkUrl { get; } = linkUrl;

    /// <summary>The image that was right-clicked, already absolute, or null.</summary>
    public string? ImageUrl { get; } = imageUrl;
}

/// <summary>
/// What the preview needs in order to keep following a diagram on behalf of a window.
///
/// The id belongs to the window and never changes. <see cref="Hash"/> is the diagram's
/// definition as the window last saw it, which is where the preview resumes tracking from;
/// it moves as the diagram is edited, and the preview reports each new value back.
/// </summary>
public readonly record struct DiagramWatch(Guid Id, Guid DocumentId, string Hash);

/// <summary>
/// A diagram the user asked to see in its own window.
///
/// Carries the rendered SVG rather than the mermaid definition: the preview has already done
/// the work, so a pop-out costs nothing and cannot disagree with what is on screen. The hash
/// of the definition comes too, so a second double-click on the same diagram can find the
/// window already showing it.
/// </summary>
public sealed class DiagramActivatedEventArgs(Guid documentId, int index, string hash, string svg) : EventArgs
{
    public Guid DocumentId { get; } = documentId;

    /// <summary>Where the diagram sat when it was opened. A label, not an identity.</summary>
    public int Index { get; } = index;

    public string Hash { get; } = hash;

    public string Svg { get; } = svg;
}

/// <summary>
/// A watched diagram re-rendered after an edit. Only diagrams with a window open are
/// reported, and only when the SVG actually changed.
///
/// The hash is the definition the preview is now tracking, which the window keeps so that
/// reopening the same diagram finds it rather than opening a second copy.
/// </summary>
public sealed class DiagramUpdatedEventArgs(Guid diagramId, string hash, int index, string svg) : EventArgs
{
    public Guid DiagramId { get; } = diagramId;

    public string Hash { get; } = hash;

    public int Index { get; } = index;

    public string Svg { get; } = svg;
}

/// <summary>
/// A watched diagram whose definition no longer parses. The window keeps its last good
/// render and says that what it shows has stopped matching the source.
/// </summary>
public sealed class DiagramInvalidEventArgs(Guid diagramId, string message) : EventArgs
{
    public Guid DiagramId { get; } = diagramId;

    /// <summary>Mermaid's own complaint, passed through rather than summarised.</summary>
    public string Message { get; } = message;
}

/// <summary>
/// The bridge to the WebView-hosted editor and preview surface.
///
/// The shell keeps one editor model and one cached preview per open document, so switching
/// tabs restores undo history, cursor and scroll position without re-rendering. The view
/// model talks to this interface only, so the hosting technology can be replaced without
/// touching MVVM code.
/// </summary>
public interface IPreviewHost
{
    /// <summary>True once the shell document has loaded and the bridge handshake completed.</summary>
    bool IsReady { get; }

    event EventHandler? Ready;

    event EventHandler<EditorTextChangedEventArgs>? EditorTextChanged;

    /// <summary>Raised when the user activates a link that points outside the document.</summary>
    event EventHandler<Uri>? ExternalLinkActivated;

    /// <summary>Raised when the user zooms with Ctrl and the mouse wheel inside a pane.</summary>
    event EventHandler<ZoomChangedEventArgs>? ZoomChanged;

    /// <summary>Raised when the user drags the in-page splitter. Value is the source-pane fraction.</summary>
    event EventHandler<double>? SplitterMoved;

    /// <summary>Raised for editor-originated commands such as save.</summary>
    event EventHandler<string>? CommandInvoked;

    /// <summary>
    /// Raised in response to <see cref="RequestSelectionForClipboardAsync"/> and to
    /// <see cref="RequestPreviewSelectionForClipboardAsync"/>. Either way the payload is
    /// text for the clipboard, so both replies come back the same way.
    /// </summary>
    event EventHandler<string>? SelectionCopied;

    /// <summary>Raised when the user right-clicks in either pane.</summary>
    event EventHandler<PaneContextMenuEventArgs>? ContextMenuRequested;

    /// <summary>Raised when the user double-clicks a rendered diagram in the preview.</summary>
    event EventHandler<DiagramActivatedEventArgs>? DiagramActivated;

    /// <summary>Raised when a diagram named by <see cref="WatchDiagrams"/> re-rendered.</summary>
    event EventHandler<DiagramUpdatedEventArgs>? DiagramUpdated;

    /// <summary>
    /// Raised when a watched diagram is no longer in its document - the fenced block was
    /// deleted, or the document was closed. Carries the window's id.
    /// </summary>
    event EventHandler<Guid>? DiagramRemoved;

    /// <summary>Raised when a watched diagram stops parsing, and again once it parses.</summary>
    event EventHandler<DiagramInvalidEventArgs>? DiagramInvalid;

    /// <summary>
    /// Names the diagrams worth reporting changes for, replacing any previous list.
    ///
    /// Pop-out windows are the only thing that cares, and there are usually none, so the
    /// shell is told what to watch rather than pushing every diagram it renders. A document
    /// full of diagrams would otherwise send its whole rendered output across the bridge on
    /// every keystroke.
    /// </summary>
    void WatchDiagrams(IReadOnlyCollection<DiagramWatch> diagrams);

    // --------------------------------------------------------------------- tabs

    /// <summary>Creates an editor model and preview cache for a newly opened document.</summary>
    Task OpenTabAsync(Guid documentId, string sourceText, RenderedMarkdown rendered);

    /// <summary>
    /// Brings a tab's editor model and cached preview on screen. The host maps the
    /// document's folder for relative assets before the preview is shown.
    /// </summary>
    Task ActivateTabAsync(Guid documentId, string? documentPath);

    /// <summary>Disposes a tab's editor model and cached preview.</summary>
    Task CloseTabAsync(Guid documentId);

    /// <summary>Replaces a tab's preview HTML, redrawing only if that tab is on screen.</summary>
    Task UpdatePreviewAsync(Guid documentId, RenderedMarkdown rendered);

    /// <summary>Replaces a tab's editor text, for a reload from disk.</summary>
    Task SetTabTextAsync(Guid documentId, string sourceText, RenderedMarkdown rendered);

    /// <summary>Clears the surface when the last tab closes.</summary>
    Task ClearAsync();

    // ----------------------------------------------------------------- app state

    Task SetViewModeAsync(ViewMode mode);

    Task SetThemeAsync(AppTheme effectiveTheme);

    Task SetZoomAsync(EditorPane pane, ZoomLevel zoom);

    Task SetScrollSyncAsync(bool enabled);

    Task SetWordWrapAsync(bool enabled);

    Task SetLineNumbersAsync(bool enabled);

    /// <summary>Render spaces and tabs in the source pane.</summary>
    Task SetShowWhitespaceAsync(bool enabled);

    /// <summary>
    /// Replaces the diagnostics shown against one document; an empty list clears them.
    ///
    /// Markers belong to a document's model rather than to the editor, so a background tab
    /// can be updated without being brought forward.
    /// </summary>
    Task SetDiagnosticsAsync(Guid documentId, IReadOnlyList<Diagnostic> diagnostics);

    /// <summary>Clears diagnostics from every open document, for when linting is switched off.</summary>
    Task ClearDiagnosticsAsync();

    /// <summary>Mark where a wrapped source line continues.</summary>
    Task SetWrapGlyphAsync(bool enabled);

    Task SetSplitterPositionAsync(double position);

    /// <summary>Returns the split to an even one, and reports the new position back.</summary>
    Task ResetSplitterAsync();

    /// <summary>Jumps a pane to the start or the end of its content.</summary>
    Task ScrollToEdgeAsync(EditorPane pane, bool toEnd, bool bothPanes);

    Task ScrollToLineAsync(int line);

    /// <summary>
    /// Selects a span in the source pane and brings it into view, bringing the pane itself
    /// into view first if the window is showing only the preview.
    ///
    /// <paramref name="documentId"/> is a guard rather than an address: it is checked against
    /// the tab actually on screen and the request dropped when they differ. Find All's
    /// results are a snapshot, so a pick can arrive after the editor has moved on. Activate
    /// the tab first, then call this.
    ///
    /// <paramref name="focusEditor"/> is false while the user steps through results and true
    /// when they ask to be taken to one, which is the difference between the keyboard staying
    /// in the results list and moving to the text.
    ///
    /// Line and column are zero-based, as everywhere else inside the app.
    /// </summary>
    Task SelectRangeAsync(Guid documentId, int line, int column, int length, bool focusEditor);

    /// <summary>Puts the keyboard in the source pane. <see cref="FocusPaneAsync"/> for Source.</summary>
    Task FocusEditorAsync();

    /// <summary>
    /// Puts the keyboard in one of the two panes.
    ///
    /// Used to hand focus back after it has been taken by the chrome — a tab, the document
    /// list, a toolbar button — where the pane to return it to is whichever one had it, not
    /// necessarily the editor. The shell decides what to do when that pane is not on screen:
    /// it knows the view mode, and a hidden pane cannot hold the keyboard.
    /// </summary>
    Task FocusPaneAsync(EditorPane pane);

    // -------------------------------------------------------------------- edit

    /// <summary>
    /// Runs a named editing command in the source pane, such as find or undo. The names are
    /// the vocabulary of the Edit menu; the host maps them onto the editor's own actions.
    /// </summary>
    Task RunEditorCommandAsync(string command);

    /// <summary>
    /// Asks the editor for its current selection so the host can place it on the system
    /// clipboard, optionally deleting it afterwards for a cut. The text comes back through
    /// <see cref="SelectionCopied"/>.
    ///
    /// Clipboard work is done host-side because a browser only permits copy and paste
    /// during a trusted user gesture, and a click on a native menu is not one.
    /// </summary>
    Task RequestSelectionForClipboardAsync(bool cut);

    /// <summary>
    /// The preview pane's equivalent: asks for whatever text is selected in the rendered
    /// document, which also comes back through <see cref="SelectionCopied"/>. Separate from
    /// the editor's because the two panes hold separate selections, and copying from the
    /// preview must not drag the source pane into view the way an editor command does.
    /// </summary>
    Task RequestPreviewSelectionForClipboardAsync();

    /// <summary>Selects the whole rendered document in the preview pane.</summary>
    Task SelectAllInPreviewAsync();

    /// <summary>Inserts text at the caret, replacing any selection.</summary>
    Task InsertTextAsync(string text);

    // ------------------------------------------------------------------ export

    /// <summary>
    /// The preview's markup exactly as rendered, with mermaid diagrams as inline SVG, maths
    /// laid out by KaTeX and code already highlighted. Export uses this rather than
    /// re-rendering, so what is exported is what was on screen.
    /// </summary>
    Task<string> GetRenderedHtmlAsync();

    /// <summary>
    /// The preview's markup for whatever is selected there, or for the whole document when
    /// nothing is, together with the plain text of that same range.
    ///
    /// Rich-text copy uses this rather than <see cref="GetRenderedHtmlAsync"/> so that
    /// selecting part of the preview and copying gives that part, the way copying anything
    /// else does.
    /// </summary>
    Task<PreviewSelection?> GetPreviewHtmlAsync();

    /// <summary>
    /// The lines the user has selected in the editor, or null when nothing is selected.
    /// Zero-based and inclusive.
    /// </summary>
    Task<LineRange?> GetSelectionRangeAsync();

    // ----------------------------------------------------------------- authoring

    /// <summary>
    /// The selection with column precision, together with the text of the lines it covers
    /// and one line either side. Null when there is no editor to ask.
    ///
    /// The lines come back with the selection rather than being read from the workspace,
    /// which trails the editor by a debounce interval: typing a word and immediately
    /// pressing Ctrl+B would otherwise act on text that is a keystroke out of date.
    /// </summary>
    Task<EditContext?> GetEditContextAsync();

    /// <summary>
    /// Applies a batch of edits as one undoable step and leaves the caret where the
    /// command asked for it.
    ///
    /// Every edit is addressed against the document as <see cref="GetEditContextAsync"/>
    /// reported it, so they are applied together rather than in sequence.
    /// </summary>
    Task ApplyEditsAsync(EditResult result);

    /// <summary>
    /// Replaces a document's text as a single undoable edit, rather than resetting the model.
    ///
    /// The formatter uses this so that Ctrl+Z takes a whole reformat back in one step, which
    /// is the first thing anyone reaches for when a formatter surprises them.
    /// </summary>
    Task ReplaceTextAsync(Guid documentId, string text, RenderedMarkdown rendered);

    /// <summary>
    /// Prints the preview to a PDF file. The editor pane is excluded by print styles rather
    /// than by changing the view, so the window does not visibly change during an export.
    /// </summary>
    Task ExportPdfAsync(string path, PdfPageSetup setup);

    /// <summary>
    /// Prints the preview, for a paper copy rather than a file. The same print styles that
    /// keep the editor pane out of an exported PDF apply here, so the window does not
    /// visibly change while the job runs.
    ///
    /// The printer is already chosen: the caller shows the Windows print dialog and passes
    /// the answer in. That is what makes this a settings-driven print rather than one of the
    /// WebView's own dialogs, and so the only route on which the browser's header and footer
    /// can be switched off.
    /// </summary>
    Task PrintAsync(PrintJob job);
}

/// <summary>An inclusive, zero-based range of editor lines.</summary>
public readonly record struct LineRange(int Start, int End);

/// <summary>
/// A slice of the rendered preview in both flavours the clipboard wants.
/// <see cref="Text"/> is empty when the whole document was taken rather than a selection.
/// </summary>
public sealed record PreviewSelection(string Html, string Text);
