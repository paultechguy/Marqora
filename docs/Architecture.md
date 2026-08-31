# Marqora architecture

This document covers how the pieces fit together and, more usefully, why several of them
are shaped the way they are. Most of the non-obvious decisions were forced by something
concrete, and those reasons are recorded here so they are not undone by accident.

---

## Layers

Dependencies point one way only. No concrete layer references another concrete layer; they
meet at the composition root in `src/PaulTechGuy.MQ.App/Program.cs`.

```
Domain          models and enums, no dependencies whatsoever
   ^
Abstractions    interfaces and DTOs
   ^        ^        ^            ^
Repositories Rendering Services   App
```

| Project | Holds | Deliberately does not |
|---------|-------|-----------------------|
| `Domain` | `AppSettings`, `MarkdownDocument`, `ZoomLevel`, enums | reference anything, including the BCL beyond primitives |
| `Abstractions` | every interface, plus event argument types | contain behaviour |
| `Repositories` | atomic JSON reads and writes | know about markdown or UI |
| `Rendering` | the Markdig pipeline and the source-line extension | touch the file system or UI |
| `Services` | the document workspace, settings, recent files, file watching | reference WinUI |
| `App` | window, view models, WebView bridge | contain file or rendering logic |

Each layer registers itself: `AddMarqoraRepositories()`, `AddMarqoraRendering()`,
`AddMarqoraServices()`. The composition root stays a list of intents.

---

## Tabs, and the one editor behind them

Every open document is a tab, but there is still exactly **one** WebView and **one** Monaco
editor. What changes per tab is the model.

`IWorkspaceService` owns an ordered list of `MarkdownDocument` records plus which one is
active. The records are immutable, so a change replaces an entry rather than mutating it,
and callers address documents by an `Id` that survives a rename through Save As. Each
document on disk gets its own file watcher; untitled documents get none, because there is
nothing to watch.

In the shell, each tab owns a Monaco `ITextModel`. Switching tabs swaps the model on the
single editor and restores the view state captured when the tab was last left:

```js
rememberActiveTab();                       // saveViewState + preview scrollTop
editor.setModel(tabs[id].model);           // undo history travels with the model
editor.restoreViewState(tabs[id].viewState);
```

That is what makes undo history, selection and scroll position survive a tab switch.
Calling `setValue` on a shared model instead would be simpler and would throw all three
away, and re-tokenizing a large document on every switch is not free either.

Rendered HTML is cached per tab too, so returning to a tab does not re-run Markdig, mermaid
or KaTeX. A background tab still receives new HTML when its text changes, but is not
redrawn until it is shown.

Only the active document's folder is mapped to the `marqora.document` origin. The mapping
moves *before* the incoming tab's preview is injected, or its relative images would resolve
against the outgoing tab's folder.

### Session persistence has one sharp edge

Open documents are recorded in settings so the next launch can restore them. Closing the
window closes every tab, and each close would rewrite the session with the shrinking list,
leaving an empty one behind — so the session recorded during the run is the one worth
keeping. `MainViewModel.BeginShutdown` freezes persistence before the tabs are closed, and
`CancelShutdown` unfreezes it if the user cancels at a save prompt.

## The single-WebView design

Both panes live in **one** WebView2, not two.

Scroll synchronization is the reason. Keeping the editor and the preview in the same
JavaScript context makes syncing a local calculation on one frame. Split across two
controls it becomes a round trip through the host on every scroll event, which visibly
lags and jitters.

The native window still owns everything a user recognises as the app: Mica, the extended
title bar, the menu bar, dialogs, the file pickers, theming and drag-and-drop. The WebView
holds only the document surface.

### The bridge

`IPreviewHost` is the contract; `WebViewPreviewHost` implements it over WebView2. Messages
are JSON envelopes, `{ type, payload }`, in both directions.

```
host -> shell   openTab, activateTab, closeTab, updatePreview, setTabText, clearSurface,
                setViewMode, setTheme, setZoom, setScrollSync, setWordWrap,
                setLineNumbers, setShowWhitespace, setWrapGlyph, setSplitterPosition,
                scrollToLine, focusPane, editorCommand, requestSelection, insertText
shell -> host   ready, editorTextChanged, zoomChanged, splitterMoved, linkActivated,
                command, paneFocused, stats, selectionCopied, log
```

Tab-scoped messages carry the document id, and `editorTextChanged` reports which tab was
edited, so a slow render arriving after a tab switch updates the right document.

Two details worth keeping:

- **Messages sent before `ready` are queued.** Opening a file from the command line
  otherwise races the shell's start-up and the document silently never appears.
- **`log` forwards the shell's JavaScript errors into Serilog.** Script errors inside a
  WebView are invisible otherwise. Nearly every bug found while building this was diagnosed
  from the log rather than a debugger.

### Rendering flow

The host owns rendering. On each debounced keystroke the shell posts the text, Markdig runs
on a background thread, and the HTML fragment goes back. The shell swaps `innerHTML` and
runs mermaid, KaTeX and highlight.js over the result.

Markdig is not asked for a full HTML document, only a fragment, so the page, its scroll
position and Monaco's state all survive an update.

---

## Scroll synchronization

A custom Markdig extension (`SourceLineExtension`) stamps every block element with
`data-src-line`, the zero-based markdown line that produced it.

The shell builds a monotonic map of `line -> vertical offset` from those attributes and
interpolates between entries. Editor position converts to a fractional line through
`getTopForLineNumber`, and back the same way.

The naive alternative, matching scroll percentage between panes, drifts badly the moment a
document mixes prose with tall content: one diagram is a screenful in the preview and three
lines in the source. Line mapping is immune to that.

A `syncOwner` flag, cleared after two animation frames, stops the two panes from echoing
each other into a feedback loop.

---

## Why mermaid runs in an iframe

This is the least obvious thing in the codebase and it is not decorative.

Monaco installs a global AMD `define`. Mermaid's diagram chunks embed several vendored
libraries, and one of them, `fastdom`, tests only:

```js
typeof define == "function" ? define(function () { return c }) : /* CommonJS path */
```

It never checks `define.amd`. With Monaco's loader present it registers an anonymous
module, and Monaco's loader rejects that outright: *"Can only have one anonymous define call
per script file."*

Three fixes were tried and rejected:

1. **Delete `define.amd`.** Fixes KaTeX and dayjs, which do check the marker. Does nothing
   for fastdom, which does not.
2. **Hide `define` while mermaid loads.** Works, until Monaco lazy-loads something in the
   same window and dies on a missing `define`. Monaco's markdown tokenizer fetches a grammar
   for *every fenced code language in the document*, at moments driven by document content
   and scrolling. There is no safe window.
3. **A tolerant `define` shim** that forwards named modules and swallows anonymous ones.
   The anonymous registration is exactly how those bundles publish their exports, so
   swallowing it leaves consumers holding an empty object.

The working answer is to stop the two module systems sharing a page. `webshell/mermaid-frame.html`
is a same-origin frame with no loader in it, so every bundle takes its ordinary browser path.
The shell reaches straight into `iframe.contentWindow` and copies the finished SVG back.

The frame is positioned off-screen rather than hidden, because mermaid measures text to lay
diagrams out and a `display: none` document has no layout to measure.

A consequence worth stating: **preview code highlighting uses highlight.js, not Monaco's
colorizer.** Reusing Monaco would have been one less dependency, but it would put grammar
fetches back on the critical path of the problem above.

---

## File dialogs are Win32, not WinRT

`FileDialogService` calls the Windows common dialogs through their COM interfaces
(`IFileOpenDialog` / `IFileSaveDialog` in `Win32Dialogs`), not the WinRT pickers in
`Windows.Storage.Pickers`.

The WinRT pickers do not work in an unpackaged app, and they fail in the worst possible way:
`PickSingleFolderAsync` and its siblings simply never complete. No exception is thrown, so
`try`/`catch` catches nothing and the awaiting code waits forever. The symptom is a menu item
that appears to do nothing at all. This went unnoticed for a while because the app was only
ever exercised through the command line, drag and drop, and session restore.

The Win32 dialogs behave identically packaged or not, need an owner window handle, and are
modal — they run their own message loop, so they are shown on the UI thread and the result
is returned as an already-completed task.

The folder picker is the *open* dialog with `FOS_PICKFOLDERS`; there is no separate folder
dialog class in the modern shell API.

## Threading

Services below the UI use `ConfigureAwait(false)`, which is correct for library code and
means their events can be raised on a thread-pool thread. XAML throws `RPC_E_WRONG_THREAD`
if such an event reaches a bound property.

`IUiDispatcher` is the seam. `MainViewModel` marshals the three events that can arrive from
a background thread — workspace changed, recent files changed, external file change — and
leaves the rest alone. The services stay UI-agnostic and unit-testable.

## Clipboard

Cut, copy and paste are handled by the host, not by Monaco.

The editor's clipboard actions go through `document.execCommand`, which a browser honours
only during a trusted user gesture. A click on the native Edit menu arrives in the WebView
as a bridge message with no user activation attached, so those actions silently do nothing —
while `Ctrl+C` typed in the editor is a real gesture and works, which is what makes the
menu's silence easy to miss.

So the host asks the shell for the current selection, writes it to the Windows clipboard
itself, and pushes text back in for a paste. No browser restriction applies there.

The preview pane holds a selection of its own, quite separate from the editor's, and it is
fetched with its own message so that copying from the preview does not drag the source pane
into view the way an editor command does. Both replies come back as `selectionCopied`,
because the host does the same thing with either one. Every clipboard write in the app —
both panes, the two windows, Copy Full Path — goes through `ClipboardText`.

---

## One place decides what a menu looks like

Every menu in Marqora is a WinUI `MenuFlyout`, and every one of them takes its font,
spacing, background and colours from `App/Themes/Menus.xaml`. Change a value there and the
header menu bar, all three context menus and the two auxiliary windows' menus all follow.

They did not start out that way. There were three menus from three toolkits: WinUI drew the
header bar, Monaco drew a DOM menu for the source pane, and Chromium drew a native one for
the preview. Three could never be made to match, and the third was the worst of them — being
Edge's own menu, it followed Edge's dark mode rather than the app's theme, so it came up
dark in a light window.

Both web-drawn menus are switched off now (`contextmenu: false` for Monaco,
`AreDefaultContextMenusEnabled = false` for the WebView). A right-click is reported over the
bridge as `contextMenu`, carrying the pointer position and what was under it — whether
anything is selected, and any link or image address — and the host puts a flyout up at that
point. What was under the pointer travels with the message rather than being fetched
afterwards, which would race with the next keystroke. Suppressing the browser menu does not
suppress the DOM event, so the page still sees the click.

The third is the tab strip's, and it is the odd one out: it is raised by an ordinary XAML
`ContextRequested` — which also covers the Menu key and a press-and-hold — rather than
arriving over the bridge, and it is fitted to the tab that was clicked rather than to what
the pointer was over. It reaches XAML at all only because the tab rectangles are carved back
out of the caption as passthrough regions; see `BROKEN_TAB_BAR.md`. It selects that tab
before opening, so its items can be the same active-document commands the File menu uses.

### Where the keyboard goes when a menu closes

A `MenuFlyout` holds focus while it is open and hands it back to whatever opened it as it
closes. For the tab strip's `…` list and its context menu that is answered on `Closed`,
which is the earliest moment a restore is not undone.

The header menu bar has no such hook — `MenuBarItem` exposes neither its flyout nor an
event, which is why `OpenMenu` has to expand it through an automation peer — so the rule
there is the other way round: **a command that can be picked from a menu hands the keyboard
back itself**, through `MainViewModel.RestoreDocumentFocusAfterChrome`. Without it the
`MenuBarItem` keeps focus and the arrow keys walk the menu bar instead of the text.

That restore is deferred by one render pass rather than done on the spot, and both halves of
that matter. Several commands finish without ever awaiting — closing a tab with nothing
unsaved in it, copying a path — so a restore made there and then would land while the menu
was still up and be undone as it closed. The ones that *do* await a dialog are covered by
the same call, because they only finish once it has been answered, so nothing is left to
take the keyboard from. One rule covers both, which is the point.

The plain `RestoreDocumentFocus` stays for the surfaces that are not menus — a tab click, a
finished drag, the add button — where running immediately is correct.

Changing the view mode is the one case that does **not** ask `ActivePane` — it sets it. The
pane the keyboard lands in depends only on the mode being switched to:

| switching to | keyboard goes to |
| --- | --- |
| Source | the editor |
| Split | the editor |
| Preview | the preview |

Split is the editing view, so it always means the editor, whichever pane you arrived from.
The alternative — remembering the pane you were last in — puts the keyboard in the preview
when you arrive from preview, and a focused preview shows no caret, so it is indistinguishable
from focus having gone nowhere. Landing somewhere visible beats remembering.

Only the user's own click moves focus. `ApplyViewModeAsync` takes a `takeFocus` flag beside
its `persist` one, and Find All passes false: it drops into split view to show a match, and
stepping the results list has to leave the keyboard in that list or the arrow keys stop
walking it. Switching modes hides a pane, and a pane that goes `display:none` takes the DOM
focus inside it down with it — which is why this is a real restore even when the mode did not
actually change.

Every other restore asks `ActivePane` which pane to go to, so its starting value is
load-bearing rather than an arbitrary seed. It is **Source**. The preview is a focusable
`<article>` with no caret in it, so a session that began in the preview looked exactly like
focus having gone nowhere — and stayed that way, because only clicking into the editor moves
`ActivePane` off its initial value. Side by side is the only case it decides: with one pane
showing, `focusPane` in the shell overrides it with the pane that is actually there, which is
what lets the welcome document open in the preview it asks for.

The styling itself is done two ways, because a menu's appearance comes from two places.
Anything a `Style` can set is set by implicit styles, each `BasedOn` the framework's default
so the control templates stay stock. The rest lives inside those templates in visual states
a style cannot reach — hover and pressed fills, disabled text, the accelerator text, the
separator — and those are reached by overriding the theme resource keys the templates read.
`Menus.xaml` says which is which.

Two things to know before editing it. It must stay merged *after* `XamlControlsResources`,
or every key the framework also defines wins over the one there. And WinUI publishes no
usable default style for `MenuBar` or `MenuBarItem`, so those two get no implicit style —
an implicit style without a `BasedOn` replaces the default outright, template and all, which
for the menu bar means a window that will not open. The header bar names the two font tokens
on its own element in `MainWindow.xaml` instead.

---

## Drag and drop, twice

WinUI 3's `WebView2` in Windows App SDK 2.2 does not expose `AllowExternalDrop`, so the
browser keeps drops that land on the page.

Rather than fight it, both routes are handled:

- **On the window chrome**, the ordinary WinUI `Drop` handler runs.
- **On the preview**, Chromium responds to a dropped file by navigating to its `file://`
  URL. `NavigationStarting` cancels that navigation and hands the path to the view model,
  which opens it as a document.

The same `NavigationStarting` handler is what keeps the WebView pinned to the shell page:
anything that is not the shell is either a document link or unexpected, and neither should
replace the app's own UI.

---

## One instance, and how a second launch reaches it

Double-clicking a markdown file while Marqora is running adds a tab to the window that is
already open. There is only ever one process.

`SingleInstance` runs at the top of `Main`, before `Application.Start`:

1. Read this launch's activation with `AppInstance.GetCurrent().GetActivatedEventArgs()`.
   It has to happen before step 2 - registering the key is what makes a *later* launch able
   to redirect here, and there is nothing left to hand over once that has happened.
2. `AppInstance.FindOrRegisterForKey` claims the key, or returns whoever holds it.
3. If someone else holds it, hand the activation over with `RedirectActivationToAsync` and
   exit. If not, this process is the instance, and subscribes to `AppInstance.Activated`.

None of this needs the app to be packaged, which matters here: Marqora ships unpackaged.

**The deadlock.** `Main` is an STA thread with no message pump yet, and the redirect is a
cross-process COM call. Waiting on it with `Task.Wait` blocks the very apartment the call
has to return through, and the launch hangs. `CoWaitForMultipleObjects` waits while still
dispatching COM, which is the whole reason it exists. The wait is bounded at ten seconds;
on expiry the launch opens its own window rather than dying silently.

**What arrives.** An unpackaged app registered through the registry is activated as
`ExtendedActivationKind.Launch`, and the payload is the raw command line with the executable
still at the front - not a tidy list of files. `SingleInstance` splits it with
`CommandLineToArgvW`, because paths contain spaces and splitting by hand gets that wrong,
then keeps the arguments that name a file that is not this executable. A registered file
activation (`IFileActivatedEventArgs`) is handled too, so adding
`ActivationRegistrationManager` later would need no change here.

**The gap in the middle.** `Activated` has to be subscribed before the host is built, but the
window it feeds does not exist until several hundred milliseconds later, and a second launch
can easily land inside that window. `ActivationRouter` holds anything that arrives early and
flushes it when `App.OnLaunched` supplies a destination. Activations arrive on a thread pool
thread; `MainWindow.OpenFromActivation` marshals to the UI thread itself, because it is the
part that knows which dispatcher.

**Foreground.** Windows gives foreground rights to the process the user last interacted with,
which is the one being redirected *from*. It passes them over with
`AllowSetForegroundWindow` before redirecting, so the receiving window can actually come
forward instead of flashing in the taskbar. `Window.Activate` does not restore a minimised
window on its own, so the presenter is restored first.

**Explorer's side of it.** `build\Register-FileAssociation.ps1` writes the association, and
sets `MultiSelectModel = Player` on the open verb so that selecting several files starts one
process with all of them rather than one process each. The command is `"%1" %*`: `%1` is the
item being opened and `%*` is the rest of the selection. `%*` alone is *not* the file - it
expands to the invocation's parameters, which for a document activation are empty, and the
app launches with nothing to open. `%~`, whose documented meaning is the second item onwards,
is not substituted at all and arrives as the literal string `%~`. Both were measured.

Single-instancing makes the registry setting an optimisation rather than a requirement: ten
processes that each hand over and exit produce the same ten tabs, just more slowly.

---

## Exporting

Both exports take the **rendered preview**, not a fresh render of the source. Diagrams are
already inline SVG at that point, maths is already laid out by KaTeX and code is already
highlighted, so an export cannot disagree with what was on screen. `IPreviewHost` grows one
request/response call for this, `GetRenderedHtmlAsync`, keyed by request id — the bridge is
otherwise one-way, and matching a reply to its request keeps an export correct even if other
traffic arrives in between.

**PDF** goes through `CoreWebView2.PrintToPdfAsync` with a print stylesheet. `@media print`
hides the editor pane and the splitter and pins the light palette, which is why the PDF holds
only the preview whatever the app is showing. The built-in header and footer are switched
off; they would print the page title and `https://marqora.assets/shell.html`.

**HTML** is assembled by `HtmlExporter`. It reuses `app.css` rather than maintaining a second
stylesheet, which is what keeps exports looking like the preview; it carries a few pane and
splitter rules an exported document has no use for, a fair trade for not having two
stylesheets to keep in step. The KaTeX and highlight.js themes are included only when the
document actually contains maths or code — KaTeX's stylesheet alone is most of the file for a
document with no equations. Local images become data URIs, read by the host rather than by
the page, which sidesteps the CSP and the cross-origin rules entirely. KaTeX's web fonts are
inlined too: the stylesheet refers to them by relative path, which resolves inside the app
but not beside an exported file, and every equation would otherwise fall back to a serif face.

---

## The formatter

`PaulTechGuy.MQ.Formatting` is its own project for one reason: it is pure. Text and options
in, text out — no UI, no I/O, no async, no state carried between calls. That is what lets it
run on a background thread and be exercised from a test project, or from a one-file
`dotnet run` script, without any of the app around it.

**It works on lines, not on a syntax tree.** Markdig can parse markdown but cannot render it
back as markdown, so a tree-based formatter would have to reconstruct every construct from
scratch — and would rewrite far more of the document than the user asked for. Lines keep the
output close to the input, and a rule that is switched off leaves no trace whatsoever.

A single forward scan classifies every line as text, fence delimiter, fence content, front
matter delimiter or front matter content. Everything downstream trusts that classification
rather than re-deciding for itself what is safe to touch, which is what makes "code is never
altered" a property of the design rather than a promise each rule has to keep.

Format-selection uses the same code path. The lines outside the range are marked frozen and
the whole document is still scanned, because a line cannot be understood on its own: whether
it sits inside a fence decides what may be done to it, and that is only knowable from what
came before. The EOF-newline rule is switched off for a selection — the end of the file is
outside the selection like anything else.

### Rules that need care

Three rules are ambiguous in ways worth recording, because each one is a bug waiting to be
reintroduced:

- **A bullet with no space after it looks exactly like emphasis.** `*text*` at the start of a
  line is not a list. A line whose asterisk has a partner later on is left alone, which is the
  reading a renderer gives it too.
- **A run of dashes under a paragraph is a heading, not a thematic break.** CommonMark says
  the setext reading wins. Getting this backwards silently deletes horizontal rules.
- **An ordered item that jumps forward after a blank line is a new list.** `1. 2. 3.` then a
  blank then `5.` means the author started counting again; renumbering it to `4.` changes what
  the document says. A number that falls *behind* is the familiar `1. 1. 1.` shorthand and
  does get renumbered.

Rules that rewrite inline text run through a helper that skips code spans, so a rule cannot
reach inside `` `a_b_c` `` — and the underscore rules additionally refuse to touch
intra-word underscores, which would otherwise corrupt `snake_case` identifiers.

### Editing rather than reloading

The result is applied through a `replaceText` message that uses Monaco's `pushEditOperations`
against the full model range, bracketed by undo stops. `setValue` would be simpler but throws
the undo stack away, and undo is the first thing anyone reaches for when a formatter surprises
them. The cursor is restored to the same line with its column clamped, since that line may
have grown or shrunk.

---

## The cheatsheet window

The second window in the app, and the second WebView2. It shows `webshell/cheatsheet.md`
rendered through the same Markdig pipeline as the preview, so the reference cannot disagree
with the renderer it documents.

It has its **own page** rather than being another view inside `shell.html`. That means no
Monaco, no editor, no tab machinery and no AMD loader are ever created for it, and its CSP
can drop `unsafe-eval`, which only Monaco needed. The one thing shared with the preview is
`app.css` — that is what makes a heading or a table look identical in both. Mermaid still
runs in the same off-screen frame the preview uses, so there is one diagram engine with one
set of options rather than a second copy that could drift.

**Closing it hides it.** Rebuilding a WebView costs the better part of a second and would
throw away the scroll position, and this is a window users dismiss and recall constantly.
`AppWindow.Closing` is cancelled and the window hidden instead; only application exit truly
closes it. That has a consequence worth stating: a hidden window is still an open window, and
WinUI keeps the process alive until every window is closed, so `MainWindow`'s closing handler
must close the cheatsheet or the app would linger invisibly after its last window went away.

### Why the toggle asks "can you see it?"

The menu item lives on the main window, so by the time it is invoked the main window is in
front and the cheatsheet never is. A rule phrased in terms of focus would make the hide
branch unreachable. A plain visible/hidden flip has the opposite failure: it hides a window
buried behind the editor, and the menu appears to have done nothing.

The question that actually matters is whether the user can see the cheatsheet right now.
Windows has no "is this obscured" call, so `CheatsheetService` asks the shell what sits at a
spread of points across the window with `WindowFromPoint`, resolving each hit to its root
with `GetAncestor`. If any point comes back as the cheatsheet, it is visible and the user is
dismissing it; otherwise they are asking for it. Nine samples is enough to tell "buried
behind the editor" from "sitting beside it", which is the only distinction being drawn.

The menu item's tick is bound to the window's actual visibility, sourced from
`AppWindow.Changed`, not to the command — so dismissing the cheatsheet with its own close
button unticks the menu too.

The caption is coloured by hand from the theme. Left alone Windows draws it in the user's
accent colour, which on a window that is almost entirely one document reads as a stripe of
unrelated colour. The main window sidesteps this by extending its content into the title bar;
this one is too small to give up the caption, so the caption is painted instead.

---

## Find All

`Ctrl+F` is Monaco's own find bar and always was. Find All is a different question — "where
does this appear?" rather than "take me to the next one" — and it is answered in C#, not in
the editor.

**The search never crosses the bridge.** Every open tab's text is already in memory:
`IWorkspaceService.Documents` holds a `MarkdownDocument` per tab with its full `Text`.
Searching all of them is therefore a loop over strings this process already has, with no
round trip to the WebView and nothing to ask Monaco for. `PaulTechGuy.MQ.Finding` does the
work, which is why it can be a plain library with no dependency on the UI and 37 tests
holding its line and column numbers in place.

The workspace's copy trails the editor by the 160ms text debounce. That cannot matter here:
reaching this window and typing a term takes far longer than the debounce, unlike `Ctrl+B`,
which fires one keystroke after typing and does have to ask the editor for fresh text.

**Matching is line by line**, so a pattern cannot span a line break even in regular-expression
mode. A results list is a list of lines and a match covering three of them would have no row
to sit on; the trade is that `^` and `$` anchor per line without `Multiline` entering into it.
Line breaks are counted as the editor counts them — CRLF, LF and a bare CR each end one — so
a file with mixed endings still reports the numbers shown in the gutter. The finder never
splits a document into an array of lines: it walks spans and materialises a string only for
lines that hold a match, shared across every match on that line.

Two ceilings keep a loose search from taking the window down with it: 5,000 matches, and one
second per regular expression per line. The finder collects one match *past* the ceiling and
throws it away, which is the difference between "exactly five thousand" and "more than we
will show".

### Results are a snapshot, and are checked before they are used

Re-running the search on every keystroke would reshuffle the rows under the reader; clearing
them would be worse. So the list stays put and says it is out of date, sourced from
`IWorkspaceService.Changed` — and because documents are immutable records whose edits
allocate a new string, reference equality on the text is an exact, free version stamp.

That leaves the real hazard: a row recorded line 40, the user deleted a paragraph above it,
and line 40 is now something else entirely. Selecting the recorded position blindly is how a
results list quietly starts lying. `MainViewModel.Locate` checks the recorded text is still
at the recorded position — one line, almost always true — and only when it has moved searches
that document again for the same text, taking the occurrence nearest the original line.

### Getting the editor there

`IPreviewHost.SelectRangeAsync` is the one new bridge message, and it carries a document id
that the shell treats as a **guard rather than an address**: if the tab on screen is not that
one, the request is dropped. Results outlive tab switches, so a pick can arrive aimed at a
document the editor has already left.

Which makes ordering the whole problem. Workspace changes are applied through
`_workspaceChain`, so `_workspace.Activate(id)` has not switched anything by the time it
returns — a selection sent straight afterwards would reach the shell before the tab did and
be dropped by that guard, and the click would appear to do nothing. The reveal is therefore
queued onto the same chain, behind whatever the activation put there.

Selecting a row does not take the keyboard; `Enter` and double-click do. That is what lets
the arrow keys walk the list with the editor following along, which is most of the value of
having a list at all.

### Three things only running it revealed

None of these fail to compile, and none of them are visible in a diff. They are worth knowing
before building a fourth window or a second code-built list.

**`PrepareContainerForItemOverride` is not routed into a managed subclass of a WinUI
control.** Overriding it on a `ListView` subclass compiles, and never runs: every row drew
itself as the record's own `ToString`, which is a dump of its properties. The results list
fills its rows from `ContainerContentChanging` instead — the documented hook for code-built
content in a virtualised list, and a plain event with no composition in the way. The row
records also carry a readable `ToString` now, so the failure mode next time is a plain line
of text rather than a wall of braces.

A **code lookup of a theme resource resolves against the application's theme**, which is the
operating system's, not the one the user chose in Marqora. `Application.Current.Resources
["ApplicationPageBackgroundThemeBrush"]` painted a black page under light controls on a
machine with a dark desktop and Marqora set to light. The cheatsheet window does the same
lookup and gets away with it only because a WebView covers every pixel of the result. Find
All writes its surface and highlight colours out per theme instead, the way the caption
colours already were.

**The accent overrides in `App.xaml` do not reach a second window's tree.** An
`AccentButtonStyle` button came up in the user's Windows accent rather than Marqora's teal,
which is the one colour in the app that is nobody's choice. Find All uses a plain button;
`Enter` runs the search anyway, so the emphasis was carrying little.

---

## Persistence

`JsonFileStore<T>` has two properties that matter for a desktop app that can be killed at
any moment:

- **Writes are atomic.** Temp file, then `File.Replace`. A crash mid-write cannot truncate
  the real file.
- **Reads never throw.** Missing, empty or corrupt yields the caller's default; an
  unreadable file is renamed `.corrupt` and the app starts normally.

Serialization uses a source-generated `JsonSerializerContext`, so there is no startup
reflection and the layer stays trim-friendly. Derived members such as
`WindowPlacement.HasPosition` carry `[JsonIgnore]`; they are computed, not state.

**Adding a setting has a trap worth knowing about.** A property initializer does not run for
a key that is absent from the file, so any settings file written before the property existed
leaves it `null` — including reference types declared non-nullable. It has caught two
properties so far, `OpenDocuments` and `CheatsheetWindow`, the second as a
`NullReferenceException` the first time the cheatsheet was opened on an existing install. The
pattern in `AppSettings` is to declare the stored property nullable and read it through a
`[JsonIgnore]` companion that supplies the default — `DocumentsToRestore` and
`CheatsheetPlacement`. New reference-typed settings should follow it.

`SettingsService` writes behind a 750 ms debounce. Window resizes, splitter drags and zoom
steps mutate settings many times a second, and every change would otherwise be a disk write.
`FlushAsync` runs on shutdown so nothing is lost.

`FileWatcher` coalesces events over a 300 ms quiet period, because saving from another
editor usually arrives as a burst of Changed, Deleted and Created events from a
write-then-rename.

---

## Web assets

`webshell/` sits outside the C# project directory on purpose. The WinUI resource indexer
walks the project folder and reads Monaco's content-hashed file names, such as
`worker-BIZPAL9O.js`, as resource qualifiers, warning about each one. Keeping the tree
outside the project and copying it with an explicit MSBuild target avoids that, and skips
evaluating several thousand items that need nothing more than a copy.

`build/Get-WebAssets.ps1` pins exact versions and prunes what is not needed: source maps,
and KaTeX fonts in formats WebView2 will never request. The vendor bundle is git-ignored.

At runtime the folder is mapped to `https://marqora.assets` through a WebView2 virtual host.
A real origin is required: `file://` would put the page in an opaque origin, which blocks
the module and worker loads Monaco needs. The document's own folder is mapped separately to
`https://marqora.document` with `DenyCors`, so relative images resolve without granting user
content any access to the shell's origin.

The shell's Content-Security-Policy lists no network origin at all. The preview cannot phone
home even if a document asks it to.

---

## Output size

A self-contained unpackaged build starts at roughly 247 MB. Three things were removed to
bring it to about 178 MB, none of which Marqora can reach at runtime.

**The Windows App SDK is referenced by component, not by metapackage.** `Microsoft.WindowsAppSDK`
also pulls in `.AI`, `.ML` and `.Widgets`, which carry `onnxruntime.dll` and `DirectML.dll`:
about 42 MB of machine-learning runtime. `Directory.Packages.props` names Base, Foundation,
InteractiveExperiences, WinUI, DWrite and Runtime instead. None of those depends on the AI
components, so the payload is never restored rather than being deleted afterwards. Bump the
component versions together when upgrading.

**Non-English WinUI resources are excluded.** WinUI ships the strings for its built-in
controls, context menus and screen-reader announcements as `.mui` files, one folder per
language: 86 folders for an app whose own text is English only.

> Be careful here. English is itself a `.mui` rather than being baked into
> `Microsoft.ui.xaml.dll`, so `en-us` and `en-GB` must survive. Excluding *every* language
> leaves the stock controls with no strings at all.
>
> The exclusion goes through `MicrosoftWindowsAppSDKFilesExcluded`, an item the SDK
> subtracts from its payload list inside `AddMicrosoftWindowsAppSDKPayloadFilesFromComponents`.
> It exists solely for consumers to populate, which makes it the supported hook. Filtering
> `None` or `ReferenceCopyLocalPaths` instead does not work: the SDK creates those items
> inside that target, after the point where such a filter would run.

**Monaco's unused language workers are pruned during restore.** Monaco carries language
services for TypeScript, CSS, HTML and JSON, roughly 16 MB. Marqora only ever creates
markdown models, and a language worker is fetched only when a model of that language exists.

> `build/Get-WebAssets.ps1` removes only the payloads under `vs/assets` and `vs/language`.
> The small AMD wrappers sitting directly in `vs/` (`ts.worker-<hash>.js` and friends) are
> static dependencies of `vs/editor/editor.main` and must stay, or the editor fails to load
> at all. The core `editor.worker` is kept as well; that one is genuinely used.

## Testing

There are no tests yet; the seams for them are in place.

- `MarkdigMarkdownRenderer` is synchronous and takes only a logger. Give it markdown, assert
  on the HTML.
- `AppPaths` has a second constructor taking a data root and an install root, so repository
  tests can point at a temp directory.
- `SettingsService`, `RecentFilesService` and `DocumentService` depend only on interfaces.
- `MainViewModel` depends on interfaces throughout, including `IUiDispatcher`, `IDialogService`
  and `IPreviewHost`, so it can be exercised with no UI thread and no WebView.

`tests/` is already recognised by `Directory.Build.props`, and `Directory.Packages.props`
pins the test packages.

---

## Things deliberately left out

- **Tab tear-out**, dragging a tab into its own window. `TabView` supports it, but it needs
  a second window hosting its own WebView and a way to move an editor model between them.
- **MSIX packaging.** The app is unpackaged and self-contained, so there is no certificate
  to manage. File associations would need registry entries; the seam is there when wanted.
- **Trimming the vendor bundle further.** Monaco's language workers are around 9 MB and are
  never fetched, because only markdown models are ever created. They could be deleted from
  the restore script, at the cost of a bundle that no longer matches the upstream package.
