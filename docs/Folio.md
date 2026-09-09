# Folios: sharing a document and what it references

A Markdown file is not self-contained. The text goes in the email; the pictures do not. This
document records what a Folio is, why it is one HTML file rather than an archive, and which of
the obvious shortcuts were tried on paper and rejected — because most of them look correct until
you ask what the person on the other end actually receives.

> **Where this has got to.** All of it. `Tools > Share as Folio...` offers the three forms, and
> `Tools > Open Folio...` - or dropping one on the window - takes a reading copy apart again into
> its documents and images. `PaulTechGuy.MQ.Folio` holds the planner, the writers, the link
> rewriting, the payload and the unpacker, with 50 tests against a real temporary tree.
>
> The preflight is a resizable window rather than a dialog; `docs/Architecture.md` records why
> under *Secondary windows*, along with why it is modeless and says its plan is out of date
> rather than blocking the editor.

---

## The problem

`Tools > Export to HTML...` already writes one self-contained file, and it is good: images become
data URIs, the stylesheets and the KaTeX fonts travel inside it, and the README is entitled to
say it survives being emailed on its own.

It answers one question and not the next three.

| What is missing | Why it matters |
|---|---|
| It takes the active tab only | `Ctrl+Shift+O` turns a folder into twelve tabs, and there is no way to send those twelve as one thing |
| It is a dead end | The recipient can read the page. They cannot edit it, and they cannot send a correction back |
| It drops what it cannot reach, silently | `RenderedHtmlPackager.EmbedLocalImages` refuses to walk outside the document's folder and skips anything over 8 MB. Both leave a reference the recipient has no file for, and nothing says so |

The third is the one that turns a good export into a bad one. An image inserted from
`C:\Users\...\Pictures` renders perfectly here and is broken the moment the `.md` leaves the
machine, and the author has no way to know.

The prior art is the whole point of the feature and worth naming: DaVinci Resolve's **Media
Management**, InDesign's **File > Package**, After Effects' **Collect Files**. Each does the same
three steps — walk what the project references, copy it into one place, and **relink the paths so
the copy opens somewhere else**. Marqora does the third step for nothing today.

---

## The four decisions

| Question | Answer |
|---|---|
| One file, or a folder of files? | **One `.html` file.** A `.zip` is offered as the second form, for a set too large to be one page |
| What can the recipient do with it? | **Read it with no software, or open it in Marqora and get the sources back.** The same file does both |
| Where does it get shared? | **Nowhere.** A Folio is a file. Marqora makes no network calls, and this feature does not become the first one |
| A new file extension? | **No.** `.html` and `.zip`, which every machine already understands |

The second is the one that carries weight. Everything else follows from refusing to choose
between the reader who has Marqora and the reader who does not.

---

## What a Folio is

**One `.html` file that is both the reading copy and the working copy.**

Opened in any browser it is an ordinary web page: the documents rendered one after another,
images inlined, a contents list at the top, and links between the documents rewritten to jump
within the page. No install, no network, no account, and it still works in five years.

Dragged onto Marqora it is a project. The file carries its own Markdown sources in an inert
`<script type="application/vnd.marqora.folio+json">` block, which a browser neither executes nor
displays. Marqora finds it, unpacks the `.md` files and their images to a folder, and opens them
as tabs.

Send it to twelve people and it does not matter which of them has Marqora.

**Carrying the sources is nearly free.** The images are already in the file as data URIs on the
`<img>` elements, and the payload does not repeat them: each `<img>` carries a `data-mq-asset`
attribute naming its path, and unpacking reads the bytes back out of the `src` attribute already
sitting there. Only the Markdown text is added, which is nothing beside the pictures.

**There is no executable JavaScript in a Folio.** Moving between documents is
`<a href="#doc-3">`, and a collapsible contents list is `<details>`. That is not squeamishness:
mail gateways strip active content, and a page that needs script to show its own text is a page
that arrives blank. It also keeps faith with a shell whose Content-Security-Policy lists no
network origin at all.

### The payload is base64, and that is not decoration

A Markdown document about HTML contains the characters `</script>`. Written as raw JSON inside a
script element, the first occurrence of those nine characters ends the block — and everything
after it, which is the rest of somebody's document, is parsed as live markup and injected into
the page.

Base64 makes the block's body `[A-Za-z0-9+/=]` and nothing else, so no document content can
terminate it. It costs a third again on the Markdown text, and nothing at all on the images,
which are the only part with any weight.

---

## Why not simply a zip

A zip is offered, and for a set of two hundred screenshots it is the right answer. It is not the
default, for one reason: **the recipient has to have somewhere to put it.**

A zip is a thing you save, extract, and then need a Markdown reader for. An HTML file is a thing
you double-click. The person being sent a handbook usually wants to read the handbook, and asking
them to install an editor first is asking them not to read it.

The zip's real job is the other half of Resolve's naming — backup, and moving a document set to
another machine — where the recipient is you, the sources are the point, and nobody is reading
anything in a browser.

---

## No new file extension

A `.folio` extension would give Windows something to associate and would make the format feel
like a format. It is refused for the same reason the preferences file is plain `.json`, and the
argument recorded on `PreferencesDocument.FileExtension` applies with more force here: a file
every tool already understands beats one only Marqora knows.

The primary recipient of a Folio is someone who may not have Marqora. Handing that person a file
type their machine cannot open defeats the entire feature. The name lives on the menu item and on
the Folio's own cover page, which is where a name belongs.

The manifest inside follows the `PreferencesDocument` envelope exactly — `format`,
`schemaVersion`, `appVersion`, `exportedUtc`, `exportedFrom` — including the rule that the
version is not a gate: a Folio from a newer build unpacks everything this build understands and
reports what it could not use.

---

## What travels, and what gets rewritten

The rule is Resolve's: preserve structure where it already works, relocate only what has to move.

| Reference | What happens |
|---|---|
| Image resolving inside the document's own folder | Copied. **The relative path is left exactly as written** |
| Image outside that folder — absolute, `..\`, another drive | Copied into `media/`, and the Markdown rewritten to point there |
| The same image used by several documents | Collected once, matched by content hash |
| Two different images sharing a name | The second is relocated rather than allowed to land on the first; two relocated images sharing a name become `chart.png` and `chart-1.png`, through `DocumentAssets.NextFreeName` |
| An image that is not there | Named in the preflight. Nothing is written and nothing is invented |
| A link to a document that is in the Folio | Kept; in the reading copy it becomes `#doc-n` |
| A link to a document that is not | Named in the preflight, and left alone |
| `http://`, `https://` | Left alone, and never checked. Checking would be a network call |

**Leaving working paths untouched matters more than it sounds.** Flattening everything into
`media/` would rename files that were fine, destroy the `guide.assets/` convention that
`ImageFolderMode` exists to maintain, and guarantee that a document coming back out of a Folio
differs from the one that went in. Re-share that copy and the paths churn again. Relocating only
what was already broken makes the round trip close to lossless, and makes re-sharing idempotent.

### Rewriting by position, not by search and replace

References are rewritten using the `SourceLine`, `SourceColumn` and `Length` that
`LinkReference` already carries from the parse, applied in descending order so that earlier edits
do not move later ones.

The tempting alternative is `AssetRelocation.Rewrite`'s: a whole-text replace of `](old)` with
`](new)`. It is simpler and it is wrong — it rewrites matching text inside fenced code blocks, so
a document *about* Markdown, of which this repository contains several, is quietly corrupted.
Markdig never reported the contents of a fence as a link, so working from positions cannot make
that mistake.

---

## The preflight

Resolve shows you what it is about to archive, and says what is offline. Nothing is written until
this has been seen and accepted.

```
Documents      [x] getting-started.md   [x] setup.md   [ ] scratch-notes.md   ...
               pre-checked to every open tab       [Select all] [None]

Images         34 - 28 alongside the documents, 6 collected from elsewhere
Missing         2   getting-started.md:41 -> diagrams/old-flow.png          !
Outside links   3   setup.md:12 -> ..\..\notes\scratch.md                   !
Estimated size  8.4 MB

Share as   (o) One file others can read without Marqora       Team Handbook.html
           ( ) A zip of the Markdown and images               Team Handbook.zip

[ ] Shrink images wider than [ 1600 ] px         8.4 MB -> 2.1 MB
```

The size line earns its place. Mail still stops at about 25 MB, and a handbook full of
screenshots passes that without trying.

### Three things the dialog has to say out loud

Each of these is a silent-corruption bug if it is left implicit, and each was found by asking
what the file does rather than what the code does.

**Shrinking and the round trip are in conflict.** Downscaled images are what gets embedded, so
downscaled images are what unpacking hands back — while the author believes they have their
originals. Re-share, and quality is lost a second time. So shrinking says, in one line, that
restored images will be the shrunk ones. Carrying the originals in the payload as well is the
obvious fix, and defeats the entire point of shrinking.

**Images the packager cannot embed have to be counted.** Today an image over 8 MB, or one outside
the document's folder, becomes a dead link with no warning. In a Folio it is either embedded or
listed as excluded, never dropped. The ceiling is raised for Folios specifically: the preflight
now shows the total, and the author is consenting to it.

**A single page has a practical ceiling**, and it is a browser layout problem before it is a file
size problem. Past a threshold the dialog steers to the zip rather than producing something that
technically contains everything and opens badly.

---

## Rendering documents that are not on screen

This is the one genuinely hard part.

Exports come from the **live preview**, which is what makes them trustworthy: mermaid is already
inline SVG, KaTeX has already laid the math out, and code is already highlighted, so an export
cannot disagree with what was on screen. But eleven of twelve documents in a Folio are not on
screen, and one of them may not have been rendered at all.

| Approach | Why not |
|---|---|
| Activate each tab in turn and export it | Visibly flickers, is slow, and covers only documents that happen to be open |
| Render with Markdig on the host | Fast and wrong: mermaid and KaTeX are JavaScript, so every diagram and equation is lost |

**What is done instead** is a new bridge request that renders Markdown into a *detached*
container in the shell, runs mermaid, KaTeX and highlight.js over it, and returns the HTML —
without the visible preview being touched. It reuses the whole existing pipeline rather than
growing a second one, and `GetRenderedHtmlAsync` is the precedent for the request-and-reply
shape, keyed by request id.

**These renders are serialized, not run in parallel.** Mermaid runs in a single off-screen frame
that the shell reaches into to copy the finished SVG back out; twelve concurrent renders would
race over one frame. See *Why mermaid runs in an iframe* in `Architecture.md`.

**Headings are namespaced per document.** Twelve documents in one page will contain more than one
`#introduction`, and the outline already knows what slug each heading emits.

---

## Reading a Folio back

**Detection is a marker in the head, not a search.** `<meta name="marqora-folio" content="1">`
sits next to the `generator` meta that `HtmlExporter` already writes, so recognizing a Folio is a
four-kilobyte read rather than a scan of a twenty-megabyte file for a block that lives at the end
of it.

**Routing needs care.** `MarkdownFileTypes.Extensions` does not list `.html`, and its own comment
says the list is shared by the dialog and the drop handlers — so `File > Open` and both drop
routes reject a Folio today. Adding `.html` to that list is the wrong fix; it would make Marqora
open any web page as Markdown. The marker check goes *ahead* of the extension test, on the WinUI
`Drop` handler and on the preview's `NavigationStarting` interception alike.

**A Folio is the only untrusted input Marqora reads.** Everything else in the app comes off the
user's own disk; this arrives by email. So:

- every path in the payload is checked to be relative and inside the chosen folder before
  anything is written, using the same containment rule the rest of the app uses;
- bytes decide each file's extension through `ImageFileTypes.ExtensionFor`, as
  `DocumentAssetStore` already does, so a renamed executable is refused;
- the document count and the total decoded size are capped, because a payload claiming ten
  thousand documents or two gigabytes should be refused rather than attempted;
- nothing is written over anything. `FileMode.CreateNew`, as the asset store already does.

---

## What is reused, and one thing that is not

| Reused | For |
|---|---|
| `LinkReference` (`DocumentOutline.cs`) | Every link and image with its position, collected during a parse that already happens |
| `LinkChecks` | Missing images and dead links — the preflight's warnings, already written and already tested |
| `RenderedHtmlPackager` | Stylesheets, conditional KaTeX and highlight themes, image inlining, font embedding |
| `DocumentAssets` | `Slug`, `NextFreeName`, and the percent-encoding that keeps a destination parseable |
| `PreferencesDocument` | The envelope: format name, schema version, and the stamped file name |
| `PdfExportDialog`, `IExportDialogService` | The shape of the dialog and its service seam |
| `ImageScaling`, `ClipboardImage.ResizeForFileAsync` | The shrink control |

**`AssetRelocation` is the one that looks reusable and is not.** It is the closest thing in the
codebase — it plans which images a Save As has to carry, and produces exactly the sort of move
list a Folio wants. But its `Plan` cannot do this job:

- `IsRelative` rejects any reference carrying a scheme, so an absolute `C:\...\chart.png` is never
  considered;
- `Contained` drops anything resolving outside the document's folder, so `..\..\Pictures\x.png`
  goes too;
- `Owns` narrows it further, to references beginning with the document's own asset folder — a
  shared `images/chart.png`, or a plain `chart.png` beside the document, is deliberately excluded,
  because moving a shared image would leave a sibling document pointing at a file now in two
  places.

Which is to say: **the one thing that makes this feature worth building — collecting an image
from outside the document's folder — is precisely what `AssetRelocation` refuses to do**, and
refuses for a good reason that Save As still depends on. It is not a filter to widen. The Folio
gets its own planner, in the same pure paths-in-plan-out shape, and the containment and scheme
tests are extracted so that there is one copy rather than two.

---

## Sharp edges

- **The "is this inside that folder" test now exists in five places** — `DocumentAssetStore`,
  `WebViewPreviewHost.ResolveDocumentFile`, `RenderedHtmlPackager.EmbedLocalImages`,
  `LinkChecks.Exists`, and now this. All five append a directory separator before comparing, so
  that `C:\docs2` does not read as being inside `C:\docs`. Extract it; a sixth copy written from
  memory is where that bug comes back.
- **An untitled document has no folder** for a relative path to resolve against, so it cannot be
  collected. It is excluded and the dialog says why, as image paste already does.
- **`PastedImageTracker` moves undone images to a recycle folder** and stops tracking at the first
  save. Collecting saved documents is safe; collecting an unsaved buffer can race an undo that is
  still in flight.
- **`ImageFolderMode` has three layouts**, and a Folio gathering documents from several folders
  will meet more than one of them at once.
- **Do not quote `PreferencesDocument`'s extension comment verbatim in this file.** It contains a
  British spelling, `Set-AmericanSpelling.ps1` scans `*.md`, and its word list grows as words turn
  up — so the quote is safe today and would be silently rewritten the day someone adds that stem.
  `CLAUDE.md` avoids the same trap by describing its examples rather than showing them.

---

## Deliberately left out

- **A hosted viewer, an upload, or a share link.** It would be the first network call in the
  product's history, against a promise defended everywhere else, including at the cost of a real
  update check. The self-contained file *is* the share link.
- **A `.folio` extension and a ProgId.** Argued above. `build/Register-FileAssociation.ps1` is
  where it would go if this is ever revisited.
- **Recursive folder collection.** `Open Folder` is deliberately not recursive, so that pointing
  it at a repository does not produce hundreds of tabs. A Folio has no business being greedier.
- **Editing a Folio in place.** Unpack it, edit it, share it again. A file that rewrites itself is
  a synchronization problem nobody asked for.
- **Fixing `AssetRelocation.Rewrite`'s fenced-code bug.** It is real, it is not this feature's to
  fix, and it is recorded here so that the next person finds it written down rather than by having
  a cheatsheet mangled.

---

## What to test

The only case that proves anything is the awkward one. Build a folder holding: an image beside
the document; an image in a shared `images/` folder used by two documents; an image referenced by
absolute path from somewhere else entirely; an image that does not exist; an image over 8 MB; two
different images both named `chart.png`; a document containing the literal text `</script>`
inside a fenced code block; a mermaid diagram; an equation; a link to another document in the
set; and a link to one outside it.

1. The preflight counts the images correctly, and names both the missing one and the outside link.
2. Opened on a machine that has never had Marqora: every image present, diagrams and math drawn,
   the contents list navigates, the `</script>` document renders as text rather than breaking the
   page, and the browser's network tab stays empty.
3. Dragged back into Marqora: the unpacked documents match what went in, byte for byte wherever
   nothing needed rewriting, and the relocated paths resolve.
4. Sharing that unpacked copy again does not churn the paths a second time.
5. The zip opens in any editor with its images intact.
