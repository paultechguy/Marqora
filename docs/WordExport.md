# Word export

`Tools > Export to Word...` writes a `.docx` that Word treats as native: its own heading styles,
its own numbering, a navigation pane that works, an updatable contents field, and page setup of
the reader's choosing. The intent is not a screenshot of the preview in a Word wrapper — it is a
document that looks like somebody wrote it in Word, carrying everything the markdown said.

This document is the design, the traps, and enough of a running start to pick up testing in a
fresh session without re-deriving any of it.

---

## 1. The one rule it breaks

`docs/Architecture.md`, *Exporting*, states the rule the other exports follow:

> Both exports take the **rendered preview**, not a fresh render of the source.

That is why mermaid arrives as inline SVG in an HTML export, why math is already laid out, and
why code is already colored. **Word cannot follow it.** WordprocessingML is not HTML, so there is
nothing in a rendered fragment to carry across. The Word export parses the markdown again and
walks the Markdig AST.

The compromise is a **sidecar**. The AST supplies structure and text; the live preview supplies
only the three things a browser alone can produce:

| Artifact | Where it already is in the preview HTML | Used for |
| --- | --- | --- |
| mermaid hash | `<pre class="mermaid" data-mq-diagram="1189641">` | asking the shell for a PNG |
| KaTeX MathML | `<span class="katex-mathml"><math>…` | converting to OMML |
| highlight.js tokens | `<code class="language-x hljs"><span class="hljs-keyword">…` | code colors |

All three arrive inside the string `IPreviewHost.GetRenderedHtmlAsync()` already returns — the
same string the HTML export uses. **Nothing in `webshell/` was changed for this feature**, and
nothing needs to be.

Two consequences worth knowing when testing:

- **A Word export never fails because the preview is busy.** HTML export refuses with *"The
  preview has not finished rendering yet"*. Word degrades instead — code loses its colors, math
  falls back to TeX source, a diagram falls back to its source as a code block — and a document
  always comes out.
- **The visual target is Word-idiomatic, not a twin of the PDF.** Aptos/Calibri, Word's heading
  scale, Word's built-in styles. Comparing an export against a PDF is a **content and structure**
  check, not a pixel check. Line breaks, page breaks and spacing will differ; that is expected.

---

## 2. Where the code lives

`src/PaulTechGuy.MQ.Docx/` — `net10.0`, no WebView, no UI. About 6,600 lines.

| File | Lines | What it owns |
| --- | --- | --- |
| `BlockRenderer.cs` | 1011 | the Markdig block walk — headings, paragraphs, lists, quotes, callouts, code, tables, diagrams, footnotes |
| `DocxStyles.cs` | 898 | `styles.xml`: docDefaults, latent styles, every built-in and Marqora style |
| `MathmlToOmml.cs` | 734 | MathML to OMML, with TeX source as the fallback |
| `NumberingPlan.cs` | 563 | `numbering.xml`: one instance per markdown list, plus the heading multilevel definition |
| `InlineRenderer.cs` | 463 | the inline walk — emphasis, code, links, marks, sub/sup, task boxes |
| `DocxExporter.cs` | 345 | `IDocxExporter`; parses, pre-fetches diagrams, writes the parts in order, builds `sectPr` |
| `DocxImages.cs` | 328 | relationships, `Drawing`/`inline`/`pic`, EMU sizing, part dedupe |
| `PreviewHarvest.cs` | 302 | the scanner over rendered HTML; keys everything on `data-src-line` |
| `DocxFurniture.cs` | 293 | header, footer, title page, contents field, section breaks |
| `DocxTables.cs` | 246 | table properties, grid widths, alignment |
| `InlineHtml.cs` | 225 | raw `<kbd>`, `<sub>`, `<br>` and friends, built from the AST |
| `DocxFootnotes.cs` | 181 | `FootnotesPart` and the mandatory separator notes |
| `RunFormat.cs` | 164 | the formatting a run accumulates down the inline tree; owns `w:rPr` child order |
| `DocxFrontMatter.cs` | 119 | the YAML keys Word has somewhere to put, plus `version` |
| `HighlightPalette.cs` | 117 | hljs class to GitHub-light hex |
| `BookmarkTable.cs` | 112 | heading bookmarks and internal anchor links |
| `Measure.cs` | 88 | twips, EMU, usable width and height |
| `DocxTheme.cs` | 87 | `theme1.xml`, accent1 patched to Marqora teal |
| `DocxSettings.cs` | 84 | `settings.xml`, including `updateFields` |
| `XmlSafeText.cs` | 83 | strips what XML cannot carry |
| `StyleIds.cs` | 72 | every style id in one place |
| `ExportReport.cs` | 47 | what could not be carried across |
| `Resources/theme1.xml` | — | the stock Office theme, patched at write time |

Contract and wiring:

- `src/PaulTechGuy.MQ.Abstractions/Ui/IDocxExporter.cs` — the interface, beside `IHtmlExporter`.
- `DocxServiceCollectionExtensions.AddMarqoraDocx()` — singleton, because it holds an immutable
  Markdig pipeline.
- `src/PaulTechGuy.MQ.App/Program.cs` — registered at the composition root.
- `src/PaulTechGuy.MQ.Rendering/MarqoraMarkdownPipeline.cs` — the **shared** Markdig builder.
  `Docx` references `Rendering` for it; that is the documented layering exemption in
  `docs/Architecture.md`. Without it the two parses drift and a new Markdig extension silently
  stops reaching Word.

---

## 3. End to end

```
MainViewModel.ExportWordCommand
  -> WriteWordExportAsync()                          MainViewModel.cs
     -> IExportDialogService.RequestDocxSetupAsync()
        -> WordExportDialog                          returns DocxExportSetup
     -> _settings.Update(s => s with { DocxSetup = setup })   saved on accept, not on success
     -> IFileDialogService.PickExportFileAsync()
     -> IPreviewHost.GetRenderedHtmlAsync()          may be empty; never fatal
     -> Task.Run(() => IDocxExporter.WriteAsync(...))          off the UI thread
     -> AnnounceExport(path) + a dialog listing anything skipped
```

`WriteAsync` takes: output path, title, markdown, `DocxExportSetup`, `HeadingNumbering`, the
source document path (for relative images), the rendered preview HTML, and a
`Func<string, Task<byte[]?>>` that turns a diagram hash into PNG bytes.

It returns `IReadOnlyList<string>` — everything that could not be carried across. The view model
shows the first ten. **An export with a hole in it is otherwise discovered by whoever opens the
document, somewhere else, with no way to know what happened.**

### Part write order, and why it is that order

1. **Theme** — the styles reference it for every color and face, and a reference into a part that
   is not there resolves to nothing.
2. **Heading numbering planned** *(before styles)* — the heading styles have to name the numbering
   instance they belong to. That link is what makes Word renumber after an insert.
3. **Styles**
4. **Settings** — written before the body, and has to know whether there will be footnotes so it
   can name the separator notes.
5. **Numbering** — only when there is something to number. An empty numbering part is one more
   thing for Word to object to.
6. **Body**: cover page → section break → contents → section break → document
7. **Final `sectPr`** on the body itself
8. **Package properties** from front matter

Diagrams are fetched **before** the walk, concurrently. The walk is synchronous, and the bridge
allows 20 seconds per diagram — a dozen diagrams one at a time is four minutes.

---

## 4. Page setup and sections

### Margins

`PageMargin` and `PageMargins` live in `src/PaulTechGuy.MQ.Domain/PdfPageSetup.cs` and are
**shared with the PDF export**. Word's presets, Word's measurements:

| Preset | Top/bottom | Sides |
| --- | --- | --- |
| Normal | 1" | 1" |
| Narrow | 0.5" | 0.5" |
| Moderate | 1" | 0.75" |
| Wide | 1" | **2"** |
| None | 0 | 0 |

`PageMargins.Labels` holds the dialog text **beside the numbers**, because a label states the
measurements and a label kept anywhere else is a second copy waiting to disagree. All four combos
— Word dialog, PDF dialog, and both preferences rows — read that one list.

> **Migration hazard.** These enums were once two, sharing four member names and agreeing on no
> measurement. A `settings.json` written by an older build can carry `"margin": "Wide"` meaning
> *1 inch all round*, which now means *2 inches at the sides*. Both `pdfSetup.margin` and
> `docxSetup.margin` are affected. If margins look absurd, read
> `%LOCALAPPDATA%\PaulTechGuy\Marqora\settings.json` first — a rebuild does not change a saved
> preference.

A4 is **11906 × 16838 twips** from the 210 × 297 mm definition, not `8.27 × 1440`. Multiplying
the inch figure gives 11908.8, which Word shows as *Custom size*.

### Sections

A document with both extras switched on comes out as three Word sections:

| Section | Header / footer | Page numbers |
| --- | --- | --- |
| Title page | none referenced | none |
| Contents | title header, number footer | `i, ii, iii`, restarts at 1 |
| Body | same header and footer | `1, 2, 3`, restarts at 1 |

Sections follow content: cover only gives two, contents only gives two, neither gives one.

- A section break is the section's properties riding in the **paragraph mark that closes it**; the
  last section's ride on the body. `DocxFurniture.SectionBreak()` builds the carrier.
- A next-page break starts a new page by itself. The explicit `<w:br w:type="page"/>` that used to
  end the title page and the contents would now leave a blank sheet.
- There is **no `titlePg`**. That flag suppressed the header on the first page of a section and
  was how the cover used to avoid carrying one; a cover with a section of its own simply
  references neither header nor footer.
- The footer is a bare `PAGE` field — no "Page", no total. Word answers `PAGE` relative to
  whichever section the page sits in and formats it with that section's own numbering, so one
  footer part serves both numbered sections.

### Contents field

The range follows the reader's numbering preference, so the contents begin at the first numbered
section:

| `HeadingNumbering` | Field range |
| --- | --- |
| `FromHeading1` | `\o "1-3"` |
| `FromHeading2` | `\o "2-4"` |
| `FromHeading3` | `\o "3-5"` |
| `Off` | `\o "2-4"` |

The "Contents" heading itself uses Word's built-in **TOC Heading** style — based on Heading 1 so
it looks like one, `outlineLvl` 9 so it appears in neither the contents nor the navigation pane.
It also sets `numId 0` **when numbering starts at Heading 1**, because a style based on Heading 1
inherits its numbering instance and the word "Contents" would otherwise become section 1.

### Title page

A **template**, not a statement. Every line is present whether or not the front matter has
anything to say, because a page that drops the lines it has no value for is a shrinking list and
the author never learns that a version number was somewhere they could have put one.

```
                       (a third of the way down the text column)
Title                  Word's Title style      front matter `title`, else the document name
Sub-Title              Word's Subtitle style   front matter `subject` / `description`
                       (two lines, 560 twips)
Date                   Normal, tight block     front matter `date`, else today, written out
Version                                        front matter `version` / `revision`
Author                                         front matter `author`
```

- Flush left, one third down the **text column** (not the paper), so it lands in the same place
  whatever the margins are.
- Unanswered lines are the word itself in gray `8A8A8A`.
- The date fills itself in as e.g. `September 11, 2026`. **Not** a Word `DATE` field: a report
  that is filed and read a year later should say when it was written, not claim to be current.
- `version` is the one front-matter key that exists purely for this page — Word has no property
  slot for it.

---

## 5. Styles

Word's built-in style **IDs** where they exist (`Heading1`..`Heading6`, `Title`, `Subtitle`,
`Quote`, `Caption`, `ListParagraph`, `Hyperlink`, `FootnoteText`, `Header`, `Footer`,
`TOC1`..`TOC3`, `TOCHeading`), and `Marqora*` ids only for what Word has no equivalent of.

Latent styles are declared by built-in **name** (`"heading 1"`, `"toc 1"`, `"TOC Heading"`), not
by styleId. Getting that wrong makes the style vanish from Word's gallery.

| Style | Notes |
| --- | --- |
| docDefaults | `After="160"` (8pt), `Line="278"` — Word's own paragraph spacing |
| `Heading1..6` | theme fonts, accent1 shaded/tinted per level, `numPr` when numbering is on |
| `TOC1..3` | ordinary body text with an indent per level. Without these the contents wear the formatting of the headings they came from — bold, heading face, a different size every line |
| `TOCHeading` | see above |
| `MarqoraCode` | shading `F0F0F0`, border `E2E2E2` on all four sides at **8pt space**, `Before/After 160`, `ContextualSpacing`, mono, `NoProof`, **not bold** |
| `MarqoraCodeChar` | inline code, **bold** — a deliberate asymmetry: a word of code in a sentence has to hold its own against the prose; thirty lines in a shaded box already stand apart |
| `MarqoraCallout{Kind}` | left bar in the kind's color, plus **top and bottom borders in the fill color** |
| `MarqoraCallout{Kind}Title` | bold, in the kind's color, `Before 0 / After 0` |
| `MarqoraTable` | accent-filled header row; `tblW pct 5000` + `tblLayout autofit` = Word's *AutoFit to Window* |
| `ListParagraph` | **no indent of its own** — the indent comes from the numbering level or from direct formatting, never from two places |

Colors come from `Domain/CalloutColors.cs` and `Domain/DocumentAccent.cs`, and
`build/Test-DocumentColors.ps1` fails the build if they drift from `webshell/app.css`.

### Shading stops at borders

The rule behind two separate fixes, and the first thing to reach for when a panel looks wrong:

> **Word fills a shaded paragraph out to its borders, and where there is no border to stop at, on
> through the paragraph's own spacing.**

A code fence is bordered on four sides, so its `After` spacing falls outside the box and reads as
a clean gap. A callout edged only on the left swallowed whatever gap was set beneath it — the
prose after it began hard against the panel. The callout now carries **top and bottom borders
drawn in its own fill color**: invisible as lines, and there purely to give the shading somewhere
to end. Their `Space` supplies the padding inside the panel.

---

## 6. Numbering

**One `numId` per markdown list.** Two lists sharing an instance are, to Word, *one list
interrupted* — the second continues 4, 5, 6. The `abstractNum` is shared; every markdown list gets
its own `w:num`. This is the single most likely defect in the feature.

**Heading numbering is real.** A multilevel definition is linked to the heading styles with
`w:pStyle` inside `w:lvl` — Word's *Multilevel List → link to Heading styles*. Numbers written as
text are right when the file is written and wrong from the first edit. Levels are fed from
`document.Descendants<HeadingBlock>()`, exactly as `HeadingNumberPass` does, so headings inside
callouts and list items count the same way they do on screen.

**Indent is 360 twips per level** (`NumberingPlan.IndentPerLevel`), a quarter inch. Word's own
step is 720, which put every list visibly further in than the same list in a PDF; the preview
indents `1.6em`.

**Task items are decided per item, not per list.** A blank line between items does not start a
second list, so

```
- Fruit
  - Apple
- Vegetables

- [x] Ship it
```

is **one** list of four items, two of them ticked. Asking the *list* whether it was a task list
answered yes and stripped the markers off `Fruit` and `Vegetables`, flattened `Apple` up to their
level, and did the same to every list nested below. `NumberingPlan.IsTaskItem(ListItemBlock)` asks
the item. A ticked item hangs its box where a bullet would sit; its plain siblings keep theirs.

---

## 7. The preview harvest

`PreviewHarvest.From(renderedPreviewHtml)` scans the string once and keys everything on
**`data-src-line`**, which `SourceLineExtension` stamps on every block and which is the same
number `block.Line` gives on the AST side.

Matching by ordinal position breaks silently on four constructs this app already supports:

| Desync | Why |
| --- | --- |
| `$$…$$` | `MathBlock` **is a** `FencedCodeBlock`, so it counts on the AST side; it renders as `<div class="math">`, so it does not count on the HTML side |
| ` ```nomnoml ` | registered by `DiagramExtension`; renders as `<div class="nomnoml">`, in neither HTML sequence but a `FencedCodeBlock` in the AST |
| code in a footnote | Markdig **relocates** footnote definitions to a `FootnoteGroup` at the end |
| raw `<pre><code>` | passes through with no AST `CodeBlock` behind it |

Inline math is the one exception to line keying — `SourceLineExtension` stamps blocks only — so a
`MathInline` keys on *containing-block line + ordinal within that block*.

**Then every artifact is guarded on content.** The AST is parsed from the current editor text; the
rendered HTML is whatever the preview last painted, and `emitTextChange` is debounced 160 ms. Edit
inside a fence and the line keys still resolve while the harvested runs carry the old text. So:
code token text is compared against the AST block's text, and KaTeX's own
`<annotation encoding="application/x-tex">` is compared against the AST's TeX source. On any
difference the AST wins and the artifact is dropped.

Harvest **every** `pre > code`, highlighted or not: `highlightOne` returns without adding `.hljs`
when the language is unknown, and indented code blocks are never candidates for highlighting at
all. Filtering on `.hljs` loses blocks that are legitimately uncolored.

---

## 8. Math

MathML to OMML in `MathmlToOmml.cs`. The fallback is the **TeX source** in the inline-code
character style inside `\( … \)` — copy-pasteable into Word's own equation editor — not a PNG.
KaTeX's visible output is HTML spans, not SVG, so the diagram rasterizer cannot help.

`ExportReport.UnsupportedMath` names the element that could not be mapped, so a gap is
discoverable rather than silent.

**N-ary operators.** MathML does not group an integrand with its integral: KaTeX writes
`\int_0^\infty e^{-x^2}\,dx` as a sub-superscripted operator followed by six unrelated siblings.
Built straight across, the `m:nary` gets an empty `m:e` — **and Word draws an empty argument as a
small dotted rectangle**. `Children()` now fills the base from the siblings that follow, stopping
at whichever comes first:

- **a differential** — `d` followed by an identifier — which ends it *and belongs to it*, and is
  what keeps `\int f\,dx + C` from swallowing the constant of integration;
- **a relation** from the `Relations` set (`=`, `≠`, `≤`, `→`, `∈`, …), which ends it and does
  *not* belong to it;
- otherwise the end of the row, which is what a bare `\sum a_i` wants.

Useful KaTeX facts: `throwOnError: false` means a malformed equation yields a `katex-error` span
with **no** `katex-mathml` child (keyed on the line, one typo costs that equation only); fences
arrive as paired `<mo stretchy="true">` inside an `<mrow>` rather than `<mfenced>`; an `<mi>` of
more than one character is upright even without `mathvariant`.

---

## 9. Traps

### WordprocessingML is schema-*sequenced*

`Append` enforces nothing. Word answers a wrong order with *"Word found unreadable content"* and
names nothing. The ones that actually caught us:

- `w:pPr` — `pBdr`/`shd` **before** `spacing`/`ind`; `numPr` before `ind`; `ind` before
  `contextualSpacing`; `jc` **after** them; `outlineLvl` near the end.
- `w:rPr` — `rStyle` first, then the toggles, then `color` **before** `sz`; `vertAlign` near the end.
- `w:lvl` — `lvlJc` comes **after** `lvlText`, the opposite of the natural guess.
- `w:numbering` — **all** `abstractNum` before **all** `num`.
- `w:styles` — `docDefaults`, then `latentStyles`, then the styles.
- `w:trPr` — `cantSplit` before `tblHeader`.
- `w:sectPr` — references, then `type`, `pgSz`, `pgMar`, then `pgNumType`.
- `w:settings` — `footnotePr` before `updateFields`.

`new OpenXmlValidator(FileFormatVersions.Office2019).Validate(doc).ShouldBeEmpty()` catches all of
it and is the highest-value assertion in the project. `ExportedDocument.ValidationErrors()` wraps it.

### Word merges adjacent identical borders

Consecutive paragraphs carrying **identical** `pBdr` are drawn as one frame. That is deliberate —
it is what makes the many paragraphs of one fence read as a single box, and the callouts rely on
it too — but Word cannot tell where one fence ends and the next begins. Two fences back to back
(the shape the cheatsheet uses to show a fence inside a fence) welded into one box with no edge
between them and no air above the second. `WriteCode` now writes a **one-point-high empty
paragraph** after every fence to break the group, the same way an empty paragraph is written after
every table.

### Other silent repairs

A `w:tc` with no `w:p`; two `w:tbl` with nothing between them; a duplicated
`w:bookmarkStart/@w:id`; a `wp:docPr/@id` that is `0` or repeated; a `ThemePart` without a
complete `a:fmtScheme`; a `FootnotesPart` without the separator notes at ids −1 and 0.

### Markdig dispatch order

Verified against `Markdig.dll` 1.3.2 by reflection:

```
AlertBlock            -> Markdig.Syntax.QuoteBlock
MathBlock             -> Markdig.Syntax.FencedCodeBlock
YamlFrontMatterBlock  -> Markdig.Syntax.CodeBlock
```

Match the subclass **first** or every callout renders as a block quote and every display equation
as a code block. Both are silent.

`FootnoteGroup` must be **walked, not skipped** — Markdig *relocates* definitions into it rather
than copying them, so skipping it drops every footnote body. What to skip is `YamlFrontMatterBlock`
(consumed into package properties) and `LinkReferenceDefinitionGroup`.

### Other

- `xml:space="preserve"` on **every** `w:t`, unconditionally. Without it ` and ` loses both spaces
  and the words run together.
- `w:noProof` on code runs, or Word squiggles every identifier in the file.
- Open XML SDK 3.x: `Close()` is gone (dispose writes the file); `ImagePartType` is a static class
  of `PartTypeInfo` fields, not an enum; relationship ids are **per part**, so an image used in
  both the body and a footer needs `footerPart.AddPart(imagePart)` and that part's own id.
- `w:space` on a border is a whole number of **points**, 0–31. There is nothing between 8 and 9.

---

## 10. Settings and UI surface

| Piece | Where |
| --- | --- |
| Menu item | `Views/MainWindow.xaml`, Tools menu, after Export to HTML |
| Context menu | `Views/MainWindow.ContextMenus.cs`, preview right-click |
| Dialog | `Views/WordExportDialog.cs` — paper, orientation, margins, three checkboxes |
| Preferences | `Views/PreferencesWindow.cs`, *Export & Print* page, WORD section |
| Settings | `AppSettings.DocxSetup`, with `[JsonIgnore] DocxDefaults => DocxSetup ?? DocxExportSetup.SeededFrom(PdfDefaults)` |

`DocxExportSetup` lives in Domain. Properties are `{ get; set; }` and **never `init`** — the JSON
source generator turns init-only properties into constructor parameters and assigns every one, so
a key absent from an older settings file would arrive as default and silently wipe the initializer.

Every computed property carries `[JsonIgnore]`, or it is serialized into `settings.json` and then
read back as if it were a stored preference.

`SeededFrom` carries paper and orientation but **not the margin** — a document meant to be edited
and one meant to be printed want different margins often enough that inheriting the answer is a
worse guess than starting from Word's default. That is now a decision rather than a units mismatch.

Defaults: header and footer **on** (a shareable Word file without page numbers is the surprising
outcome); contents and title page **off** (both insert visible content the markdown did not ask
for); heading bookmarks always on (invisible, and what makes Word's navigation pane work).

---

## 11. Testing

### Commands

```powershell
# gates - all four are idempotent and have a -Check CI form
pwsh ./build/Add-FileHeaders.ps1 -Check
pwsh ./build/Set-AmericanSpelling.ps1 -Check
pwsh ./build/Test-ButtonStandards.ps1 -Check
pwsh ./build/Test-DocumentColors.ps1 -Check

# the fast loop while iterating on the exporter
dotnet test tests/PaulTechGuy.MQ.Docx.Tests/PaulTechGuy.MQ.Docx.Tests.csproj -c Debug

# the full check before calling anything done
dotnet build PaulTechGuy.MQ.slnx --no-incremental -c Debug
dotnet test PaulTechGuy.MQ.slnx --no-build -c Debug
```

> `dotnet test --no-build` will happily run **stale binaries** if the build failed. Use
> `--no-incremental` when a result surprises you.

> A `webshell/` edit only reaches the output tree you actually build, and Release is run from
> Visual Studio. A preview fix needs the Release rebuild before it is visible.

### The test helper

`tests/PaulTechGuy.MQ.Docx.Tests/ExportedDocument.cs` is how to iterate quickly:

```csharp
using var exported = await ExportedDocument.FromAsync(
    markdown,                       // required
    setup: null,                    // DocxExportSetup, defaults to DocxExportSetup.Default
    headingNumbering: HeadingNumbering.Off,
    renderedPreviewHtml: null,      // the sidecar; null exercises the degraded path
    sourceDocumentPath: null,       // for relative images
    diagramPng: null);              // Func<string, Task<byte[]?>>

exported.DocumentXml();             // word/document.xml
exported.StylesXml();               // word/styles.xml
exported.NumberingXml();            // word/numbering.xml
exported.FootnotesXml();
exported.FootnotesText();
exported.PlainText();               // body text, for "is it in there at all"
exported.ValidationErrors();        // OpenXmlValidator, Office2019
exported.Skipped;                   // the report the dialog shows
exported.Path;                      // open it in Word if you need to look
```

`MathTests.Display(mathml, tex)` and `Paragraph(line, html)` build the preview-HTML fixtures.

### What each file covers

| File | Facts |
| --- | --- |
| `FurnitureTests.cs` | 22 — header, footer, contents field and range, sections, title page, package properties |
| `MathTests.cs` | 16 — OMML shapes, n-ary, fences, fallback |
| `BlockTests.cs` | 14 — headings, paragraphs, quotes, rules, code |
| `ListTests.cs` | 13 — instances, levels, task items, tight vs loose, indent |
| `InlineTests.cs` | 13 — emphasis, links, marks, sub/sup |
| `InlineHtmlTests.cs` | 13 — `<kbd>`, `<br>`, `<sub>`, and what is dropped |
| `CalloutAndNoteTests.cs` | 12 — the five callouts, definition lists, footnotes |
| `AwkwardDocumentTests.cs` | 12 — the third fixture: grid tables, custom containers, nomnoml, raw HTML, a heading inside a callout, code inside a footnote |
| `TableTests.cs` | 11 — pipe and grid, widths, alignment, header repeat |
| `PreviewHarvestTests.cs` | 11 — line keying, staleness guards, diagram sizing |
| `AppearanceTests.cs` | 10 — margins, TOC styles, code spacing, fence seams |
| `ImageTests.cs` | 9 — EMU sizing, dedupe, missing and remote images |
| `PageSetupTests.cs` | 6 — paper, orientation, twips |

**181 tests in the Docx project; 11 test projects pass overall.**

### Corpus

- `src/PaulTechGuy.MQ.App/Assets/Welcome to Marqora.md`
- `webshell/cheatsheet.md`

Between them: all five callouts, task lists, footnotes, abbreviations, definition lists, mermaid,
math, every emphasis extra, nested fences. Neither has a **grid table** or a **custom container** —
`grep -c '^+-'` and `grep -c '^:::'` both return 0 — which is why `AwkwardDocumentTests` exists.

### The check unit tests cannot give you

Every `.docx` must open with **no "Word found unreadable content" prompt**. Word repairs a great
deal silently and says nothing. Open the corpus files by hand once per session.

For the PDF comparison: export both, print the `.docx` to *Microsoft Print to PDF* at the same
paper and margins, and judge **content and structure** — every heading present and numbered
identically, every table with the same rows and alignment, lists nested to the same depth, every
diagram and equation present, footnotes pointing at the right references, nothing duplicated or
silently dropped. Pixel differences are expected and are not defects.

---

## 12. Fixed already — do not re-report

Everything below was found and fixed during the first testing session. Listed so a fresh session
does not spend time re-finding them, and so a regression is recognizable.

| Area | Was | Now |
| --- | --- | --- |
| Heading numbers | literal text; Word would not renumber | real multilevel numbering linked to heading styles |
| Contents entries | wore the formatting of their headings | `TOC1..3` styles, ordinary body text |
| Contents listing itself | "Contents" was `Heading1`, inside the field range | `TOCHeading`, `outlineLvl` 9 |
| Contents listing the title | range fixed at `1-3` | range follows `HeadingNumbering` |
| Margins | Normal meant 0.5" (the PDF's meaning) | Word's 1"; PDF unified onto the same enum |
| Margin labels | `"Wide (1 in, 2 sides)"` — reads as *1 inch on 2 sides* | `"Wide - 1 in top, 2 in sides"`, one shared list |
| Footer | left-aligned, `Page X of Y` on `NUMPAGES` | right-aligned, bare `PAGE` field |
| Code blocks | bold; no gap beneath | not bold; `After 160` outside the border |
| Adjacent fences | welded into one gray box | one-point seam paragraph after every fence |
| Table width | content-fitted, far narrower than the PDF | `tblW pct 5000` + autofit (AutoFit to Window) |
| `<kbd>` | plain text | inline code style |
| Task lists | one ticked item stripped the markers off the whole list and flattened every nested list | decided per item |
| List indent | 720 twips, visibly deeper than the PDF | 360 |
| Callouts | no air beneath; prose began against the panel | fill-colored top/bottom borders + `After 160` |
| Diagrams | 120 twips of spacing, top and bottom unequal | 360 both sides |
| Title page | one line from front matter | five-line template with gray placeholders |
| Sections | one section, `titlePg` | three sections, roman front matter, body restarts at 1 |
| Integrals | dotted rectangle from an empty `m:e` | integrand absorbed into the n-ary base |
| Footnotes in the preview | two horizontal rules at the end of every document | `app.css` hides Markdig's `<hr>`; the styled `border-top` stays |

---

## 13. Known gaps and open items

- **`.mq-preview ul.contains-task-list` and `.mq-preview .task-list-item` in `app.css` are dead.**
  Nothing emits those classes — Markdig does not (verified against all five binaries in the
  package cache) and no webshell JS adds them. Checkbox lists in the preview fall through to the
  plain `ul` rule and render as bullet-plus-native-checkbox. Preview-side, not export-side, and
  not yet fixed.
- **Standalone images** get Normal's 8pt spacing and have a milder version of the problem diagrams
  had. Not yet raised.
- **The fence seam is a real paragraph mark.** With formatting marks on, a small `¶` appears after
  every fence. The alternative is to look ahead and emit it only between two adjacent fences.
- **The diagram pop-out has no Word export**, deliberately — a `.docx` holding one picture is a
  worse artifact than the PNG its Copy as PNG already produces.
- **Folio stays HTML.** A Folio is a self-contained web bundle by definition.
- **No keyboard accelerator**, matching PDF and HTML export.
- **MathML to OMML is the one part with unbounded scope.** Microsoft's own `MML2OMML.XSL` is an
  order of magnitude larger than this converter. Unmapped elements are named in the export report
  rather than dropped silently.
- **Remote images cannot be embedded** — no network calls at runtime, a stated product rule. They
  are reported as skipped. The preview cannot load them either, so nothing is lost visually.

---

## 14. If something looks wrong, check these first

1. **Is the Release build current?** Compare the `.dll` timestamps under
   `src/PaulTechGuy.MQ.App/bin/Release/…` against the source file that changed. There are three
   output trees and a build only updates the one you built.
2. **Is it a saved preference rather than a bug?** Read
   `%LOCALAPPDATA%\PaulTechGuy\Marqora\settings.json` — `docxSetup` and `pdfSetup`. A rebuild never
   changes one. Settings are written 750 ms after a change, so the file is current once the app has
   been idle a moment; editing it while Marqora is running is pointless, because the in-memory copy
   is reflushed on the next change.
3. **Does it reproduce without the preview?** Pass `renderedPreviewHtml: null` in a test. If it
   does, the bug is in the AST walk; if it does not, it is in the harvest or the staleness guards.
4. **Does the validator complain?** `exported.ValidationErrors()` catches every schema-order
   mistake, and those are exactly the ones Word reports as unreadable content without saying why.
5. **Is shading running past where you expected?** See *Shading stops at borders* above.
6. **Are two things merging?** Adjacent paragraphs with identical borders become one frame;
   adjacent tables become one table.
