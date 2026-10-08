# Export alignment: handoff to a fresh session

Rewritten 2026-10-08 at the end of the session that finished the plan. Read this first, then
`docs/Export-Alignment-Plan.md` only for the section an item points at. **Every decision recorded
here is settled. Do not reopen it, re-derive it, or re-ask it.**

---

## 1. The goal, in one paragraph

A Marqora user said Word export beat PDF export, so they exported to Word and then to PDF. The
goal was for **Export to PDF, Print and Export to Word to look the same**: same fonts, sizes and
spacing; the same cover, contents and page-number furniture; the same content, with anything
that cannot be carried named in a report rather than silently dropped. That is done. What is
left is six smaller items (§6).

## 2. Where it stands

Every phase is committed and pushed to `origin/dev`.

| Phase | What | Commit |
| --- | --- | --- |
| 0 | Bug fixes from the side-by-side comparison | `6a92c7d` |
| Spike | Paged.js engine proven (S1–S6) | `9db875a` |
| 1 | Paper spec: one statement of type and space for Word and the PDF | `c47b469` |
| 2 | Paged engine behind PDF and Print, shared furniture, PDF report | `5178994` (startup fix `359fc83`) |
| 3 | Word fidelity: math, quotes, raw HTML, tables, vector diagrams | `9eb28a5` (shell fixes `f68c4ef`) |
| 4 | A footnote Paged.js cannot place makes the whole PDF endnotes | `9bdbc1f` |
| Open items | Word equation numbers and pictures, pie opacity, no empty contents | `123f085` |
| — | Each file dialog remembers its own folder | `cc0c437` |
| — | Classic print engine removed from the UI (kept as automatic fallback) | `11a3eb6` |
| — | Release notes: "PDF, Print and Word Now Match" | `0e1c8d0` |
| — | Diagram window refits when restored from maximized | `834569e` |

The scorecard in `docs/WordVsPdf-Comparison.md` §2 was rerun after phase 3: the only ❌ left is
mhchem, in both columns, which the fixture expects.

## 3. Standing rules for whoever picks this up

- **Commits and pushes:** only when Paul asks. Author `PaulTechGuy <pcarver@gmail.com>`
  (`git -c user.name=PaulTechGuy -c user.email=pcarver@gmail.com commit ...`), **no**
  Co-Authored-By, no Claude trailer or session line. Stay on `dev`; never branch or merge.
- **Never drive the app or the printer.** No SendKeys, no clicking. Paul runs every in-app step
  and reports; read results from the log at `%LOCALAPPDATA%\PaulTechGuy\Marqora\logs\` (newest
  `marqora-YYYYMMDD_NNN.log`) and from files he saves to the session scratchpad. **Give him the
  scratchpad's full path every time**, and the exact file name to save as: the path changes per
  session and he has been given two before.
- **Keep his rounds few.** Batch every change a unit test can cover, then ask for one export. Paul
  approved converting a `.docx` to PDF yourself through a hidden Word instance (§7); do that
  rather than asking him to save from Word.
- **After any `.cs` edit:** `pwsh ./build/Add-FileHeaders.ps1 -Check`. Also run
  `Set-AmericanSpelling.ps1 -Check`, `Test-NetworkClaim.ps1 -Check`, `Test-ButtonStandards.ps1
  -Check`, `Test-WebShell.ps1`, `Update-PaperDesign.ps1 -Check`, and every test project. A
  dictionary key or string literal that is a British spelling (`"grey"`) will be rewritten by the
  spelling script: leave it out.
- **Webshell edits** (`webshell/*.js|css|html`) only reach the build Paul rebuilds; tell him to
  rebuild Debug.
- **Never format `docs/UltimateMarkdownContent.md`** (its mistakes are its tests, and its byte
  order mark is one of them).
- **American English** in files and in replies. Say "offline by default", never "no network
  calls".
- **Shell gotchas on this machine:** no Python. The Write and Edit tools turn ` ` and the
  like into literal characters: put escapes back with a PowerShell `.Replace`. `sed` mangles
  `\c`/`\t` in replacements into control characters; use the Edit tool for anything with
  backslashes. `WriteAllLines` writes CRLF; the repo is LF. WinRT PDF rendering needs
  `powershell.exe` (5.1), not `pwsh`. `pdftotext` is in Git's `mingw64\bin`.

## 4. Settled decisions (do not revisit)

- **Engine:** PDF and Print use Paged.js 0.4.3 in an off-screen WebView2 (`PagedPrintHost`),
  then DevTools `Page.printToPDF` (PDF) or `CoreWebView2.PrintAsync` (printer). The classic
  engine (`WebViewPrinting`, printing the live preview) is **only the automatic fallback** now;
  Paul had its Preferences switch and `PdfPageSetup.UseClassicEngine` removed on 2026-10-08.
- **Fallback rules** (`Services/PagedJobs.cs`): fall back when the page gives no markup or the
  engine fails *before sending*; never after pages may have reached a printer (it would print
  twice) - report instead; never for an unwritable PDF.
- **Printer edge:** printer jobs lay out 0.25 in inset with 0.25 in printer margins
  (`PrinterEdgeInches`); zero margins hung an HP LaserJet.
- **Page ranges are never handed to Chromium**; `print.js keepPages` hides the other pages.
- **Paper spec:** `PaperSpec`/`PaperFaces` in Domain; Segoe UI text, Segoe UI Semibold display,
  Cascadia Mono code; body 11 pt / 1.4 / 8 pt after.
- **One furniture record:** `ExportLayout`, shared by the Word, PDF and Print dialogs and
  Preferences. A contents that would list nothing is left out of both exports
  (`ContentsListing.Levels`, `print.js`, `DocxExporter`).
- **Footnotes (O4):** one model per document. If Paged.js cannot place every note at its page
  foot, the whole PDF takes endnotes and the report gets an advisory row.
- **Equation numbers:** the shell counts them (`numberEquations` in `app.js`) and writes each onto
  its element; every output reads that. Word puts an `align`'s numbers in a right-hand matrix
  column beside the equation.
- **Equations Word cannot hold** go in as the preview's picture (`mathPngOf` in `app.js`: KaTeX
  HTML in an SVG `foreignObject`, fonts as `data:` URLs), alt text the TeX, reported as "not
  editable in Word". Inline ones are lowered by their depth to sit on the line.
- **Vector diagrams in Word:** only the ten types S6 saw drawn right
  (`PreviewHarvest.DrawsAsVector`), SVG as `asvg:svgBlip` with the PNG fallback; the SVG carries
  presentation attributes (`svgWithPresentation`) because Word ignores mermaid's class rules.
- **Color themes:** diagrams and exports use the screen's theme unless Preferences names an export
  theme - the pale Nordic pie is that, by design.
- **File dialogs:** each purpose keeps its own folder in `AppSettings.DialogFolders` (session
  state, never exported), because Windows' own per-dialog memory never records `%TEMP%`.

## 5. The test material

- **`docs/UltimateMarkdownContent.md`** - the fixture. All its equations convert; its three
  footnotes all fit at the page foot.
- **`OpenItems-Test.md`** - lived in the last session's scratchpad and is gone. Recreate it when
  needed: an inline and a displayed `\cancel` equation (Word has no form for `<menclose>`, so they
  test the picture fallback), a two-line `align`, a mermaid `pie`, and a long footnote. Write it
  with PowerShell or the Write tool and check its size from a second process before giving Paul
  the path.

## 6. The six remaining items

Do them in this order unless Paul says otherwise. None blocks a release.

### 6.1 Check the diagram window refit (`834569e`) in the app

Built and committed, never run. Ask Paul to open a diagram in its own window, maximize it,
restore it: the diagram should refit to the smaller window. Code: `DiagramWindow.OnWindowChanged`
sends the page `zoomFit` when the presenter goes from Maximized to Restored, as `MaximizeAndFit`
does. If it fails to refit, the likely cause is the order of `AppWindow.Changed` events (size
before presenter); log `presenter.State` there and look.

### 6.2 See the endnote fallback (phase 4) happen once

Never triggered in the app. Paged.js placed even a 90-line footnote, carrying it on to the next
page's foot, which counts as placed. A note it cannot place at all is the trigger - most likely
one holding block content: a table, a long fenced code block, a list. Write a test document with
such a note and have Paul export it to PDF. Expect: the log line `Paged PDF: N of M footnotes
reached the foot of their page; laying the document out again with every note at the end.`, the
notes as a numbered list at the end, and the report's advisory row "The footnotes are endnotes in
this PDF". Code: `print.js` `notesAsEndnotes`, `PagedPrintHost.RunAsync`, the row in
`WebViewPreviewHost.ExportPdfAsync`. If no document can trigger it, say so and leave it.

### 6.3 Inline formatting inside a raw HTML block, in Word

The fixture's styled callout (line 871) keeps its box in Word but loses its `<strong>`: a raw HTML
block's text goes in as plain paragraphs (`HtmlBlockText.Paragraphs` strips every tag). Turn the
inline tags the walker already understands into runs - `InlineHtml.Parse`/`Apply` handle `b`,
`strong`, `i`, `em`, `code`, `mark`, `sub`, `sup`, `small`, `q` for inline HTML elsewhere - and
keep the paragraph splitting and the summary-in-bold as they are. Unit-testable; the report row
then becomes "its text and its box are in the document" with less to say it lost.

### 6.4 The matryoshka's nesting, in Word

Fixture §16.1: a list item holding a blockquote holding a list. In Word the inner list leaves the
quote - it is set at the margin with no bar. `BlockRenderer.WriteQuote` styles a list paragraph
inside a quote with the Quote style but leaves its indent alone (a list keeps its own numbering
indent), so the quote's bar and step are lost on exactly those paragraphs. Work out the indent
the list needs inside the quote (quote step plus list indent) and test it with the fixture's
shape. Check Word's rendering with the conversion script.

### 6.5 Word equation numbers at the right margin

Word sets an `align`'s numbers in a column beside the equation; the PDF sets them at the right
margin. Matching means writing the equation as Word's own numbered form - an equation array with
`#(n)` - instead of a matrix, which `MathmlToOmml.Number` would have to rebuild, alignment points
included. It is the riskiest OMML in the converter and is 🟡, not ❌. **Ask Paul before starting**,
and research the exact OMML Word 365 writes for a numbered equation first (make one in Word, unzip
the `.docx`).

### 6.6 Remove the classic engine's code

Its switch is gone; its code remains as the automatic fallback (`WebViewPrinting`, the
`exportClassic`/`printClassic` paths in `PagedJobs`, the "(with the classic engine; ...)" status
lines in `MainViewModel`). Removing it means a paged-engine failure becomes an error the user
sees instead of a lesser PDF. **Paul's decision, once he trusts the paged engine on other
machines** - ask; do not do it unasked.

## 7. Tools

Recreate these in the new session's scratchpad.

- **Word to PDF through a hidden Word** (Paul approved it): Word via COM, invisible, read-only,
  alerts off, contents fields updated, `ExportAsFixedFormat(path, 17, ..., CreateBookmarks: 1)`,
  run inside a `Start-Job` with a timeout that ends only the `WINWORD` process the script
  started. Takes about ten seconds for the fixture. Word may linger a few seconds after `Quit`.
- **Render a PDF to PNGs:** WinRT `Windows.Data.Pdf` in `powershell.exe` 5.1.
- **Montage:** `System.Drawing`, pages side by side, for looking at several at once.
- **Text:** `pdftotext -layout`.

## 8. Accepted, not bugs

- Microsoft Print to PDF looks a hair heavier than the direct PDF: the printer driver.
- Word's report names a remote picture by its alt text where the PDF's names its URL.
- Inline `<img>` and `<svg>` given as an SVG `data:` URL cannot be shown by Word without a raster
  fallback, and are reported under their alt text.
- To test the cheatsheet's Ctrl+P, click into the cheatsheet first: it opens without focus on
  purpose, so Ctrl+P straight after opening it prints the main window's document.
- Two commits since `v1.0.12` that are not this work - `62afd5f` (the cheatsheet takes the color
  theme) and `3a9795e` (diagram padding, open-in-fit-mode fix) - are not in the vNext release
  notes. Paul has not said whether they should be.

## 9. Where to look

- Plan: `docs/Export-Alignment-Plan.md` (§6 phase 2, §7 phase 3, §8 paper spec, §11 gates).
- Comparison and scorecard: `docs/WordVsPdf-Comparison.md`. Paper table: `docs/Paper-Design.md`.
- Release notes: `build/release-notes-vnext.md`.
- Claude project memory: `export-alignment-handoff`, `commits-bear-pauls-name`,
  `no-desktop-input-automation`, `hp-paged-print-stall`.
