# Blocked content

How Marqora says, in three places at once, that a picture in your document is not going to
appear here — and why that is a statement about the app rather than a fault in the document.

`docs/Architecture.md` carries the design argument under *"Saying so, rather than leaving a
blank box"*. This file is the working reference: what runs, in what order, what each stage
decides, and what to look at when the marks stop showing up.

---

## The claim

Marqora makes no network calls at runtime, and the preview serves files only from the
document's own folder. Both are properties of the product, not omissions. The cost is that
some perfectly correct documents render a hole, and for a long time nothing explained it: an
absolute URL was left exactly as written, the content policy killed the fetch, and the reader
concluded the app was broken.

Blocked content is the explanation. It marks **two kinds** of reference:

| Kind | `LinkFindingKind` | What is true |
| --- | --- | --- |
| On the web | `RemoteMedia` | The address is valid and it renders on GitHub. Marqora will not fetch it. |
| Elsewhere on this machine | `OutsideFolder` | The file exists. It is not beside the document, so the preview will not serve it. |

And it is deliberately **not** these, which travel through the same pipeline and wear their
own marks:

| Kind | Mark | Claim |
| --- | --- | --- |
| `MissingImage` | wavy amber | The file is not there. |
| `BrokenLink` / `DeadAnchor` | wavy amber | The target does not exist. |
| `MissingAltText` | dotted gray | The image is fine and says nothing about itself. |

Red is wrong, amber is broken, gray is incomplete, violet is *"not here"*. Four claims, four
marks — a ruler tick has no texture, so hue is the only thing that can tell them apart at two
pixels wide.

**Loads, not navigations.** Only something the renderer would fetch by itself is marked. A
link is never marked however far away it points, because nothing happens until a click and a
click works. Get that line wrong and a README's dozen ordinary links light up, which is the
fastest possible way to have the whole rule switched off.

---

## The pipeline

| # | Stage | Where |
| --- | --- | --- |
| 1 | Collect every reference, markdown and raw HTML | `MarkdigMarkdownRenderer.ReadLinks`, `MarkdownMediaReader` |
| 2 | Decide what kind of place a target is | `MediaTarget.Classify` |
| 3 | Decide what to report | `LinkChecks.Run`, `ImageChecks.Run` |
| 4 | Cross into the shell | `WebViewPreviewHost.SetLinkFindingsAsync` |
| 5 | Underline and tick the source pane | `setLinkFindings` in `app.js` |
| 6 | Explain on hover | `registerImageHover` in `app.js` |
| 7 | Offer the repairs | `MainWindow.ContextMenus.cs`, `MainViewModel` |
| 8 | Chip the preview | `markBlockedMedia` in `app.js`, `.mq-blocked-media` in `app.css` |
| 9 | Count them on the menu | `RefreshBlockedImagesMenuText` |

### 1. Collecting references

`ReadLinks` is two walks joined, because Markdig models the two shapes quite differently.

- **`MarkdownLinks`** walks `LinkInline` — every `[a](b)` and `![a](b)`, told apart by
  `IsImage`. Position comes off the base `MarkdownObject`, so this costs nothing the parse has
  not already done.
- **`MarkdownMediaReader.ReadMedia`** walks `HtmlInline` and `HtmlBlock`. Markdig keeps raw
  HTML as unparsed text, so `<img src="...">` reached no check at all before this existed —
  and pinning a width is exactly why people reach for the tag.

`MediaAttributes` lists only what a browser loads by itself: `img`/`source` (`src`, `srcset`),
`video` (`src`, `poster`), `audio`, `track`, `iframe`, `embed` (`src`), `object` (`data`).
`href` is absent on purpose.

Three things `ReadMedia` deliberately does:

- **Reads from the parsed tree, not the raw text.** HTML inside a code fence never becomes an
  `HtmlBlock`, so an example in a document *about* HTML costs nothing to exclude.
- **Marks the address, not the whole tag.** A tag can carry two addresses, and two underlines
  over one span means only one of them can be right-clicked. It also gives the repairs
  something to replace — `LinkTargetSpan` reads `](url)` syntax and would find nothing here.
- **Skips a tag split across lines.** Every column is an offset into one line, so a tag opening
  on one line and carrying its address on the next produces a column past the end of the
  opening line. Silence is the right failure; a mark in the wrong place costs the reader their
  trust in the marks that are right.

A raw tag sets `IsRawHtml`, and `ImageChecks` skips those entirely — nothing here reads an
`alt` attribute, so every tag would otherwise look like an image nobody had described.

### 2. Classifying a target

`MediaTarget.Classify` is pure, disk-free, and lives in `Domain`, because *"what kind of place
is this"* has one answer whether or not the document was ever saved.

| Written as | `MediaTargetKind` |
| --- | --- |
| `#section` | `Fragment` |
| `//host/path` | `Remote` |
| `http:…`, `https:…` | `Remote` |
| `file:…` | `LocalAbsolute` |
| `C:\…`, `C:/…`, `C:` | `LocalAbsolute` |
| any scheme of **two or more** characters, then `:` | `OtherScheme` |
| anything else, `../x` and `/x` included | `Relative` |

The two-character rule is what tells a scheme from a drive letter. Before it existed, `C:` was
read as somebody else's URL and waved through — a blank picture with nothing to explain it.

`/x.png` and `../x.png` stay `Relative` on purpose. Both are rooted or climbing and neither
resolves inside the document's folder, but both have always gone down the relative path, and
moving them would change what an existing document says for unrelated reasons.

### 3. Deciding

One pass, inside `LinkChecks.Run`, rather than a second checker beside it. A separate
`MediaChecks` would overlap on every relative path and draw two marks on one reference, so
classification happens where resolution already happens.

| Target | Image | Link |
| --- | --- | --- |
| `#anchor` naming nothing in the outline or the raw anchors | `DeadAnchor` | `DeadAnchor` |
| `Remote` | `RemoteMedia` | *nothing* |
| `OtherScheme` (`mailto:`, `data:`, `blob:`) | *nothing* | *nothing* |
| **document never saved, or the folder is gone** | *nothing below this line* | *nothing below this line* |
| `LocalAbsolute` | see below | *nothing* |
| `Relative`, and the file resolves and exists | *nothing* | *nothing* |
| `Relative`, missing, but really there outside the folder | `OutsideFolder` | — |
| `Relative`, missing | `MissingImage` | `BrokenLink` |

`LocalAbsolute` for an image, in `ReportLocalAbsolute`:

1. Not expressible as a path → `MissingImage`.
2. A UNC share (`\\host\...`) → `OutsideFolder`, **without probing it**.
3. Inside the document's folder and present → *nothing*; the spelling is unusual, the reference
   is fine.
4. Present → `OutsideFolder`.
5. Otherwise → `MissingImage`.

Query strings and fragments are stripped before any of this: they are not part of a file name.

### 4. Crossing the bridge

`SetLinkFindingsAsync` sends the domain type, not a Monaco marker. A marker brings a hover
repeating the squiggle, an Alt+F8 peek panel, and an untrue "No quick fixes available" line —
the exact chrome these were moved off markers to escape, since the fixes are on the right-click
menu.

The kind travels **as its name** (`"RemoteMedia"`), so neither side keeps a table of numbers in
step. Positions are zero-based on the wire, as they are everywhere inside the app; `app.js`
adds the one Monaco wants.

### 5. The source pane

`setLinkFindings` turns each finding into a model decoration:

| Finding | `inlineClassName` | Underline | Ruler lane | Color slot | Tick color |
| --- | --- | --- | --- | --- | --- |
| `RemoteMedia`, `OutsideFolder` | `mq-unrenderable` | dotted `--mq-blocked` | **Left** | `editorError.foreground` | `--mq-blocked` |
| `MissingImage`, `BrokenLink`, `DeadAnchor` | `mq-dead-link` | wavy `--mq-warning` | **Center** | `editorWarning.foreground` | `--mq-tick-link` |
| `MissingAltText` | `mq-missing-alt` | dotted `--mq-text-tertiary` | **Center** | `editorHint.foreground` | `--mq-text-tertiary` |

Spelling is the third category and does not come through here: it rides the **Right** lane in
`--mq-tick-spelling`. See `setSpelling`.

`--mq-blocked` is `#a259ff` in light, `#c9a6ff` in dark, and `#8250df` on paper — the screen
value washes out in print. It was once an alias of `--mq-important`, one violet being better
than two to keep in step; that lasted until it had to be *read*, because the callout violet
passes for black at hairline width.

`editorError.foreground` is a free slot rather than a claim about severity: `StyleChecks` is
the only source of markers and reports Hint, never Error.

**The ruler is painted and placed.** A tick has no texture — dotted and wavy are identical at
two pixels wide — so hue and position are all a reader has.

*Position.* Everything used to ride the right lane, so two findings a few lines apart landed on
the same few pixels and whichever was drawn second won outright: a misspelling on line 887 hid
a blocked picture on 889 completely, with nothing on screen to say a second mark existed.
Blocked pictures took the left lane, which fixed that pair and left spelling and the link
findings sharing the right. There are now three categories and three lanes, one each, so no
mark this app draws can hide another at any distance.

Spelling keeps the right lane rather than moving to the free one, and that is the choice worth
defending. The center is not really free — Monaco draws its own find matches and
word-occurrence highlights there — so whichever category takes it gets a lane that floods the
moment the Find widget opens or a word is selected. Misspellings are the numerous kind, the
kind somebody scans the ruler for, and the kind whose ticks get clicked one after another,
which selects a word, which fills the center. The link findings are few by comparison, so they
pay the rent.

Alt text rides in the center too, in its own gray — the fourth color and the only passenger. It
can lose a collision to a broken link, which is the cheapest pair in the set to lose, and it
can hide neither a misspelling nor a blocked picture.

*Hue.* The ticks take `--mq-tick-*` colors rather than the squiggle tokens. A squiggle is
several pixels tall with the text it marks beside it; a tick is two pixels on empty background
with nothing to compare against, and the two jobs want different numbers. `--mq-danger`
(`#b3261e`) and `--mq-warning` (`#9a6700`) have relative luminances of 0.15 and 0.16 — under
words they read as red and amber, as ticks they read as two dark smudges. The ruler pair
separates by lightness as well as hue, and both stay above the 3:1 an indicator owes its
background. Blocked and alt text keep their squiggle token: `--mq-blocked` was tuned for this
measurement already, and gray beside three hues needs no tuning.

Stickiness is `NeverGrowsWhenTypingAtEdges`, so typing at either edge does not drag the mark
along with the text. `tab.linkInk` and `tab.linkFindings` are kept parallel — `deltaDecorations`
returns ids in the order they went in, so `linkInk[i]` belongs to `linkFindings[i]`. The
decoration knows where the reference is after an edit; the finding knows the target and the
kind.

### 6. The hover

`registerImageHover` composes the whole hover itself, message first. No `hoverMessage` is put on
the decoration: Monaco merges contributions at a position in an order nothing here controls, and
one line of text against a 320px thumbnail ended up below the fold where nobody found it.

Both blocked kinds are refused a thumbnail, and for a stronger reason than tidiness. A remote
address cannot be previewed without fetching it, which is the one thing this app does not do —
a hover that tried would be exactly the network call the mark exists to say never happens. One
elsewhere on the disk is refused too: the virtual host serves the document's folder and nothing
outside it, so the request could only come back empty.

### 7. The repairs

Right-clicking a mark shows what can be *done*, not a guess at what was meant — the address is
exactly what the author wrote.

| Item | Shown for | What it does |
| --- | --- | --- |
| **Copy it in** | `OutsideFolder` | Copies the file in beside the document and repoints the reference. The only repair that fixes the problem outright. |
| **Open in browser** | `RemoteMedia` | Hands the address to the shell. No network call happens inside Marqora. |
| **Copy address** | `RemoteMedia` | Clipboard. |
| **Make this a link instead** | `RemoteMedia`, markdown syntax only | Drops the `!`. An empty label is filled with the host, so it is not a link with nothing to click. |
| **Replace with a file…** | both | Picks one off the disk and writes it beside the document. |
| **Paste image over it** | both | The closest the app comes to fetching a remote image, at a very deliberate distance: *you* fetched it, in your own browser, with your own sign-in, and you saw what you were getting. |
| **Remove this image** | both | Text changes from "link" to "image" for the media kinds. |

**Make this a link instead** is offered only when `CanDemoteToLink` confirms the reference
actually starts `![`. A picture written as a tag needs a different edit, and an item that
quietly does nothing is worse than no item. The last three all land in `PlaceImageAsync`, the
same tail `InsertImagesAsync` uses.

### 8. The preview chip

`markBlockedMedia` runs in `applyPreviewHtml`, after `rewriteRelativeUrls`. That order is the
whole trick: everything servable has already been pointed at `https://marqora.document`, so
`willNotLoad` is one rule — *did it end up there?* — instead of a list of schemes kept in step
with the analyzer's. `data:` and `blob:` are exempt.

Each blocked element is wrapped in `<span class="mq-blocked-media" data-mq-blocked="...">`. The
element is hidden with `display: none`; the chip is drawn by CSS `::after` from the attribute.

**That is load-bearing, not tidy.** Generated content never serializes into `innerHTML`, so a
paste into Outlook carries the original `<img src="https://...">` and loads it exactly as it
should. Replacing the element instead would have put a violet chip into somebody else's
document.

The label is the author's `alt` if there is one, the host for a web address, and the file name
for a path — `C:\Users\...\2026\q1\shot.png` in the middle of a paragraph is a chip wider than
the paragraph. A row of eight build badges has no alt text between them, and "img.shields.io"
eight times is at least eight true statements.

The chip stays inline-block and modest so a badge row stays a row. Sizing it to the picture it
stands in for would be more informative and is not available: nothing here knows how big a file
it never fetched would have been.

An unsaved document has no `documentBaseUrl`, so `markBlockedMedia` returns immediately — the
same reason `LinkChecks` gives up without a folder. The two surfaces agree, or neither is
believed.

### 9. The count

`PublishDiagnosticsAsync` records `_blockedImageCounts[documentId]` for whichever document was
checked, not only the active one: a background tab is checked on open and after a save, and its
answer has to be waiting when somebody switches to it. `View > Show Blocked Images` then reads
*"Show Blocked Images (3)"*.

Counts are dropped when a tab closes and cleared outright when diagnostics are switched off — an
unchecked document says nothing, which is the truth.

---

## The switch

`ShowBlockedImages`, default **true**, on `View > Show Blocked Images` and in Preferences as
*"Underline pictures that will not appear"*.

Its own flag rather than riding with the rest, because these arrive in a different quantity: a
README with a row of build badges has eight on one line and nothing wrong with it, and somebody
who works on such documents all day should be able to quiet this without losing the dead links
and the broken anchors.

Two consequences of turning it off are worth knowing, because they are not symmetrical:

- A remote or absolute-path image reports **nothing at all**.
- `../shared/logo.png` falls back to **`MissingImage`** — *"No image at…"* about a file sitting
  happily on the disk one folder over. That is the pre-feature behavior, restored on purpose
  rather than by accident.

`ShowDiagnostics` gates the whole publish above it. With diagnostics off nothing is checked and
nothing is drawn, whatever this switch says.

---

## When the markup leaves

| Route | Blocked content |
| --- | --- |
| Copy as Rich Text | `withoutBlockedChips` — the wrapper is unwound, the original element travels |
| Export HTML | the same |
| Print / PDF | **the chip stays**, restyled to `#8250df` |
| Folio | never sees a wrapper; built from the host's Markdig markup |
| Word (`.docx`) | `DocxImages.TryBuild` skips a remote image and writes the alt text, recording *"not on this machine"* |

The printer is the deliberate odd one out. It paints this very DOM rather than serializing it,
and a picture that cannot be fetched cannot be in the PDF either — so a small quiet chip beats a
silent gap, which is the very defect this feature exists to close. A *squiggle* is not the same
case and stays off the page: a squiggle marks a fault in the document, the chip marks content
missing from the artifact, and the artifact is the only place that fact is still useful.

---

## Two things are never done

**A web address is never fetched**, not even to build a hover thumbnail. Relying on the content
policy to stop it would be below the standard: the claim Marqora makes is that it never goes to
the network, and a hover that *would* try is not made harmless by being blocked.

**A UNC path is never probed.** `File.Exists` on a share blocks until the other machine answers,
and for a disconnected VPN that is measured in seconds — while somebody is typing.
`MediaTarget.IsNetworkShare` short-circuits it, and the finding reports what can be known for
certain: it is not beside the document, which is the part that decides whether it appears.

---

## When the marks do not appear

Work down this list before touching the checks.

**Is either switch off?** `View > Show Blocked Images` and `View > Show Diagnostics`. The second
turns off the first.

**Has the document ever been saved?** No folder means no relative resolution, so the source pane
and the preview both stay quiet, together and on purpose.

**Is the reference inside a fenced code block?** Nothing in one ever reaches a check — the parser
already decided it is an example. This is correct, and it is also the trap below.

> ### An unterminated fence swallows the rest of the document
>
> A fenced block with no closing fence runs to the end of the file. Every reference after it is
> then inside a code block, so no finding is produced for any of them, and the preview renders
> one enormous `<pre>` where the rest of the document should be.
>
> This happened for real. A commit rewrote the closing `~~~` of one block in
> `docs/UltimateMarkdownContent.md` into two backtick fences, and everything from line 574 to
> line 2326 became a single code block. The `<iframe>` on line 889 stopped being an `HtmlBlock`,
> and its underline and its ruler tick both vanished — with nothing in the log, because nothing
> had failed.
>
> **The tell is the pair of symptoms.** Marks go missing *and* the preview stops scrolling
> somewhere in the middle of the document. The second is the same cause: `buildLineMap` reads
> `data-src-line` off block elements, one giant `<pre>` contributes a single entry, and every
> source line past it interpolates to the same offset — so the preview parks and the editor is
> pulled back to meet it. It reads as a freeze; it is a line map with nothing left in it.
>
> A fence closes only on the same character it opened with, at least as many of them, and with
> nothing after it. ` ``` ` does not close `~~~`, which is the entire point of the tilde form.
> To check a document, count them:
>
> ```powershell
> $open = $null
> Get-Content doc.md | ForEach-Object { $n++
>   if ($_ -match '^\s*(`{3,}|~{3,})\s*(\S*)\s*$') {
>     if (-not $open) { $open = $Matches[1]; $at = $n }
>     elseif ($Matches[1][0] -eq $open[0] -and $Matches[1].Length -ge $open.Length -and -not $Matches[2]) { $open = $null }
>   } }
> if ($open) { "UNCLOSED fence opened at line $at" } else { "balanced" }
> ```

**Is the tag split across lines?** Deliberately not reported. See stage 1.

**Is it a raw `<img>` you expected an alt-text mark on?** Also deliberate — `ImageChecks` skips
`IsRawHtml`, because nothing reads the attribute yet.

**Are you looking at the build you actually ran?** `webshell/` is copied into the output tree at
build time, so an edit to `app.js` reaches only the tree you built. See `docs/WebViewDebugging.md`
for getting a console onto the running preview.

> **A tick that appears when you click the line is not this feature.** Monaco draws cursor
> positions in the overview ruler itself, so clicking line 889 puts a mark there whether or not
> a finding exists — in the cursor color rather than violet. It is not evidence that the check
> ran.

---

## Tests

| File | Covers |
| --- | --- |
| `tests/PaulTechGuy.MQ.Domain.Tests/MediaTargetTests.cs` | `Classify`, `LocalPathOf`, `IsNetworkShare` — the drive-letter-versus-scheme rule |
| `tests/PaulTechGuy.MQ.Rendering.Tests/MediaReaderTests.cs` | `MarkdownMediaReader`: which attributes, `srcset`, multi-line tags, code fences |
| `tests/PaulTechGuy.MQ.Analysis.Tests/BlockedImageTests.cs` | `LinkChecks` end to end — both kinds, links staying quiet, the switch off |

---

## File map

| File | Role |
| --- | --- |
| `src/PaulTechGuy.MQ.Domain/MediaTarget.cs` | Classification. Pure, disk-free. |
| `src/PaulTechGuy.MQ.Domain/LinkFinding.cs` | The finding and its six kinds. |
| `src/PaulTechGuy.MQ.Domain/AnalysisRequest.cs` | `CheckBlockedImages`, `CheckImageAltText`. |
| `src/PaulTechGuy.MQ.Domain/AppSettings.cs` | `ShowBlockedImages`. |
| `src/PaulTechGuy.MQ.Rendering/MarkdownMediaReader.cs` | Raw-HTML media references. |
| `src/PaulTechGuy.MQ.Rendering/MarkdigMarkdownRenderer.cs` | `ReadLinks` joins the two walks. |
| `src/PaulTechGuy.MQ.Analysis/LinkChecks.cs` | The decision table. |
| `src/PaulTechGuy.MQ.Analysis/ImageChecks.cs` | Alt text; skips raw HTML. |
| `src/PaulTechGuy.MQ.App/ViewModels/MainViewModel.cs` | Publishing, the count, the six repairs. |
| `src/PaulTechGuy.MQ.App/Views/MainWindow.ContextMenus.cs` | Which repairs are shown. |
| `src/PaulTechGuy.MQ.App/Services/WebViewPreviewHost.cs` | `SetLinkFindingsAsync`. |
| `webshell/app.js` | `setLinkFindings`, `registerImageHover`, `markBlockedMedia`, `withoutBlockedChips`. |
| `webshell/app.css` | `.mq-unrenderable`, `.mq-blocked-media`, `--mq-blocked`, the print rule. |
