<div align="center">

<img src="src/PaulTechGuy.MQ.App/Assets/MarqoraLogo.png" alt="Marqora logo" width="144" height="144">

<h1 style="margin-top: 0.2em;">Marqora</h1>

<p><strong>A Markdown viewer and editor for Windows 11, built on .NET 10 and WinUI 3.</strong></p>

<p>
Source and preview live side by side with synchronized scrolling, mermaid diagrams render<br>
inline, and everything runs locally: no network calls, no telemetry, no account.
</p>

</div>

---

## Download

**[Get the latest release](https://github.com/paultechguy/Marqora/releases/latest)** — one
zip, around 84 MB. Extract it into a folder of its own and double-click `Install.cmd`.

Everything is per-user: no administrator, no UAC prompt, nothing written outside `HKCU` and
your own profile. The .NET runtime and the Windows App SDK travel inside the zip, so the
machine needs neither, and Marqora makes no network calls at runtime. It is not code-signed,
so Windows warns the first time; the release page says what you will see and why.

---

## Getting started

**Requirements**

- Windows 10 1809 (17763) or later; Windows 11 recommended
- .NET 10 SDK
- WebView2 Runtime (pre-installed on Windows 11, and on Windows 10 via Windows Update)

**First build**

```powershell
git clone <this repository>
cd PaulTechGuy.Marqora

# Third-party web assets are not in source control. Restore them once.
pwsh .\build\Get-WebAssets.ps1

dotnet build PaulTechGuy.MQ.slnx
```

The build fails with a clear message if the web assets are missing, so this step is hard to
forget twice.

**Run**

```powershell
dotnet run --project src\PaulTechGuy.MQ.App
# or open a document directly
.\src\PaulTechGuy.MQ.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Marqora.exe README.md
```

**Publish a copy you can move anywhere**

```powershell
dotnet publish src\PaulTechGuy.MQ.App -c Release
```

Release builds are self-contained and unpackaged: the output folder holds the .NET runtime,
the Windows App SDK and the web assets, so it can be copied to another machine as-is. No
MSIX, no signing certificate, no installer.

The output is about 178 MB. Three things Marqora cannot reach at runtime are deliberately
left out — the Windows App SDK's machine-learning components, WinUI's non-English resource
files, and Monaco's language services for TypeScript, CSS, HTML and JSON. That is roughly
68 MB. See `docs/Architecture.md` for how each is excluded and what not to trim further.

**Build an installer for someone else's machine**

```powershell
pwsh .\build\New-Release.ps1

# gate the release on a green test run
pwsh .\build\New-Release.ps1 -Test
```

This is the one command that produces something shippable. It restores the web assets if
they are missing, publishes, stages the app next to the installer scripts, and writes
`build\artifacts\Marqora-<version>-win-x64.zip` with a `.sha256` beside it. The zip is
around 84 MB and has no wrapper folder — `README.txt`, `Install.cmd`, `Uninstall.cmd`,
`install\` and `app\` sit at its root, so it extracts into a folder of the recipient's
choosing rather than one of its own making.

The person on the other end extracts it and double-clicks `Install.cmd`. That installs to
`%LOCALAPPDATA%\Programs\Marqora`, adds a Start menu and desktop shortcut, registers the
markdown file types, and puts a real entry in **Settings > Apps** whose Uninstall button
works. Everything is per-user: no elevation, no UAC prompt, nothing written outside `HKCU`
and the user's own profile. Removing it is that Uninstall button, or `Uninstall.cmd` from
the extracted folder, and either way settings and snippets under
`%LOCALAPPDATA%\PaulTechGuy\Marqora` are kept unless `-RemoveUserData` is passed.

There is no code-signing certificate, so the installer strips the mark of the web from the
installed copy. Without that, every launch of an unsigned executable that came out of a
downloaded zip meets the full-screen SmartScreen panel; with it, the download is vetted
once and the app then starts normally. The scripts live in `build\installer\` and are
Windows PowerShell 5.1 compatible on purpose — pwsh 7 is not on a stock Windows install,
and needing to install a shell before you can install the app defeats the point.

`docs/Installer.md` records the design: what each script does, the constraints that are
easy to break by accident, and what was deliberately left out.

**Associate markdown files with Marqora**

The installer above does this for you. Doing it by hand is for working in the source tree:

```powershell
# after building or copying the app somewhere permanent
pwsh .\build\Register-FileAssociation.ps1 -ExePath 'C:\Program Files\Marqora\Marqora.exe'

# with no arguments it registers this repository's Release build
pwsh .\build\Register-FileAssociation.ps1

# and to undo it
pwsh .\build\Register-FileAssociation.ps1 -Unregister
```

This writes a ProgId and an Open With entry under `HKCU`, so no elevation is needed and
nothing outside the current user changes. It also sets `MultiSelectModel` on the open verb,
which is what lets Explorer hand a whole selection to one process instead of starting one
process per file. Run it once per user; an installer should run it after copying the files,
and pass `-Quiet`.

Windows does not allow an application to make itself the default handler — that choice is
sealed with a per-user hash only the Settings UI can produce. The script makes Marqora
available and keeps its command line correct; choosing it is a one-time manual step:
right-click a `.md` file, **Open with**, **Choose another app**, **Always**.

---

## Features

Everything below ships in the box, works offline, and is driven from the keyboard if you
would rather not reach for the mouse.

| | Feature | What you get |
|---|---------|--------------|
| 🔒 | **Yours alone** | No network calls, no telemetry, no account, no sign-in. Your documents never leave the machine |
| 📑 | **Tabbed workspace** | Every document in its own tab, each with its own undo history, cursor and scroll position. Open a whole folder at once, drag tabs to reorder, and pick up exactly where you left off next launch |
| ⚡ | **Live side-by-side preview** | Source and rendered output together, scrolling in lockstep — mapped through line numbers, so tall diagrams never throw the alignment off |
| ✍️ | **Monaco editing** | The editor from VS Code: real find and replace, go-to-line, multi-level undo, line numbers, word wrap and per-pane zoom |
| 🎨 | **A formatting bar that pays attention** | Bold, lists, headings and the rest, one click away — and the buttons light up for whatever the caret is sitting inside |
| 🔍 | **Find All** | Every match in one window, across one tab or all of them, grouped by document. Walk the list with the arrow keys and the editor follows along |
| 🧭 | **Outline panel** | Every heading beside the document, highlighting whichever section you are reading. `Alt+4` shows it, `Alt+Shift+4` puts the keyboard in it, and the arrow keys walk the document from there |
| 🧹 | **One-key document formatting** | Sixteen independently switchable tidy-up rules, `Shift+Alt+F` to run them, `Ctrl+Z` to take the whole thing back. Never changes a single rendered word |
| 📊 | **Mermaid 11 diagrams** | Flowcharts, sequence, class, state and the rest, rendered inline, re-themed with the app and cached so typing stays fast |
| ∑ | **KaTeX math** | Inline and display math, laid out properly, exported the same way it looks |
| 🌈 | **Rich markdown** | Tables, footnotes, task lists, definition lists, YAML front matter, auto-links, emoji, and syntax-highlighted code in *both* panes |
| 🩺 | **Document problems** | Dead links, missing images and broken anchors underlined as you write — the preview renders a broken link exactly like a working one, so nothing else would tell you |
| 🔤 | **Spell check** | Misspellings underlined as you type, corrections on `Ctrl+.` or a right-click, and a dictionary of your own that lives in a plain text file you can share. Windows' own words, so nothing is sent anywhere and nothing needs downloading |
| 📤 | **Exports worth sending** | Self-contained HTML with fonts and images embedded, print-ready PDF with full page setup, and rich text on the clipboard for Word, Outlook or Confluence |
| 🧩 | **Snippets and diagram starters** | A catalogue of ready-made blocks on the Insert menu, plus your own snippet files alongside them |
| 📖 | **Cheatsheet at your elbow** | `Ctrl+F1` opens a live markdown reference — real diagrams, real math — in a window you can leave open beside the editor |
| 🌗 | **Light, dark or system** | Mica, an extended title bar and a theme that tracks Windows as it changes, diagrams included |
| 🪟 | **One window, well behaved** | Single-instance by default, so "Open with" adds a tab instead of another copy. Drag files or folders straight onto it |
| 👀 | **Files stay in sync** | Every open document is watched; change one outside the app and the tab reloads, or asks first if you have unsaved work |
| ⌨️ | **Keyboard all the way down** | `Alt` drives the menu bar, every command has a shortcut, and `Help > Keyboard Shortcuts...` lists the lot with a button to copy them |
| 🧳 | **Preferences that travel** | Every setting on six pages, applied as you change them and undone by Cancel — and exportable to a file you can import on another machine, whichever version of Marqora is on it |

The rest of this section covers each of these in detail.

**Opening documents**

- File menu, `Ctrl+O`, or drag files onto the window — each opens in its own tab
- **`File > Open Folder...` (`Ctrl+Shift+O`)** opens every markdown file in a folder, one per
  tab, sorted by name and leaving the first one active. Dropping a folder does the same
- Dropping onto the preview works too, by a different route (see Architecture)
- Recent files list with pinning, shown both in `File > Open Recent` and on the start screen.
  **Clear all** on the start screen — and `File > Open Recent > Clear Recent Files...` — empties
  it after confirming, offering to spare pinned entries when there are any
- Command line: `Marqora.exe path\to\file.md ...`, so "Open with" works
- Supported extensions: `.md`, `.markdown`, `.mdown`, `.mkd`, `.mdx`, `.txt`
- Opening a file that is already open switches to its tab rather than duplicating it
- **One window.** Marqora is single-instance: opening a document while it is already running
  adds a tab to the window you have, and brings it forward, rather than starting a second
  copy. `Marqora.exe --new-instance` opts out when you do want two

Opening a folder is deliberately **not recursive** — subfolders are ignored, so pointing it
at a repository does not produce hundreds of tabs. It also skips `.txt`, which is fine to
open by name but would sweep up unrelated files from a folder. Past 25 files it asks first.

**Tabs**

Documents open as tabs in the title bar. Each tab keeps its own undo history, cursor and
scroll position, so switching between them is instant and loses nothing.

| Action | How |
|--------|-----|
| New empty document | `Ctrl+N` or `Ctrl+T`, or the `+` button |
| The tab's own menu | **Right-click a tab** |
| Close a tab | The tab's `×`, **middle-click** it, or `Ctrl+W` |
| Close all | `Ctrl+Shift+W` |
| Close others | Right-click the tab, or `File > Close Other Tabs` |
| Reorder | Drag a tab along the strip |
| Select tab 1–8 | `Ctrl+1` … `Ctrl+8` |
| Select last tab | `Ctrl+9` |
| Next / previous | `Ctrl+Tab` / `Ctrl+Shift+Tab` |

A tab with unsaved changes shows a dot before its name and prompts before closing. New
documents are named `Untitled 1`, `Untitled 2`, and so on until the first save.

Right-clicking a tab selects it and offers what the File menu offers for that one document —
**Save**, **Save As**, **Reload from Disk**, the three **Close** commands, **Open in File
Explorer** and **Copy Full Path**. It is the quickest way to ask where a file lives when
several tabs come from different folders. Right-clicking the empty part of the strip is
right-clicking the title bar, so Windows puts up the window menu there instead.

Your open tabs are reopened next time you start, along with which one was active. Untitled
documents are not restored, because they have no file to restore from.

**Menus**

The menu bar is text only. The four file commands worth a click rather than a walk through a
menu — **Open**, **Open Folder**, **Save**, **Save All** — sit first on the formatting bar
below it, ruled off from the formatting proper. Save and Save All are lit only when there is
something to write: Save when this tab has unsaved changes, Save All when any tab does.

`Alt` on its own puts the keyboard on the menu bar. From there the arrow keys walk along it
and `Enter` opens a menu, so nothing has to be memorized to drive the app without a mouse.

Each menu also opens directly: `Alt+F` File, `Alt+E` Edit, `Alt+O` Format, `Alt+V` View,
`Alt+T` Tools, `Alt+H` Help. Format takes `O` because File has `F`, as Windows menus have
always split those two.

`Help > Keyboard Shortcuts...` lists every shortcut in the app, with a button that copies
the lot — as a table for anything that keeps formatting, and as aligned columns for anything
that does not.

**Views**

| View | Shortcut |
|------|----------|
| Source only | `Alt+1` |
| Split | `Alt+2` |
| Preview only | `Alt+3` |

View mode, zoom and word wrap are application-wide rather than per tab, matching how
editors and browsers behave. `Ctrl+1`–`Ctrl+9` belong to tab selection.

In split view the panes scroll together, mapped through source line numbers rather than
scroll percentage, so tall diagrams and images do not throw the alignment off. Drag the
divider to resize. Double-clicking evens the split up again — either on the divider itself or
on the **Split** button in the toolbar, which is a much larger target than a six-pixel rule.

The toolbar row thins out as the window narrows rather than clipping: below about 900px the
view switcher tightens and the scroll-sync toggle steps aside, and below about 720px the
switcher and the zoom readout go too. Nothing becomes unreachable — view modes stay on the
View menu and on `Alt+1`–`Alt+3`, and `Ctrl+0` still resets the zoom. The formatting bar
below sheds on its own schedule, at about 990px and 780px.

**Outline**

`View > Outline` lists the document's headings in a panel down the left-hand side, indented
by level. It is off until you ask for it, and remembered after that.

| Action | How |
|--------|-----|
| Show or hide the panel | `Alt+4`, or `View > Outline` |
| Go to the panel, and back again | `Alt+Shift+4` |
| Jump to a heading, staying in the panel | ↑ / ↓, or a single click |
| Jump to a heading and start editing there | `Enter`, or a double-click |
| Narrow the list | Type in the filter box |
| Leave the panel for the document | `Escape` |
| Copy a heading | `Ctrl+C` with the panel focused |

Visibility and the keyboard are two separate questions, so they get two separate keys.
`Alt+4` shows and hides. `Alt+Shift+4` takes you to the panel and brings you back, opening it
first if it was closed — which is what you want when the outline is already on screen and you
are typing, where a visibility toggle could only close the thing you were reaching for. It is
the same split VS Code makes between showing the sidebar and focusing it.

Opening the panel puts the keyboard in it, on whichever section you are currently reading, on
the grounds that asking for the outline is usually asking to use it. `Escape` hands the
keyboard back and leaves the panel open. This is the only View menu item that moves the
focus; the rest leave you where you were.

The highlight follows what you are reading — the caret in the source pane, the top of the
viewport in preview — so the panel says where you are as well as where you can go. Clicking a
heading moves *both* panes, since the jump is by source line like everything else here.

`View > Preferences > Preview` limits how deep the list goes if a document's fourth-level
headings are more than you want to see. The panel is chrome rather than part of the preview,
so it never appears in a printed page or an exported PDF.

Because the outline is a place to work rather than a menu you pass through, the commands that
edit at the caret — the whole Format menu and formatting bar, plus Cut, Paste and Select All —
grey out while the keyboard is in it. There is no caret on screen to apply them to, and
applying them to one you cannot see is how documents get changed by accident. Everything else
keeps working: Save, Undo, the Find family, view modes and zoom all behave exactly as usual,
and zoom still targets the last pane you were in.

**Zoom**

| Action | Shortcut |
|--------|----------|
| Zoom the active pane | `Ctrl` `+` / `-` / `0`, or `Ctrl`+wheel over it |
| Zoom both panes together | `Ctrl+Shift` `+` / `-` / `0`, or `Ctrl+Shift`+wheel |

Each pane remembers its own level between sessions.

**Editing**

Full Monaco editing with find, replace, go-to-line, undo and redo, all reachable from the
Edit menu with the usual shortcuts. `Ctrl+S` saves, and saving a new document asks where to
put it. **`File > Save All` (`Ctrl+Shift+S`)** writes every tab that has unsaved changes in
one go, asking where to put each untitled one. Both go dim when there is nothing to write.
Closing anything with unsaved work prompts first.

Cut, copy and paste are handled by the app rather than by the editor. A browser only permits
clipboard access during a genuine user gesture, and a click on a native menu is not one, so
the menu items would silently do nothing otherwise.

**Find All**

`Edit > Find All...` (`Ctrl+Shift+F`) opens a window listing *every* match at once, in the
active tab or across every open one, grouped under the document each came from. `Ctrl+F` is
still the editor's own find bar for stepping through one match at a time; this is for the
question "where does this appear?".

| | |
|---|---|
| Options | Match case, whole word, regular expression — the same three the find bar offers |
| Scope | Active tab, or all open tabs |
| Run it | `Enter` in the search box, the **Find All** button, or `F5` to run it again |
| Go to a match | Select a row. The source pane switches tabs if it has to, scrolls the line into view and selects the matched text |
| Start again | **Clear**, or the search box's own clear button — either empties the term and the results together |
| Dismiss | `Esc`. The window hides rather than closing, so the results are still there next time |

Selecting a row shows the match without taking the keyboard, so the arrow keys walk down the
list with the editor following along. `Enter` or a double-click goes there properly and hands
the keyboard to the text.

Results are a snapshot. Editing a document that was searched puts an amber **Documents have
changed** notice on the status line — search again to update it — rather than reshuffling the
rows while you are reading them; closing a document greys its results out. Going to a match that has since moved still lands
on the right text: the match is looked for again before anything is selected, so a row can
never quietly select the wrong thing.

Regular expressions are .NET's, matched a line at a time — a pattern cannot span a line
break, and `^` and `$` anchor to the line. A pattern that will not compile is reported in
place of the results, and one that runs away is cut off rather than hanging the window.

**Writing markdown**

The `Format` menu applies markdown constructs so you do not have to type the punctuation.

| Command | Shortcut |
|---------|----------|
| Bold / italic / link | `Ctrl+B` / `Ctrl+I` / `Ctrl+K` |
| Inline code | ``Ctrl+` `` |
| Strikethrough | `Ctrl+Shift+X` |
| Code block | `Ctrl+Shift+K` |
| Blockquote | `Ctrl+Shift+.` |
| Bullet / numbered list | `Ctrl+Shift+8` / `Ctrl+Shift+7` |
| Heading level up / down | `Ctrl+Shift+]` / `Ctrl+Shift+[` |
| Heading 1–6, task list, table, horizontal rule | Menu only |

Everything toggles. `Ctrl+B` with nothing selected wraps the word under the cursor, and
pressing it again takes the markers off. Applied to several lines at once, a list or quote
marks every line the selection touches, and clears them only when all of them already carry
it — a part-marked selection gets finished rather than emptied. Switching between bullets,
numbers and headings replaces the marker rather than stacking a second one in front of it.

`Ctrl+K` uses whatever is selected for the half it can work out: selected text becomes the
label and leaves `url` selected to type over; a selected URL becomes the destination and puts
the cursor between the brackets.

Pressing Enter inside a list carries the list on to the next line, numbering as it goes.
Pressing it on an item you have not written anything in ends the list instead.

Explicit heading levels are on the menu without shortcuts. `Ctrl`+digit belongs to tab
selection, and `Ctrl+Alt`+digit is indistinguishable from `AltGr`+digit on European
keyboards, where it types a character.

The same commands sit on a toolbar under the menu bar, which is live: the buttons light up
for whatever the caret is inside, and the heading control reads `H2` on an H2. Each says
what it would *do* rather than what the text is, so a lit button always turns itself off.
The bar runs **Open · Open Folder · Save · Save All | undo · redo | bold · italic ·
strikethrough · code · link · rule | lists · blockquote | Heading ▾ | Diagram ▾ Insert ▾**,
where **Insert** holds code block and table among the snippet catalogue in name order — one
list of things to insert rather than banded by which of them happen to be commands, broken
once under a **Your snippets** heading where your own files start. The diagram menu keeps a
curated order instead, flowchart and sequence first, because those are the two anyone
actually reaches for. Blockquote stays a
button rather than a menu entry because it toggles — like the three list commands, it marks
every line the selection touches and lights up when the caret is inside one, which is worth
knowing without opening anything. Nothing in Insert toggles. The
formatting half is disabled in Preview-only view, where there is no source pane to edit —
the Format menu and the shortcuts go quiet with it — but the four file commands stay live,
because opening and saving are as reasonable while reading as while writing.

As the window narrows the bar hands whole groups to a `»` button rather than clipping: the
two insert menus go first, then the file commands, the lists and the heading control. Undo,
bold, italic, code and link never leave, the `»` appears only when something is actually
hidden, and nothing becomes unreachable — the file commands have no `»` entry, because Open
and Save don't belong under "More formatting", but the File menu and `Ctrl+O` / `Ctrl+S`
still have them.

The menu-bar Format menu is deliberately *not* regrouped to match. It stays the complete
flat inventory, which is what you want from the surface you go to when you can't find
something on the bar.

**Formatting**

`Edit > Format Document` (`Shift+Alt+F`) tidies the current file. With text selected it
formats just those lines. `Edit > Format Markdown...` opens the rule list; `Edit > Format All
Open Documents` does the whole set of tabs at once.

Sixteen rules, each independently switchable, because "tidy" is a matter of taste:

| | | |
|---|---|---|
| Heading space | Blank lines | Collapse blanks |
| Trailing whitespace | List marker space | Ordered numbering |
| Normalize markers | Link syntax | Blockquote space |
| Line endings | EOF newline | Table formatting |
| Code fences | Underlined headings | Emphasis markers |
| Re-wrap paragraphs | | |

Two are off by default. **Normalize markers** rewrites every bullet to one character, and
**re-wrap paragraphs** reflows prose to a column you choose (80 by default) — that one
rewrites every line of every paragraph, which ruins one-sentence-per-line writing and turns a
one-word edit into a diff covering the file. Re-wrapping skips lists, quotes, tables and code,
where a wrong continuation indent would change the structure rather than just the layout.

Three guarantees hold whatever the rules say:

- **Formatting never changes what a document renders to.** It moves whitespace and
  punctuation, never words.
- **The inside of a fenced code block and YAML front matter are never touched**, including
  their trailing spaces and blank lines.
- **`Ctrl+Z` takes a whole reformat back in one step.** The editor is edited rather than
  reloaded, so the undo history survives.

`Format automatically when saving` in the rules dialog runs the formatter before every
`Ctrl+S`. Off by default: it is a real convenience, but it does mean saving can change lines
you did not edit.

**Exporting**

| Export | What you get |
|--------|--------------|
| `Edit > Copy as Rich Text` (`Ctrl+Shift+C`) | The preview on the clipboard, formatting intact, for pasting into Word, Outlook or Confluence |
| `Tools > Export to PDF...` | The preview, printed. A page-setup dialog offers paper size, orientation, margins, and whether to keep background colors |
| `Tools > Export to HTML...` | One self-contained `.html` file |

All three are enabled whenever a document is open, and the two file exports default the
filename to the document's own name with the new extension.

**Copy as Rich Text** takes whatever is selected in the preview, or the whole document when
nothing is. It puts two things on the clipboard at once: the formatted version for anything
that understands formatting, and the markdown source for anything that does not, so pasting
into a terminal or a code editor still gives you markdown. Colors are resolved to fixed
values on the way out, because the stylesheet is built on CSS custom properties and Word has
never supported them.

Exports come from the **live preview**, not from a fresh render, so diagrams are already
inline SVG, math is already laid out and code is already highlighted — what you export is
what you were looking at. The HTML export inlines the stylesheets, embeds local images as
data URIs and embeds the KaTeX fonts, so the file survives being emailed on its own. Exports
are always light-themed: a dark background is rarely wanted in something printed or pasted
into someone else's document.

**Markdown cheatsheet**

`Help > Markdown Cheatsheet` (`Ctrl+F1`) opens a small floating reference showing every
construct Marqora renders, with the syntax beside its result — live mermaid diagrams and
KaTeX included. Choosing it again hides it; if it is open but buried behind the editor, it
comes forward instead. The menu item is ticked while it is on screen, and unticks if you
dismiss the window with its own close button.

It is a real window rather than a dialog, so you can leave it beside the editor and keep
typing. Its size, position and scroll offset are remembered between sessions, though it
always starts closed. The content is `webshell/cheatsheet.md`, rendered through the same
pipeline as the preview — edit that file to change what it says.

**Welcome document**

The first launch of each new release opens **Welcome to Marqora** — a stock document covering
what the app does and how it is driven — in preview view, as the tab in front. Tabs from the
previous session are restored as usual and are left where they were; only the focus differs.
A file named on the command line still wins: it opens last, keeps the focus and keeps your
view mode, and the welcome document waits in a tab beside it.

The master ships as `src/PaulTechGuy.MQ.App/Assets/Welcome to Marqora.md` and is copied to
`%LOCALAPPDATA%\PaulTechGuy\Marqora\Welcome to Marqora.md` before it opens, so the tab points
at a file you can edit, save, or close and forget. The name never changes and each release
overwrites the copy, which is what keeps the document a description of the version actually
running.

Preview view here is for that document only and is not written to settings — the view mode
you chose is still yours, and comes back the next time you start.

**Hold `Shift` while starting Marqora** to open it any time, whether or not this version has
already shown it. That also replaces your copy with the shipped one, so it is the way back
from having scribbled on it. A Shift launch outranks a file on the command line: the welcome
document opens last and takes the focus, because holding Shift is asking for it in as many
words. It applies to the instance that is starting — if Marqora is already running, the launch
is handed to that window and the gesture is only noted in the log.

What has been shown is recorded as `lastWelcomeVersion` in `settings.json`; deleting that key
brings the document back on the next launch, the same as holding Shift.

**Help**

`Help > About Marqora` shows the version, runtime, where the data and logs live, and the
bundled third-party components. Its **Copy details** button puts all of that on the clipboard,
which is what a bug report needs. `Help > Open Log Folder` goes straight to the logs.

**Markdown support**

Tables, footnotes, task lists, definition lists, YAML front matter, auto-links, emoji, and
math via KaTeX. Fenced code is syntax-highlighted in both panes. Relative image and link
paths resolve against the document's own folder.

**Mermaid diagrams**

Fenced ```` ```mermaid ```` blocks render inline. Flowcharts, class diagrams, sequence
diagrams and the rest of mermaid 11 are supported, and diagrams re-render on theme change.
Rendered SVG is cached by diagram source, so typing elsewhere in a diagram-heavy document
does not re-render the diagrams.

**Checking a document**

`View > Show Problems` underlines things that are wrong, in the source pane. It is on by
default, because none of it shows up any other way: the preview renders a dead link exactly
like a live one.

| Underlined | Why |
|------------|-----|
| `[text](./gone.md)` | Nothing at that path, relative to the document's own folder |
| `![alt](missing.png)` | Same, for an image |
| `[text](#no-such-heading)` | No heading in this document produces that anchor |
| `##Heading`, `-item`, `>quote`, trailing spaces, `[text] (url)` | Syntax the formatter would tidy up |

The first three are warnings; the style rules are hints, because `Edit > Format Document`
fixes all of them on request. Links that leave the machine are never checked — that would
mean going to the network, which Marqora does not do. A document that has never been saved
has no folder for a relative path to resolve against, so its file links are left alone,
though its anchors are still checked.

Nothing inside a fenced code block or YAML front matter is ever flagged.

**Spell check**

`View > Spell Check` (`F7`) underlines words that are not in the dictionary. It is on by
default. Right-click one — or press `Ctrl+.` — for corrections, to add the word to your
dictionary, or to delete a word you have typed twice in a row.

The words come from the spelling dictionary Windows already has, so there is nothing to
download and nothing leaves the machine. If Windows has no dictionary for your language, spell
check stays quiet and the setting greys out to say why.

A document about software is mostly not prose, so most of what is on screen is never checked:

| Never checked | Checked |
|---------------|---------|
| Fenced, indented and inline code | The text of a link, and a reference's title |
| Link and image targets, autolinks, bare URLs | The alt text of an image |
| HTML tags and attributes, entities | Everything else |
| Maths, footnote markers, emoji shortcodes | |
| YAML front matter | |
| `SDK`, `win-x64`, `MainViewModel` — acronyms, anything with a digit, camelCase names | |

**Your own dictionary** is a plain text file, one word per line, at
`%LOCALAPPDATA%\PaulTechGuy\Marqora\user-dictionary.txt`. Lines beginning `#` are comments.
Open it in Marqora and correct it like any other document — it is re-read the moment you save.
Keep it in a project beside the documents it belongs to, review it in a diff, and export or
import it from the Advanced page of Preferences; an import adds words and never removes any.

A word in your dictionary stays known when you make it possessive, so adding `Marqora` covers
`Marqora's` too.

**Appearance**

System, Light or Dark, under `View > Theme`. System follows Windows and tracks changes made
while the app is running. The window uses Mica and an extended title bar.

**Other View options**

Word wrap (`Alt+Z`), line numbers, show whitespace, and markers showing where a wrapped line
continues. Window size, position, split position, view mode and every toggle persist.

**Status bar**

Along the bottom: the last thing that happened on the left, and on the right the cursor's
line and column, the word count and the character count, all of which appear only when a
document is open.

**Files on disk**

Every open document is watched independently. If one changes externally and you have no
unsaved edits in it, that tab reloads silently; if you do, Marqora asks first.

**Preferences**

`File > Preferences...` gathers every setting in the app onto six pages — Appearance, Editor,
Preview, Files, Export & Print and Advanced. Changes apply as you make them, so you can see a
font or a theme before committing to it, and **Cancel** puts all of them back. Four settings
wait for **OK** instead: the recent-files limit, autosave and its delay, and log retention.
Those act on your disk rather than only describing how things look, so Cancel could not undo
them after the fact.

**Carrying preferences to another machine**

On the Advanced page, **Export preferences...** writes every preference to a JSON file,
stamped inside with the version of Marqora that wrote it and the machine it came from.
**Import preferences...** reads one back.

The name offered carries the date and time it was taken —
`Marqora-preferences-2026-09-01-143022.json` — so exporting again leaves a second file
rather than an overwrite prompt, and a folder of them sorts into the order you took them.

Your open documents, window position, splitter, recent files and search history are not
included. They describe the machine rather than your preferences, and an import never
disturbs the ones on the machine you are importing to.

The two machines do not have to be on the same version. Import applies everything the running
build understands and then says what it could not use — a setting the file's Marqora had and
this one does not, a setting this one has that the file predates, a value outside the range
Marqora allows. A `settings.json` copied straight off the other machine is accepted too.

Like every other change in the dialog, an import is undone by **Cancel**.

---

## Where things are kept

| Path | Contents |
|------|----------|
| `%LOCALAPPDATA%\PaulTechGuy\Marqora\settings.json` | Preferences and window placement |
| `%LOCALAPPDATA%\PaulTechGuy\Marqora\recent-files.json` | Recent and pinned files |
| `%LOCALAPPDATA%\PaulTechGuy\Marqora\snippets\` | Your own snippets, one per file |
| `%LOCALAPPDATA%\PaulTechGuy\Marqora\logs\` | Rolling logs, 14 days |
| `%LOCALAPPDATA%\PaulTechGuy\Marqora\WebView2\` | WebView2 cache |
| `%LOCALAPPDATA%\PaulTechGuy\Marqora\Welcome to Marqora.md` | Your copy of the welcome document, replaced by each release |

Deleting any of these is safe; the app recreates them. A corrupt state file is moved aside
rather than blocking startup.

---

## Project layout

```
PaulTechGuy.MQ.slnx
├── build/Get-WebAssets.ps1              Restores Monaco, mermaid, KaTeX, highlight.js
├── build/Register-FileAssociation.ps1   Makes Windows offer Marqora for markdown files
├── docs/Architecture.md         How it fits together, and why
├── docs/WebViewDebugging.md     Getting a DevTools console onto the preview
├── webshell/                    The preview shell: HTML, CSS, JS, vendor bundle
├── src/
│   ├── PaulTechGuy.MQ.Domain          Models and enums. No dependencies at all.
│   ├── PaulTechGuy.MQ.Abstractions    Interfaces. Everything else talks through these.
│   ├── PaulTechGuy.MQ.Repositories    JSON persistence
│   ├── PaulTechGuy.MQ.Rendering       Markdig pipeline
│   ├── PaulTechGuy.MQ.Formatting      The 16 tidy-up rules
│   ├── PaulTechGuy.MQ.Editing         The Format menu's markdown commands
│   ├── PaulTechGuy.MQ.Analysis        Link, image and style checks
│   ├── PaulTechGuy.MQ.Finding         Find All's search engine
│   ├── PaulTechGuy.MQ.Services        Document lifetime, settings, recent files, watching
│   └── PaulTechGuy.MQ.App             WinUI 3 shell and view models
└── tests/                       xUnit v3 projects for Editing, Analysis and Finding
```

No concrete layer references another. They meet at the composition root in
`src/PaulTechGuy.MQ.App/Program.cs`.

---

## Tests

```powershell
dotnet test PaulTechGuy.MQ.slnx
```

Three projects, all pure text in and assertions out:

- **`PaulTechGuy.MQ.Editing.Tests`** exercises every Format-menu command through a small
  stand-in for the editor, so the edits are proven to compose rather than merely to look
  right one at a time.
- **`PaulTechGuy.MQ.Analysis.Tests`** runs documents through the real renderer and checks
  them against real files in a temporary folder — the link checks are about whether a path
  is there, so substituting the filesystem would test nothing.
- **`PaulTechGuy.MQ.Finding.Tests`** pins Find All's line and column numbers, which is the
  whole of what makes a result clickable: mixed line endings, whole-word boundaries, regular
  expressions and the ceiling on how many matches come back.

Adding another needs no wiring. A project under `tests\` is picked up as a test project by
`Directory.Build.props` automatically, and package versions come from
`Directory.Packages.props`, which pins xUnit v3, NSubstitute and Shouldly.

```powershell
dotnet new xunit3 -o tests\PaulTechGuy.MQ.Services.Tests
dotnet sln PaulTechGuy.MQ.slnx add tests\PaulTechGuy.MQ.Services.Tests
```

If the `xunit3` template is not installed, copy an existing test `.csproj` instead: xUnit v3
projects are executables, which is the only setting that is not inherited.

The layers below the UI were written to make this straightforward: `MarkdigMarkdownRenderer`
is synchronous and takes only a logger, `AppPaths` accepts a temp directory through its
second constructor, and every service depends on interfaces that substitute cleanly.

---

## Troubleshooting

**"Web assets are missing" at build time, or an empty preview**
Run `pwsh .\build\Get-WebAssets.ps1`. The vendor bundle is git-ignored.

**Something rendered oddly**
`%LOCALAPPDATA%\PaulTechGuy\Marqora\logs\` has the answer. Errors raised by the preview's
JavaScript are forwarded into the same log, so a failure in the shell is visible there
rather than only in a developer-tools console.

**Reset everything**
Close Marqora and delete `%LOCALAPPDATA%\PaulTechGuy\Marqora`.

---

## Third-party components

Restored by `build/Get-WebAssets.ps1`, served locally, never fetched at runtime.

| Component | Version | License |
|-----------|---------|---------|
| [Monaco Editor](https://github.com/microsoft/monaco-editor) | 0.56.0 | MIT |
| [Mermaid](https://github.com/mermaid-js/mermaid) | 11.17.0 | MIT |
| [KaTeX](https://github.com/KaTeX/KaTeX) | 0.18.4 | MIT |
| [highlight.js](https://github.com/highlightjs/highlight.js) | 11.12.0 | BSD-3-Clause |

NuGet: Markdig, CommunityToolkit.Mvvm, Serilog, Windows App SDK, WebView2.

---

## Support the project

Marqora is free and open source, and stays that way. Nothing is held back, and nothing here
depends on paying for it.

If you find it useful, you can support continued development through
[GitHub Sponsors](https://github.com/sponsors/paultechguy). Starring the repository helps
too, and costs nothing. ⭐

The same links are in the app under **Help → Support the Project**.

---

## License

Copyright (c) 2026 Paul Carver.

Marqora is released under the [Apache License 2.0](LICENSE). You are free to use, modify and
redistribute it, commercially or otherwise, provided you keep the copyright and license
notices, state what you changed, and accept that it comes with no warranty.

Every source file carries the license as a two-line
[SPDX](https://spdx.dev/learn/handling-license-info/) header:

```csharp
// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0
```

`build/Add-FileHeaders.ps1` applies it across the tree and is idempotent, so re-running it
after adding files is safe. `-Check` reports anything missing without writing, which is the
form to call from CI:

```powershell
pwsh ./build/Add-FileHeaders.ps1          # apply
pwsh ./build/Add-FileHeaders.ps1 -Check   # verify, non-zero exit if any are missing
```

The header is also declared as `file_header_template` in `.editorconfig` with `IDE0073` set
to warning level, so a new file without one is flagged at build time.

The third-party components listed above keep their own licenses, which are separate from
this one.
