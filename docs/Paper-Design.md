# Paper design

How a document is set on paper: the faces, sizes, weights, line heights and spacing that both
exports use. Word's styles are written from these values, and the print stylesheet reads them,
so the `.docx` and the PDF of one document are set alike.

The values live in code, in `src/PaulTechGuy.MQ.Domain/PaperSpec.cs` and
`src/PaulTechGuy.MQ.Domain/PaperFaces.cs`. The tables below are generated from them. Do not edit
the tables by hand; change the code and run:

```powershell
pwsh ./build/Update-PaperDesign.ps1          # rewrite the tables from the code
pwsh ./build/Update-PaperDesign.ps1 -Check   # report only; non-zero exit if they have fallen behind
```

## The rules

- **A font is named in `PaperFaces` and nowhere else.** An element names a role - text, display,
  code, diagram - never a font. Changing a face is a one-row edit, and Word's theme fonts, the
  print stylesheet, the paged print page and every printout follow it. `PaperSpecTests` fails if
  a font is named in Word's styles or theme, the print block of `app.css`, or `print.js`.
- **A size, line height or gap is stated in `PaperSpec` and nowhere else.** `DocxStyles` reads
  every one; the print rules read them as `--mq-paper-<element>-<value>` custom properties the
  host pushes, with the screen's old values only as a fallback for pages the host never reaches
  (the cheatsheet, an exported HTML file).
- **Every element is printed.** `PaperSpecTests` fails if an element's size is read by no print
  rule.
- **Sizes are whole or half points**, because Word stores half-points.
- **Paper only.** The screen keeps its own sizing and the reader's chosen preview font.

Colors are not here. They are the color theme's, by the rules in `docs/ColorThemes-Authoring.md`.

## Why these faces

Segoe UI and Cascadia Mono, not Aptos. Aptos is an Office cloud font, installed where WebView2
cannot see it, so a spec naming it gave Word Aptos and the PDF Segoe UI. Every face below is
visible to both engines on every supported Windows; the paged print engine's spike measured it
(`docs/Export-Alignment-Plan.md`, §4, S4). Display is a separate family in Word ("Segoe UI
Semibold") because Word's weight is only bold or not; CSS reaches the same face as weight 600.

## The values

Settled on 2026-10-06 and 2026-10-07: body 11 pt at a line height of 1.4 with 8 pt after a
paragraph; the preview's heading scale in semibold; code at 9.5 pt, regular weight; quotes
upright.

<!-- paper-table:start -->
| Role | Word | CSS | CSS weight |
|---|---|---|---|
| Text | Segoe UI | `"Segoe UI Variable Text", "Segoe UI", sans-serif` | 400 |
| Display | Segoe UI Semibold | `"Segoe UI Variable Display", "Segoe UI", sans-serif` | 600 |
| Code | Cascadia Mono | `"Cascadia Mono", Consolas, monospace` | 400 |
| Diagram | — | `"Segoe UI", sans-serif` | 400 |

| Element | What it is | Face | Size | Line | Before | After | Set |
|---|---|---|---|---|---|---|---|
| `body` | Paragraphs, list items and anything else not listed below. | Text | 11 pt | 1.4 | 0 pt | 8 pt | — |
| `h1` | Heading 1. | Display | 22 pt | 1.2 | 18 pt | 6 pt | — |
| `h2` | Heading 2, with the theme's rule beneath it. | Display | 17 pt | 1.2 | 16 pt | 6 pt | — |
| `h3` | Heading 3. | Display | 14 pt | 1.25 | 14 pt | 4 pt | — |
| `h4` | Heading 4. | Display | 12 pt | 1.3 | 12 pt | 4 pt | — |
| `h5` | Heading 5. | Display | 11 pt | 1.3 | 10 pt | 4 pt | — |
| `h6` | Heading 6, in capitals. | Display | 10 pt | 1.3 | 10 pt | 4 pt | capitals |
| `code-block` | A fenced or indented code block. | Code | 9.5 pt | 1.35 | 8 pt | 8 pt | — |
| `code-inline` | Inline code, inside a sentence. | Code | 9.5 pt | 1.4 | 0 pt | 0 pt | — |
| `quote` | A blockquote, upright. | Text | 11 pt | 1.4 | 8 pt | 8 pt | — |
| `table` | Table cells, header row included. | Text | 10.5 pt | 1.3 | 2 pt | 2 pt | — |
| `footnote` | A footnote at the foot of the page. | Text | 9.5 pt | 1.3 | 0 pt | 0 pt | — |
| `caption` | A figure's caption. | Text | 10 pt | 1.3 | 0 pt | 10 pt | italic |
| `callout-title` | A callout's label: Note, Warning. | Text | 11 pt | 1.4 | 0 pt | 0 pt | bold |
| `header` | The running header: the document's title. | Text | 9 pt | 1 | 0 pt | 0 pt | — |
| `footer` | The running footer: the page number. | Text | 9 pt | 1 | 0 pt | 0 pt | — |
| `contents-entry` | One line of the contents. | Text | 11 pt | 1.4 | 0 pt | 0 pt | — |
| `cover-title` | The cover page's title. | Display | 28 pt | 1.15 | 0 pt | 4 pt | — |
| `cover-subtitle` | The cover page's subtitle. | Text | 14 pt | 1.3 | 0 pt | 8 pt | — |
<!-- paper-table:end -->
