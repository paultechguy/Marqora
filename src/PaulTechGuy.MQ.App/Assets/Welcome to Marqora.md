# Welcome to Marqora

**A modern Markdown workspace for Windows 11 — a real editor, a live preview, and
everything in between.**

Marqora pairs a full code editor with a rendered preview that keeps pace as you type.
Diagrams draw themselves, equations typeset themselves, links check themselves, and the whole
thing runs on your machine: no account, no sign-in, no network call, no telemetry.

> Write in Markdown. See the finished page. Send it anywhere.

---

## Try it right now

- [x] Open this document — done
- [ ] Press `Alt+2` for the split view, then `Alt+3` to come back to the preview
- [ ] Press `Alt+4` to list this page's headings beside it, and arrow down them
- [ ] Press `Ctrl+F1` for the Markdown cheatsheet, and leave it beside the editor
- [ ] Double-click the diagram further down this page
- [ ] Press `Ctrl+Shift+F` and search every open document at once
- [ ] Press `Ctrl+Shift+H` and replace across all of them, in one step
- [ ] Type a word wrong on purpose, watch the scrollbar, then press `Ctrl+.` on it

> [!NOTE]
> Nothing here is a demo mode. It is the app, and this is an ordinary Markdown file on your
> disk — edit it, save it, or write over it entirely.

---

## The workspace

```mermaid
flowchart LR
    A[Your Markdown file] --> B[Source pane<br/>Monaco editor]
    B --> C{Live render}
    C --> D[Preview pane]
    C --> E[Diagrams · Math · Highlighting]
    C --> F[Problem checks]
    D --> G[PDF · Word · HTML · Rich text]

    classDef input fill:#2f7c85,stroke:#1f5a61,color:#fff
    classDef engine fill:#6f42c1,stroke:#553098,color:#fff
    classDef out fill:#1a7f37,stroke:#125926,color:#fff
    classDef ship fill:#9a6700,stroke:#6f4a00,color:#fff

    class A,B input
    class C engine
    class D,E,F out
    class G ship
```

Source on the left, the finished page on the right, and a render between them fast enough
that the preview is simply what your document looks like. The panes scroll together — mapped
through source line numbers rather than scroll percentage, so a tall diagram never throws the
alignment off.

| View         | Shortcut | For                   |
| ------------ | -------- | --------------------- |
| Source only  | `Alt+1`  | Writing at speed      |
| Split        | `Alt+2`  | Writing and watching  |
| Preview only | `Alt+3`  | Reading and reviewing |

---

## What makes it worth your time

**A real editor, not a text box.** The source pane is Monaco — the editor from Visual Studio
Code — with find and replace, multiple cursors, undo that goes as far back as you need, and
syntax highlighting inside fenced code blocks.

**Documents in tabs.** Every tab keeps its own undo history, cursor and scroll position, so
switching costs nothing and loses nothing. `Ctrl+1`–`Ctrl+8` jump straight to a tab,
`Ctrl+Tab` walks along them, and dragging reorders them.

**Your session comes back.** Close Marqora with a dozen documents open and they are all there
next time, with the same tab in front.

**Markdown without the punctuation.** The Format menu and the toolbar beneath it apply bold,
italic, links, lists, quotes, headings, tables and code blocks — and everything toggles, so
the button that switched something on switches it off again. The bar is live: it lights up
for whatever the cursor is inside, and the heading control reads `H2` when you are in one.

**One tidy-up command.** `Shift+Alt+F` formats the document against sixteen rules you choose
from — spacing, list markers, ordered numbering, table alignment, line endings and the rest.
Three promises hold whatever you switch on: it never changes what the document renders to, it
never touches the inside of a code block or your front matter, and `Ctrl+Z` takes the whole
reformat back in one step.

**Problems, underlined.** A dead link renders exactly like a live one, so Marqora checks them
for you — missing files, missing images, and anchors that point at no heading — along with the
small style slips the formatter would fix.

**And ticked in the scrollbar.** Every mark also leaves a tick in the source pane's scrollbar,
so a long document can be scanned for problems without being scrolled through. Click a tick to
jump to it. The color says what kind of mark it is:

| Tick   | What it means                                       | Switch it off with           |
| ------ | --------------------------------------------------- | ---------------------------- |
| Red    | A misspelled or repeated word                       | `View > Spell Check`         |
| Amber  | A broken link, a missing image or a dead anchor     | `View > Show Problems`       |
| Gray   | A picture with no alt text — incomplete, not broken | `View > Show Problems`       |
| Violet | A picture that will not appear in the preview       | `View > Show Blocked Images` |

Those four are Marqora's own. The editor adds ticks of its own too — for find matches while
`Ctrl+F` is open, and for the other places the word at the cursor appears.

**Find All.** `Ctrl+Shift+F` answers "where does this appear?" in one window: every match at
once, across every open document, grouped by the file it came from. Select a row and the
source pane switches tabs, scrolls to the line and selects the text. `F3` and `Shift+F3` walk
the matches without leaving the window.

**Replace All.** `Ctrl+Shift+H` is the same window with a *Replace with* box unfolded — or use
the chevron beside the search box to fold it out yourself. Fill both boxes and press **Replace
All**; it finds the matches and asks before changing anything, naming how many and where. In
regular-expression mode the replacement can refer back to what matched with `$1`, `${name}` or
`$&`, and the icon beside the box lists them. Every document it touches is one `Ctrl+Z`.

**The outline.** `Alt+4` lists this document's headings down the side, indented by level, and
highlights whichever section you are reading — the caret's in the source pane, the top of the
page in preview. Click one, or walk them with the arrow keys, and both panes move together.
Type in the box at the top to narrow a long document down to the headings you meant.

`Alt+4` shows and hides it. Once it is open, `Alt+Shift+4` is the way to it and back again,
and `Escape` returns you to the text without closing it. This page is long enough to be worth
trying it on.

---

## Diagrams, math and code

Fenced ` ```mermaid ` blocks become diagrams in the preview. **Double-click one** and it opens
in a window of its own that you can resize, park on a second monitor, and leave open while you
keep editing — it redraws as the diagram changes.

```mermaid
sequenceDiagram
    participant You
    participant Marqora
    participant Anyone

    rect rgba(63, 143, 152, 0.15)
        You->>Marqora: Type a paragraph
        Marqora-->>You: Rendered, instantly
    end

    rect rgba(154, 103, 0, 0.15)
        You->>Marqora: Export to PDF
        Marqora-->>Anyone: A document that stands on its own
    end
```

Math is typeset with KaTeX, inline as $a^2 + b^2 = c^2$ or as a display block:

$$
\sigma = \sqrt{\frac{1}{N}\sum_{i=1}^{N}(x_i - \mu)^2}
$$

Code is highlighted in both panes, in whichever language you name on the fence:

```csharp
public static string Greet(string name) => $"Hello, {name}.";
```

The rest of it is here too — tables, footnotes, task lists, definition lists, YAML front
matter, auto-links, emoji :rocket:, ==highlighting==, super^script^ and sub~script~.[^1]

[^1]: All of it rendered locally. Marqora never sends your document anywhere.

---

## Taking it elsewhere

| Export                             | What you get                                                                      |
| ---------------------------------- | --------------------------------------------------------------------------------- |
| `Ctrl+Shift+C` — Copy as Rich Text | The formatted page on the clipboard, ready for Word, Outlook or Confluence        |
| `Tools > Export to PDF...`         | A printed copy, with paper size, orientation and margins of your choosing         |
| `Tools > Export to HTML...`        | One self-contained `.html` file — styles inlined, images embedded, fonts included |
| `Tools > Export to Word...`        | A real `.docx` — Word's own heading styles, numbering, tables and footnotes       |
| `Tools > Share as Folio...`        | Every open document *and* the images they use, gathered into one thing to send    |

Exports come from the preview you are looking at rather than from a fresh render, so diagrams
are already drawn, math is already typeset and code is already colored. What you see is what
leaves the building.

Word is the one that works differently, because it has to. A `.docx` is not a web page, so the
document is read again and written into Word's own constructs: headings that the navigation
pane can find, lists Word counts itself, tables it can resize, equations you can click into and
edit. Only the three things a browser alone can make — the diagrams, the typeset math and the
colored code — are taken from the preview. Which means a Word export never refuses: if the
preview has not caught up, you get the document without its colors rather than no document.

> [!TIP]
> `Ctrl+Shift+C` copies the formatted page rather than the Markdown — the quickest way into an
> email or a Word document, with the diagrams already drawn and the math already typeset.

A **Folio** is the one that takes more than a single document. The others hand you the page in
front of you; a Folio takes the whole set, collects every picture they point at — including the
ones living somewhere else on your disk — and fixes the paths so none of it breaks on the way.
Sent as one file, it opens in any browser without Marqora, and dropping that same file back on
this window unpacks it into the documents it was made from.

> [!IMPORTANT]
> A Folio is the only export that takes more than the document in front of you — and the only
> one you can drop back on this window to get those documents back.

---

## Yours, and only yours

Marqora is local software. It opens files from your disk, renders them in its own process, and
writes them back where you put them. There is nothing to sign in to and nothing to opt out of.

| Where                                                    | What                                    |
| -------------------------------------------------------- | --------------------------------------- |
| `%LOCALAPPDATA%\PaulTechGuy\Marqora\settings.json`       | Preferences and window placement        |
| `%LOCALAPPDATA%\PaulTechGuy\Marqora\recent-files.json`   | Recent and pinned files                 |
| `%LOCALAPPDATA%\PaulTechGuy\Marqora\user-dictionary.txt` | Words you have taught the spell checker |
| `%LOCALAPPDATA%\PaulTechGuy\Marqora\snippets\`           | Your own snippets, one per file         |
| `%LOCALAPPDATA%\PaulTechGuy\Marqora\logs\`               | Rolling logs, kept 14 days              |

Deleting any of it is safe. Marqora writes it again.

Spell checking is part of that promise rather than an exception to it. The words come from the
dictionary Windows already has on this machine, so nothing you type is sent anywhere to be
checked and there is nothing to download. Editors that lean on a web service for this are
common; Marqora is not one of them.

Checking for updates is the same kind of thing. Marqora never asks GitHub — or anyone else —
whether a newer version exists, which is precisely why it cannot tell you. What it keeps
instead is a clock: every thirty days a line appears in the status bar suggesting you look,
and clicking it hands the releases page to your browser. Nothing is fetched and nothing is
sent. The reminder waits for a pause in your typing, and appearing is what resets it, so it
will not come back sooner because you ignored it. Change the interval on the Advanced page of
Preferences, or set it to zero and never see it. `Help > Check for Updates` opens the same
page whenever you want it.

---

## Worth knowing

- **One window.** Opening a document while Marqora is running adds a tab to the window you
  already have, rather than starting a second copy.
- **`Ctrl+Shift+O` opens a whole folder**, one tab per Markdown file — and deliberately not its
  subfolders, so pointing it at a repository does not produce hundreds of tabs.
- **Files are watched.** If a document changes on disk and you have no unsaved edits in it, the
  tab quietly catches up. If you do have edits, Marqora asks first.
- **Snippets are just files.** Drop a `.md` file into your snippets folder and it appears on the
  toolbar's **Insert** menu, under *Your snippets*.
- **Everything is on the keyboard.** `Alt` puts you on the menu bar, and `Help > Keyboard
  Shortcuts...` lists every shortcut in the app with a button that copies the lot.

---

## The shortcuts worth memorizing

| Command                  | Keys                      | Command                        | Keys                |
| ------------------------ | ------------------------- | ------------------------------ | ------------------- |
| Open                     | `Ctrl+O`                  | Bold / italic                  | `Ctrl+B` / `Ctrl+I` |
| Open folder              | `Ctrl+Shift+O`            | Link                           | `Ctrl+K`            |
| Save / Save all          | `Ctrl+S` / `Ctrl+Shift+S` | Format document                | `Shift+Alt+F`       |
| New tab / close tab      | `Ctrl+N` / `Ctrl+W`       | Find All / Replace All         | `Ctrl+Shift+F` / `Ctrl+Shift+H` |
| Source / split / preview | `Alt+1` `Alt+2` `Alt+3`   | Cheatsheet                     | `Ctrl+F1`           |
| Show / hide the outline  | `Alt+4`                   | Go to the outline, and back    | `Alt+Shift+4`       |
| Spell check on / off     | `F7`                      | Correct the word at the cursor | `Ctrl+.`            |
| Zoom the active pane     | `Ctrl` `+` `-` `0`        | Word wrap                      | `Alt+Z`             |

Those are the keys; the syntax is one more. `Ctrl+F1` opens the Markdown cheatsheet — every
construct on this page and a few that are not, each one's markup sitting beside its result, with
the diagrams and the equations really drawn rather than pictured. It is a window rather than a
dialog, so it can stay open beside the editor while you type, and it comes back to the place you
left it. `Ctrl+F1` closes it again.

---

## About this document

This page opens by itself the first time you run a new release of Marqora, so you always know
what the version in front of you can do. It is a copy kept in your own data folder, which means
you can scribble on it, save it, or close it and never think about it again — the next release
brings a fresh one.

**Want it back?** `Help > Welcome to Marqora` opens it again, exactly as you left it. To undo
your scribbles as well, hold `Shift` while starting Marqora: that replaces the copy with the
one that shipped, however long ago this version introduced itself.

`Help > About Marqora` shows the version you are running, along with where everything lives and
a **Copy details** button for when something needs reporting.

**Now open something of your own.** `Ctrl+O`, or drag a file onto the window.
