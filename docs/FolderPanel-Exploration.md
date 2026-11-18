# Folder panel: feature exploration

A product exploration of a folder panel for Marqora: what it looks like, how it behaves, and
where its edges are. It is not an implementation plan. Seven decisions were settled in
discussion on 2026-11-16 and are recorded in [Decisions](#decisions); the rest of the document
follows from them. What is still open is listed at the end.

---

## Why now

Opening a folder of documentation is where Marqora is weakest today. A tab per file stops
scaling past a handful of documents, and `marqora-cli` already understands folders while the app
does not.

The panel is bounded on purpose: a tree sharing the left column with the outline, click to open,
no project files, no indexing. A tree is the first step toward a workspace, so the boundary is set
before starting. This exploration keeps it with one deliberate widening: the panel can **rename
and delete** files, not only open them. [File operations](#file-operations) says what that costs.

---

## What Marqora has today

The panel lands beside things that already exist, and most of its behavior is borrowed from them.

| Existing piece | What it does now | What the folder panel takes from it |
|:---|:---|:---|
| **Outline panel** | A native `ListView` in the left column, outside the WebView, with a filter box, a hide button, a drag handle for width, and a collapsed rail that stands in its place while hidden (`Alt+4`) | The column, the rail pattern, the filter box, the teal active row, and the reason it is chrome rather than web content: it can never reach a print or an export |
| **File > Open Folder** (`Ctrl+Shift+O`) | Opens every markdown file directly inside a folder as tabs, asking first above 25 files; subfolders are not read | Becomes the way to choose the panel's root. "Open all in tabs" survives as a folder's right-click item |
| **Local links** | A link to another file opens it at the named heading | A link and a click in the tree arrive at the same place: one tab per path |
| **Disk watching** | Each open file is watched; a rewrite or delete marks the tab, never loses a keystroke | The tree's markers show the same states, and a rename or delete from the panel must not trip the "file went missing" path |
| **Find All** | Searches every open tab | Stays tabs-only for this feature; searching the folder is the [next step](#later) |
| **Secondary windows** | Five `PaletteWindow`s: modeless, floating, placement remembered | The undocked panel is a sixth |
| **`marqora-cli`** | Takes `notes/**/*.md` patterns and folder options | Nothing shared in code, but the app catching up with the CLI is part of the argument |

---

## Principles

1. **Files and outline at the same time.** Making the reader choose between the file tree and
   the outline costs the one view they were not looking at. Stacking them in one column keeps
   both.
2. **The tree can leave the window.** On a second screen, a floating tree leaves the main window
   its full width for source and preview.
3. **The root stays where it was put.** Following the active document's folder is a view of the
   tree (reveal), not a change of root.
4. **File management grows without end.** A panel that starts with "open a file" soon moves,
   renames and rewrites links. Marqora stops at rename and delete.

---

## Decisions

| # | Question | Decision |
|:--|:---|:---|
| 1 | How does it share the left side with the Outline? | **Stacked in one column.** Folder section above, Outline below, a splitter between, each section collapsible |
| 2 | What is it when undocked? | **A `PaletteWindow`**, like Find All: floats above Marqora, not in Alt+Tab or the taskbar, minimizes with it, remembers placement. Pop out and Dock are buttons; there are no drag-to-dock targets |
| 3 | Which folder does it show? | **One chosen root, remembered, with the active document revealed in it.** File > Open Folder sets the root; "Open all in tabs" moves to a folder's right-click menu |
| 4 | What does a click do? | **Opens a normal tab**, or switches to it if the file is already open. No preview tabs |
| 5 | Can it change files? | **Rename and delete, plus New document here.** No moving, no new folders, no link rewriting |
| 6 | What does the tree list? | **Markdown and images.** Images dimmed and draggable into the source; folders with neither hidden; `.git`, `node_modules`, `bin`, `obj` and dot-folders always hidden; what `.gitignore` names is **grayed, not hidden**; a *Show all files* switch |
| 7 | Does Find All search the folder? | **Not in this feature.** It is the documented follow-on |

---

## The experience

### Docked in the main window

```text
┌─────────────────────────┬──────────────────────────────────────────────────┐
│ ▾ MARQORA-DOCS     ⧉ … ✕│                                                  │
│ [ Filter files       ]  │                                                  │
│  ▾ docs                 │                                                  │
│     Architecture.md     │        source            │       preview         │
│   ▸ cli-tour            │                          │                       │
│     CLI.md           ●  │                          │                       │
│ ▌   FolderPanel.md   ✎  │                          │                       │
│     Releasing.md     🔒 │                          │                       │
│   README.md             │                          │                       │
│ ═══════════════════════ │                          │                       │
│ ▾ OUTLINE            ✕  │                          │                       │
│ [ Filter outline     ]  │                          │                       │
│   Folder panel          │                          │                       │
│ ▌   Why now             │                          │                       │
│     Decisions           │                          │                       │
└─────────────────────────┴──────────────────────────────────────────────────┘
  ⧉ pop out   … panel menu   ✕ hide   ● open   ✎ unsaved   🔒 read-only   ▌ active
```

- **One column, two sections.** The column keeps today's single width and drag handle. A
  horizontal splitter divides it, starting at even halves. Once dragged, the split is
  remembered as a proportion rather than pixels, so both halves grow with the window or a
  bigger screen. Double-clicking the splitter evens it up again, as double-clicking the Side by
  Side button does for the panes. The splitter never moves on its own: not when a folder
  expands, a file appears or the active tab changes. Each section's header collapses it to one
  line, giving its space to the other, so a long outline can have the whole height without
  hiding the tree; expanding it again restores the remembered split.
- **Either section can be hidden on its own.** `Alt+4` keeps meaning the outline; the folder
  panel gets `Alt+6` (`Alt+5` is heading numbers). With one hidden, the other takes the column.
  With both hidden, the rail returns, carrying one icon per section in the order they stack:
  folder above, outline below. Each icon restores only its own section, so the way back is still
  where the panel was and does what it says. An undocked folder panel is not in the column and
  has no rail icon; the rail then looks exactly as it does today. A double-click on the empty
  strip restores both.
- **The header names the root**, the folder's own name in small capitals, with the full path as
  its tooltip. Its buttons are *Pop out*, the panel menu, and *Hide*.
- **The filter box** behaves like the outline's: type and the tree narrows to matching names,
  with their folders kept open so each match shows where it lives. Escape clears it.
- **The active document's row** wears the same teal bar the outline uses for the current
  heading. That is Marqora's own accent, not a theme color, so it does not change with the
  color theme.
- **Row markers** reuse states the app already tracks: open in a tab, unsaved, read-only,
  changed on disk. A file that is not open has no marker; the tree does not read files to
  decorate them.

### Undocked

```text
 main window (screen 1)                         Folder (screen 2)
┌────────────┬─────────────────────────┐       ┌──────────────────────────┐
│ ▾ OUTLINE  │                         │       │ Folder · marqora-docs ⇲ ✕│
│ [Filter  ] │   source   │  preview   │       │ [ Filter files        ]  │
│  Folder p… │                         │       │  ▾ docs                  │
│ ▌ Why now  │                         │       │ ▌   FolderPanel.md   ✎   │
│   Decisi…  │                         │       │     README.md            │
└────────────┴─────────────────────────┘       └──────────────────────────┘
                                                 ⇲ dock back into the main window
```

- **Pop out** (the header button, or *Pop Out Folder Panel* in the panel menu and `View`) moves
  the section into its own `PaletteWindow`. The outline takes the full column.
- **The window is the same panel**, not a copy: same root, same expansion, same filter text,
  same selection. It moves rather than duplicates.
- **Dock** (`⇲` in the window's caption area, or the same `View` item, now reading *Dock Folder
  Panel*) puts it back as the top section.
- **Closing the window hides the panel and remembers it was floating.** `Alt+6` brings it back
  where it was, floating, on whatever screen it was left on. Docking is a deliberate act, never a
  side effect of closing.
- **It behaves like the other palettes.** It floats above Marqora, stays out of Alt+Tab and the
  taskbar, minimizes and restores with the main window, and remembers its placement, scaled
  to the display it lands on, per the rules `PaletteWindow` already applies. If its saved screen
  is no longer connected, it opens on the main window's screen.
- **Nothing is modal.** Clicking a file in the floating window opens it in the main window and
  brings the main window's editor forward, without moving the floating window.

### Choosing the root

- **File > Open Folder…** (`Ctrl+Shift+O`, unchanged), and the landing page's **Open folder**
  button, now set the panel's root and show the panel if it was hidden. They open no tabs. The
  old meaning is retired outright: Marqora has few users and the command is rarely used, so
  there is no one-time notice, no preference for the old behavior and no prompt, only a line in
  the release notes. The command stays in the File menu, where people look for it, rather than
  only in the panel, where it could not be found while the panel is hidden.
- **Open All in Tabs**, today's behavior including its ask-first above 25 files, is on the
  right-click menu of any folder in the tree, and of the root's header. It stays because Find
  All, Replace All and Format All work on open tabs: until Find All has a folder scope, this is
  the only way to search or format a whole folder in the app. Revisit it when that scope exists.
- **Other ways to set the root:**
  - *Use this document's folder* on a tab's right-click menu.
  - Dropping a folder on the window. Today a dropped folder is not a document; it becomes a
    root.
  - `Marqora.exe <folder>` on the command line, forwarded to a running instance like a file
    argument is today, so a terminal user can type `Marqora.exe .` in a docs folder.
- **File > Close Folder** clears the root. The panel shows its empty state.
- **One root at a time.** Opening another folder replaces it. Recent folders appear in the
  root header's menu, recorded in the settings file alongside recent files, with nothing
  written anywhere in the folder itself.
- **The root is remembered across sessions** with the rest of the session, and restored on
  start. A root that no longer exists is dropped quietly, and the panel shows its empty state
  with a line saying which folder could not be found.

### Revealing the active document

- When the active tab's file is inside the root, the tree expands to it and scrolls it into
  view. This is on by default and can be switched off in the panel menu (*Reveal active
  document*), for anyone who wants the tree to stay where they left it.
- When the active document is **outside** the root, nothing is highlighted, and a single line
  at the foot of the tree says so: *This document is in another folder*, with a *Show its
  folder* link that sets the root. The panel never changes root on its own.
- An untitled tab reveals nothing.

### Opening files

- **Click opens.** A markdown file opens in a normal tab, or its existing tab is activated.
  There is one tab per path, so a file reached by the tree, a link and the Open dialog is the
  same tab.
- **Keyboard.** Arrows move, Left and Right collapse and expand, Enter opens, `F2` renames,
  `Delete` deletes, typing a letter jumps to the next name starting with it.
- **Two keys, as the outline has.** `Alt+6` shows and hides the panel; `Alt+Shift+6` goes to it
  and, pressed again, back to the editor. They are separate for the reason `Alt+4` and
  `Alt+Shift+4` are: with the panel open and the caret in the source, a toggle can only close the
  thing being reached for. The focus key shows a hidden panel (it cannot focus what is not
  there), brings a floating panel's window forward, and lands on the **Open Folder** button when
  no root is chosen. There is no `Ctrl+Shift+E` alias. Both keys are carried by the window and
  the webshell, as `Alt+4` and `Alt+5` are, or they work only when the caret is outside Monaco.
  The View menu, the cheatsheet and the Welcome page's shortcut table list them.
- **Images do not open in a tab.** A click selects; dragging one into the source pane inserts a
  relative image link at the drop point, through the same path as dropping an image from
  Explorer. Double-click shows it in the system's image viewer, under the rule local links
  already follow for launching programs.
- **Shown non-markdown files** (with *Show all files* on) are dimmed. Their right-click menu has
  *Reveal in Explorer* and *Copy path*, and nothing that opens them.

### The right-click menu

| On | Items |
|:---|:---|
| A markdown file | Open · Open to the Side *(only if a second document window ever exists; not now)* · Reveal in Explorer · Copy Path · Copy Relative Path · Rename · Delete |
| An image | Insert Link in Source · Reveal in Explorer · Copy Path · Copy Relative Path · Rename · Delete |
| A folder | New Document Here · Open All in Tabs · Set as Root · Reveal in Explorer · Copy Path · Rename · Delete |
| The root header | New Document Here · Open All in Tabs · Collapse All · Reveal Active Document · Recent Folders ▸ · Close Folder |

*Copy Relative Path* is relative to the **active document**, since that is what someone pasting a
link wants. *Insert Link in Source* writes into the active document, so it follows the read-only
rule and is grayed on a read-only tab, registered with `Writes(...)`.

### File operations

The panel can create a document, rename, and delete. It cannot move, copy, make a folder, or
rewrite links.

**New Document Here** opens an untitled tab whose first Save As starts in that folder. Nothing
is written to disk until the user saves, so it is a file operation in name only and cannot fail
half way.

**Rename** (`F2`) edits the name in place in the tree.

- The extension is shown and can be changed. Changing a markdown file to a non-markdown
  extension asks first, because the file will leave the tree unless *Show all files* is on.
- **An open file's tab follows the rename.** The tab's title changes, its path changes, and its
  unsaved text, undo history, scroll position and read-only mark all stay with it. This is the
  real cost of the decision: Marqora keys read-only marks, the saved session and the disk watch
  on the path (`docs/Architecture.md`, *What path keying costs*). All three must be moved to the
  new path, and the disk watch must recognize *we renamed it* rather than marking the tab as
  missing.
- Renaming a folder moves every open tab beneath it the same way.
- **Links are not rewritten.** A link in another document that named the old path now points at
  nothing, and the existing dead-link marking will show it in place when that document is
  opened. Rewriting links across a folder is the workspace feature this panel is bounded
  against; see [Later](#later).
- A name that already exists, or is not a legal Windows name, is refused at the text box with
  the reason, and the box stays open.
- **Say so afterwards, with Undo.** A completed rename leaves a one-line notice at the top of the
  panel: *Renamed "setup.md" to "install.md".* with an **Undo** button. It is Marqora's
  disk-reload pattern (act, say so, make it easy to take back) applied to the realistic regret,
  a rename that broke links or carried a typo. The notice stays until the next file operation
  replaces it or it is dismissed, never on a timer, which would penalize anyone who reads slowly
  or looked away. Undo renames back and open tabs follow back. If it can no longer be done
  (a file has since taken the old name), the notice says why rather than failing silently.
- **`Ctrl+Z` is not file undo.** It belongs to the editor. Giving it a second meaning while the
  tree has focus would let a rename followed by a click into the source undo a
  word of text instead, or the reverse.

**Delete** sends to the Recycle Bin and never deletes outright.

- A single confirmation names what goes: *Move "CLI.md" to the Recycle Bin?* For a folder it says
  how many files are inside. The verb is explicit, the button is neutral, and it is not the
  default, per the button standards. This is a `ContentDialog`, which may be modal; the "nothing
  is modal" rule is about secondary windows.
- **Deleting an open file closes its tab.** If the tab has unsaved changes, the confirmation says
  so and offers *Delete and discard changes*, *Cancel*, and nothing that silently loses text.
  Deliberately, this differs from what happens when something *else* deletes the file, where the
  tab stays and the buffer becomes unsaved: there, the user did not ask for it.
- **Read-only:** Delete is grayed on a file marked read-only in Marqora. That mark exists to
  protect a reference document from a stray keystroke, and `Delete` is a stray keystroke.
  Rename stays enabled, because it changes no text, and the mark follows the new path.
- If the Recycle Bin is unavailable (some network drives), the confirmation says the delete is
  permanent before it happens.
- A completed delete leaves the same notice line, *Moved "setup.md" to the Recycle Bin.*, with no
  Undo. Pulling a file back out of the Recycle Bin is awkward from a program, and the Recycle Bin
  is already a delete's undo.

### What the tree lists

- **Markdown files**, using the extensions Open Folder already recognizes, and **images**,
  dimmed.
- **Folders that lead to either.** A folder containing neither, anywhere beneath it, is not
  shown. That is what keeps a code repository's tree down to its documentation.
- **Always hidden:** `.git` and every other dot-folder, `node_modules`, `bin`, `obj`.
- **What `.gitignore` names is grayed, not hidden.** When the root is a Git repository, files
  and folders its `.gitignore` files name (the root's and any subfolder's) stay in the tree,
  dimmed, with *Ignored by .gitignore* as their tooltip. The reason is the direction
  of failure. `.gitignore`'s full rules (negation, anchoring, nested files, global excludes) are
  more than a simple reader gets right, and a reader that *hid* would hide exactly the files an
  author had un-ignored, with nothing saying why they were missing. A reader that only *grays*
  can be simple, because a mistake changes a shade and the file is still there and still opens.
  Reading the files is a local read; Marqora never writes them and never runs `git` for this.
- **Show all files** in the panel menu lifts every one of these filters except the always-hidden
  folders.
- **Sort:** folders first, then by name, compared as Explorer compares them (`Chapter 2` before
  `Chapter 10`). Sort by modified date is a panel menu option. There is no custom ordering.

### Keeping up with the disk

- The tree watches the root and updates live: a file added, removed or renamed in Explorer, by
  Git, or by `marqora-cli` appears without a refresh. A *Refresh* item exists in the panel menu
  for network folders, where change notifications are unreliable.
- **A folder is read when it is first expanded,** not before. Opening a root reads one level.
  Deciding whether a folder "leads to markdown" needs a look below it; that look runs in the
  background and stops at the first markdown file it finds, and a folder appears as soon as the
  answer is known.
- A very large root (a home folder, a drive) must stay responsive. The tree virtualizes like the
  outline's `ListView`, and the background look has a ceiling past which a folder is shown
  without having been checked.

### Empty and edge states

| State | What the panel shows |
|:---|:---|
| No root chosen | *Open a folder to browse its documents.* An **Open Folder** button, and, when a saved document is active, **Use this document's folder** |
| Root has no markdown | *No markdown files in marqora-docs.* with *Show all files* as a link |
| Root disappeared | *marqora-docs could not be found.* and the Open Folder button |
| Filter matches nothing | *No files match "xyz".* |
| No documents open, no root | Nothing: today's landing page, unchanged and centered in the whole window |
| No documents open, root chosen | The panel, with the full column (there is no outline to share it with). The landing page centers in the space beside it |

The last row is the one change to today's empty window, which hides the outline column so the
drop card is centered in the whole window. The outline has nothing to say with no document
open; a folder panel with a root has the most useful thing to say at exactly that moment, the
documents you were working in. It also keeps the landing page's own **Open folder** button
honest: it sets the root, and the root appears, rather than nothing visible happening until a
file is opened some other way. Someone who has never chosen a folder sees no change at all, and
a panel hidden with `Alt+6` stays hidden here too.

---

## How it meets the existing rules

| Rule (CLAUDE.md) | How the panel complies |
|:---|:---|
| Secondary windows derive from `PaletteWindow` | The undocked panel is a `PaletteWindow`, the sixth. It is the *kept and hidden* kind, like Find All, because its state is worth coming back to |
| Nothing is modal | Docked or floating, the panel never blocks the editor. Only Delete's confirmation is a `ContentDialog` |
| Read-only gates every writer | *Insert Link in Source* writes text and is gated and registered with `Writes(...)`. Delete is grayed for a read-only document. Rename changes no text and stays enabled |
| The preview never changes the source | The panel is not the preview. Dragging an image works only onto the **source** pane; a drop on the preview does nothing |
| Offline by default | The panel reads the local disk only. No thumbnails fetched, no indexing service, no lookups |
| Marqora's teal is not a theme color | The active-row bar is teal in every theme, matching the outline |
| Button standards | Empty-state buttons and the Delete confirmation take their tier styles; the Delete button is neutral and never the default |
| American spelling | Labels as written here; run the spelling check on whatever ships |

---

## Release notes to write when it lands

The feature's `##` section in `docs/releases/vNext.md` must say that the old Open Folder is
gone. Not before the code lands: `vNext.md` holds only what has shipped to `dev`. Draft:

> **Open Folder now shows the folder instead of opening every file.** The old *File > Open
> Folder* command, which opened each markdown file in a folder as its own tab, has been removed.
> *File > Open Folder* and `Ctrl+Shift+O` now open the folder in the new folder panel, where
> each document is one click away. To open a folder's documents as tabs, for example to search
> or format all of them, right-click the folder in the panel and choose **Open All in Tabs**.

---

## Later

Kept out of this feature on purpose, in roughly the order they would make sense:

1. **Find All over the folder.** An *Open tabs / Folder* scope. The hard part is not the search
   but the results: a row pointing at a file that is not open, and a Replace All that would
   write to files nobody is looking at. Designed on its own.
2. **Quick switcher** (`Ctrl+P`) across open tabs and the root, which this panel makes possible.
3. **Git markers on rows**, if a small Git indicator for open documents is built: the same local
   `git` call, applied to the tree.
4. **Share a folder as a Folio** from the folder's right-click menu.
5. **Move by drag, and link rewriting.** Only together: a move that leaves every link to the file
   dead is worse than no move. This is the point at which the panel becomes a workspace.
6. **Several roots.** It needs somewhere to keep the list, which is a project file in all but
   name.
7. **Preview tabs.** Rejected for now (decision 4). Worth revisiting if people open thirty tabs
   reading a docs folder and say so.

---

## Still open

Questions this exploration does not settle, for the plan to answer:

1. ~~**The rail with both sections hidden.**~~ *Settled 2026-11-16:* one icon per section, each
   restoring only its own; see [Docked in the main window](#docked-in-the-main-window). One icon
   that restored "whatever was last shown" was rejected because it depends on history the user
   cannot see.
2. ~~**The splitter's default.**~~ *Settled 2026-11-16:* even halves, remembered as a proportion,
   double-click to reset. Sizing the tree to its content was rejected because the splitter would
   move under the mouse as folders expanded; a fixed 40/60 was rejected as a guess that is wrong
   for repositories.
3. ~~**`Alt+6` for the panel, and its focus shortcut.**~~ *Settled 2026-11-16:* `Alt+6` and
   `Alt+Shift+6`, no alias; see [Opening files](#opening-files). `Alt+5` keeps heading numbers
   rather than being renumbered to sit the folder beside the outline. `Ctrl+Shift+E` was
   rejected, alone or as an alias, as outside the Alt-digit run.
4. ~~**Rename and Ctrl+Z.**~~ *Settled 2026-11-16:* a notice with **Undo** after a rename, kept
   until replaced or dismissed; delete gets the notice without Undo; see
   [File operations](#file-operations). `Ctrl+Z` in the tree was rejected because it belongs to
   the editor; no undo at all was rejected because it relies on remembering the overwritten name.
5. ~~**The empty window.**~~ *Settled 2026-11-16:* the panel shows on the empty window only when
   a root is chosen and it has not been hidden; see [Empty and edge states](#empty-and-edge-states).
   Keeping the landing page as it is was rejected because Open folder there would then show
   nothing; a flat file list inside the landing page was rejected as a second copy of the tree.
6. ~~**The `.gitignore` reader.**~~ *Settled 2026-11-16, revising decision 6:* ignored files are
   grayed, not hidden, so a simple reader is enough; see [What the tree lists](#what-the-tree-lists).
   Hiding them exactly by asking `git` was rejected because the tree would then depend on git
   being installed; ignoring `.gitignore` altogether, and an opt-in hide switch, were set
   aside. Still to decide in the plan: whether grayed-because-ignored needs to look different
   from grayed-because-image, or whether the tooltip is enough.
7. ~~**Open Folder's change of meaning.**~~ *Settled 2026-11-16:* the old meaning is retired with
   only a release-note line, because there are few users and the command is rarely used; Open
   All in Tabs stays on the folder's right-click menu; see [Choosing the root](#choosing-the-root).
   A one-time notice, a preference for the old behavior, a prompt on every use, and moving the
   command out of the File menu were all set aside.
