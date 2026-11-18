# Folder panel: implementation plan

The plan for building what `docs/FolderPanel-Exploration.md` describes, and the place where
progress against it is tracked. The exploration is the authority on what the panel does; its
seven decisions and seven settled questions are not reopened here. This document says how to
build it, in what order, and what the code turned out to say that the exploration did not know.

Written 2026-11-16 against `dev` at `9eb3d74`, and revised the same day after the two-reviewer
review logged in section 12. The three points that needed Paul's eye before building, listed in
section 1.1, were agreed as written on 2026-11-17.

---

## 1. Context

When this plan is finished:

- The left column holds two stacked sections, the folder panel above the outline, with a
  splitter between them. Either can be hidden; with both hidden the rail carries one icon each.
- `File > Open Folder` (`Ctrl+Shift+O`) chooses the panel's root instead of opening every file
  as a tab. *Open All in Tabs* keeps the old behavior on a folder's right-click menu.
- A click opens a document in a normal tab. The panel can rename, delete to the Recycle Bin, and
  start a new document in a folder. An open document's tab follows a rename made from the panel,
  with its read-only mark, pin, session entry, recent entry and disk watch.
- The panel can pop out into a sixth `PaletteWindow` and dock back.
- `Alt+6` shows and hides it; `Alt+Shift+6` goes to it and back.

Most of the work is in the app project, but the parts that can be wrong without anyone seeing
it live in libraries with tests, because the app project has no test project and gets none here:
what the tree lists, its order, `.gitignore` graying, name validation, the row changes the list
applies, the Recycle Bin decision, command-line qualification, and the rename that has to carry
five path-keyed things across.

### 1.1 For Paul before building

These are not reopened decisions. Each is a place where a settled decision meets an existing rule
or leaves a gap, and the plan has taken the conservative reading. **Paul agreed all three as
written on 2026-11-17;** the alternatives named below are not taken.

1. **An image outside the document's folder cannot be linked from the panel (P24).** The
   exploration asks for a relative link to the dragged image. But the preview only draws images
   kept in the document's folder or beneath it, the link checker reports anything else as
   "outside the document's folder", and the PDF export leaves it out (C21). A link Marqora itself
   calls broken is not worth inserting, so *Insert Link in Source* and the drag are refused for an
   image above or beside the document's folder, with a status line saying why. Allowing it with a
   warning is the alternative.
2. **Deleting a document under review is grayed (P25),** as Delete is for a marked document,
   because deleting closes the tab without the "discard comments?" question a normal close asks.
3. **The root comes back on start whatever the startup preference (P21).** The exploration says
   it is remembered "with the rest of the session"; the startup preference can say not to
   restore the session. The plan reads the preference as being about tabs. The other reading is
   that the root is restored only under *Restore session*.

---

## 2. How to use this plan

### 2.1 Work packages

They run in the order given. WP-0 is a short list of observations whose answer could change a
later package. WP-1 to WP-4 have no UI and are proven by tests. WP-5 to WP-9 are the app, each
usable on its own before the next starts. WP-10 is the documents. WP-11 is Paul's hand check.

### 2.2 Status values

`Open`, `Built` (done, tests green, left uncommitted for review), `Checked` (passed Paul's hand
check), `Withdrawn`.

### 2.3 Standing rules

- Nothing is committed or pushed until Paul asks. Each work package is left pending for review.
- The work stays on the branch Paul is on. `dev` is pulled (`--ff-only --autostash`) before each
  work package starts, because other agents merge to it.
- No GitHub Actions, no test switch in the app, no throwaway harness. An observation is made in a
  test that stays in the suite, or in a Debug run of the real app with a log line.
- After any `.cs` edit: `pwsh ./build/Add-FileHeaders.ps1 -Check`. After any label or prose:
  `Set-AmericanSpelling.ps1 -Check`. After any button: `Test-ButtonStandards.ps1 -Check`. Before
  WP-10 is called done: `Test-NetworkClaim.ps1 -Check`.
- The release note goes into `docs/releases/vNext.md` when the code lands (WP-10), not before.
- A finding outside the named work goes to section 10, not into the change.

### 2.4 Todo

- [x] Exploration settled (2026-11-16)
- [x] This plan, and its review (section 12)
- [x] Paul's answer on the three points in 1.1 (all agreed, 2026-11-17)
- [ ] **WP-0** Observations *(case-only move done; two Debug runs moved to WP-5 and WP-7)*
- [x] **WP-1** The folder library *(Built 2026-11-17)*
- [x] **WP-2** Path-keyed stores learn to move *(Built 2026-11-17)*
- [x] **WP-3** The workspace renames and deletes *(Built 2026-11-17)*
- [x] **WP-4** Settings *(Built 2026-11-17)*
- [ ] **WP-5** The docked panel: listing, opening, revealing, keys, panel menu *(Built 2026-11-17, reworked the same day for P29; waiting on Paul's look)*
- [ ] **WP-6** Every other way to choose the root *(Built 2026-11-17; the terminal and drop checks are Paul's)*
- [ ] **WP-7** File operations *(Built 2026-11-17; waiting on Paul's look; the Recycle Bin run from WP-0 is his call)*
- [ ] **WP-8** Images: insert, drag, launch *(Built 2026-11-17; the drag into the source pane is Paul's to see)*
- [x] **WP-9** ~~Pop out and dock~~ *(Dropped 2026-11-17: P29's docked / edge / closed replaces the floating window)*
- [x] **WP-10** Documents, release note, checklist *(Built 2026-11-17)*
- [ ] **WP-11** Paul's hand check *(Paul)*

---

## 3. What the code says

Findings from reading the code, several of which correct something the exploration assumed. Each
is cited so it can be checked. The review in section 12 checked every one.

**C1. There is no drag route for an image to reuse.** The exploration says a dragged image inserts
a link "through the same path as dropping an image from Explorer". A file dropped on the WebView
makes Chromium navigate to its `file://` URL; `OnNavigationStarting` (`WebViewPreviewHost.cs:1867`)
cancels it, and `OnExternalLinkActivated` (`MainViewModel.cs:10758`) shows *Unsupported file* for
anything but markdown. Monaco's `dropIntoEditor` is on in the vendored build but has no provider
for a file. The nearest real route is **pasting** an image copied in Explorer: `ClipboardImage`
reads storage items (`ClipboardImage.cs:114`) into `InsertImagesAsync` (`MainViewModel.cs:6939`),
which **copies** the picture beside the document unless `DocumentAssets.ExistingReferenceFor`
finds it already where it would be put. The exploration asks for a link with no copy, so the drag
is new work (WP-8), and `InsertImagesAsync` is not its engine.

**C2. A relative path on the command line is resolved by the wrong process.** On a second launch,
`TryHandOff` redirects any activation without looking at it (`SingleInstance.cs:73`), and
`Deliver`/`PathsFrom` run in the **receiving** instance, where `IsOpenable` checks
`File.Exists(arg)` (`:101`, `:115`, `:197`) against the first instance's working directory. Today
that silently drops `Marqora notes.md` typed in a terminal while Marqora runs. For `Marqora.exe .`
it would be worse: `.` exists everywhere, so the root would be whatever folder the first instance
started in. Fixed in WP-6 (P9).

**C3. The read-only marks and pins cannot be moved.** `IDocumentLocks` and `IDocumentPins` offer
only `LoadAsync`, `DroppedOnLoad`, a per-path query and `SetAsync`. A folder rename has to move
every mark beneath the folder, including marks on files that are not open (WP-2). Keys are stored
through `DocumentPathKeys.Normalize`, which resolves a link with `File.ResolveLinkTarget` only
when the full path *is* a link (`DocumentPathKeys.cs:49`). Files beneath a junction are stored
under the junction's spelling, and the junction itself normalizes to its target, so a folder
prefix has to be matched both ways.

**C4. The disk watch can mark our own rename as a deletion.** `FileWatcher.Notify` reads
`WatchedPath`, then tests `File.Exists` (`FileWatcher.cs:183`). `StopWatchingCore` nulls the path
and disposes the timer (`:110`), so a callback starting after the stop does nothing, but one that
read the path just before it can test existence just after the move and raise `FileRemoved` for a
document whose `Path` has not yet been re-pointed. `OnFileRemoved` marks by `Id` without asking
anything (`DocumentWorkspace.cs:850`). And `MarkExternal` and `RecordStamp` write `_documents`
from the watcher's pool thread with no lock, so a record computed before a rename can land after
it and put the old path back. WP-3 fixes both, and a path check is not enough on its own.

**C5. Save As carries a pin and drops a mark; a rename must carry both.** `SaveAsAsync`
(`DocumentWorkspace.cs:304`) moves the pin and leaves the mark, because a mark guards a *file* and
Save As leaves the file where it was. A rename moves the file. It also shows the order that works:
the store is changed first, then `IsLocked`/`IsPinned` are read back from it.

**C6. Nothing in the app sends a file to the Recycle Bin.** The only "recycle" is
`PastedImageTracker` moving files into Marqora's own `RecycleDirectory` (`IAppPaths.cs:58`).

**C7. The left column only exists when a document is open.** `IsOutlineVisible` and
`IsOutlineRailVisible` both end `&& HasDocument` (`MainViewModel.cs:9714`, `:9723`), and the
landing page spans all four columns (`MainWindow.xaml:2851`).

**C8. The outline's focus latch has seven readers, not two.** `OutlineHasFocus` feeds `CanFormat`
and `CanEditText` (`MainViewModel.cs:929`, `:943`), the Edit menu's Paste (`CanPaste`,
`MainWindow.xaml:615`), Copy, which copies the selected heading instead of the editor's hidden
selection (`MainViewModel.cs:6449`), and `RestoreDocumentFocusAfterChrome`, which sends the
keyboard back to the panel after a menu command (`:3996`). It is cleared on hide (`:9856`), on a
row click (`:10129`) and by `LeaveOutlineAsync` (`:10154`), as well as by a pane taking focus.
The folder latch needs an answer for each (WP-5).

**C9. `RunEditAsync` refuses whenever `CanFormat` is false.** It checks `if (_host is null ||
!CanFormat) return;` (`MainViewModel.cs:8959`), and the snippet and image inserts go through it.
With focus in the panel `CanFormat` is false, so an insert started from the panel's menu would do
nothing at all. *Insert Link in Source* needs its own gate (P11).

**C10. `Writes(...)` belongs to `MainWindow`.** The read-only registry is a private list on the main
window's partial class (`MainWindow.ContextMenus.cs:920`). The folder panel's menu has to work in
the floating window too (P12).

**C11. The Save dialog cannot be told where to start, and the folder picker is titled for the old
meaning.** `PickSaveFileAsync` takes only a name (`IFileDialogService.cs:15`). `PickFolderAsync` is
titled "Open every markdown file in a folder" (`FileDialogService.cs:191`) and is shared with the
Folio export destination and unpack.

**C12. Dropping a folder on the WebView does nothing today, and the chrome route is shared.** A
WebView drop arrives at `OnExternalLinkActivated` as one `file://` URI; `File.Exists` is false for a
directory and the method logs and stops. The chrome drop goes `OpenDroppedAsync` →
`OpenSeveralAsync(picked: false)` (`MainWindow.xaml.cs:2397`, `MainViewModel.cs:1754`), and the
multi-file Open dialog shares `OpenSeveralAsync` with `picked: true` (`:1394`).

**C13. The edit accelerators already step aside for any text box.** `IsTextInputFocused` checks the
control type (`MainWindow.Outline.cs:389`). `Delete` and `F2` are not window accelerators.

**C14. The two keyboard lists.** `RegisterAccelerators` (`MainWindow.xaml.cs:476`, `Alt+4` at
`:542`) and `HOST_SHORTCUTS` (`webshell/app.js:4358`, `Alt+4` at `:4408`). `PaletteWindow`
registers none, so the floating window needs its own.

**C15. The cheatsheet lists no shortcuts.** The places that do are `Views/KeyboardShortcuts.cs`, the
Welcome document's table (`Assets/Welcome to Marqora.md:369`) and `README.md`.

**C16. The tab menu's wording differs from the exploration's.** *Open in File Explorer*, and *Copy >
Name / Relative Path / Full Path / as Markdown Link* (`MainWindow.TabContextMenu.cs:304`, `:441`).
Its *Open in File Explorer* runs `RevealInFolderCommand` on the active document; the panel needs
`RevealInExplorer(path)` and `LaunchPath(path)` (private today, `MainViewModel.cs:10961`) reachable.

**C17. The relative-link helpers exist.** `RelativeLink(fromFolder, target)` (`MainViewModel.cs:7392`)
and `LinkTarget` (`:7456`), which wraps a target with spaces or parentheses; the tab menu uses both
(`MainWindow.TabContextMenu.cs:449`, `:478`).

**C18. Settings follow a pattern with two traps.** Reference-typed settings are nullable with a
`[JsonIgnore]` accessor; session state is listed in `SessionKeys` and `WithSessionOf`
(`AppSettings.cs:754`, `:794`). `PreferencesTransferTests` only catches a member missing from one
list if its fixture gives that member a non-default value (`PreferencesTransferTests.cs:299`).
`outlineWidth` keeps its name.

**C19. Find All and the export report key their rows by document `Id`.** A rename leaves their rows
working; only the name shown is stale until the next search.

**C20. Path-keyed state, the full list.** Marks and pins (stores), the session (`PersistSession`,
`:4198`, from tab paths), recent files (own store, `IAppPaths.cs:17`, keyed by full path), the
disk watch, closed-tab records (`_closedTabs`, `:110`, which Reopen Closed Tab follows to "File
not found"), and, new in this plan, `MarkdownDocument.SaveFolder` and `RecentFolders`.
`PastedImageTracker` holds image paths too (section 10).

**C21. The preview draws only images inside the document's folder.** `DocumentFiles.Resolve` uses
`PathContainment.ResolveWithin` (`WebRender/DocumentFiles.cs:114`); the link check says "outside the
document's folder" (`Analysis/LinkChecks.cs:168`); the PDF export leaves such an image out
(`Exporting/PdfExportJob.cs:106`). This is why `ExistingReferenceFor` stays narrow
(`DocumentAssets.cs:84`).

**C22. Closing a document under review asks about its comments; a workspace close does not.**
`ConfirmEndReviewForCloseAsync` (`MainViewModel.Review.cs:358`) runs from the tab's close, not from
`IWorkspaceService.Close`.

**C23. `RowSplitter` exists without a reset.** `Controls/RowSplitter.cs` (used by `FolioWindow.cs:289`)
resizes the row below it and has no double-click.

**C24. `DocumentPathKeys` is internal and answers for files only.** `HasBeenDeleted` ends
`return !File.Exists(path)` (`DocumentPathKeys.cs:85`), which is true for every folder.

**C25. A theme brush looked up in code is the operating system's theme.** `Architecture.md`,
*Secondary windows*. A view built in C# for two hosts must derive its dimmed text from the
effective `AppTheme`, as `SurfaceBrush` does.

**C26. The single-instance handoff has a startup gap.** A second launch can arrive while the first
is still building its window, which is why `ActivationRouter` buffers (`ActivationRouter.cs:9`,
*The gap in the middle* in `Architecture.md`). Nothing in the app hooks a window procedure.

---

## 4. Decisions taken while planning

None reopens a product decision. They are the choices the exploration left to the plan, or that the
code forced, and each is open to Paul's veto. P21, P24 and P25 are the three in section 1.1,
which Paul agreed on 2026-11-17.

| # | Decision | Why |
|:--|:---|:---|
| P1 | **The tree is a flat `ListView` of visible rows, not a `TreeView`.** The model flattens expanded folders into rows with a depth, and changes the list by **inserting and removing ranges**, never by replacing it, so focus, scroll and Narrator's place survive an expand. Automation `Level`, `PositionInSet` and `SizeOfSet` are set in `ContainerContentChanging` (containers are recycled); a small peer reports the Tree/TreeItem control types and Expand/Collapse | Shared state between two hosts, and testable diffs. `TreeView` keeps expansion in its containers |
| P2 | **The pure logic is a new library, `PaulTechGuy.MQ.Folders`**, `net10.0`, with its own test project and its own `TempFolder` | The parts most likely to be wrong unseen |
| P3 | **Rename and delete are workspace operations** in `DocumentWorkspace` | The workspace holds the watchers, marks and pins and does all file I/O |
| P4 | **A rename raises a new `WorkspaceChange.Renamed`**, not `Saved`. The "folder moved" tail of the `Saved` case (re-point the preview, republish links, re-check) runs **only for the active document**, because it calls `ActivateTabAsync`; a background document gets its new path when next activated | `Saved` means clean. Running the tail for background tabs would switch the shell to each in turn |
| P5 | **"The same panel" is the view model, not a moved control.** One `FolderPanelViewModel`; each host builds its own `FolderPanelView`. No two-way `SelectedItem`: a host writes selection only from a user's `SelectionChanged`. A host that is not on screen detaches its `ItemsSource` | Moving a control between windows' `XamlRoot`s is not reliable in WinUI 3; two live lists bound two-way would clear each other's selection |
| P6 | ~~**The panel starts hidden until a root is first chosen.** `ShowFolderPanel` defaults to false; setting a root turns it on~~ *Superseded by P6a* | Nobody who never opens a folder sees a change beyond the rail icon |
| P6a | **A new install shows both panels docked** *(Paul, 2026-11-17)*. `ShowOutline` and `ShowFolderPanel` default to true, and both pins to true. With a document open and no folder chosen, the folder panel offers Open Folder; with no document and no folder it has nothing to show, and the landing page keeps the window. A settings file from before the folder panel gets it docked too; an existing file's `showOutline` is kept | Both panels are found rather than hunted for |
| P7 | **Delete uses `SHFileOperationW` with `FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING`, on an STA thread, behind `IRecycleBin`.** The decision whether a delete will really be recycled lives in the library (`RecyclePolicy`): true only for a fixed local drive whose Recycle Bin is not set to delete immediately (`NukeOnDelete`) and whose maximum size is larger than what is going (`MaxCapacity`, from the per-volume `BitBucket` settings, defaulting to "recycles" when they cannot be read). Otherwise the confirmation says "permanently" | `FOF_ALLOWUNDO` silently deletes outright when the bin will not take the item. The exploration says a delete is never outright without saying so first |
| P8 | **A folder holding a marked document cannot be deleted.** Grayed, with the reason as its tooltip; the `Delete` key checks the same predicate and says so on the status line | The mark protects against a stray keystroke; deleting its folder is the same keystroke |
| P8a | **The read-only box in a file's Windows properties is named in the delete confirmation, not enforced.** The title says it (*Delete read-only file*, *Delete folder with read-only files*), and a warning bar above the question says *"x.md" is marked read-only in its Windows properties*, or how many of a folder's files are; a line inside the message was read past. `ConfirmAsync` gained an optional `warning` for it. The forecast counts them in the walk it already makes. Folders are not counted | Explorer deletes such a file without a word; blocking would be stricter than Windows and awkward across a large folder, while saying nothing surprised Paul in the first hand check (2026-11-17) |
| P9 | **The launching process qualifies arguments, and the receiver refuses anything not fully qualified.** In `Program.Main`, before the handoff, every non-switch argument goes through `Path.GetFullPath`. If none was relative, today's redirect runs unchanged. If any was, the launcher starts a third copy of itself with the absolute arguments, hands it foreground rights with `AllowSetForegroundWindow`, and exits; that copy redirects normally. The receiver's `IsOpenable` gains `Path.IsPathFullyQualified`, which on its own makes C2's wrong-folder case impossible | No new interprocess channel, no window procedure, no startup gap (C26). The cost is one extra process start for a relative argument only |
| P10 | **Session:** `FolderRoot`, `RecentFolders` (newest first, ten), `FolderPanelFloating`, `FolderPanelWindow`, `FolderSplit` (the outline's share, 0 to 1). **Preferences:** `ShowFolderPanel`, `FolderRevealActive` (on), `FolderShowAllFiles`, `FolderSortByModified`. Recent folders are pruned with a folder-aware twin of `HasBeenDeleted` made public in Services (C24) | Mirrors the outline: whether a panel shows is a preference, layout and history are session |
| P11 | **Insert Link in Source has its own predicate and its own edit runner.** Enabled when a saved document is active, not read-only, and not in Preview view; raised from `ActiveTabIsReadOnly`, `HasDocument` and `ViewMode`. It runs through `RunEditAsync` with a parameter naming the gate (`EditGate.Caret` today's `CanFormat`, `EditGate.Writable` for this), keeping `RefuseActiveIfReadOnly` | C9: through today's runner the click would do nothing |
| P12 | **The panel's menus come from one `FolderPanelMenus` class with its own `Writes(...)` list**, grayed on `Opening` when the active tab is read-only, used by both hosts | Keeps the read-only rule in a named second place (C10). Only *Insert Link in Source* writes text |
| P13 | **An untitled document can carry a save folder**, `MarkdownDocument.SaveFolder`, set by *New Document Here*, passed to `PickSaveFileAsync(..., folder)`, cleared by the first save, and re-pointed by a folder rename | The first Save As starts in that folder |
| P14 | **Labels follow the tab menu**: *Open in File Explorer*, *Copy > Name / Relative Path / Full Path* | One action, one name, across menus. New items keep the exploration's names |
| P15 | **Grayed-because-ignored and grayed-because-image look the same; the tooltip is enough** (the exploration's last open detail) | An image's extension says what it is; a third visual state is not worth keeping apart from the markers. An ignored image's tooltip says both |
| P16 | **`.gitignore` files are read from the enclosing repository's top down.** Supported: comments, blank lines, `*`, `?`, `**`, leading `/`, trailing `/`, `!` with last match winning. Not read: `.git/info/exclude`, global excludes | Roots are often `repo/docs`. Anything unsupported fails toward not grayed |
| P17 | **The filter searches below closed folders** within the ceiling, debounced 250 ms, canceled by the next keystroke, and says *Not every folder was searched* when the ceiling stopped it. Nothing is kept once the filter clears | Narrowing only the open folders hides the file being looked for |
| P18 | **The ceiling and the throttles.** 20,000 entries per look; at most two probes at once; every probe and walk canceled when the root changes; the tree watcher's buffer at 64 KB, `LastWrite` subscribed only while date sort is on, and a refresh after a watcher error at most once every five seconds. A watch that cannot start (common on a share) leaves the panel on *Refresh* | A large or busy root stays responsive and cannot fall into a rescan loop |
| P19 | **Sort by modified** keeps folders first by name, then files newest first | Folders jumping on every save is worse |
| P20 | **Row markers are Segoe Fluent glyphs**, with `⟳` and `!` as the tab strip has them | Emoji draw in color and cannot be recolored (`DiskWatch.md`) |
| P21 | **The root is restored on start whatever the startup preference** *(section 1.1)* | The preference decides which tabs appear; a root opens none |
| P22 | **A dropped folder sets the root.** On the chrome, files in the same drop still open and only the first folder counts, said on the status line. On the WebView a drop is one URI, so it is one folder or one file. The multi-file Open dialog is not changed | One root at a time |
| P23 | **Recent files, closed-tab records and recent folders follow a rename;** recent entries for a deleted file are removed | Each is a path that would otherwise lead to "File not found" |
| P24 | **Insert Link in Source and the drag only link images in the document's folder or beneath it** *(section 1.1)*. The link is `![name](target)`, the name without its extension as alt text, the target from `RelativeLink` wrapped by `LinkTarget` | C21 |
| P25 | **Delete is grayed for a document under review** *(section 1.1)* | C22 |
| P26 | **The panel menu (`…`)** holds *Reveal Active Document* (a one-shot command, also a target button in the header), *Always Reveal Active Document* (toggle), *Show All Files*, *Sort by Modified Date*, *Refresh*, *Collapse All* and *Pop Out Folder Panel*. The root header's right-click keeps the exploration's table | The exploration names these as "panel menu" items without listing the menu |
| P27 | **Marqora stops holding its start folder open.** After qualifying arguments, `Program.Main` sets the working directory to `AppContext.BaseDirectory` | A process's working directory cannot be renamed. Started by double-clicking `docs/guide/intro.md`, Marqora would otherwise block renaming `guide` from its own panel |
| P28 | **Workspace operations on one document are serialized.** A per-document gate is taken by `SaveAsync`, `SaveAsAsync`, `RenameAsync` and `DeleteAsync`. A rename or delete waits for a save in flight; a save that arrives during one returns false, which autosave already treats as "not written" | An autosave landing between a recycle and the tab closing would write the deleted file back |
| P29 | **Each panel is docked or on the edge** *(Paul, 2026-11-17; replaces the rail and the per-section hide buttons of the first WP-5 build. A third, closed, place was built and then dropped the same day: with both panels closed the window showed no way back)*. Docked: in the left column, stacked when both are, header with fold, menu and unpin. On the edge: a vertical tab on the edge strip, always at the window's far left; a click slides the panel out over the text, past the docked column, so it never covers a docked panel; the tab's word reads bottom to top with its glyph below it; header with pin. There is no X. View > Folder Panel / Outline and Alt+6 / Alt+4 do what the pin button does, the menu item checked while docked. Alt+Shift+6 / Alt+Shift+4 go to it. The Show preferences still switch a panel off altogether. Whether each panel is pinned, and the split, are session state. A slid-out panel slides away on a click outside it, on Escape, after opening a file from the folder panel, and after picking a heading in the outline: four preferences, all on by default. The edge strip shows only while some panel lives there. The folder panel stays off until a folder is first chosen, and Open Folder then shows it, docked the first time | The first build's rail, hide buttons and "show the other one" buttons each patched a gap the others left. One place per panel, set by the panel's own pin and X, is one model. Tried in a clickable mock before building |

---

## 5. Target shape

```text
src/PaulTechGuy.MQ.Folders/                    new library (P2)
    FolderListing.cs        markdown, images, always-hidden folders, Show all files
    NaturalNameComparer.cs  "Chapter 2" before "Chapter 10"
    GitIgnoreRules.cs       P16
    FileNameRules.cs        legal names, reserved names, case-only renames, extension change
    FolderProbe.cs          "leads to markdown or images", ceiling and throttle (P18)
    FolderTree.cs           lazy children, expansion, Reveal, idempotent Apply
    FolderRows.cs           flattening, filter, sort, and range diffs (P1)
    FolderTreeWatcher.cs    recursive watcher, coalesced and throttled (P18)
    RecyclePolicy.cs        will this delete really be recycled (P7)
    LaunchArguments.cs      qualify, and is-fully-qualified (P9)

src/PaulTechGuy.MQ.Abstractions/
    Services/IWorkspaceService.cs   + RenameAsync, DeleteAsync, WorkspaceChange.Renamed
    Services/IDocumentLocks.cs      + PrepareMove, CompleteMoveAsync, AnyUnder
    Services/IDocumentPins.cs       + PrepareMove, CompleteMoveAsync, RemoveUnderAsync
    Services/IRecentFilesService.cs + RenameAsync, RemoveUnderAsync
    Services/IRecycleBin.cs         new
    Ui/IFileDialogService.cs        PickSaveFileAsync(name, folder), PickFolderAsync(title)
    Ui/IDialogService.cs            DialogAnchor.FolderPanel

src/PaulTechGuy.MQ.Services/   DocumentWorkspace, DocumentLocksService, DocumentPinsService,
                               RecentFilesService, DocumentPathKeys (folder twin, public),
                               ServiceCollectionExtensions
src/PaulTechGuy.MQ.Domain/     AppSettings, MarkdownDocument.SaveFolder

src/PaulTechGuy.MQ.App/
    ViewModels/FolderPanelViewModel.cs, FolderRowViewModel.cs
    Views/FolderPanelView.cs, FolderPanelMenus.cs, FolderPanelWindow.cs, MainWindow.FolderPanel.cs
    Services/FolderPanelWindowService.cs, RecycleBin.cs, WindowContext.cs (anchor)
    Program.cs, Services/SingleInstance.cs
```

---

## 6. Work packages

### WP-0: Observations

**Status:** Open. Half a day.

**Progress, 2026-11-17.** The case-only move is answered: .NET 10 renames a file and a folder by
case alone (`CaseOnlyMoveTests`, kept), so no temporary name is needed. The Recycle Bin run and
the watcher-on-the-profile run need the app code that WP-7 and WP-5 write, and are done as the
first step of each instead. The Recycle Bin run needs the volume's bin set to delete immediately,
which is a machine setting, so it is Paul's to make or skip.

Each answer could reshape a later package, so they come first. Every one is a test kept in the
suite or a Debug run of the real app; none is a scratch program.

| Observation | How | If it fails |
|:---|:---|:---|
| A case-only `Directory.Move` and `File.Move` on .NET 10 | Tests in the new library, kept | `FileNameRules` and WP-3 route a case-only rename through a temporary name |
| `FOF_ALLOWUNDO` with the flags in P7 on an item larger than the bin's maximum, and with `NukeOnDelete` set | Debug run on a test file | `RecyclePolicy` gains the condition that was missed |
| The recursive watcher on the user profile while a browser and a build run | Debug run with a counter in the log | The P18 throttles are tightened before WP-1 is written around them |
| A text drag from a WinUI `ListView` into the WebView lands in Monaco at the drop point, and does nothing on the preview | Done as the first step of WP-8, which is the first code that can drag | WP-8 withdraws the drag; *Insert Link in Source* stands alone |
| Narrator reads a flat list with the P1 properties and peer as a tree | Done as the first step of WP-5's list | Section 10 records the gap; `TreeView` is not brought back, because P5 rules it out |

### WP-1: The folder library

**Status:** Built 2026-11-17, uncommitted. 126 tests in `PaulTechGuy.MQ.Folders.Tests`.

**As built.** Two pieces the section 5 list did not name: `FolderPaths` (prefix-at-a-separator
and rebase, shared with Services) and `GitIgnoreReader` (finds the repository above the root and
reads its files top down; `GitIgnoreRules` stays pure). `FolderChangeCoalescer` is split from
`FolderTreeWatcher` and handed the time, so batching is tested without waiting. One consequence
of P18's "names only unless date sort": a `.gitignore` edited in place is picked up by *Refresh*,
not live, unless date sort is on; one written by rename (as most editors save) is live.

**Files:** `src/PaulTechGuy.MQ.Folders/*`, `tests/PaulTechGuy.MQ.Folders.Tests/*`,
`PaulTechGuy.MQ.slnx`.

**Changes:**

- **`FolderListing`.** Markdown is `MarkdownFileTypes.FolderExtensions` (no `.txt`, by that list's
  own reasoning); images are `ImageFileTypes.AllowedExtensions`. Always hidden: folders whose
  name starts with `.`, and `node_modules`, `bin`, `obj`. *Show all files* lifts everything else,
  dot-*files* included. Reparse points are listed but never entered by a look.
- **`NaturalNameComparer`**: digit runs as numbers, the rest `CurrentCultureIgnoreCase`, ties
  broken ordinally so the order is total.
- **`GitIgnoreRules`** (P16), built from text so the tests need no repository.
- **`FileNameRules.Check(folder, oldName, newName)`**: empty, illegal character, reserved device
  name (with or without an extension), trailing dot or space, too long, already taken (other than
  by the file itself, case-only included). Reports *leaves the tree* for a markdown name given a
  non-markdown extension while *Show all files* is off.
- **`FolderProbe`** and **`FolderTree`**/**`FolderRows`** as in section 5. `Apply` is idempotent:
  the panel's own rename is applied at once and the watcher's report of it 300 ms later changes
  nothing. While a rename box is open, changes queue and are applied on commit or cancel.
- **`FolderTreeWatcher`** with P18's settings. Events under always-hidden folders are dropped
  before coalescing.
- **`RecyclePolicy.WillRecycle(volume facts, total size)`** (P7), facts read by the app.
- **`LaunchArguments.Qualify(args, currentDirectory)`** and **`IsAcceptable(arg)`** (P9).

**Verified by:** the new tests: listing, order, ignore rules, names, the ceiling honored, probe
cancellation on root change, range diffs for expand, collapse, filter and sort, idempotent apply,
queued changes under an open rename, the recycle policy's table, argument qualification.

### WP-2: Path-keyed stores learn to move

**Status:** Built 2026-11-17, uncommitted. `PathKeyMoveTests`, including a real junction made
with `mklink /J`.

**As built.** `PreparedMove` lives in Abstractions; the shared logic is `PathKeyMoves` in
Services. The second parameter is `destination`, not `to` (CA1716: `To` is a Visual Basic
keyword). `DocumentPathKeys` is now public for `FolderHasBeenDeleted`.

**Files:** `IDocumentLocks.cs`, `IDocumentPins.cs`, `IRecentFilesService.cs`, their services,
`DocumentPathKeys.cs`, `tests/PaulTechGuy.MQ.Services.Tests/Fakes.cs` and tests.

**Changes:**

- **A move in two halves.** `PrepareMove(from)`, called while `from` still exists, collects every
  key equal to `from` or beneath it, matched under both the literal full path and its normalized
  form (C3). `CompleteMoveAsync(prepared, to)` swaps the matched prefix for `to` and writes once.
  If the write fails after the file has moved, the in-memory swap stands, the failure is logged,
  and the next write of the store carries it; nothing rolls the file back.
- `IDocumentLocks.AnyUnder(folder)` (P8). `IDocumentPins.RemoveUnderAsync(folder)`.
- `IRecentFilesService.RenameAsync(from, to)` and `RemoveUnderAsync(path)`, keeping each entry's
  pin and time (P23). Recent files are keyed by full path only, so they need no two halves.
- `DocumentPathKeys.FolderHasBeenDeleted`, public, for recent folders (C24).

**Verified by:** a file move; a folder move with marks on unopened files beneath it; a mark stored
under a junction folder's spelling, and a rename of the junction itself; a case-only move; a
prefix without a separator (`notes` must not move `notes-old\a.md`); a failed write after the
move; a present folder kept and a missing one dropped by the folder twin.

### WP-3: The workspace renames and deletes

**Status:** Built 2026-11-17, uncommitted. `FolderOperationTests`, 19 tests covering the matrix
below, plus the save folder, folder delete, and the read-only and review refusals.

**As built.**

- Step 2's order is reversed: the watchers are stopped *before* `External` is recorded, so the
  recorded state is the last word.
- `OnFileRemoved` now asks `File.Exists` again under the lock and drops a "gone" report for a
  file that is there. This closes the "before the move" case of test 5. One existing test,
  `A_review_holds_over_a_missing_file`, raised "removed" without deleting the file; it now
  deletes it first, as the real watcher would require.
- `MarkdownDocument.SaveFolder` (P13, listed under WP-4) is added here, because the rename
  re-points it. `CreateUntitled` takes it.
- `RecycleBin` (the app's `IRecycleBin`) is written and registered here, because the workspace
  cannot be built without one. Its forecast reads the volume's `BitBucket` settings.

**Files:** `IWorkspaceService.cs`, `DocumentWorkspace.cs`, `IRecycleBin.cs`,
`ServiceCollectionExtensions.cs`, `Program.cs` (registration), `Fakes.cs`,
`tests/PaulTechGuy.MQ.Services.Tests/FolderOperationTests.cs`.

**Changes:**

- **The watcher fix (C4).** `StartWatching` captures the watcher it made; each handler first
  checks, under the workspace's lock, that `_watchers[id]` is still that instance, and drops the
  event if not. `StopWatching` removes the entry before anything moves, so a late callback is
  dropped whatever its timing. Every `_documents` write from a watcher thread takes the same lock.
  This also covers Save As, which re-arms a watcher the same way.
- **The operation gate (P28).**
- **`RenameAsync(from, to)`**, file or folder:
  1. Take the gate for every open document at `from` or beneath it. Refuse if `to` is taken.
  2. Record each document's `External`. Stop their watchers.
  3. `PrepareMove` on marks and pins.
  4. `File.Move` or `Directory.Move`. On failure, restart the watchers and answer with the reason.
  5. `CompleteMoveAsync` on marks and pins (C5's order: store first).
  6. Re-point each document: new `Path` by prefix, `IsLocked`/`IsPinned` read back, `External`
     as recorded in step 2, `SaveFolder` re-pointed for untitled documents with one beneath.
  7. Restart the watchers on the new paths. Raise `Renamed` for each document.

  The comment says what C5 says: unlike Save As, the mark moves.
- **`DeleteAsync(path)`**: take the gate, stop the watchers, `IRecycleBin.RecycleAsync`; only on
  success close the documents (raising `Closed`) and remove their pins. On failure every tab stays
  and the watchers restart. If the delete was permanent and only part of it happened, documents
  whose files went are marked `Missing`, the rule for a file something else deleted.
- `IRecycleBin` is injected into `DocumentWorkspace` and registered in the app.

**Verified by:** the DiskWatch-style matrix:

1. Rename a clean open file: same `Id`, new `Path`, `InSync`.
2. Rename a dirty open file: text kept, still dirty, Save writes to the new path.
3. Rename a marked file: `document.IsLocked` is true and the store holds only the new path.
4. Rename a pinned file: `document.IsPinned` is true, stored under the new path.
5. A watcher event raised for the old path at each point: before the move, between the move and
   the re-point, after the re-point. The document ends `InSync` every time. (`FakeFileWatcher`
   gains `RaiseRemoved(string path)`.)
6. Rename a folder with two open documents and a marked unopened file: all carried.
7. Rename onto an existing name: refused, nothing moved, watchers running.
8. Rename fails because the file is held open with `FileShare.None`: nothing moved, watchers back.
9. Case-only rename of a file and a folder.
10. Delete an open dirty file with a fake bin: tab closed, no prompt from the workspace.
11. Delete fails: tab stays, watcher running.
12. Undo: rename `a` to `b` and back; mark, pin and tab where they started.
13. An autosave arriving during a delete returns false and writes nothing; a delete arriving during
    a save waits for it.
14. Rename of a document whose file is `Missing`: refused, there is nothing on disk to move.

### WP-4: Settings

**Status:** Built 2026-11-17, uncommitted. `MarkdownDocument.SaveFolder` went in with WP-3.
`AppSettingsTests` has the "settings file from before" check.

**Files:** `AppSettings.cs`, `MarkdownDocument.cs`, `PreferencesTransferTests.cs`.

**Changes:** P10's members, nullable with `[JsonIgnore]` accessors where reference-typed; the five
session members in `SessionKeys` and `WithSessionOf` together; `MarkdownDocument.SaveFolder`,
cleared by `AsSaved` and Save As.

**Verified by:** `PreferencesTransferTests` with the fixtures extended (C18): the five session
members given non-default values in `WithSession`, the four preferences in `Customised()`. A
settings file from before this feature loads with the panel hidden, no root, split at one half.

### WP-5: The docked panel

**Status:** Built 2026-11-17, uncommitted. The solution builds with no warnings and every test
passes; nothing on screen has been seen yet. Open until Paul has looked at it and the two runs
below are done. Listing, opening, revealing, keys, the panel menu, and the File menu's Open
Folder setting the root, so it can be used from the first build. Every file operation is WP-7.

**Reworked for P29, 2026-11-17.** The first build's rail, the per-section hide buttons and the
"show the other one" buttons are gone. Each panel's header now carries fold (only while both are
docked), its menu (the folder panel's), and pin or unpin; the X built first was removed the same day. Unpinned panels live as vertical tabs
on an edge strip in front of the column, and `SlideOutHost` draws a slid-out panel over the text.
The panel's own content element moves between the column and the host, and
`MainWindow.FolderPanel.cs` plays the slide. Settings: `FolderPanelPinned` and `OutlinePinned`
(session, default true) replace WP-4's `FolderPanelFloating` and `FolderPanelWindow`.
`ShowFolderPanel` and `ShowOutline` keep meaning "not closed". The four `SlideAwayOn…` preferences
are on a new Preferences group, *Panels on the window edge*. A click into the text reaches the
window only as a pane taking the keyboard, because the WebView keeps its pointer events, so that
is how a click outside is heard there. Other clicks in the window are heard directly.

**As built.**

- The panel is `FolderPanelView` (built in code, so the column and the slide-out can both host it) over
  `FolderPanelViewModel`, with the list in `FolderTreeList`. The main window's half is
  `MainViewModel.FolderPanel.cs` and `MainWindow.FolderPanel.cs`.
- The teal bar is a visual state, `Active`, of a new row style, `MqTreeRowStyle`, so it stays a
  theme resource (C25). Selection keeps the plate and gives up the bar.
- The outline has a header ("Outline", with a fold chevron) only while the folder section is
  above it. Alone in the column it looks as before. Which section is folded is not remembered
  between sessions; P10 lists no setting for it.
- Alt+Shift+6 with no document open and no root has nothing to land on, because the landing page
  is the whole window. It says *Choose a folder with File > Open Folder* on the status line.
- `PickFolderAsync` now takes a title. The two Folio callers say what they are for.
- The filter's search below closed folders leaves the folders it read loaded but closed. They are
  not listed or opened, and no index is kept.
- Two things need the app running, so they are Paul's or a later Debug session's. **Narrator**
  (WP-0): the row peer depends on `GetContainerForItemOverride` being honored, which Find All
  found is not true of `PrepareContainerForItemOverride`. The level and position are set on the
  container either way. **The watcher on the user profile during a build** (WP-0): not run.

**Files:** `FolderPanelViewModel.cs`, `FolderRowViewModel.cs`, `FolderPanelView.cs`,
`MainWindow.FolderPanel.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `MainViewModel.cs`,
`FileDialogService.cs`, `RowSplitter.cs`, `webshell/app.js`, `KeyboardShortcuts.cs`.

**Changes:**

- **The column.** Column 0 becomes three rows: the folder section, a `RowSplitter`, the outline
  section, whose panel and rail move inside unchanged. `RowSplitter` gains a double-click that
  resets `FolderSplit` to one half (C23). A collapsed header gives its space to the other section
  without changing `FolderSplit`.
- **Visibility, rewritten (C7).** The folder section is in the column when shown, docked, and
  (there is a document or a root). The column exists when either section is in it, or when there
  is a document and both are hidden, for the rail. The rail's folder icon shows only while
  docked, and a double-click on the empty strip restores both sections. The landing page's
  column and span are bound to the column's presence.
- **`FolderPanelViewModel`** holds tree, filter, selection, notice and root. It follows
  `IWorkspaceService.Changed` for the active document and the markers, through `IUiDispatcher`,
  and reads no file to decorate a row.
- **Rows.** Depth indent, chevron, name, markers (P20). The teal bar is a row property, not
  `ListView` selection. Dimmed text comes from the effective `AppTheme`, repainted on theme
  change (C25).
- **Opening.** Click or Enter on markdown: `OpenPathAsync`, then the editor gets the keyboard.
- **Keyboard.** Arrows, Left and Right, Left on a child goes to its folder, Enter, type-ahead.
- **Reveal**, the foot line for a document outside the root, nothing for untitled.
- **The focus latch, `FolderPanelHasFocus` (C8).** For each reader of `OutlineHasFocus`: it joins
  `CanFormat`, `CanEditText` and `CanPaste`; Copy with the tree focused copies the selected row's
  full path; `RestoreDocumentFocusAfterChrome` returns to the tree after a menu command, **except**
  after a file was opened from it, when the editor keeps the keyboard. It is cleared on hide, on
  opening a file, on Escape, and when a pane takes focus.
- **Keys (C14).** `Alt+6` and `Alt+Shift+6` on `RootGrid` and in `HOST_SHORTCUTS`, posted as
  `toggleFolderPanel` and `focusFolderPanel`. The focus key shows a hidden panel, brings a floating
  one forward, and lands on **Open Folder** when there is no root. `View > Folder Panel` beside
  `View > Outline`; both keys in `KeyboardShortcuts.cs`.
- **The panel menu** (P26) with its three preferences, *Refresh* and *Collapse All*. *Pop Out* is
  added in WP-9.
- **Empty states** as the exploration's table, including both buttons in the no-root state
  (**Open Folder** and **Use this document's folder**), in the standard tiers.
- **Open Folder sets the root.** `OpenFolderCommand` picks a folder and sets the root, showing the
  panel. The old body becomes `OpenFolderInTabsAsync(folder)`, unchanged, for WP-7.
  `PickFolderAsync` takes a title, so the Folio callers keep theirs and this one says *Open
  Folder*. `File > Close Folder`. The toolbar tooltip (`MainWindow.xaml:1701`) and the landing
  page's button are reworded.
- **The tree watcher** starts and stops with the root.

**Verified by:** a Debug run against this repository's `docs` folder and against the user profile
during a build, and the checklist.

### WP-6: Every other way to choose the root

**Status:** Built 2026-11-17, uncommitted. Builds with no warnings; every test passes. The checks
below need the app and a terminal, so they are Paul's.

**As built.**

- The tab menu's **Use This Document's Folder** sits under *Open in File Explorer*, grayed for an
  untitled tab, and acts on the clicked tab's path rather than through the active-folder command,
  which follows the workspace a moment behind the click.
- A drop: OpenSeveralAsync collects folders instead of expanding them; the first becomes the
  root, a second one is named on the status line, and the files in the same drop still open. A
  folder dropped on the text or preview arrives at OnExternalLinkActivated as a directory URI
  and sets the root.
- The command line: Program.Main qualifies the arguments, moves the working folder to
  AppContext.BaseDirectory (P27), and passes the qualified list to TryHandOff only when one was
  relative. A second launch with a relative path starts a copy of itself with the absolute
  arguments, and that copy redirects. FilesFrom is now ArgumentPaths; it accepts a folder and
  refuses anything not fully qualified. OpenActivatedAsync makes the first folder the root and
  opens the files, at startup and on a forwarded launch alike.
- The panel's `…` menu gains Recent Folders (newest first, leaving out the folder shown; one that
  has gone is dropped when picked) and Close Folder. The root header has **no right-click menu**
  (Paul, 2026-11-17): it would have repeated half the `…` menu, so the exploration's root-header
  items live in `…`, and New Document Here and Open All in Tabs for the root join it in WP-7.
- The root restored on start, and a missing root named in the empty state, were done in WP-5.

**Files:** `MainViewModel.cs`, `MainWindow.TabContextMenu.cs`, `SingleInstance.cs`, `Program.cs`,
`App.xaml.cs`.

**Changes:**

- *Use this document's folder* on the tab menu, for a saved document.
- Drops (P22, C12): `OpenDroppedAsync` sets the root from a folder rather than expanding it, and
  `OnExternalLinkActivated` sets it for a non-clicked `file://` URI that is a directory.
- The command line (P9, P27): `Program.Main` qualifies, relaunches if any argument was relative,
  then moves its working directory. `SingleInstance.FilesFrom` becomes `ArgumentPaths` and accepts
  a folder; `IsOpenable` refuses anything not fully qualified. `App` sets the root from the first
  folder argument at startup; a forwarded folder sets it in the running instance.
- Recent folders in the root header's menu, pruned on load (P10).
- The root restored on start (P21); a missing root dropped, and the empty state names it.

**Verified by:** `LaunchArguments` tests (WP-1), and checklist items for `Marqora.exe .` and
`Marqora.exe notes.md` from a terminal elsewhere, with and without a running instance; and a folder
dropped on the source pane, which nobody has yet seen navigate.

### WP-7: File operations

**Status:** Built 2026-11-17, uncommitted. Builds with no warnings; every test passes. Nothing has
been seen on screen. WP-0's Recycle Bin run (the bin set to delete immediately) is still Paul's to
make or skip.

**As built.**

- Row menus come from FolderPanelMenus, built per row as it is right-clicked (or the Menu key),
  with its own Writes(...) list grayed on a read-only active tab. Nothing in it writes yet; WP-8's
  Insert Link in Source will be the first. The root folder's New Document Here and Open All in Tabs
  are at the top of the panel's … menu, since the header has no right-click (WP-6).
- Rename is a TextBox in the row itself, the stem selected; the reason a name cannot be used shows
  under it as it is typed. Enter commits, Escape cancels, and losing focus commits a usable name or
  abandons an unusable one. F2 opens it. The tree is held while it is open.
- RenameFromPanelAsync and DeleteFromPanelAsync (MainViewModel.FileOperations.cs) wrap the
  workspace's operations: recent files and closed-tab records follow (P23), the change lands on the
  tree at once, and the notice says what happened. WorkspaceChange.Renamed updates the tab and the
  session, and runs the folder-moved tail for the active document only (P4).
- The notice is an InfoBar under the header, with Undo after a rename; Undo says what it put
  back, or why it could not, and offers no Undo of its own.
- Delete asks through the main window's ConfirmAsync with Cancel the default. No
  DialogAnchor.FolderPanel: with the floating window dropped (WP-9) there is no second window to
  anchor to. The Delete key on a marked or reviewed document says why on the status line.
- PickSaveFileAsync takes a folder, and Save As passes SaveFolder (P13).

**Files:** `FolderPanelMenus.cs`, `FolderPanelViewModel.cs`, `FolderPanelView.cs`, `RecycleBin.cs`,
`MainViewModel.cs`, `IFileDialogService.cs`, `FileDialogService.cs`.

**Changes:**

- **Menus** as the exploration's table with P14's words, through `FolderPanelMenus` (P12). Shown
  non-markdown files get *Open in File Explorer* and *Copy* only. *Open to the Side* is not built.
  `RevealInExplorer(path)` and `LaunchPath(path)` become reachable from the panel (C16).
- **Rename** in place: a `TextBox` over the row, the name selected without its extension, Enter
  commits, Escape cancels, the reason under the box while it stays open; the tree holds still
  while it is open (WP-1). A markdown-to-other extension asks first.
- **The `Renamed` handler**: update the tab, `PersistSession`, move recent files and closed-tab
  records (P23), run the "folder moved" tail for the active document only (P4), republish the
  active document's link targets.
- **The notice**: an `InfoBar` at the top of the panel, with **Undo** after a rename, none after a
  delete; it stays until the next operation or ✕. Undo renames back and says in place why when it
  cannot.
- **Delete**: one `ContentDialog`, anchored to the window the click came from
  (`DialogAnchor.FolderPanel`, registered through `WindowContext`). It names the file, or the folder
  and its file count; names unsaved changes with *Delete and discard changes* as the verb; says
  "permanently" when `RecyclePolicy` says the bin will not take it. Neutral commit, Cancel the
  default. Grayed for a marked file, a folder holding one, and a document under review (P8, P25),
  and the `Delete` key says why on the status line. Recent entries beneath are removed (P23).
- **New Document Here**: `CreateUntitled` with `SaveFolder` (P13); Save As passes it on.
- **Open All in Tabs**: `OpenFolderInTabsAsync`.

**Verified by:** WP-3's tests for the workspace half, the checklist for the rest.

### WP-8: Images: insert, drag, launch

**Status:** Built 2026-11-17, uncommitted; the drag is not yet seen on screen.

**Files:** `FolderPanelView.cs`, `FolderPanelMenus.cs`, `MainViewModel.cs`.

**First:** the drag observation in WP-0.

**Changes (C1, C9, C21):**

- **Insert Link in Source** builds `![name](target)` per P24 and inserts it at the caret through
  `RunEditAsync` with `EditGate.Writable` (P11), so `Ctrl+Z` takes it back. An image outside the
  document's folder, or an untitled document, is refused with a status line.
- **Drag** carries the same text as plain text and no storage items (a file would be navigated to
  and end in *Unsupported file*). It is not started for an untitled, read-only or under-review
  document, or an image P24 refuses; the status line says why.
- **Double-click** launches the image with `LaunchPath`.

**Verified by:** the checklist, including an `Undo` after a drop and a drop on the preview.

**As built:**

- `ImageLinks` in the Folders library makes the markdown, tested there: the stem as alt text with
  its brackets escaped, a relative target with forward slashes, angle brackets around a target
  with a space or parenthesis, and null for an image outside the document's folder. Its `Target`
  is now the one copy of the bracket rule; `MainViewModel.LinkTarget` calls it.
- `WhyCannotLinkImage` gives the one reason for the menu's tooltip, the drag's status line and the
  insert: no document, an untitled one, read-only or under review, Preview view, or an image
  outside the folder.
- `RunEditAsync` takes an `EditGate`. `Writable` uses `CanWriteFromChrome`, which is `CanFormat`
  without the focus terms. The insert replaces the selection and leaves the caret after the link.
  A slid-out panel then slides away under the open-file preference rather than a fifth one.
- The image row's menu starts with *Insert Link in Source* (registered with `Writes`) and *Open*.
  `Writes` graying now only ever grays, so an item grayed for its own reason stays grayed.
- The list sets `CanDragItems`; `DragItemsStarting` cancels for anything but one image, or for a
  refused one with the reason on the status line, and otherwise carries the image's path in a
  `Marqora.FolderImage` format, with the link as text beside it.
- **The web view does not take a drag that starts inside the app** (Paul, on screen: the no-drop
  cursor, even after the window's own drag handler stopped refusing non-file drags). So the window
  takes it: handlers on `RootGrid` (handled events too) accept the format over the web view with
  an *Insert link* caption, and on the drop send the point to the web shell's `insertTextAtPoint`.
  It asks Monaco for the line and column under the point and inserts there as one undoable edit,
  caret after it. Over the splitter and the preview the drag is refused, so the cursor shows the no-drop sign
  with *Drop on the source pane*: `MainViewModel.IsSourcePaneAt` works the source pane out from
  the view mode and the splitter position, repeating the `.mq-root` grid rule in `app.css` (both
  places say so), because Windows asks on every move and wants the answer at once. Asking the page
  each time was rejected as laggy, and having it report its edges as more machinery than the
  layout needs. Browser zoom is off in the preview, so DIPs are CSS pixels.
- Double-click and `Enter` on an image open it with `LaunchPath`; a single click still only
  selects it.
- WP-0's drag observation was answered on screen by Paul: the web view refuses the drag, so the
  window-side drop above replaced it. Still Paul's to see: the drop landing at the point, `Ctrl+Z`
  after it, and a drop on the preview doing nothing.

### WP-9: Pop out and dock

**Status:** Dropped 2026-11-17 (P29). A floating window would be a fourth place for a panel, and
Paul chose docked, edge and closed instead. Kept below as a record, in case it comes back.

**Files:** `FolderPanelWindow.cs`, `FolderPanelWindowService.cs`, `MainWindow.FolderPanel.cs`,
`MainWindow.xaml.cs`, `IDialogService.cs`, `WindowContext.cs`, `AppSettings.cs`.

**Changes:**

- `FolderPanelWindow : PaletteWindow`, the *kept and hidden* kind. A second `FolderPanelView` on the
  same view model (P5); the docked view detaches while floating. Default 320 by 640 at 100% scale.
- *Pop Out Folder Panel* in the header, the panel menu and `View`; *Dock Folder Panel* in `View`
  and as a button at the top of the window's **content**, because `PaletteWindow` keeps the system
  caption, where the exploration's `⇲` cannot go. Closing the window hides the panel and leaves
  `FolderPanelFloating` set.
- The window carries `Alt+6` and `Alt+Shift+6`; from it, `Alt+Shift+6` activates the main window
  and the editor.
- Opening a file from it activates the main window without moving the palette.
- `MainWindow`'s closing handler shuts it down with the cheatsheet and Find All.
- Theme: `ApplyTitleBarTheme`, `SurfaceBrush`, and the rows' dimmed text (C25).

**Verified by:** the checklist, including two displays with the saved one unplugged, as optional
hardware items.

### WP-10: Documents, release note, checklist

**Status:** Built 2026-11-17, uncommitted.

**Files:** `docs/releases/vNext.md`, `README.md`, `Assets/Welcome to Marqora.md`,
`docs/Architecture.md`, `docs/DiskWatch.md`, `CLAUDE.md`, `docs/FolderPanel-Checklist.md`.

**Changes:**

- `vNext.md`: `## The folder panel`, with the exploration's drafted note on Open Folder.
- `README.md` (`:243`), the Welcome document (`:342`, `:369`): Open Folder, the panel, `Alt+6`.
- `Architecture.md`: *Secondary windows* says six; a new *Folder panel* section (P1, P5, P12, P18,
  P28); *What path keying costs* gains the panel's rename and keeps the line that an outside rename
  still leaves its mark behind.
- `DiskWatch.md`: *Following a rename* says Marqora follows the renames it makes itself, and why the
  watcher check exists.
- `CLAUDE.md`: the window count and list.
- `docs/FolderPanel-Checklist.md`: a **core sitting** of about an hour, run once in order with one
  folder of test files; and an **optional hardware** section (USB stick, a mapped drive, a second
  display, Narrator) that can be run another day or skipped. Everything a test already proves is
  left out of it.

**Verified by:** the four scripts in 2.3.

**As built:**

- `vNext.md`: `## The folder panel`, ending with the drafted Open Folder note, with drops and the
  command line added to it.
- `README.md`: a *Folder panel* row in the feature table and its own section after *Outline*;
  *Opening documents* says what Open Folder does now; the *Outline* section rewritten for docked
  and edge (it still described the hide button and the strip of the first build).
- `docs/index.html` (Paul, during WP-10: it was missing from this list): a *Folder panel* tile
  second in the feature grid, after *Split view*; the *Outline panel* tile mentions the edge.
- The Welcome document: a try-it line for `Ctrl+Shift+O`, the outline paragraph rewritten for
  docked and edge, a folder panel paragraph, and an `Alt+6` row in the shortcut table.
- `Architecture.md`: a *The folder panel* section; *Drag and drop, twice* gains the panel's drag
  and why the window takes it; *One instance* gains relative arguments and the working folder;
  *What path keying costs* says the panel's renames carry marks and pins.
- `DiskWatch.md`: *Following a rename made elsewhere*, and why the panel's own renames are
  followed.
- Not done, because WP-9 was dropped: *Secondary windows* still says five, and `CLAUDE.md`'s
  window list is unchanged.
- The outline's double-click comment in `MainWindow.Outline.cs` described the first build's hide;
  it now says it does what `Alt+4` does.
- `docs/FolderPanel-Checklist.md`: a core sitting of 31 steps with a PowerShell block that makes
  the test folder, and five optional hardware checks.

### WP-11: Paul's hand check

**Status:** Open. The core sitting, in a Release build.

---

## 7. Testing

- `PaulTechGuy.MQ.Folders.Tests`: the biggest group (WP-1).
- `PaulTechGuy.MQ.Services.Tests`: WP-2 and WP-3.
- `PaulTechGuy.MQ.Repositories.Tests`: `PreferencesTransferTests` with extended fixtures.
- By hand: the checklist's core sitting, which only confirms wiring; every decision that could be
  separated from the UI is in a tested library.

---

## 8. Risks

| Risk | What happens | Answer |
|:---|:---|:---|
| The text drag does not reach Monaco | No drag gesture | WP-0; *Insert Link in Source* stands alone |
| The bin refuses an item `RecyclePolicy` thought it would take | A file deleted outright | `FOF_WANTNUKEWARNING` as a backstop; the WP-0 observation; the policy errs toward "permanently" |
| A watcher callback lands mid-rename | A renamed tab shows `!`, or loses its path | The watcher-identity check and the lock (WP-3, test 5) |
| Autosave races a delete or rename | A deleted file comes back | The operation gate (P28, test 13) |
| `Directory.Move` refused by a handle inside | The rename fails | Answered with the reason; Marqora's own watchers and working directory are out of the way (P27) |
| A busy, huge root | Event storms, rescans | P18 |
| The relaunch for a relative argument fails | That launch opens nothing | Logged; absolute arguments are untouched, and the receiver's check means it can never open the wrong folder |
| Two hosts fight over selection | A cleared or wrong selection | P5 |
| Narrator does not read the list as a tree | An accessibility gap | WP-0, recorded if it fails |

---

## 9. Effort

Revised after the review: **twelve to fifteen working days**. WP-0 half a day; WP-1 three (the row
diffs and throttling were understated); WP-2 one; WP-3 two (the gate and the watcher lock); WP-4
half a day; WP-5 three (the list as a tree, the focus latch's seven readers, the column); WP-6 one;
WP-7 two (inline rename in a virtualized list, the notice, delete's cases); WP-8 half a day; WP-9
one; WP-10 one. Paul's core sitting is about an hour; the hardware section another hour if run.

---

## 10. Backlog

From the exploration's *Later* list: Find All over the folder, quick switcher, Git markers, a Folio
from a folder, move with link rewriting, several roots, preview tabs. Found while planning:

- Find All and export-report rows show a renamed document's old name until the next search (C19).
- `PastedImageTracker` holds the full path of an image pasted into an unsaved document; after a panel
  rename it can no longer recycle that file on undo. The file stays where it is.
- Background tabs in a renamed folder re-check their links when next activated (P4).
- An outside rename still leaves a read-only mark behind; only the panel's renames carry it.
- The receiver's new fully-qualified check means a relative argument from an old shortcut or
  script reaches nothing rather than the wrong file. It is the right failure; it is still a change.

---

## 11. Traceability

| Exploration item | Where |
|:---|:---|
| Decision 1: stacked column, split as a proportion, double-click reset, collapsible sections, rail icons, rail double-click | WP-5 |
| Decision 2: `PaletteWindow`, pop out and dock, close hides, focus key brings it forward | WP-5, WP-9 |
| Decision 3: one root, remembered, reveal, Open Folder, Close Folder, recent folders, Open All in Tabs | WP-5, WP-6, WP-7 |
| Decision 4: click opens a normal tab | WP-5 |
| Decision 5: rename, delete, New Document Here, the notice | WP-3, WP-7 |
| Decision 6: markdown and images, hidden folders, `.gitignore` grayed, Show all files, sort | WP-1, WP-5 |
| Decision 7: Find All stays tabs-only | Nothing to build |
| Settled 1–3: rail, splitter, keys | WP-5 |
| Settled 4: rename notice with Undo, `Ctrl+Z` stays the editor's | WP-7 |
| Settled 5: empty window | WP-5 |
| Settled 6: grayed not hidden, and its last detail | WP-1, P15 |
| Settled 7: release note only | WP-10 |
| The panel menu, Refresh | WP-5 (P26), WP-9 |
| Keeping up with the disk: live updates, read on expand, background look, ceiling | WP-1, WP-5 |
| Empty and edge states | WP-5, WP-6 |
| Rename carries marks, session, watch | WP-2, WP-3, WP-7 (P23) |
| Images: insert, drag, double-click | WP-8 (P24) |
| Other ways to set the root | WP-5, WP-6 |
| Non-markdown files: dimmed, Open in File Explorer and Copy only | WP-5, WP-7 |

---

## 12. Review log

A review on 2026-11-16 by two adversarial reviewers working independently: one checked every code
claim in the plan against the source, the other attacked design, feasibility and sequencing. Their
code claims were spot-checked against the source before being accepted, and all of those checked
held. Every finding below was accepted.

| # | Finding | Outcome |
|:--|:---|:---|
| R1 | **Insert Link in Source would have done nothing.** `RunEditAsync` returns silently when `CanFormat` is false, and focus in the panel makes it false | C9; P11 gives the runner a gate parameter |
| R2 | **A relative image link outside the document's folder is one the preview will not draw**, the link checker reports, and the PDF drops. Also, the target was not wrapped by `LinkTarget` | C21; P24, raised for Paul in 1.1 |
| R3 | **The rename read the mark and pin back before moving them**, so a marked file came back editable; the tests checked the store, not the document | WP-3 order fixed to C5's; tests 3 and 4 assert the document |
| R4 | **The path guard did not close the watcher race.** A callback can test existence between the move and the re-point, and step 5 then kept `Missing`; `_documents` is written from pool threads without a lock (both reviewers) | Watcher-identity check, a lock, `External` restored as recorded; test 5 fires at three points |
| R5 | **Autosave could write a deleted file back**, or recreate a renamed one, between the recycle and the tab closing | P28's per-document gate; test 13 |
| R6 | **The "folder moved" tail activates the tab it runs for**, so a folder rename would switch the shell through every background tab (both reviewers) | P4: active document only |
| R7 | **`WM_COPYDATA` was underspecified and contradicted by the startup gap**, and its fallback to the old redirect brought back the wrong-folder case | P9 replaced: relaunch with absolute arguments, and the receiver refuses anything not fully qualified |
| R8 | **Marqora's own working directory would block renaming a folder it was started from** | P27 |
| R9 | **`FOF_ALLOWUNDO` deletes outright on a fixed drive whose bin is set to delete immediately, or for an item over the bin's size**, with no warning | P7: `RecyclePolicy` reads the volume's bin settings, `FOF_WANTNUKEWARNING`, STA thread, partial failure defined |
| R10 | **The tree watcher's managed filter does not prevent kernel buffer overflow, and error-driven refreshes could loop**; probes and filter walks had no total limit or cancellation | P18 |
| R11 | **The panel menu and Refresh were in no work package**, nor the rail double-click, the focus key's floating case, or the non-markdown files' menu | P26; WP-5, WP-7, WP-9; traceability rows added |
| R12 | **Two live lists bound to one view model would fight over selection**, and rebuilding the row list would lose focus and Narrator's place | P1 range diffs; P5 one-way selection and detached hidden host |
| R13 | **Pruning recent folders with `HasBeenDeleted` would drop every folder**: it tests `File.Exists`, and it is internal | C24; a public folder twin |
| R14 | **The outline's focus latch has seven readers**; mirroring two would leave Paste lit at a hidden caret, Copy copying the wrong thing, and focus pulled back to the panel after opening a file | C8; WP-5 answers each |
| R15 | **Dimmed rows used a theme brush looked up in code**, the documented trap | C25; derived from `AppTheme` |
| R16 | **Delete would drop a review's unshared comments without asking** | C22; P25, raised for Paul in 1.1 |
| R17 | **`PreferencesTransferTests` only catches a missing member if the fixture sets it** | C18; WP-4 extends the fixtures |
| R18 | **The workspace has no recent-files service or recycle bin to call**; registration files were not listed | Recent files move in the `Renamed` handler; `IRecycleBin` injected and registered |
| R19 | **`OpenSeveralAsync` also serves the multi-file Open dialog**; a WebView drop is one URI | C12; P22 scoped |
| R20 | **A read-only folder does not make a rename fail on Windows** | Test 8 holds the file with `FileShare.None` |
| R21 | `FilesFrom` → `PathsFrom` collides with an existing private method | Named `ArgumentPaths` |
| R22 | The folder picker is titled for the old meaning and shared with the Folio | C11; a title parameter |
| R23 | **`PrepareMove` on a folder that is itself a junction** would miss every child key; a failed store write after the move would lose marks on the next load | WP-2 matches both spellings; the in-memory swap stands |
| R24 | **Path-keyed state missed**: closed-tab records, `SaveFolder`, `RecentFolders`; delete left recent entries | C20; P23; WP-3, WP-7 |
| R25 | **Quiet changes to the exploration**: one button where its no-root state has two; dot-files hidden under Show all files; the `⇲` in a caption `PaletteWindow` does not own; P21's reading | Fixed in WP-5, WP-1, WP-9; P21 raised in 1.1 |
| R26 | **WP-5 could not be used before WP-6** set a root; WP-6 promised a test in a project with none; only one unobserved behavior was named | Open Folder moved into WP-5; qualification moved to the library; WP-0 lists five |
| R27 | **Inline rename under live updates** could lose the box; the watcher re-reports the panel's own rename | WP-1: changes queue while the box is open; `Apply` idempotent |
| R28 | **Eight to ten days and a ninety-minute check were optimistic** | Twelve to fifteen days; a one-hour core sitting and an optional hardware section |
| R29 | Small citation corrections: C3's line, C4's line and its overstated window, C1's omission of the Explorer paste route, `RowSplitter` having no reset, `LaunchPath` being private, `WindowContext.Register`, `TempFolder` being internal to one test project | Corrected in section 3 and the work packages |

**Claims attacked that held:** C2's receiver-side resolution, end to end; C5's pin-moves,
mark-stays; C6, nothing in the app recycles; C7's `HasDocument` gates and the four-column landing
page; C10's private `Writes`; C13's free `Delete` and `F2`; C14's two keyboard lists; C15's
shortcut documents; C16's tab menu wording; C19's `Id`-keyed rows; the save dialog's missing folder
parameter; `OpenAsync` activating an open document; `ConfirmAsync`'s `destructivePrimary` moving the
default to Cancel; Find All being kept and hidden; shutdown closing the palettes.

**Design points attacked that held:** the flat list over `TreeView` (with R12's fixes); rename and
delete in the workspace with their own change; the mark moving on rename and not on Save As; delete
before close; a text-only drag with *Insert Link in Source* as the fallback; the per-menu `Writes`
list; the save folder on the untitled document; the `.gitignore` reader failing toward not grayed;
folders first under date sort; Fluent glyphs; the sixth `PaletteWindow` as the kept-and-hidden kind.
