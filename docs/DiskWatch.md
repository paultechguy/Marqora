# Watching files on disk

What Marqora does when the file behind a tab is rewritten, or disappears, while it is open.

Detection was already here before this feature: `FileWatcher` watched each open document and
raised one event per change. What was missing was everything above it — a document that could
hold the state, a tab that could show it, and a prompt that said *which* file and what was
about to happen to it. This document records the design and, as elsewhere in `docs/`, the
reasons for the parts that are not obvious.

---

## The four decisions

| Question | Answer |
|----------|--------|
| How is a change surfaced? | An inline `InfoBar` above the editor. Never a modal. |
| Four files change at once? | Every affected tab is marked; the banner shows the one you are looking at. |
| The file was deleted? | The tab stays, and the buffer becomes unsaved so `Ctrl+S` writes it back. |
| The buffer is clean? | Reload without asking, say so afterwards, and let `Ctrl+Z` take it back. |

The last two are the ones that carry weight. Together they mean **no external event can lose
a keystroke**: a delete cannot, because the text is still in the buffer and now counts as
unsaved; an auto-reload cannot, because it goes onto the undo stack.

---

## Three states, one per document

`MarkdownDocument.External` is the single value everything downstream reads — the tab marker,
the banner, whether the close prompt appears, whether `Ctrl+S` is offered. Keeping it in one
place is what stops the tab and the banner from disagreeing.

| State | Meaning |
|-------|---------|
| `InSync` | The buffer matches what was last read from or written to disk. |
| `Changed` | The file was rewritten with content that genuinely differs, and the user has not resolved it. |
| `Missing` | The file is gone from `Path` — deleted, moved, or its folder removed. |

### Transitions

| From | Trigger | To |
|------|---------|-----|
| `InSync` | rewritten, differs, buffer dirty or auto-reload off | `Changed` |
| `InSync` | rewritten, differs, buffer clean and auto-reload on | `InSync`, reloaded in place |
| `InSync` | touched, or rewritten with identical bytes | `InSync`, swallowed |
| `InSync` | deleted, moved, or the folder removed | `Missing` |
| `Changed` | Reload, or Keep Mine, or a save over the file | `InSync` |
| `Changed` | banner dismissed with ✕ | `Changed`, marker stays |
| `Changed` | deleted while still pending | `Missing` |
| `Missing` | file reappears | `Changed`, or `InSync` if it matches |
| `Missing` | saved, re-creating the file | `InSync` |

`Missing` → `Changed` needs no code of its own. `FileSystemWatcher` watches the *folder*
filtered to a name, so it survives the file's deletion and fires `Created` when the file comes
back; `FileWatcher.Notify` already re-checks existence after the quiet period. A
`git stash pop` just rewords the banner under the reader.

---

## Two decisions that make or break it

Everything else here is scaffolding. These two are the difference between a feature you leave
switched on and one you turn off within a week.

### Compare the content, not just the event

`FileSystemWatcher` fires on `LastWrite | FileName | Size`. A `touch`, an antivirus scan, a
build tool restamping a file, a save that writes identical bytes — all of them fire. Prompting
for those is exactly what makes file-watching irritating.

So before anything is announced, the file is read and compared against `SavedText`. Identical
means the stamp is updated and nothing is said. Markdown files are kilobytes and the read is
already off the UI thread, so this costs nothing worth measuring. Files above
`MaxCompareBytes` skip the comparison and trust the stamp, so a pathological file cannot stall
the watcher thread.

The same stamp covers the gap in the older self-write suppression. That was a two-second
window after Marqora's own write, which a slow network share can outrun; comparing the
recorded stamp against what is on disk catches the case the window misses.

### Reload through `ReplaceTextAsync`, not `SetTabTextAsync`

`setTabText` calls `model.setValue`, which resets the Monaco model: undo history gone, cursor
back at line 1. Auto-reloading a document you were reading at line 400 threw you to the top of
it.

`replaceText` already existed for the formatter, which needs `Ctrl+Z` to take a reformat back
in one step. It pushes a single edit operation over the full model range and restores the
caret and scroll position afterwards — and it guards on `isActive`, so it is equally correct
for a background tab. Routing reload through it means the cursor holds, the scroll holds, and
**`Ctrl+Z` undoes a reload.**

That last property is what makes silent auto-reload defensible in the first place. Without it,
the honest design would have to ask before every reload.

---

## Deleted files: one line does the work

A vanished file makes the buffer unsaved:

```csharp
public bool IsDirty => External == ExternalState.Missing
    || !string.Equals(Text, SavedText, StringComparison.Ordinal);
```

Every existing consumer then behaves correctly without being touched: the `●` in the tab, the
close confirmation, `CanSave`, and session persistence. `Ctrl+S` re-creates the file at the old
path, because `SaveAsync` already writes to `document.Path` and `File.WriteAllTextAsync`
creates what is not there.

The tempting alternative — writing a sentinel into `SavedText` — is wrong. `IsDirty` is a
comparison precisely so that dirty state cannot drift out of sync with the text, and
corrupting one side of that comparison breaks reload and the close prompt in ways that surface
much later and read as unrelated bugs.

If the folder is gone too, the write throws and the app falls back to Save As with a message
that says so, rather than reporting a failure the user cannot act on.

---

## Where the prompt lives

A WinUI `InfoBar` in its own row of `RootGrid`, between the format bar and the content. It
animates open and closed, follows the theme, and announces itself to Narrator — none of which
a hand-rolled `Border` gets for free. It never takes the keyboard, so it cannot interrupt
typing, and only ever one is on screen.

| Part | Modified | Deleted |
|------|----------|---------|
| Severity | Warning | Error |
| Title | *notes.md changed on disk* | *notes.md was deleted or moved* |
| Second line | The full path. This is the line that answers *which file*. | |
| Detail | *You have unsaved edits…* — omitted when the buffer is clean | *Your text is still here and now counts as unsaved…* |
| Primary | Reload | Save Now |
| Secondary | Keep Mine · Save Mine As… · Reload All | Save As… |

**Keep Mine and ✕ are not the same thing.** Keep Mine resolves the change: the user has
decided the buffer is the truth, the marker clears, and the state returns to `InSync`. ✕ only
defers: the marker stays and the banner comes back the next time that tab is activated. That
distinction is why dismissing the banner can never quietly lose a change.

`Reload All` offers two items rather than one, because merging them would lie about what is
being discarded:

- **Reload N unmodified** — the safe sweep after a branch switch, and what you want nearly
  every time.
- **Reload all N, discarding my edits** — the deliberate one.

### Which banner is showing

The one for the active tab, if it has anything pending. Switching tabs re-evaluates. Four
files changing at once produces four tab markers and one banner; the `1 of 4` count says the
rest are waiting. When background tabs are pending and the active one is not, the count moves
to the status bar — otherwise a change to a tab you never revisit would be invisible.

### Saying that a reload happened

Reloading a clean buffer without asking is the right thing to do, and it is also the one
external event with nothing on screen to show for it. Step away for lunch, come back, and the
document in front of you is not the one you left.

So it is announced — but only at the two moments the user can act on it: when the reload lands
on the document they are looking at, and when they arrive at a document it landed on earlier.
`DocumentWorkspace` records the fact on the document as `AutoReloadedUtc`; `MainViewModel`
holds the ids that have not been announced yet in `_unannouncedReloads` and takes one off the
list the moment it speaks, so a reload is announced exactly once however the user gets there.

The message goes in the left of the status bar wearing an amber pill and a refresh glyph,
because it is shown once and has to be noticed. Amber rather than the app accent: the accent
is what Marqora looks like when everything is fine, and this is reporting something that
happened to the user rather than something they did. After eight seconds the pill goes and the text
stays, and that clock only runs while the window has focus — a message that arrived while
Marqora was behind another window would otherwise spend its eight seconds shouting at an empty
desk. Anything else that writes `StatusText` takes the pill down with it, which is enforced in
`OnStatusTextChanged` rather than at the forty-odd sites that write there.

Three things deliberately did **not** happen:

- **No new `ExternalState` value.** `HasExternalChange` is `External != InSync` and drives
  `_pendingExternal` and the banner, so a fourth value would have every silently reloaded
  document demanding a decision it has none to offer.
- **No fourth tab marker.** The strip charges the widest marker's width to every tab, and the
  slot is a precedence chain the reload would have to be ranked into. The tab tooltip carries
  "Reloaded from disk at …" instead — it is the lasting record once the message has faded.
- **No use of the banner.** That is the decision surface. This has no decision in it.

### Tab markers

| Marker | Meaning |
|--------|---------|
| `⟳` | changed on disk |
| `!` | missing from disk |
| `●` | unsaved edits |

There is deliberately no marker for a document that was reloaded without asking — see above.

External state outranks the dirty dot, because a missing file is already dirty under the rule
above and the marker says more. `TabTitleFitter.Shorten` takes the middle out of a name and
keeps both ends, so a one-glyph prefix survives truncation — the pre-existing `●` prefix
already proved that.

`!` rather than `⚠` deliberately: several Windows font stacks render U+26A0 as a color emoji,
which sits at the wrong size and weight next to tab text and cannot be recolored.

---

## Watcher errors are no longer silent

`FileSystemWatcher.Error` used to be logged and otherwise ignored, so deleting a document's
containing folder left the tab watching nothing with no sign of it.

It now re-checks the file. Gone means the document is marked `Missing` and the watch stops.
Still present means the failure was most likely an internal buffer overflow — events were
missed — so the watch is re-armed and a change is reported. The content comparison makes that
second path harmless: if nothing actually changed, nothing is shown.

---

## Threading

The watcher's debounce timer fires on a thread-pool thread, and the file read and comparison
run there. Everything that touches the UI goes through `IUiDispatcher.Post`, as it did before.
Workspace mutation still happens on the watcher thread for the reload path, which is
pre-existing behaviour and unchanged here.

---

## The setting

`AppSettings.ReloadOnExternalChange` has existed since before this feature, defaulted to on,
and appeared in no menu — a setting nobody could reach. It is now **View ▸ Reload Files
Changed on Disk**.

Off means every external change waits for you, clean buffer or not. That is the answer for
anyone who finds a document moving under them unsettling, whatever the undo stack can do.

---

## Deliberately not built

**A diff view.** A *Compare* button was in the first sketch. Marqora has no diff and building
one is its own feature — an algorithm, a two-column render mode, gutter markers. *Save Mine
As…* stands in for it: it writes the buffer to a sibling `name.local.md` and opens it in a new
tab, so both versions are on screen and can be compared in whatever tool the user already has.
One method, no new UI.

**Following a rename.** `FileSystemWatcher.Renamed` does hand over the new name, but only for
a rename inside the watched folder, and a "rename" is very often another editor's
save-by-replace. Guessing wrong re-points a tab at a file the user never asked for, and they
find out when they save over it.

---

## What to test

`DocumentWorkspace` takes an `IFileWatcherFactory`, so the whole detection and decision matrix
is exercisable with a fake watcher — no disk, no UI thread.

1. A save from Marqora on a slow share, past the suppression window — the stamp still
   suppresses it.
2. An external write of identical bytes — no marker, no banner, stamp advances.
3. Clean buffer, auto-reload on — reloads, the caret holds, `Ctrl+Z` restores the old text.
4. Dirty buffer — marker and banner; Keep Mine leaves the buffer alone and clears the marker.
5. Four files at once — four markers, one banner, count reads `1 of 4`.
6. *Reload N unmodified* leaves the dirty one alone and says so.
7. File deleted — the tab keeps its text, shows `!`, and `Ctrl+S` re-creates the file.
8. File deleted and the folder with it — the save falls back to Save As.
9. File deleted then re-created differently — `!` becomes `⟳`, the banner rewords itself.
10. Tab closed with a change pending — dropped from the pending set, banner and count
    re-evaluate.
11. Untitled document — no watcher, never affected; Save As starts one and stamps it.
12. Reload while the writing process still holds the file — the existing
    `FileShare.ReadWrite | Delete` read should succeed.
13. A case-only rename on Windows — paths compare `OrdinalIgnoreCase`, so the tab keeps its
    old casing. Cosmetic, but worth knowing about.
