# Export alignment plan — Word and PDF

**Status:** revision 2, 2026-10-06, after an adversarial review by two reviewers (§12). The
four decisions the review raised are settled (§2), and the §8 values are approved.
**Progress (2026-10-06):** phase 0 is in the working tree, uncommitted. W1–W8 are done and tested (`FixtureExportTests` validates the fixture in all four numbering modes); P1–P4 are done; P5 waits on a measured export. The engine spike (§4) runs behind a Debug-only menu (File > Export > Paged PDF engine): S1 to S6 are done; S6 limits Word's SVG to an allowlist of ten diagram types (§7.2).
**Evidence:** `docs/WordVsPdf-Comparison.md`, the side-by-side of `UltimateMarkdownContent.md`.
**Goal:** a document exported to Word and to PDF comes out as close to the same document as
the two formats allow. Each export takes what the other does better, and the bugs in both
are fixed on the way.

---

## 1. Decisions taken

| # | Question | Decision |
| --- | --- | --- |
| D1 | Shared visual target | **One paper spec**, stated once, read by both exports |
| D2 | PDF engine | **Paged.js** in a **hidden print WebView**, then DevTools `Page.printToPDF` |
| D3 | Cover, contents, header and footer | **Offered by both dialogs, from one shared record** |
| D4 | Footnotes | **At the page foot in both** (Paged.js `float: footnote` in the PDF) |
| D5 | Heading numbers | **The documented design everywhere**: a heading above the first numbered level restarts the count. Word changes |
| D6 | Math in Word | **Fix the OMML conversion, with a picture of the KaTeX render as the fallback** |
| D7 | Diagrams in Word | **SVG with a PNG fallback**, where Word can draw the SVG (§7.2) |
| D8 | Raw HTML | **Word mirrors the preview**, inline styles included |
| D9 | Verification | **Validator test on the fixture** and a **paper-spec test**, plus the additions in §9 |
| D10 | Order | **Bugs first**, with the engine spike run alongside them (§4) |

### What these reverse

Each of these is a written decision. The phase that lands the change also rewrites the
sentence.

| Written decision | Where | Reversed by | Phase |
| --- | --- | --- | --- |
| *"Print is the live page, not the HTML export printed in a hidden view. That was weighed and rejected,"* with five reasons | `docs/Architecture.md`, *Screen and output*, decision 3 | D2. **Each reason is answered in §6.1**; the answer is a print-specific markup, not the HTML export | 2 |
| *"Print and PDF print the live page, as they always have"* | `docs/Architecture.md`, *Screen and output* | D2 | 2 |
| *"The visual target is Word-idiomatic, not a twin of the PDF"* | `docs/WordExport.md` §1 | D1 | 1 |
| *"No sensible place for a cover page or a table-of-contents field on the PDF dialog"* | comment on `DocxExportSetup` | D3 | 2 |
| *"A template, not a statement. Every line is present"* (title page) | `docs/WordExport.md` §4 | §6.4: an unfilled line is left out | 2 |
| *"Aligned equations become a matrix … not an `eqArr`"* | `docs/WordExport.md` §13 | §7.1 | 3 |
| `ExportReportWindow` *"serves both Word export and the Folio share"* | `CLAUDE.md`, *Secondary windows* | §6.5: the PDF report | 2 |
| One `w:num` per list is enough to restart it | `NumberingPlan.cs:32-34`, `docs/WordExport.md` §6 | W3 | 0 |

---

## 2. Decisions taken after the review

| # | Question | Decision |
| --- | --- | --- |
| **O1** | **Which faces does the paper spec use?** The review found that **Aptos is not a Windows font**. It exists only under `%LOCALAPPDATA%\Microsoft\FontCache\4\CloudFonts`, which Office uses and WebView2 cannot see, so a spec naming Aptos would give two different looks. | **Segoe UI Variable Text, then Segoe UI, for both exports, with Cascadia Mono for code.** **Each face is defined in exactly one place** (§8, *Faces*): every element names a role, not a font, so changing a face later is a one-line edit that Word, the PDF, every printout and the diagram labels all follow. S4 confirms what WebView2 can see. |
| **O2** | **Do the cheatsheet's Print and the diagram pop-out's Print and PDF move to the new engine?** They print through `WebViewPrinting` today (`CheatsheetWindow.cs:379`, `DiagramWindow.cs:667`, `:749`). | **They move too.** Every printout goes through the print host, which keeps *"all three printouts agree"* true rather than rewriting it. §6.6 covers the two extra jobs. `WebViewPrinting` keeps only what the host calls. |
| **O3** | **What is the default for header and footer in the shared record?** Word defaults it on (`DocxExportSetup.cs:43`), so sharing the record turns a page-number footer on for every PDF from the next release. | **On.** It changes only new exports, never a user's files. The release notes say so. |
| **O4** | **If Paged.js cannot place one footnote at the page foot, what happens?** | **The whole document's PDF footnotes become endnotes, and the report says why.** One model per document, never a mix. |

---

## 3. What "aligned" means

Two exports are aligned when the following hold for the fixture and for any ordinary
document:

1. **Same content.** No paragraph, list item, table cell, equation, diagram or footnote is in
   one and not the other. Where something cannot be carried, both exports report it, in the
   same words.
2. **Same structure.** Same heading levels and numbers, same list numbers and depth, same table
   rows and columns, table header rows repeated on every page, footnotes numbered the same and
   placed the same way.
3. **Same look, by spec.** Faces, sizes, weights, spacing and colors come from the paper spec
   (D1). Not pixel-identical: Word and Chromium break lines differently, so pages will not
   fall at the same words.
4. **Same furniture.** Same cover page, contents with page numbers, running header and
   page-number footer, for the same choices in the dialog.
5. **Same navigation.** Clickable links and internal links, a bookmarks pane, a real title,
   author and language, and tagging for screen readers in both.

### Differences we keep on purpose

| Difference | Why it stays |
| --- | --- |
| Line and page breaks fall in different places | Two layout engines. Pixel parity is not the goal |
| Word's equations are editable OMML; the PDF's are KaTeX | Editable math is the point of a `.docx`. Where OMML cannot match, Word gets a picture (D6) |
| Word's contents listing is a field that updates on open | That is how Word contents work. The PDF's is computed once, at export |
| A skipped heading level: the preview reads "9.1", Word "9.0.1" | `HeadingNumbers` drops the missing level's zero (`HeadingNumbers.cs:16-20`); Word's `lvlText` cannot. Documented, with a test that pins it |
| Some mermaid diagrams are pictures in Word and vectors in the PDF | §7.2: Word cannot draw labels held in `foreignObject` |

---

## 4. Week one: the engine spike (S), run alongside phase 0

D2, D3 and D4 all depend on Paged.js running in a WebView2 that is never shown. The design
reviewer found that this is not a given:
- A controller with `IsVisible = false` *"is not rendered"*, and Chromium throttles the page.
- Paged.js advances on `requestAnimationFrame`, on a `ResizeObserver` callback, and on
  `requestIdleCallback`.

So the spike comes first, and the phase 2 gate is in numbers.

| # | Spike | Pass when |
| --- | --- | --- |
| S1 | A WebView2 controller from WinUI 3, three ways: `IsVisible = false`; visible with bounds outside the parent's client area; and in a cloaked, owned window. Paged.js 0.4.3 on the fixture in each | It finishes without stalling, at a usable number of pages per second (measured; target under 10 s for the fixture's ~45 pages) |
| S2 | Paged.js on the hard cases: a table taller than a page, a `pre` two pages long, the 141-column fence, tall diagrams, a footnote holding a paragraph and code, roman then arabic page numbers, contents page numbers by `target-counter` | No hang (Paged.js issue #340 is a known chunker hang with avoid-blocks); header rows repeat with a handler (#36 is open); page numbers correct across the reset (#91) |
| S3 | `Page.printToPDF` from that controller, with `preferCSSPageSize`, all margins 0, `generateTaggedPDF`, `generateDocumentOutline`, `transferMode: "ReturnAsStream"` | A PDF with an outline and tags, at 100% scale (body line pitch ÷ line height = the spec's size) |
| S4 | Fonts: whether WebView2 can draw each face O1 names, measured against a generic fallback | Every face in the spec is visible to both engines |
| S5 | Paged.js output to a real printer through `CoreWebView2.PrintAsync`, with page size equal to `@page` and margins 0 | No double margins and no rescaling; hardware margins do not clip the footer |
| S6 | Word with five light mermaid SVGs, with and without `foreignObject`, with computed styles inlined | Know, per diagram type, whether Word draws it |

**If S1 to S3 fail**, D2 cannot be delivered as chosen. The fallback is `Page.printToPDF` of the
live page. That gives page numbers through `footerTemplate`, an outline, tags and the right
scale, but footnotes stay endnotes and the contents page has no page numbers. That would
reopen D4, and Paul decides at that point, not this plan.

### Results so far (2026-10-06, fixture, Letter, Normal margins)

| # | Result |
| --- | --- |
| S1 | **`IsVisible = false` stalls**: no PDF and no error until the layout timeout, as the review predicted. **Off-screen works**: 61 pages laid out in 1.07 s, printed in 2.44 s, 3.0 MB. The host uses the off-screen view; the hidden one is no longer offered |
| S2 | Wide code wraps with nothing lost; `<details>` opens; math and diagrams exact; styled HTML kept. Paged.js ignores `break-after: avoid` and does not repeat table headers (#36); `print.js` now has a handler for each, and the second run confirmed both: "Setext H1" no longer ends a page, and the scorecard repeats its header row at the first page's column widths. A third gap: a tall diagram after a heading still filled its page and left the heading alone, because Paged.js rewrites `+` rules at lower specificity and the heading room was lost; fixed by class, and the third run confirmed it (page 32 holds the heading and its diagram). **Cover, contents and page-foot footnotes pass** (2026-10-07): the cover leaves unfilled lines out and carries no number; the contents lists the `ContentsListing` levels with leaders and correct page numbers; all three fixture notes sit at the foot of their page, numbered as Markdig numbers them, note 2 with its second paragraph and code block. Two Paged.js faults met on the way: **its page counter honors a reset for one page only** (the body went 1, 5, 6 …, and the contents pointed four pages early), so pages are now labeled after layout and the footer and contents both read the label; and **a mermaid class name collided with Markdig's** (a git-graph commit named `footnotes`), so the notes group is found by its exact shape. Footnote calls are forced to a real superscript, since Segoe UI has no superscript glyphs |
| S3 | **Tagged and outlined** (`/StructTreeRoot` and `/Outlines` read from the file), **true scale** (body line pitch 17 pt ÷ 1.55 = 11 pt), Title from the front matter, running header and page numbers drawn by Paged.js |
| S4 | **Segoe UI Variable Text, Segoe UI and Cascadia Mono are visible to WebView2; Aptos is not** (measured, second run). Confirms O1. The first run used `document.fonts.check`, which says yes to any face it has no web font for |
| S5 | **Passes.** Microsoft Print to PDF took the paged pages at once, Letter at 1:1, identical to the paged PDF. An **HP Color LaserJet Pro M478f hung on zero margins**: both pages reached the spooler and Chromium never closed the job, which spooled until its process was killed. Fixed by `PrinterEdgeInches`: printer pages are laid out 0.25 in smaller on every side with margins 0.25 in smaller, and the printer is asked for 0.25 in, so nothing moves on the paper. The HP then printed in 3.6 s; header and page number clear the edge; text block measured at 6.5 in. Printout and PDF are one layout: both 59 pages for the fixture (the 61-page runs predate the diagram fix) |
| S6 | **Done** (2026-10-07): the fixture's 24 light drawings, saved by the Debug menu and placed by Word 365. **Word draws 10 types correctly**, text included: sequence, gantt, pie, quadrant (title slightly clipped), C4, sankey, XY chart, packet, architecture, radar. **Every type with labels in `foreignObject` loses all its text** (class, state, ER, journey, block, kanban, flowchart): shapes, no words. **Mermaid's class-based `<style>` rules are not honored**, only the one keyed on the diagram's id, so the timeline, treemap and git graph lose fills or text even without `foreignObject`. **Two drawings Word cannot place at all** (a flowchart and the mindmap: an empty 75 pt box). See §7.2 |

Found on the way: **Paged.js 0.4.3 rewrites every rule holding a `+`**. It splits the selector list on every comma, including commas inside `:is()`, so the fragments are not selectors; and it re-emits the rule as an attribute rule of lower specificity, so a `+` rule can lose to rules it used to beat. The one such rule in the print sheets now lists its six headings, and its effect is asked for by class in the paged print page. A §9 check should keep `+` out of the print sheets.

---

## 5. Phase 0 — fix what is broken today

Every Word item lands against the current exporter. The PDF items are chosen so that they carry
into phase 2: CSS in `app.css`, which `print.html` will also load, rather than steps in the live
page's print handshake.

### Word

| # | Defect | Fix | Where |
| --- | --- | --- | --- |
| W1 | A footnote's hyperlink is registered on the main part, so Word asks to repair the file | Several changes are needed, all scoped to the part that owns the paragraph: <br>• Create the `FootnotesPart` **before** the first note is rendered; today it is created lazily, after the note (`DocxFootnotes.cs:74` vs `:107`, `:138`). <br>• Give `InlineRenderer` and `DocxImages` a "current part" that `DocxFootnotes.Write` switches; both take `MainDocumentPart` in their constructors today. <br>• Add relationships to that part. <br>• Key the image cache per part, or call `owner.AddPart(existing)`, because ids are per part (`DocxImages.cs:37-38`). <br>• Diagram bytes go through the same scope (`TryBuildFromBytes`) | `InlineRenderer.cs:27`, `:291`, `:447`, `:485`; `DocxImages.cs`; `DocxFootnotes.cs` |
| W2 | A second `w:ind` after `w:contextualSpacing`, or a `w:ind` (and `w:numPr`) after `w:jc` | The cause is that `WriteList` reformats every paragraph a non-list child produced (`BlockRenderer.cs:316-337`): <br>• A quote holding a list has already had its paragraphs formatted by the inner list. <br>• A diagram's or math fallback's `pPr(jc)` gets `ind`/`numPr` appended after `jc` (`:789-793`, `:1027-1030`). <br>Fix: <br>• Skip paragraphs that already carry numbering or indentation from a deeper list. <br>• Use the typed setters (`NumberingProperties`, `Indentation`, `ContextualSpacing`), which insert in schema order. <br>• Keep `Quote` style when a quote is inside a list (`:366-369` vs `:722-725`). <br>This also fixes the matryoshka flattening | `BlockRenderer.cs` |
| W3 | Every ordered list continues the previous one's count | `BuildInstance` writes a `startOverride` for **every level** of the instance, not only level 0: a list whose first item is a task item numbers its first paragraph below level 0 (`BlockRenderer.cs:371`). <br>Also correct the class comment and `WordExport.md` §6, and replace `Two_separate_ordered_lists_do_not_continue_each_other`, which only checks that the ids differ | `NumberingPlan.cs:452` |
| W4 | A link target with spaces prints "Error! Hyperlink reference not valid." | Escape with `Uri.AbsoluteUri` at the three `AddHyperlinkRelationship` calls | `InlineRenderer.cs` |
| W5 | `[broken][no-such-ref]` loses its `[` | A leftover `LinkDelimiterInline` writes its literal, `[` or `![` (`IsImage`), before its children. The same fallthrough drops it from alt text | `InlineRenderer.cs:209`; `Rendering/InlinePlainText.cs:77` |
| W6 | `&shy;` prints as a hyphen | Split runs on U+00AD and U+2011 into `<w:softHyphen/>` and `<w:noBreakHyphen/>`. Not in `XmlSafeText`, which returns a string | `RunFormat.ToRun` (`RunFormat.cs:194`), `BlockRenderer.CodeRun` (`:982`), header title (`DocxFurniture.cs:51`) |
| W7 | Word ignores the restart (D5) | Every level above the first numbered one joins Word's heading list as an **unnumbered** level (`lvlText=""`, `suff=nothing`); `FromHeading3` has two. <br>The numbered levels' patterns skip the empty ones (`%2`, `%2.%3`; `NumberingPlan.cs:338-347`). <br>`DocxStyles.HeadingStyle` maps `ilvl` from H1, not from the start (`DocxStyles.cs:337-343`). <br>`TOCHeading` gets `NumId=0` in every mode, because it is based on Heading1 (`:733-745`). <br>The `\o` range is unaffected. A test pins the skipped-level difference (§3) | `NumberingPlan.cs`, `DocxStyles.cs` |
| W8 | A raw HTML block is dropped without a report | Until §7.3, write its text as paragraphs and add a report row. Skip `HtmlBlockType.Comment` and blocks with no text once tags are stripped; the fixture has six comments and the `<details>` tag lines. `MarkdownMediaReader.cs:65-71` filters the same way | `BlockRenderer.cs:219` |

### PDF

| # | Defect | Fix | Where |
| --- | --- | --- | --- |
| P1 | Wide code is cut off at the margin | On paper: <br>• `pre` wraps with `white-space: pre-wrap; overflow-wrap: anywhere`. <br>• Wide tables wrap their cells rather than widening the layout (`.mq-table-scroll` is `overflow: visible` on paper). <br>• `.katex-display` gets the same treatment | `app.css` print block |
| P2 | `<details>` prints collapsed | CSS only: `details::details-content { content-visibility: visible; display: block }` (Chromium 131+). Nothing opens in the live window, and it carries into `print.html` | `app.css` print block |
| P3 | A callout splits across a page | Add `.markdown-alert` to `break-inside: avoid` | `app.css` print block |
| P4 | The PDF title is `Marqora - <full path>` | The title is set once, as the print job's name (`setPrintSource`, `app.js:5878`). The host sends the front-matter title, or the file name, instead of the path. The front-matter key list moves from `Docx` (internal) to a project both can use | `app.js`, `MainViewModel.cs`, `DocxFrontMatter.cs` |
| P5 | Everything prints at about 64% | **Measure after P1**, on the fixture, on a short plain document, and on one with a wide table. The note in `Architecture.md` that Chromium shrinks *"by a third at least"* is treated as unverified. If P1 brings the scale to 100%, it is done here; otherwise phase 2 settles it (S3) | — |

### Phase 0 tests

- **`FixtureExportTests`** exports `docs/UltimateMarkdownContent.md` **with a captured preview
  HTML and stub diagram PNGs**: `ExportedDocument.FromAsync` already takes both. Without them,
  diagrams fall back to code and math to TeX, and the diagram-in-list and OMML paths never
  run.
  - The preview HTML is captured by a checked-in script, as `build/Update-MathCorpus.js` does
    for math.
  - The test requires **zero `OpenXmlValidator` errors**.
  - It also checks W3 (each list starts at its own start), W4 (no relationship target holds a
    space) and W6 (no U+00AD in `w:t`).
- A heading test for W7 with each `HeadingNumbering` mode and a skipped level.

---

## 6. Phase 2 — the PDF engine and shared furniture (D2, D3)

Phase 1, the paper spec, is §8. It is listed after this phase because it reads more easily
once the print host exists, but the order of work is still 0 and S, then 1, then 2.

### 6.1 Answering the rejected design

`docs/Architecture.md` rejected printing *the HTML export* in a hidden view, for five reasons.
The host does not print the HTML export. It prints a **print markup** of its own, and each
reason is answered by how that markup is made:

| Reason it was rejected | Answer |
| --- | --- |
| The export restores remote pictures, so a view loading it would fetch them | The print markup does **not** go through `withoutBlockedChips`. Remote media stay blocked, and `print.html` carries `shell.html`'s CSP verbatim (`shell.html:14-24`). The host also answers every request outside its two virtual hosts with a 404 |
| It drops the "not shown" chip print relies on | The chip stays, for the same reason |
| Pictures over the embedding limit become links nothing in a hidden view can resolve | Pictures are not embedded. The host maps the assets host and serves `https://marqora.document/...` with the same resolver the preview uses (`ResolveDocumentFile` / `ResolveDocumentAsset`, `WebViewPreviewHost.cs:393-460`), moved somewhere both can call |
| It overflows `NavigateToString`'s size limit | The host navigates to `https://marqora.assets/print.html` and the markup arrives by message, the way the preview receives its HTML |
| Its 16px body would repaginate every PDF | Sizes come from the paper spec (§8), not the HTML export's stylesheet |

### 6.2 The print host

`PrintRenderHost` in `src/PaulTechGuy.MQ.App/Services/`:

1. **A controller**, created the way S1 found works, in the app's existing environment, with a
   white `DefaultBackgroundColor` (the equivalent of `ForceLightCanvas`). It is created on
   first use and `Close()`d at shutdown.
2. **One job at a time.** A PDF export started while a print is running waits in a queue.
3. **A fresh navigation per job**, because Paged.js accumulates styles and handlers and cannot
   be run twice in one page.
4. **`webshell/print.html`** loads:
   - **all of `app.css`**: the print block only overrides screen rules, and heading, table,
     callout and mermaid label rules live in the screen rules;
   - `color-theme.js`;
   - KaTeX's CSS and `syntax.css`;
   - the paper-spec properties (§8);
   - `print-paged.css`;
   - Paged.js 0.4.3 as `paged.js`, not `paged.polyfill.js`, which runs itself on load. The shell
     drives `Previewer.preview()`.

   The markup is wrapped in `.mq-preview`.
5. **The markup** comes from a new shell request, `requestPrintHtml`. It returns
   `outputMarkup(withoutCommentMarks(clone))`: the light drawings, laid-out math and the
   chips, with no comment marks.
   - It waits on `whenOutputReady` with a bounded timeout and then proceeds. **A PDF export
     never refuses** because the preview is busy, which is today's behavior and the principle
     in `WordExport.md` §1. It does not use `GetRenderedHtmlAsync`'s hard 10 seconds.
6. **The PDF**: when Paged.js reports it is done, `Page.printToPDF` is called through
   `CallDevToolsProtocolMethodAsync` with:
   - `preferCSSPageSize: true` and every margin `0`, because Paged.js draws the margins inside
     its page boxes;
   - `generateDocumentOutline` and `generateTaggedPDF`, feature-detected by runtime version.
     They are marked experimental, and an older runtime gets an untagged PDF rather than an
     error;
   - `transferMode: "ReturnAsStream"`, read with `IO.read`, so a large document does not pass
     through one base64 string.
7. **Progress and cancellation**: a status line with the page count, a Cancel, and a timeout
   that scales with the document's length.

The old live-page path stays behind a setting for one release, as the way back if Paged.js
misbehaves on someone's document.

### 6.3 Paged-media rules (`webshell/print-paged.css`)

- `@page` with the dialog's paper and the spec's margins.
- **Running header and footer.**
  - The header takes the title with `string-set`; the footer shows `counter(page)`.
  - Margin boxes are marked so a tagged PDF does not read them on every page.
- **Front matter.**
  - The cover is a named page with no header or footer.
  - The contents is a named page in roman numerals.
  - The body resets the page counter to arabic 1, matching Word's three sections.
- **Contents.**
  - Entries take their page numbers from `target-counter(attr(href url), page)`.
  - Depth follows the same `\o` table Word uses for each `HeadingNumbering` (`WordExport.md`
    §4).
  - The "Contents" title is a single string both exports read, and it is not a heading, so it
    stays out of the outline.
- **Tables.** A handler repeats `<thead>` on each page a table spans, as Word and Chromium do
  today; Paged.js does not (#36).
- **Keeping blocks together.** `break-inside: avoid` is applied only to blocks the shell has
  marked as shorter than a set fraction of the page box. Applied to everything, it is the known
  source of Paged.js hangs (#340, #295, #114, #202).
- **Footnotes (D4)** are `float: footnote`.
  - Before Paged.js runs, the shell moves each definition from Markdig's end-of-document list
    to its reference.
  - Block content inside a footnote becomes `display: block` spans, so there is no block flow
    inside the float (#68, #320).
  - ↩ back-links are removed.
  - A second reference to the same note shows the same number in both exports, and the note is
    printed once.
  - Under O4, if any note cannot be placed, the whole document's notes become endnotes, with a
    report row.
- **Language.** `<html lang>` comes from a front-matter `lang`, with en-US by default. Word
  takes the same value; today it hard-codes en-US (`DocxStyles.cs:112`, `DocxSettings.cs:64`).

### 6.4 One furniture record (D3)

- **`ExportLayout`** in `PaulTechGuy.MQ.Domain`: `IncludeCoverPage`, `IncludeTableOfContents`
  and `IncludeHeaderAndFooter`, the last defaulting on (O3). Both dialogs show it, and both read
  and write one saved copy.
- **Migration.** `DocxExportSetup` keeps the three members as nullable legacy properties,
  ignored on write when null. On load they move into `ExportLayout`, in both JSON contexts
  that read `AppSettings` (`MarqoraJsonContext.cs:18`, `PreferencesJsonContext.cs:36`), so a
  preferences import of an old file migrates too. `SeededFrom` is a fallback, not a
  migration, and is not used for this. A test loads an old `settings.json`.
- **Callers that move:**
  - `PreferencesWindow.cs:558-566` and `:1860-1866`;
  - `DocxFurniture.WriteHeaderAndFooter`;
  - `AppearanceTests.cs` and `AwkwardDocumentTests.cs`.
- **Cover page.** It reads the same front-matter keys in both exports, from the shared key list
  (P4). An unfilled line is left out in both, so Word's gray placeholders no longer reach
  paper.
- **The PDF dialog** gains the three checkboxes. Both dialogs are `ContentDialog`s, which may
  be modal. Their buttons are the dialog's own, so `Button-App-Standards.md` applies through
  the dialog's styles rather than `CommandFooter`. Run `build/Test-ButtonStandards.ps1`.

### 6.5 A report for the PDF too

`ExportReportWindow` takes PDF rows. The report is built **host-side**, because the shell does
not know a local file is missing:
- **Missing and remote pictures** come from the same checks Word uses (`LinkChecks.cs:73`,
  `MediaTarget.Classify`).
- **Diagrams that did not parse** come from the shell.

The row wording is shared with Word's. `CLAUDE.md`'s description of the window is updated.

### 6.6 Every printout through one host (O2)

- **The document's Print** moves to the print host, so a printout and a PDF are one layout.
  The route uses a page size equal to `@page` and zero margins (S5), because
  `CoreWebView2PrintSettings` has no `preferCSSPageSize`. The Print dialog shows the same three
  furniture checkboxes.
- **The host takes three kinds of job.** The job names its markup source and whether furniture
  applies:

  | Job | Markup | Furniture |
  | --- | --- | --- |
  | Document PDF and Print | `requestPrintHtml` from the preview | from `ExportLayout` |
  | Cheatsheet Print | the cheatsheet's rendered body, light drawings | none; page numbers only |
  | Diagram pop-out Print and PDF | the one light drawing, by hash (`outputSvgOf`) | none |

- **The diagram pop-out:** a single diagram is sized to one page by the same rule as today
  (`diagram.css`), now against the Paged.js page box.
- **The cheatsheet:** printing now waits for its light drawings, which closes the gap noted
  under decision 3 in `Architecture.md` (*"the cheatsheet's Print does not wait for its light
  drawings"*).
- **`WebViewPrinting`** keeps only the printer and PDF calls the host makes. Its summary is
  rewritten around the host, and the promise *"all three printouts agree"* holds by
  construction.
- **S1 and S5 include a cheatsheet job and a pop-out job.**

---

## 7. Phase 3 — Word fidelity (D6, D7, D8)

### 7.1 Math (D6)

- **`MathmlToOmml`.**
  - Map stretchy pairs around an `mtable` (`pmatrix`, `bmatrix`, `vmatrix`, `cases`) to `m:d`.
    The fixture shows the bracket logic misses that shape.
  - `mspace` and `\quad`/`\qquad` become OMML spacing.
  - `\color` and `\colorbox` become run color and shading, opaque `#rrggbb`.
- **Equation numbers.** KaTeX numbers every `align` line "(1)" in the preview, and the PDF
  prints that. The shell computes the numbers as text, which both exports read. That replaces
  the matrix shape with `m:eqArr`, the reversal named in §1.
- **The picture fallback.** An equation still holding an unmapped element becomes a PNG of its
  KaTeX render.
  - The render needs KaTeX's fonts inlined as `data:` URIs inside the SVG before it is
    rasterized. The isolated image document cannot fetch web fonts (`diagram-raster.js:17-19`),
    and KaTeX is all web fonts.
  - The TeX goes in the alt text, and the export report gets a row.
- **Tests.** `MathCorpusTests` gains the fixture's equations.

### 7.2 Diagrams (D7)

Mermaid's labels are HTML in `<foreignObject>`, which Word does not draw. `htmlLabels: false`
is not a clean switch in mermaid 11: it overflows mindmap nodes and breaks entities, markdown
strings and escapes (mermaid #8387, #7013, #7015, #7016). So the rule depends on the drawing:

- **If the light drawing has no `foreignObject`** (sequence, gantt, pie, gitGraph, xychart and
  similar, confirmed by S6), Word gets it as `asvg:svgBlip` with the PNG as the fallback.
  - The SVG comes from the existing `requestDiagramSvg` (`app.js:6358`).
  - Computed presentation attributes are inlined first, because mermaid styles through an
    embedded `<style>` with `#id` selectors.
- **Otherwise the PNG stays primary**, and its serif labels are fixed: the face the isolated
  render falls back to is made the spec's diagram face.
- No third mermaid drawing.
- **S6 settled the rule.** Word gets the SVG only for the ten types it was seen to draw correctly, as an allowlist of `aria-roledescription` values with the PNG as the fallback; every other type keeps the PNG. Inlining computed styles before embedding (above) is tried next on the timeline, treemap and git graph, and a type joins the allowlist only after Word is seen drawing it. A `foreignObject` type never joins: its words would be lost.

### 7.3 Raw HTML (D8)

- **Styled blocks.** A block `<div>` or `<p>` with style becomes paragraphs with `pBdr` and
  `shd`.
  - The styles are **computed in the shell** with the existing `withInlineStyles`
    (`app.js:5286`), not parsed in C#, so `var()`, named colors, `hsl()` and classes resolve.
  - Any alpha is blended over white to an opaque `#rrggbb`. These are the author's colors, not
    the theme's, so the theme rule does not apply. The opacity rule does, because Word has no
    alpha.
- **`<details>`.** Its `<summary>` becomes a bold paragraph above the content in Word. The PDF
  shows the summary line natively (P2).
- **Inline elements.**
  - Inline `<hr>` becomes a bottom-bordered empty paragraph.
  - Inline `<img>` goes through `DocxImages`.
  - `<q>` gets curly quotation marks.
  - `<small>` gets the spec's small size.
- **HTML tables.** A raw `<table>` becomes a Word table, with `colspan` as `gridSpan` and `rowspan` as `vMerge`. Until then W8 writes its cell text row by row. (The first comparison marked this ✅ for Word; it was dropped, as the `<div>` was.)
- **Images.** Pandoc `{width=… height=…}` sets the extent.
- **The floor.** Anything still unmapped keeps its text and gets a report row (W8).

---

## 8. Phase 1 — the paper spec (D1)

### The single source

Colors already work this way: one theme file, the stylesheet reads `var(--mq-theme-<slot>)`
and declares none, and Word reads the palette through `DocxColors`. The paper spec does the
same for type and space.

- **`PaperSpec`** in `PaulTechGuy.MQ.Domain`: one record. For each element, it gives:
  - **a face role** (`Text`, `Display`, `Code` or `Diagram`) and a weight, never a font name;
  - size, italic, space before and after, and line height, in points;
  - the theme slot for color.

  The elements are body, H1 to H6, code block, inline code, blockquote, callout title and
  body, table header and cell, footnote, caption, the contents entries, header, footer and
  diagram labels.
- **Word** reads it in `DocxStyles` and `DocxTheme`. `BodyHalfPoints`, `CodeHalfPoints`,
  `CodeFont`, `MinorFont` and `MajorFont` stop being constants.
- **The PDF** reads it as `--mq-paper-<element>-<property>`, from a **writer of its own**.
  `color-theme.js` accepts only `--mq-theme-` slots with `#rrggbb` values
  (`color-theme.js:5-17`), so it cannot carry sizes and face chains.
- **The print rules for each element** use those variables with **fallbacks equal to today's
  values**. The cheatsheet links `app.css` and prints without the variables, and an exported
  HTML file reuses `app.css` in a reader's browser.
- **The screen is not affected.**

### Faces: one place (O1)

Paul's condition on O1 is that a future change of font is easy, and defined in one place. So
fonts are named in **one table and nowhere else**: `PaperFaces`, beside `PaperSpec`.

| Role | Word family | CSS chain | Used by |
| --- | --- | --- | --- |
| `Text` | Segoe UI | `"Segoe UI Variable Text", "Segoe UI", sans-serif` | body, tables, lists, footnotes, header, footer, contents |
| `Display` | Segoe UI Semibold | `"Segoe UI Variable Display", "Segoe UI", sans-serif` at weight 600 | headings, callout titles, cover title |
| `Code` | Cascadia Mono | `"Cascadia Mono", Consolas, monospace` | code blocks, inline code, `<kbd>`, `<samp>` |
| `Diagram` | — (diagrams are pictures in Word) | `"Segoe UI", sans-serif` | mermaid labels in every output drawing and its PNG |

Changing a face means editing one row. Everything below follows it:
- **Word:** `DocxTheme` writes the theme fonts from `Text` and `Display`, and `DocxStyles` names
  `Code`. The constants `MinorFont`, `MajorFont` and `CodeFont` are deleted, not kept as copies.
- **The PDF and every printout:** the host pushes `--mq-face-text`, `--mq-face-display`,
  `--mq-face-code` and `--mq-face-diagram`. The paper rules use only those.
- **Diagrams:** the light output drawing's mermaid `fontFamily` and the PNG rasterizer take
  `Diagram`. This also fixes the serif labels in Word's PNGs.

A test fails if any font name appears in `DocxStyles`, `DocxTheme`, the paper rules in
`app.css` or `print-paged.css`, or the output-drawing configuration in `diagram-output.js`,
except through `PaperFaces`. That is the same kind of rule as the theme's *"declares none"*,
and it is what keeps "one place" true after the first change. The screen's own fonts in
`app.css` (`--mq-font-ui`, `--mq-font-mono`) are not paper, and stay outside the check.

### The values

The faces are set above. The rest is approved (2026-10-06):

| Element | Today, PDF | Today, Word | Proposed |
| --- | --- | --- | --- |
| Body | 11pt intended (≈7pt printed) | 12pt | **11pt** |
| Headings | bold | Aptos Display, regular weight | **semibold** |
| Code | 0.94em of body | 9.5pt | **9.5pt** |
| Blockquote | upright | italic | **upright** |
| Rule under H2 | yes | yes | yes |

### The test (D9)

`PaperSpecTests` checks three things:

- **Word:** the written `styles.xml` matches the record, style by style.
- **Stylesheets:** every stylesheet `print.html` loads (`app.css`, `print-paged.css`) gives
  each element in the spec its size, face, weight and line height through
  `var(--mq-paper-…)`. The test checks coverage, not silence, because checking only that the
  print block declares no sizes would pass today: today every size comes from a screen rule.
- **Computed styles,** at the phase 2 gate, in the real app: `getComputedStyle` in the print
  host for one element of each kind, compared with the record.

`docs/Paper-Design.md` is generated from the record, as `build/Update-ThemeSlotTable.ps1` does
for theme slots.

---

## 9. Keeping them aligned

The two tests chosen in D9, plus three cheap additions:

| Check | What it catches |
| --- | --- |
| Fixture validator test (§5) | Every schema error Word reports as unreadable content |
| Paper-spec test (§8) | One export's type and spacing drifting from the other's |
| **Content census** (added) | A footnote, list item, table, equation or diagram present in one export and not the other: counts from the `.docx` against counts from the print markup |
| **Third-party listing** (added) | Paged.js in README *Third-party components* (`README.md:1092`) and the About list |
| **Comparison rerun** (added) | The scorecard in `docs/WordVsPdf-Comparison.md`, run at each phase gate |

---

## 10. Risks

| Risk | Mitigation |
| --- | --- |
| A hidden WebView2 does not produce frames, so Paged.js stalls | S1 in week one, three host variants, before anything depends on it |
| Paged.js: npm `latest` is 0.4.3 (2023); 0.5 is a beta with a performance regression (#247); fixes live on `main` only | Pin 0.4.3; vendoring a `main` build would need a build step `Get-WebAssets.ps1` lacks. The old path stays behind a setting for one release |
| Paged.js footnotes with block content (#68, #320, #4, #91) | Spans inside the float; O4's all-or-nothing fallback |
| `generateTaggedPDF` / `generateDocumentOutline` are experimental | Feature-detect; an untagged PDF is not a failure |
| Printers have hardware margins; Paged.js draws margins itself | S5 on a real printer |
| Settings migration of the three booleans | Nullable legacy members, both JSON contexts, an old-file test |
| Mermaid labels in `foreignObject` | §7.2 per-diagram rule, S6 |
| OMML has no full equivalent for KaTeX | Picture fallback with inlined fonts, reported |

---

## 11. Order of work and gates

| Phase | Leaves behind | Gate to the next |
| --- | --- | --- |
| 0 + S | A valid `.docx`; correct list and heading numbers in Word; a PDF that loses nothing; spike results | Fixture validator test green; Word opens the fixture with no prompt; P5 measured; S1 to S6 answered, and Paul decides on D2 with the numbers |
| 1 | `PaperSpec`, both exports reading it, `docs/Paper-Design.md` | `PaperSpecTests` green |
| 2 | Paged.js PDF and Print with furniture, outline, tags and true scale; `ExportLayout`; the PDF report | Fixture PDF at 100% scale; fixture under the S1 time; furniture matches Word's; computed-style check passes |
| 3 | Word math, diagrams and HTML at the PDF's fidelity | Comparison scorecard has no ❌ for Word |
| 4 | Page-foot footnotes in the PDF, or O4's fallback reported | Comparison rerun clean; content census green |

---

## 12. Review log

Two adversarial reviewers read revision 1. One checked every claim against the code; the other
covered external feasibility, design, sequencing and risk. Every finding below was checked
against the source or the machine before it was accepted.

| # | Finding | Severity | Disposition |
| --- | --- | --- | --- |
| R1 | Printing the export in a hidden view is a recorded, rejected design (`Architecture.md` decision 3) | Blocker | **Verified.** §1 names it; §6.1 answers all five reasons |
| R2 | A hidden controller is throttled; Paged.js needs frames | Blocker | **Accepted.** S1 in week one; phase 2 gated on it; fallback stated |
| R3 | Aptos is a cloud font that WebView2 cannot see | Blocker for D1 | **Verified** (`FontCache\4\CloudFonts`, absent from `Windows\Fonts`). Decided as O1: Segoe UI, faces in one place |
| R4 | `outputMarkup` as HTML export uses it removes the chips and restores remote media | Blocker | **Verified** (`app.js:6277`, `:2467`). New `requestPrintHtml`, CSP verbatim |
| R5 | `marqora.document` pictures and the assets host do not resolve in a second view | Major | **Accepted.** Shared resolver, mapped host (§6.1) |
| R6 | The print stylesheet is a block in `app.css`, not a file | Major | **Accepted.** `print.html` loads all of `app.css` |
| R7 | `color-theme.js` cannot carry `--mq-paper-*`; the "declares none" test passes trivially | Major | **Verified** (`color-theme.js:5-17`). Own writer; coverage test |
| R8 | Var-only print rules break the cheatsheet and exported HTML | Major | **Accepted.** Fallbacks equal to today's values |
| R9 | W2's cause was misstated; "set in place" alone flattens nesting | Major | **Verified** (`BlockRenderer.cs:316-337`). W2 rewritten, §6.4 of revision 1 folded in |
| R10 | W3 needs every level, not level 0 | Major | **Accepted**, with the comment, doc and test corrections |
| R11 | W7 has knock-ons in `DocxStyles`, patterns, suffix, TOC Heading, `FromHeading3`, skipped levels | Major | **Verified** (`HeadingNumbers.cs:16-20`). W7 rewritten; difference documented |
| R12 | W1 misses lazy part creation, the image cache and constructor injection | Major | **Accepted.** W1 rewritten |
| R13 | A fixture test with no preview HTML cannot reach two of the five errors | Major | **Accepted.** Captured preview HTML and stub PNGs |
| R14 | W8 would flood the report with comments and tag lines | Major | **Accepted.** Skip rules added |
| R15 | No after-print hook for P2/P4; opening `<details>` changes the window | Major | **Accepted.** P2 is CSS only; P4 goes through `setPrintSource` |
| R16 | A KaTeX PNG renders without its web fonts | Major | **Accepted.** Fonts inlined (§7.1) |
| R17 | The printer route ignores `@page` | Major | **Accepted.** S5; zero margins and page size |
| R18 | `SeededFrom` is not a migration; missed callers; header and footer default changes PDFs | Major | **Verified** (`AppSettings.cs:586`, `PreferencesWindow.cs:564`). Migration rewritten; O3 |
| R19 | "A PDF refuses as HTML export does" is a new failure | Major | **Accepted.** Bounded wait, never refuse |
| R20 | Cheatsheet and pop-out printing left undecided | Major | **Verified** (`CheatsheetWindow.cs:379`, `DiagramWindow.cs:667`, `:749`). Decided as O2: they move to the host |
| R21 | Four more reversals unlisted | Major | **Verified.** Table in §1 |
| R22 | Paged.js version, pinning and entry point | Major | **Accepted.** 0.4.3, `paged.js`, `Previewer.preview()` |
| R23 | Paged.js does not repeat `<thead>` | Major | **Accepted.** Handler in §6.3, S2 |
| R24 | `break-inside: avoid` causes Paged.js hangs | Major | **Accepted.** Only on marked short blocks |
| R25 | Footnote fragility, mixed models, repeated references, ↩ links | Major | **Accepted.** §6.3, O4 |
| R26 | `htmlLabels: false` is not clean | Major | **Accepted.** Per-diagram rule, no third drawing |
| R27 | P1 alone may not reach 100%: tables and `.katex-display` widen too | Minor | **Accepted** |
| R28 | P2/P4 on the live page are wasted once the host exists | Minor | **Accepted.** Both now carry over |
| R29 | `printToPDF` margins default to 0.4 in; base64 size | Minor | **Accepted.** Zero margins, stream mode |
| R30 | Host lifecycle: fresh navigation, a queue, `Close()`, white background | Minor | **Accepted.** §6.2 |
| R31 | Tagged margin boxes, Contents in the outline, language | Minor | **Verified** (en-US hard-coded). §6.3 |
| R32 | The comparison's "defaults" were saved settings (cover and contents are off by default) | Minor | **Verified** (`DocxExportSetup.cs`). The comparison is corrected |
| R33 | No time, progress, cancel or timeout budget | Minor | **Accepted.** §6.2 step 7, S1 target |
| R34 | Contents depth, `target-counter` syntax, counter reset, one "Contents" string | Minor | **Accepted.** §6.3 |
| R35 | `align` numbers are CSS counters Word cannot read | Minor | **Accepted.** Numbers produced as text |
| R36 | Parsing inline `style` in C# misses most of CSS | Minor | **Accepted.** `withInlineStyles` (`app.js:5286`) |
| R37 | `XmlSafeText` returns a string, so W6 cannot live there | Minor | **Accepted.** `RunFormat.ToRun` and two others |
| R38 | W5 also needs `![` and `InlinePlainText` | Minor | **Accepted** |
| R39 | `requestDiagramSvg` exists; a third mermaid config would fight the author's directives | Minor | **Accepted.** No third drawing |
| R40 | The PDF report must be built host-side | Minor | **Accepted.** §6.5 |
| R41 | Dialogs are `ContentDialog`s, not `CommandFooter`; front-matter keys are internal | Minor | **Accepted.** §6.4, P4 |
