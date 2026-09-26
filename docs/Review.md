# Review: commenting on a document without changing it

A reader who wants to comment on someone else's markdown used to add a callout, save the file
as a copy and send the copy back. This records what replaced that, why each piece is shaped the
way it is, and what was tried on paper and dropped.

> **Where this has got to.** Built, with tests for everything that is not a control or the
> page: the session (`ReviewSession`), the lock (`ReviewLockTests`), the CriticMarkup writer and
> the review page's text parts. The sidebar, the page's selection handling and Share build and
> were code-reviewed adversarially, but they have not yet been walked through by hand in the
> running app. Resuming from a shared page is the same (see its section).

---

## The four decisions

| Question | Answer |
|---|---|
| Where do the comments live? | **In memory until the first Share; after that, in the page.** Nothing on disk until Share, and a shared page can be resumed (see *Resuming from a shared page*) |
| What does the author get? | **One read-only `.html` page**: the preview as reviewed, each comment highlighted and its note in the margin |
| What does an AI get? | **The same file.** It carries the reviewed source with the comments written in as CriticMarkup |
| Can the author reply? | **No.** One way: the reader comments and sends it back |

The second answer is the one that carries weight, and it came from a correction. The first
design sent a list of notes with line numbers. But the author keeps editing while the reader
reads, so by the time the notes arrive every line number is wrong. The comments have to travel
**frozen together with the text they were made on**, and a page that is a snapshot of that text
does that without any format of Marqora's own.

A shelved design (2026-09-17) kept comments in a `.review.yaml` sidecar with threads and a round
trip. Nothing from it is reused. What made it expensive was persistence, a file format, and a
save lifecycle with a live data-loss bug, and this design has none of the three.

---

## The flow

1. **Start.** Review > Start Commenting, or Ctrl+Shift+R. The document is refused on two
   grounds:
   - it has unsaved edits and a file (those edits would be stranded, see below);
   - another document is already under review (one at a time).

   Untitled text is allowed: pasted AI output is the common case, and it has no autosave to
   lose. The source is locked, and the window switches to the preview alone: comments are made
   there, and a locked source beside it is only in the way. The view the reader had comes back
   when the review ends, unless they chose another in the meantime. A banner says why typing does
   nothing. It can be closed, and each message stays closed once closed, while news - the file
   changing on disk, a review saved - still shows the first time it is said.
2. **Comment.** The reader selects a passage in the preview. A small **Add comment** button appears
   above it; the button or Ctrl+Shift+M opens a card in the sidebar with its box focused.
   Ctrl+Enter or Save keeps the note - both wait for more than whitespace - and Escape cancels
   it. Tab goes from the box to Save, then Cancel. Hovering a comment in the preview lights its
   card, and hovering a card lights its comment. Share Review... and Copy as Markdown stay grayed
   until there is a saved comment to send.
   Double-clicking a comment in the preview opens its card for editing.

   A comment may carry five styles, written the way the preview reads them: `**bold**`,
   `*italic*`, `++underline++`, `==highlight==` and `` `code` ``, or put on the selected words with
   Ctrl+B, Ctrl+I, Ctrl+U, Ctrl+Shift+H and Ctrl+Backtick (`CommentMarkup.Toggle`). One parser,
   `CommentMarkup`, reads them for both places a comment is shown: the card builds its runs from
   it, and the shared page is handed each note as HTML from it rather than parsing the markup a
   second time in the page. A marker with no partner is text; the CriticMarkup copy keeps the
   markdown as written.
3. **Share Review...** writes `<name> (review by Marqora).html`. The first time the save dialog opens in
   Documents, and after that wherever the last one went. Windows remembers it through the
   dialog's own client id, so no setting holds it. The banner then offers **Show in Folder** and
   **Copy File**; the second puts the file itself on the clipboard, so pasting into a message
   attaches it.
4. **Copy as Markdown** puts the reviewed source, with the comments written in, on the clipboard
   for pasting into an AI. It counts as sharing.
5. **End.** Final for anything never shared: there is no restore of comments that were only in
   memory. With comments nobody has seen, the sidebar's footer turns into the question in place,
   with Discard and End or Keep Commenting. Closing the tab or the app asks the same thing as its
   own prompt, before any save prompt.
6. **Resume.** A shared page dropped back in, or opened with Review > Resume Shared Review...,
   takes the review up again on the text it holds - see *Resuming from a shared page*.

---

## Holding the document still

A comment is anchored in the text as it stood when the review began. A document that moved under
its comments would misplace every one of them, silently. So a review folds into read-only:

```csharp
public bool IsReadOnly => (IsLocked && External != ExternalState.Missing) || IsUnderReview;
```

That one line brings every refusal the read-only mark already has, with no second list of call
sites to keep in step: the buffer, the editor, save, autosave, Replace All, the formatter and
heading renumbering. Messages say "Commenting" rather than "Read-only" for a document under
review (`ReadOnlyWord`), because the reader did not mark it.

Three paths needed more than that, and each was found by reading rather than assumed:

| Path | Why read-only did not cover it | What happens instead |
|---|---|---|
| Reload | `ReloadCoreAsync` assigns the text directly and never reaches `ApplyEdit` | Refused while under review, and `ReloadAsync` now answers `bool` so callers stop announcing a reload that did not happen |
| Automatic reload | Same path, from the watcher | Recorded as `Changed` rather than taken. Ending the review settles it the way the watcher would have |
| Save As | Deliberately unguarded, and its image relocation rewrites the text through `ReplaceTextAsync`, which ignores Monaco's `readOnly` | Refused while under review |

A review holds even over a deleted file, where the mark stands down. The missing-file clause
exists so the buffer can be written back, and a reviewer who wants that has End one click away.

Starting a review on a dirty document that has a file is refused rather than allowed. Read-only
takes save and autosave with it, so its unsaved edits would sit unwritable for the whole review.

The close prompt asks about unshared comments **first and separately**. Folded into the save
prompt, answering Save As there would also throw the comments away without asking; and an
untitled document under review would deadlock, because its read-only save prompt offers only
Save As, which a review refuses.

---

## Anchoring in the preview

Every block Markdig renders carries `data-src-line`, the zero-based source line
(`SourceLineExtension`). An anchor is:

- **`Line`**: the innermost block's line;
- **`Index`**: which of the elements carrying that line. Every cell of a table row shares the
  row's line, and a loose list item shares its line with its paragraph;
- **`Start`** and **`End`**: character offsets in that block's text;
- **`Quote`**: the selected text.

A selection may not span two blocks, with one exception the mouse makes on its own: a
triple-click ends at offset 0 of the next block, and is pulled back.

"The block's text" is measured by one walker, used both to take an anchor and to put one back,
so the two cannot disagree about where character 40 is. It skips two things:

- a **heading number**, which Marqora writes into the render rather than the source, and which
  can be switched on or off mid-review;
- **KaTeX's hidden MathML** copy of each equation.

**The comment number is generated content.** `mark.mq-comment[data-n]::after` draws it; it is
never a text node. A `<sup>2</sup>` inside the mark would be text in the block, so every offset,
quote and plain copy taken after the first comment would carry the digits with it, and adding a
comment above would turn a "9" into a "10" and shift every later anchor in the block.

**Marks are redrawn after every render.** The preview replaces its markup wholesale on each render,
theme change and tab switch, so `applyCommentMarks` runs when a render settles. It unwraps
everything first, so running twice draws each comment once. It waits for KaTeX: marks put in
before the auto-render can split a `\( … \)` across text nodes. If a block's text no longer
matches at the stored offsets, the quote is searched for on the same line.

**Marks never leave the page by the ordinary routes.** Print and PDF paint the live page, so
`@media print` neutralizes them. The HTML export, Word and Copy as Rich Text take a copy with the
marks unwrapped (`withoutCommentMarks`). The Comment button is fixed to the page, outside
`#preview`, so no copy of the article can contain it.

---

## The review page

`ReviewPageWriter` is `HtmlExporter`'s assembly (the same styles, embedded images, click-to-view
diagrams and page layout) with three additions from `ReviewPage`:

- **Margin notes.** The Tufte sidenote pattern: each note floats into a column reserved to the
  right of the reader's own measure, and at narrow widths it drops into the text after its
  passage. A note inside a link would become part of the link, so it goes after the mark's
  outermost inline element. A float cannot leave a table cell or a code block, so a note there is
  hosted just before the table or block. Hover pairs a highlight with its note by `:has()` on the
  note's number rather than by adjacency, because those hosted notes are not beside their
  highlights. Nothing in the page runs.
- **The stamp**: file name, when it was reviewed, the first seven hex digits of the source's
  SHA-256, and the comment count, so an author who has edited since knows which draft the
  comments are about.
- **The source block**: `<script type="text/markdown" id="mq-review-source"
  data-format="criticmarkup">`, inert and readable in View Source. Not base64 like a Folio's
  payload, because a person or an AI should be able to read it as it stands. That means the text
  must not be able to end the element: `</script`, `<!--` and `<script` each have a backslash
  put after their `<`. The second pair matters because a `<!--` followed later by `<script`
  moves the parser into a state where the real closing tag closes nothing. A reader turns `<\`
  back into `<`.

The page is light, like every export.

### CriticMarkup

`CriticMarkupWriter` puts each comment beside its words: `{==passage==}{>>comment<<}`. Rendered
text is not source text, so the passage is searched for:

1. **As it stands.** The search runs over a copy with link targets, URLs and code spans blanked
   out (`LineMasker`), so a link whose text is its own address is not wrapped inside the
   address.
2. **With markup skipped on both sides** (emphasis, `^ + =`, link brackets and targets,
   escapes, blockquote markers) and whitespace runs treated as one space.

Markers standing directly against the passage are taken inside the wrap, so `**bold** word` is
wrapped whole. That happens only at a word boundary, so `snake_case` is not cut in two. Anything
not placed inline becomes a standalone `{>>On "passage": comment<<}` after its block. That covers
text an emoji shortcode or entity changed on its way to the screen, a code block (never
altered), and a comment overlapping another. In a table row or heading, a note's line breaks
become spaces and pilcrows, and in a table row its pipes are escaped.

Marqora reads this markup back, too. `CriticMarkupPass` runs over the parsed document, so a reviewed
copy pasted into a tab shows its comments as comments: the passage classed `mq-critic` and drawn in
the comments' teal - code inside it included, so the passage is not broken into pieces - and the
note gathered into one element after it. Nothing is rewritten before the parse, so the positions the
analyzer and scroll sync depend on stay where they were.

---

## Things deliberately left out

- **Threads, replies and a round trip.** The flow is one way. The shelved design had them; they
  were most of its cost.
- **Comments on things that are not text**: an image, a diagram, an equation, a whole heading
  as a block. The Comment button does not offer itself over them.
- **The reviewer's name** on the page.
- **A dark review page.** Every export is light.
- **Restoring comments that were never shared**, and **surviving a crash**. Nothing is written
  before a share, which was chosen; a shared page is the only thing a review resumes from.
- **Passing a review between reviewers**, and **the author resuming it** as anything other than
  their own copy. Pages carry no names.
- **A resizable sidebar.** Fixed at 340; a splitter would be the next thing.

---

## Resuming from a shared page

> **Built 2026-09-25**, with tests for the state block, the pictures and the snapshot tab
> (`ReviewStateTests`, `ReviewAssetsTests`, `ReviewLockTests`). The drop, the share guard and
> the preview's pictures have not yet been walked through by hand in the running app.

A shared page holds the text that was reviewed, and now the comments' anchors too, so dropping
it back into Marqora puts the reviewer back in the same review: an hour later, days after End,
or on another machine. The anchors stay correct, because the text they point into is frozen.

**What it changed.** Two decisions above got narrower rather than overturned. "In memory, for
one sitting" describes a review until its first share; after that, the shared page is the
reviewer's save file. And End is final only for comments that were never shared.

### Settled

| Question | Answer |
|---|---|
| Who resumes? | Whoever drops the page in. It carries no names, so the author could too, on their own copy. No relays, no author view. |
| From where? | A drop on the window or the preview, or Review > Resume Shared Review.... Not the command line or Recent: `OpenPathAsync` is untouched, so a page is resumed only when someone hands it over. |
| What opens? | A tab with no file behind it (`Path` null), labeled `notes.md (review)` - `(review 2)` if that is open - clean, under review, its comments restored and counted as shared. Closing it straight away asks nothing. |
| Pictures? | The ones the page carries, held in memory and served to the preview and every export from there. Written only where the reader sends them, and for a Folio briefly into its temporary folder (see *Where a resumed review's pictures live*). |
| Sharing again? | The dialog opens on the page it came from, unless that is a temporary folder (an Outlook attachment, a zip) or read-only. |
| Two copies? | A page of this same review that another copy wrote since is never overwritten: only Save as New File is offered. |
| Pages from 1.0.10? | Refused, with an explanation. They carry the comments but not what resuming needs. |
| How does anyone find out? | Once a run, in the status after a share. The menu item. |

### The page

The head gains `<meta name="marqora-review" content="1" />`, so a dropped file is recognized
from its first 4 KB, the way a Folio is. The body ends with a second inert block, after the
CriticMarkup one: `<script type="application/vnd.marqora.review+json" id="mq-review-state">`,
base64 JSON for the reason a Folio's payload is base64. `ReviewState` holds it:

- `format`, `schemaVersion`, `minimumReader`, `appVersion`;
- `sessionId` - the review, across every page and sitting - and `writeId`, new on every share;
- `shareCount`, `startedUtc`, `sharedUtc` (UTC, so a page does not say where its reviewer is);
- `fileName` (the document's, never the tab's label), `sourceSha256` and `source`, exactly;
- `comments`, each with its id, anchor and note.

The CriticMarkup block cannot do this job. It is not reversible: comments that could not be
placed inline become standalone notes, and its escaping of `<script` loses text.

**The last block wins.** A document can hold raw HTML, so its own text could carry a block with
the same id, which would land in the article, before the real one. The reader takes the last:
the CriticMarkup block between them escapes every `<script`, so nothing the document says can
stand after the block Marqora wrote.

**Refused whole, never half-restored.** The block is capped in size before decoding, the text
must match its hash, and every anchor must point somewhere the text has - a line it has, a
non-empty quote, a start before its end - with unique ids, at most 5,000 comments and no note
longer than 100,000 characters. A file name from a page is stripped of folders, control and
direction-override characters before it becomes a tab's label or a suggested name.

**The version is not a gate**, as with a Folio. The generated reader skips fields it does not
know, so a page from a later Marqora is resumed for what this build understands - but sharing
it again never overwrites it, since that would drop what the later version added.
`minimumReader` is the gate a future format sets on purpose, when an earlier build must not
read it at all.

**Pictures.** Each image the page embeds from the document's folder carries `data-mq-asset`
naming the path the document wrote, as a Folio's do. `ReviewAssets` collects them from the
article alone - the CriticMarkup block is the document's own text - recognizes each by its
bytes rather than the type its data URI claims, and caps the count and total. One spelling of
each path, `ReviewAssets.NormalizeKey`, is used by the writer, the reader and the preview,
because each receives it written differently. A picture too large to embed was never in the
page, and is missing when the review is resumed.

### Two copies of one review

Two instances can resume the same page, and so can two machines working from one synced folder.
A share count cannot tell them apart - both copies count the same shares - so each share writes a
new `writeId`, and each copy remembers every write id it made or read. Share reads the page it is
about to write over. If that page is this review's and its write id is one this copy does not
know, another copy wrote it since, and only a new file is offered. Sharing to X, then Y, then X
again is not a conflict: all three are this copy's own.

What a document knows about its pages lives beside the session rather than in it
(`ReviewSnapshot` in `MainViewModel.Resume.cs`), because it is needed after End: the real file
name for Save As, the pictures for the preview, and the session id - a review started again on
the same text continues the same review, so its old pages are still recognized. It goes when the
tab closes, or when Save As gives the document a file of its own.

### Comments that cannot be placed

An anchor is taken from the preview, and a later Marqora may render the same text slightly
differently - emoji, or a change in Markdig. The shell's fallback of finding the quote elsewhere
on its line catches most of it. What it does not catch, it now reports after every draw: the
card says the comment is not on the page, the status says how many, and Share asks before
writing a page whose margin would leave them out. They stay in the source block and in the
state, so resuming again brings them back.

### Exporting a resumed review

Every export asks one lookup where a document's pictures are: `DocumentImages`, which answers
from the document's folder or, for a resumed review, from the pictures its page carried - and
never from a folder in that case, so a resumed review is not shown an unrelated file that
happens to sit where its picture once did.

| Export | How it gets a resumed review's pictures |
|---|---|
| Print, PDF | The live preview, which already serves them from memory |
| HTML, Copy as Rich Text | `EmbedLocalImages` asks `DocumentImages`; the same 8 MB ceiling applies to a page's pictures as to files |
| Word | `DocxImages` asks `DocumentImages`; WebP, SVG and AVIF are refused as they are from disk |
| Folio | A stand-in in the share's temporary folder - see `docs/Folio.md`, *A review resumed from its page* |
| Save As | The text only; the status says how many pictures stayed in the page |

A reference the page did not carry is left as written in an HTML export - a relative link - and
reported "Not found" by Word.

### Where a resumed review's pictures live

| Holder | Released |
|---|---|
| The tab's snapshot (`ReviewSnapshot.Assets`) | When the tab closes, or Save As gives it a file of its own. Kept after End, because the text still names them |
| The preview's copy (`WebViewPreviewHost`) | With the snapshot, and when the tab closes |
| The browser's cache | Never holds them: every `marqora.document` answer says `Cache-Control: no-store` |
| A Folio's stand-in | When the share ends; at shutdown if a preflight was open; at the next share or start if Marqora was stopped mid-share |
| The copy of a page being shared | `SafeFileWriter` writes the page whole elsewhere and swaps it in. On the temp folder's drive the copy is in a locked scratch folder, swept like a Folio's. On another drive it sits beside the page, hidden and locked while written, and a leftover is cleared the next time that page is shared or resumed |

Not Marqora's to clear, and said here so nobody assumes otherwise: Windows clipboard history
after Copy as Rich Text, Explorer's thumbnail cache, antivirus scanning, and the files the reader
exports.

### Left out

- **Video** a page embedded is not collected; only images are recognized by their bytes.
