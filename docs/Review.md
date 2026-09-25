# Review: commenting on a document without changing it

A reader who wants to comment on someone else's markdown used to add a callout, save the file
as a copy and send the copy back. This records what replaced that, why each piece is shaped the
way it is, and what was tried on paper and dropped.

> **Where this has got to.** Built, with tests for everything that is not a control or the
> page: the session (`ReviewSession`), the lock (`ReviewLockTests`), the CriticMarkup writer and
> the review page's text parts. The sidebar, the page's selection handling and Share build and
> were code-reviewed adversarially, but they have not yet been walked through by hand in the
> running app.

---

## The four decisions

| Question | Answer |
|---|---|
| Where do the comments live? | **In memory, for one sitting.** Start Commenting to End Commenting; nothing on disk until Share |
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
   when the review ends, unless they chose another in the meantime.
2. **Comment.** The reader selects a passage in the preview. A small **Add comment** button appears
   above it; the button or Ctrl+Shift+M opens a card in the sidebar with its box focused.
   Ctrl+Enter or Save keeps the note - both wait for more than whitespace - and Escape cancels
   it. Tab goes from the box to Save, then Cancel. Hovering a comment in the preview lights its
   card, and hovering a card lights its comment. Share Review... and Copy as Markdown stay grayed
   until there is a saved comment to send.
3. **Share Review...** writes `<name> (review).html`. The first time the save dialog opens in
   Documents, and after that wherever the last one went. Windows remembers it through the
   dialog's own client id, so no setting holds it. The banner then offers **Show in Folder** and
   **Copy File**; the second puts the file itself on the clipboard, so pasting into a message
   attaches it.
4. **Copy as Markdown** puts the reviewed source, with the comments written in, on the clipboard
   for pasting into an AI. It counts as sharing.
5. **End.** Final, by decision: there is no restore. With comments nobody has seen, the
   sidebar's footer turns into the question in place, with Discard and End or Keep Commenting.
   Closing the tab or the app asks the same thing as its own prompt, before any save prompt.

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

---

## Things deliberately left out

- **Threads, replies and a round trip.** The flow is one way. The shelved design had them; they
  were most of its cost.
- **Comments on things that are not text**: an image, a diagram, an equation, a whole heading
  as a block. The Comment button does not offer itself over them.
- **The reviewer's name** on the page.
- **A dark review page.** Every export is light.
- **Restoring comments after End**, and **surviving a crash**. Both follow from "in memory, for
  one sitting", which was chosen.
- **A resizable sidebar.** Fixed at 340; a splitter would be the next thing.
