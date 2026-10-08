# Word export vs PDF export — a side-by-side comparison

A user reported that *"the export to Word does a better job than exporting to PDF, so I had to
export to Word, then to PDF to get the best results."* This document checks that claim against
real output, finds out why it is true, and lists what each export gets wrong.

**Test subject:** `docs/UltimateMarkdownContent.md`, exported on 2026-10-06 from the Release
build at `3a9795e`, in light mode with the Nordic color theme. Both export dialogs were accepted
as they opened, which means with the saved settings: Letter, portrait, Normal margins (1 inch),
backgrounds on, and for Word a cover page and a contents listing. The cover and the contents
were on from earlier saved choices; their code defaults are off (`DocxExportSetup`).

**Outputs compared:**

| Route | File | Pages |
| --- | --- | --- |
| PDF export | `UltimateMarkdownContent.pdf`, from WebView2 `PrintToPdfAsync` | 41 |
| Word export | `UltimateMarkdownContent.docx` | — |
| Word, then PDF (the user's route) | the `.docx` saved as PDF by Word 365 | 56 (4 front matter + 52 body) |

The Word export's own report listed four items: one remote image, one missing file, and two
diagrams that do not parse (the deliberately broken flowchart and the requirement diagram).
All four are correct.

---

## 1. The verdict

> **As of the 2026-10-08 rerun (§2), this verdict is history.** The PDF prints at true size
> with the same cover, contents, header and page numbers as Word, bookmarks, tags and a real
> title, and it loses nothing it used to. What separates the routes now is Word's fidelity:
> math, a few raw HTML elements, and nested quotes, which is phase 3. The verdict below is the
> 2026-10-06 one, kept because it is why the work was done.

The user is right, and mostly for one reason: **the PDF prints the document at about two
thirds of its intended size.** Body text set at 11pt comes out at roughly 7pt, and code at
roughly 6pt. Word sets body text at a true 12pt (`DocxStyles.BodyHalfPoints`). Laid next to each other, the Word route reads
like a document and the PDF reads like a reduced photocopy. That alone explains the report.

The PDF also lacks things a reader of a long PDF expects: page numbers, a bookmarks pane, a
real document title, and tagging for screen readers. Word supplies a cover page, a contents
listing with page numbers, a running header, page numbers, and true page-foot footnotes, and
Word's own PDF export turns the headings into bookmarks.

On **fidelity**, the PDF is clearly better. It is a print of the preview, so whatever the
preview shows is what prints: KaTeX math is exact, diagrams are vector, HTML blocks survive,
and list numbering is correct. The Word export has real defects: list numbering that runs on
through the whole document, math that loses its stretchy delimiters, an HTML block dropped
without a word in the report, and a `.docx` that **fails the project's own schema validator**
and that Word opens only after repairing it.

So "export to Word, then to PDF" trades fidelity for readability. Fixing the PDF's scale, and
adding page numbers, makes the direct PDF the better route for most documents, and the Word
defects below are worth fixing whichever route people take.

---

## 2. Scorecard

✅ correct · 🟡 degraded but readable · ❌ wrong or lost

**Rerun 2026-10-08, the phase 2 gate.** Debug build of `dev` with phase 2 uncommitted (paged
engine behind PDF and Print), light mode, default dialogs: Letter, Normal margins, header and
page numbers on, and contents and title page as the shared layout had them (both on). Files:
`gate-ultimate2.pdf` from Export to PDF (59 pages: cover, four contents pages, 54 body pages),
`UltimateMarkdownContent.docx` exported at 05:06, and Word 365's *Save As PDF* of that file (63
pages: cover, four contents pages, 58 body pages). Word opened the file with no repair prompt.
The PDF's paper check logged *15 kinds of element match the spec*.

Where a cell changed since the 2026-10-06 run, the note says so. Everything in §3 and §4 is the
2026-10-06 record of causes; each now carries its status.

### Element fidelity

| Construct | PDF | Word | Notes |
| --- | --- | --- | --- |
| YAML front matter | ✅ hidden, feeds the cover and the PDF title | ✅ hidden, feeds the cover page and document properties | |
| ATX and Setext headings, levels 1–6 | ✅ | ✅ | Word now restarts H2 numbering after each H1, as the preview does |
| Inline emphasis matrix | ✅ | ✅ | |
| Hard breaks (two spaces, backslash, `<br>`) | ✅ | ✅ | |
| Blockquotes, nested to three levels | ✅ | 🟡 | No longer italic. **Nested quotes do not indent**, and adjacent quotes merge into one panel (phase 3) |
| GFM callouts | ✅ | ✅ | Was 🟡 for the PDF: callouts are kept whole now. Word still has no icons |
| Ordered lists | ✅ | ✅ | Was ❌ for Word: every list restarts (§3.2 fixed) |
| Nested and task lists | ✅ | 🟡 | Word still flattens the matryoshka: the list inside the quote inside the list leaves the quote |
| Inline code | ✅ | ✅ | |
| Fenced code, syntax colors | ✅ | ✅ | |
| Wide code block (141 columns) | ✅ wraps | ✅ wraps | Was ❌ for the PDF: wraps on paper, nothing lost (§4.2 fixed) |
| Long unbroken token or URL | ✅ wraps | ✅ wraps | |
| Links, external | ✅ clickable | ✅ clickable | |
| Link with spaces in `<…>` | ✅ | ✅ | Was ❌ for Word (§3.3 fixed) |
| Undefined reference `[broken][no-such-ref]` | ✅ literal | ✅ literal | Was ❌ for Word (§3.4 fixed) |
| Empty link text | 🟡 nothing visible | ✅ shows the URL | |
| Relative link to another `.md` | ✅ text only on paper | ✅ text only | Same by design |
| Internal anchor link | ✅ | ✅ | |
| `data:` PNG images | ✅ | ✅ | |
| Pandoc `{width=64px}` image | — not rechecked | ❌ 1px | Word still ignores the size attribute. The image is a transparent pixel, so the PDF's size cannot be seen on the page; it was 64px on 2026-10-06 |
| Remote image | 🟡 "not shown" chip, reported | 🟡 linked alt text, reported | Both reports name it; the PDF's by URL, Word's by alt text |
| Missing local image | 🟡 broken-image glyph + alt, reported | ✅ bracketed alt, reported | |
| Footnotes | ✅ at the page foot | ✅ at the page foot | Was 🟡 for the PDF: 3 of 3 placed at the foot, no ↩ links |
| Pipe, aligned, ragged, grid tables | ✅ | ✅ | |
| HTML table with a merged cell | ✅ | 🟡 | Was ❌ for Word: its text is in the document and reported, its merge is not |
| Horizontal rules (all syntaxes) | ✅ | ✅ | |
| Block HTML `<div>` callout | ✅ | 🟡 | Was ❌ for Word: the sentence is there as plain text, and reported |
| `<details>` | ✅ printed open | 🟡 | Was ❌ for the PDF (§4.5 fixed). Word keeps summary and content, reported |
| `<iframe>` | 🟡 "not shown" chip, reported | ❌ | **Word drops it without a report row** (phase 3) |
| `<form>` | ✅ | 🟡 | Word keeps the button's text, reported |
| Inline HTML (`<sup>`, `<sub>`, `<mark>`, `<kbd>`, `<var>`, …) | ✅ | 🟡 | Word still loses `<q>`'s quotation marks and `<small>`'s size |
| `<hr />` and `<img>` written inline | ✅ | ❌ | **Both still absent in Word, and not reported** |
| Definition lists, abbreviations | ✅ | ✅ | |
| `&shy;` soft hyphen | ✅ invisible | ✅ invisible | Was ❌ for Word (§3.6 fixed) |
| Repeated spaces | ✅ collapsed | 🟡 kept | |
| Inline and display math | ✅ KaTeX, exact | 🟡 | Simple equations convert well |
| Matrices, `cases`, `\left…\right` | ✅ | ❌ | Unchanged: Word's matrix parentheses and `\det` bars do not stretch, and the `cases` brace is missing (phase 3) |
| Several equations on one line (`\qquad`) | ✅ | ❌ | Unchanged: the spacing is lost (phase 3) |
| `\text`, `\color`, `\colorbox` in math | ✅ | ❌ | The line 1119 equation is not converted; its source is in the document, reported (phase 3) |
| `\overbrace` and `\underbrace` | ✅ | 🟡 | |
| `align` equation numbers | ❌ (0)(0)(0) | 🟡 none | **Changed for the PDF:** was (1)(1)(1). KaTeX's `katexEqnNo` counter does not survive Paged.js. The preview's own (1)(1)(1) is the older defect underneath |
| mhchem `\ce{}` | ❌ | ❌ | Not loaded in the preview, so both print raw `\ce` (expected by the fixture) |
| Mermaid diagrams (24 types) | ✅ vector | 🟡 raster | Labels are now Segoe UI in Word, not a serif face |
| Broken mermaid | ✅ red parse-error box, reported | ✅ source as code, reported | |

### Page layout

| | PDF | Word, then PDF |
| --- | --- | --- |
| Page size and margins | Letter, 1 inch | Letter, 1 inch |
| **Effective text size** | **11pt body**, 9.5pt code, by the paper check | 11pt body, 1.4 line, 8pt after |
| Page numbers | roman in front matter, arabic in body | roman in front matter, arabic in body |
| Running header | document title | document title |
| Cover page | title, date, author; no placeholders | title, date, author; no placeholders |
| Contents with page numbers | computed at export, dotted leaders | Word TOC field, updated on open |
| Blocks kept off page breaks | code, tables, quotes, callouts, images | Word's keep-with-next on headings; tables may split |
| Callout split across a page | no | no |
| Tall diagrams | held to one page | held to one page |
| Pages | 59 | 63 |

### Typography and color

| | PDF | Word |
| --- | --- | --- |
| Body face | Segoe UI | Segoe UI (theme minor font) |
| Headings | Segoe UI Semibold, theme colors | Segoe UI Semibold (theme major font), theme colors |
| Code | Cascadia Mono, theme syntax colors | Cascadia Mono, theme syntax colors |
| Light output whatever the screen | ✅ | ✅ |
| Color theme applied | ✅ | ✅ read through `DocxColors` |

### Navigation and metadata

| | PDF | Word | Word, then PDF |
| --- | --- | --- | --- |
| Clickable external links | ✅ | ✅ | ✅ |
| Clickable internal links | ✅ | ✅ bookmarks | ✅ |
| Bookmarks or outline | ✅ from headings | ✅ navigation pane | ✅ from headings |
| Document title | ✅ front-matter title | ✅ front-matter title | ✅ |
| Language | ✅ `en` | ✅ `en-US` | ✅ `en` |
| Tagged for accessibility | ✅ | ✅ | ✅ |
| Report of what was left out | ✅ when something is missing | ✅ always | — |

### What is left

Every ❌ above is in Word's column except two that both exports share or that sit in the
preview: mhchem (expected) and the `align` numbers. Word's ❌s are phase 3's list: stretchy
delimiters and spacing in math, the line 1119 color equation, the `<iframe>` and the inline
`<hr />` and `<img>` that leave no report row, and the Pandoc image size.

---

## 3. Word defects, with causes

### 3.1 The `.docx` fails validation, and Word only opens it with repair

**Status 2026-10-08: fixed.** The fixture validator test is green and Word opens the export with no prompt.

Word 365 refused to open the file without repair ("The file appears to be corrupted"). It
opened only with *Open and Repair*. `docs/WordExport.md` §11 makes "no unreadable-content
prompt" a requirement. Running the project's own check,
`OpenXmlValidator(FileFormatVersions.Office2019)`, on the exported file gives five errors:

- **`/word/footnotes.xml` — footnote 2's `w:hyperlink` names relationship
  `R214512ed6a7743e5`, which does not exist in the footnotes part.** The link in `[^long]` was
  registered on the main document part. `InlineRenderer` always calls
  `_main.AddHyperlinkRelationship` (`src/PaulTechGuy.MQ.Docx/InlineRenderer.cs:291`, `:447`,
  `:485`), but a footnote's relationships belong to its own `FootnotesPart`. The same would
  apply to an image inside a footnote. This is the error that makes Word refuse the file.
- **Four paragraphs carry a `w:ind` out of schema order.** Two hold a second `w:ind` after
  `w:contextualSpacing` (the nested task items in the matryoshka block), and two hold one after
  `w:jc` (a diagram inside a list item). `StyleListParagraph` appends an `Indentation`
  (`src/PaulTechGuy.MQ.Docx/BlockRenderer.cs:385`) to properties that a nested list, or the
  diagram writer, has already filled. It should set the property in place, not append a second
  copy.

The unit tests validate their own fixtures. This document is the first to put a link inside a
footnote and a diagram inside a list item.

### 3.2 Ordered lists number continuously through the whole document

**Status 2026-10-08: fixed.** Every list instance restarts.

Every ordered list after the first continues where the previous one stopped: the "Ordered
lists" section prints 17, 18, 19 …, the list-item diagram's "second item" prints 27, and the
matryoshka's "Ordered item" prints 3. In `numbering.xml`, 28 `w:num` instances share 7
abstract definitions, and only one, the 1986 list, carries a `w:lvlOverride`. Word counts every
instance of one abstract definition as one list unless the instance restarts it.
`NumberingPlan.BuildInstance` (`src/PaulTechGuy.MQ.Docx/NumberingPlan.cs:451`) writes a
`startOverride` only when the start is not 1. Writing one for level 0 of every instance, at
whatever the start is, makes each markdown list restart.

### 3.3 A link target with spaces becomes a Word field error

**Status 2026-10-08: fixed.**

`[spaces](<https://example.com/a b c>)` produces a relationship with
`Target="https://example.com/a b c"`. Word cannot resolve it and prints **"Error! Hyperlink
reference not valid."** in bold where the link text belongs. The target needs escaping
(`Uri.AbsoluteUri` gives `a%20b%20c`) before it reaches `AddHyperlinkRelationship`.

### 3.4 An unmatched `[` is dropped

**Status 2026-10-08: fixed.**

`[broken][no-such-ref]` comes out as `broken][no-such-ref]`. The likely cause is that the
`ContainerInline` fallback in `InlineRenderer.Write` walks a leftover `LinkDelimiterInline`'s
children without writing the delimiter's own `[`.

### 3.5 Raw HTML that Word cannot draw disappears without a report

**Status 2026-10-08: mostly fixed.** The `<div>`, `<details>`, merged-cell table and `<form>` keep their text and are reported. The `<iframe>` and the inline `<hr />` and `<img>` are still dropped with no report row (phase 3).

The `<div style="…">` callout in *Block HTML with styling* is gone: heading, then nothing. The
fixture's own pass condition is that a converter that strips HTML still shows the sentence as
plain text. An inline `<hr />` and an inline `<img>` disappear the same way. None of the three
is in the export report, which breaks the report's whole promise: *"Everything else came
across."*

### 3.6 Soft hyphens print as hyphens

**Status 2026-10-08: fixed.**

`&shy;` is written as a literal U+00AD inside `w:t`. Word draws that as a visible hyphen. A
soft hyphen in WordprocessingML is the `<w:softHyphen/>` run element.

### 3.7 Math: stretchy delimiters and spacing

**Status 2026-10-08: open (phase 3).** Unchanged, and the line 1119 `	extcolor` equation is not converted (reported).

The MathML-to-OMML conversion turns `\left( … \right)`, `\begin{pmatrix}` and `cases` into
plain single-height characters instead of `m:d` delimiter objects, so matrices, determinants
and the `cases` brace do not grow with their content. `\qquad` and `\quad` between equations
are lost, so `\lim … = e \qquad \prod … = n! \qquad \bigcup … \qquad \oint …` runs together.
`\color` and `\colorbox` are dropped. `docs/WordExport.md` §13 already calls this converter
the one part with unbounded scope. These are the cases that showed up most often in the
fixture.

### 3.8 Smaller items

**Status 2026-10-08:** diagram labels are Segoe UI now, quotes are no longer italic, and the cover prints no placeholders. The image size, `<q>`, `<small>` and the matryoshka nesting are open.

- The Pandoc `{width=… height=…}` attribute is ignored, so a 1×1 PNG stays 1px.
- `<details>` keeps its content but loses its `<summary>` line.
- `<q>` loses its quotation marks, and `<small>` is set at full size.
- The matryoshka block's nesting is flattened: the blockquote's inner list leaves the quote.
- Diagram PNG labels are in a serif face. The rasterizer is not finding the face the SVG
  names.
- The cover page prints its gray "Sub-Title" and "Version" placeholders when the front matter
  does not fill them. That is right in Word, where they are prompts, but they reach the PDF on
  the user's route.

---

## 4. PDF defects, with causes

### 4.1 Everything prints at about two thirds of its size

**Status 2026-10-08: fixed** by the paged engine. The paper check measures 11pt body and 9.5pt code.

The print stylesheet sets `.mq-preview { font-size: 11pt }` (`webshell/app.css`, the
`@media print` block). On the page, body line pitch measures 10.9pt; at `line-height: 1.55`
that is a **7pt** font. Code, at its own smaller em size, is about 6pt. The Word route, at
12pt, wraps after about 87 characters on a 6.5-inch line; the PDF fits about 141.

`docs/Architecture.md` (*Screen and output*) already records the mechanism: *"Chromium lays a
printed page out wider than the paper and scales it down, by a third at least."* The diagram
sizing was rebuilt around that (`--mq-print-page-ratio`), but the text never was. The measured
scale here, about 0.64, is deeper than a third. The likely reason is that the 141-column
`pre` keeps `white-space: pre` with `overflow: visible` on paper, and Chromium shrinks the
*whole* document to make room for its widest line, while that line is still clipped (§4.2).

Two things to try, in order:

1. Let `pre` wrap on paper (`white-space: pre-wrap; overflow-wrap: anywhere`). That removes
   the over-wide element, and then measure what scale remains on a document with nothing
   wide in it.
2. Size the print text for the scale that remains, or print through the DevTools
   `Page.printToPDF` call with `preferCSSPageSize` and an explicit `@page` size and margin,
   and see whether Chromium stops shrinking when the page box is stated. This needs checking
   in the real app, not assuming.

### 4.2 Wide code is cut off

**Status 2026-10-08: fixed.**

On page 35 of the PDF, the log-table fence ends at `padded 2` and the rest of the line is not
on the page. Nothing in the PDF tells the reader something is missing. Word wraps the same
block and keeps every character. The fix from §4.1 point 1 covers this too.

### 4.3 No page numbers, header or footer

**Status 2026-10-08: fixed.** Header, page numbers, cover and contents come from the shared layout, as Word's do.

`ShouldPrintHeaderAndFooter = false` (`src/PaulTechGuy.MQ.App/Services/WebViewPrinting.cs`),
deliberately, because Chromium's band prints the `https://marqora.assets/` URL. The cost is a
41-page document without a single page number. Chromium's `Page.printToPDF` takes
`headerTemplate` and `footerTemplate` HTML with `pageNumber` and `totalPages` classes, which
would give Marqora's own footer without the browser's band. That would need the DevTools call
in place of `PrintToPdfAsync`.

### 4.4 No bookmarks, no tagging, and the title is a file path

**Status 2026-10-08: fixed.**

The PDF has no `/Outlines`, no `/StructTreeRoot` and no `/Lang`. Its `/Title` is
`Marqora - C:\Users\pcarver\Documents\…\UltimateMarkdownContent.md`, because `app.js:5879`
sets `document.title` to the window caption. That puts a local path into metadata that travels
with the file. `Page.printToPDF` has `generateDocumentOutline` (bookmarks from the headings)
and `generateTaggedPDF`. Setting `document.title` to the front-matter title, or the file name,
for the length of the print fixes the title.

### 4.5 `<details>` prints collapsed

**Status 2026-10-08: fixed.**

Only the summary line prints, so the list and the code block inside are not in the PDF at all.
`prepareForPrint` could open every `<details>` and close them again afterward. Chromium's
`::details-content` pseudo-element may also let the print stylesheet do it alone.

### 4.6 Callouts split across pages

**Status 2026-10-08: fixed.**

The print block's `break-inside: avoid` list (`pre, table, blockquote, img,
.mq-table-scroll`) does not include `.markdown-alert`, which is a `div`. The *Caution* label
sits alone at the foot of page 5 and its sentence opens page 6.

### 4.7 Smaller items

**Status 2026-10-08:** footnotes sit at the page foot and the PDF has a report. The `align` numbers now print (0) on every line under the paged engine, open.

- Footnotes are collected at the end with ↩ back-links, which do nothing on paper.
- Heading numbers restart after every H1, so the fixture's three H1s give two sections
  numbered 1 and two numbered 2. That is the documented design (`HeadingNumbering`: *"each
  "#" chapter restarts its own "##" numbering"*), and the preview and outline follow it. The
  Word export departs from it: Word numbers H2s straight through (1 to 22), because H1 is not
  part of its heading list. The defect is on the Word side.
- `align` numbers every line "(1)". That is in the preview, not the print.
- No export report. A broken diagram prints as a red error box and a remote picture as a
  "not shown" chip, both visible, but nothing tells the author before they send it.

---

## 5. Where each route wins

| Concern | Better route | Why |
| --- | --- | --- |
| Readable text size | Word | §4.1: the PDF prints at about 64% |
| Page numbers, header, contents with page numbers | Word | §4.3 |
| Bookmarks, title, accessibility | Word | §4.4 |
| Footnotes | Word | true page-foot notes |
| Wide code | Word | §4.2: the PDF loses content |
| `<details>` content | Word | §4.5 |
| Math | **PDF** | KaTeX exactly; Word loses delimiters and spacing (§3.7) |
| List numbering | **PDF** | §3.2 |
| Raw HTML | **PDF** | §3.5 |
| Diagrams | **PDF** | vector, with the right faces |
| Opening without a repair prompt | **PDF** | §3.1 |
| Links with spaces, unmatched brackets, soft hyphens | **PDF** | §3.3, §3.4, §3.6 |

---

## 6. Suggested order of work

**PDF, to close the gap the user found:**

1. Fix the print scale (§4.1). This is most of the complaint.
2. Let wide `pre` wrap on paper (§4.2). This is the same change as the first step in §4.1.
3. Add page numbers, bookmarks, tagging and a real title through `Page.printToPDF` (§4.3,
   §4.4).
4. Open `<details>` for print, and keep callouts whole (§4.5, §4.6).

**Word, because the file is not well formed:**

1. Give footnote links their relationships in the footnotes part (§3.1).
2. Set list indentation in place rather than appending it (§3.1).
3. Restart every list instance (§3.2).
4. Escape hyperlink targets, keep the unmatched `[`, write `w:softHyphen` (§3.3, §3.4, §3.6).
5. Write unsupported raw HTML as plain text, or at least report it (§3.5).
6. Add this fixture's `.docx` to the validator tests. All five validation errors above were
   reachable from one document the test suite never exports.

---

## 7. Repeating the comparison

1. Export `docs/UltimateMarkdownContent.md` both ways with the defaults.
2. Validate the `.docx` with `OpenXmlValidator(FileFormatVersions.Office2019)`. It should
   report nothing.
3. Open the `.docx` in Word by hand. It should open with no repair prompt.
4. Save it as PDF from Word (*File > Save As > PDF*, *Options > Create bookmarks using
   headings*).
5. Compare the two PDFs page by page. Measure the body text in the direct PDF from the line
   pitch: pitch ÷ 1.55 should come to 11pt.
