# Export alignment: handoff to a fresh session

Written 2026-10-08 to restart the Word/PDF/Print alignment work in a new conversation. Read this
first, then `docs/Export-Alignment-Plan.md` only for the section you are working on. **Every
decision recorded here is settled. Do not reopen it, re-derive it, or re-ask it.**

---

## 1. The goal, in one paragraph

A Marqora user said Word export beat PDF export, so they exported to Word and then to PDF. The
goal is for **Export to PDF, Print and Export to Word to look the same**: same fonts, sizes and
spacing; the same cover, contents and page-number furniture; the same content, with anything
that cannot be carried named in a report rather than silently dropped. Where an export cannot
match, the difference must be known and reported.

## 2. Where it stands

| Phase | What | State |
| --- | --- | --- |
| 0 | Bug fixes found by the side-by-side comparison | **Committed** `6a92c7d` |
| Spike | Paged.js engine proven (S1–S6) | **Committed** `9db875a` |
| 1 | Paper spec: one statement of type and space read by Word and the PDF | **Committed** `c47b469` |
| 2 | Paged engine behind PDF and Print, shared furniture, PDF report, gate | **Gate passed 2026-10-08, committed** |
| 3 | Word fidelity: math, diagrams, raw HTML | **Gate passed 2026-10-08, committed** `9eb28a5` (shell fixes `f68c4ef`) |
| 4 | Footnote fallback reporting | **Built 2026-10-08**: a note Paged.js cannot place sends the whole PDF to endnotes, with an advisory report row. Not yet seen in the app: the fixture's notes all fit |

**Phase 2 passed its gate on 2026-10-08** (§6 steps 1–4 done; the rerun scorecard is §2 of `docs/WordVsPdf-Comparison.md`). Next is phase 3, §6 step 5.

## 3. Standing rules for whoever picks this up

- **Commits:** only when Paul asks. Author `PaulTechGuy <pcarver@gmail.com>`, **no**
  Co-Authored-By, no Claude trailer or session line. Stay on `dev`; never push, branch or merge.
- **Never drive the app or the printer.** No SendKeys, no clicking. Paul runs every in-app test
  and reports; read results from the log at `%LOCALAPPDATA%\PaulTechGuy\Marqora\logs\` (newest
  `marqora-YYYYMMDD_NNN.log`) and from PDFs he saves to the scratchpad.
- **After any `.cs` edit:** `pwsh ./build/Add-FileHeaders.ps1 -Check`. Also run
  `Set-AmericanSpelling.ps1 -Check`, `Test-NetworkClaim.ps1 -Check`, `Test-ButtonStandards.ps1
  -Check`, `Test-WebShell.ps1`, `Update-PaperDesign.ps1 -Check`, and every test project.
- **Webshell edits** (`webshell/*.js|css|html`) only reach the build Paul rebuilds; tell him to
  rebuild.
- **Never format `docs/UltimateMarkdownContent.md`** (its mistakes are its tests).
- **American English** in files and in replies. Say "offline by default", never "no network
  calls".
- **Shell gotchas on this machine:** no Python. `sed` multi-line inserts with `\` often collapse
  onto one line — use the Edit tool for multi-line text. `\t`/`\c` in sed replacements become
  control characters. `WriteAllLines` writes CRLF; the repo is LF. Git Bash mangles `/PID` —
  use `//PID` or PowerShell. WinRT PDF rendering needs `powershell.exe` (5.1), not `pwsh`.
- **Scratchpad tools** (in the session scratchpad; recreate if gone): `Render-Pdf.ps1 -Pdf
  <file> -OutDir <dir> -Width 700` (pages to PNG) and `Montage.ps1 -Dir <dir> -Out <png>
  -PageList "1,2,3" -Columns 4 -CellWidth 350`.

## 4. Settled decisions (do not revisit)

- **Engine:** PDF and Print use Paged.js 0.4.3 in an off-screen WebView2 (`PagedPrintHost`),
  then DevTools `Page.printToPDF` (PDF) or `CoreWebView2.PrintAsync` (printer). The old
  live-preview path is the **classic engine**: automatic fallback, and a Preferences switch
  "Use the classic print engine" (`PdfPageSetup.UseClassicEngine`) kept **for one release only**,
  then removed with `WebViewPrinting`.
- **Fallback rules** (`Services/PagedJobs.cs`, one copy for all windows): fall back when the page
  gives no markup or the engine fails *before sending*; never fall back after pages may have
  reached a printer (would print twice) — report instead; never fall back for an unwritable PDF.
- **Printer edge:** printer jobs lay out 0.25 in inset with 0.25 in printer margins
  (`PrinterEdgeInches`); zero margins hung an HP LaserJet.
- **Page ranges are never handed to Chromium.** A range not starting at page 1 hung every
  printer (HP and Microsoft Print to PDF). `print.js keepPages` hides the other pages instead.
- **Paper spec** (phase 1): `PaperSpec`/`PaperFaces` in Domain; Segoe UI text, Segoe UI
  Semibold display, Cascadia Mono code; body 11 pt / 1.4 / 8 pt after; inline code regular.
- **One furniture record:** `ExportLayout` (header+page numbers default on, contents off, title
  page off), shared by Word, PDF and Print dialogs and Preferences ("WORD AND PDF" group). Old
  settings migrate on read (`AppSettings.LayoutDefaults`) and on preferences import.
- **Title page:** unanswered front-matter lines are left out in both Word and PDF (no gray
  placeholders).
- **Shading box:** labeled "Shade code, tables and callouts"; one setting shown in the PDF
  dialog, the Print dialog (all three windows) and Preferences. Word always shades.
- **Dialogs:** Print, Export to PDF and Export to Word are two-column ContentDialogs
  (`DialogFields.TwoColumns`) so they fit a 768 px laptop screen. PDF export has **no** page range
  (use Print → Microsoft Print to PDF for that).
- **PDF report:** shown only when something is missing (Word's report always shows). Rows:
  pictures from the editor's link check (Not found / Not on this machine / Outside the
  document's folder), diagrams from the shell's print markup.

## 5. What phase 2 built

Domain: `ExportLayout.cs`, `PaperFurniture.cs` (with `PrintEngine`), `PdfPageSetup.UseClassicEngine`,
`AppSettings.Layout/LayoutDefaults/WithLayout`, `DocxExportSetup` legacy keys.
Services: `PagedJobs.cs` (new), `PagedPrintHost.cs` (own WebView2 environment in
`...\Marqora\WebView2-Print`, `EndBrowser` on timeout and shutdown, `PagedOutputException`,
paper check, page kinds), `WebViewPreviewHost.cs` (PDF/Print via `PagedJobs`, diagram issues,
`AttachAsync` readable error, `FirstDocumentShown`), `HtmlPageLayout.cs` (renamed from the old
static `ExportLayout`).
Views: `LayoutFields.cs` (new), `PrintDialog`, `PdfExportDialog`, `WordExportDialog`,
`DialogFields`, `PreferencesWindow`, `CheatsheetWindow`, `DiagramWindow`, `MainWindow.xaml(.cs)`,
`MainWindow.DiagramSvgs.cs` + `MainViewModel.DiagramSvgs.cs` (Debug-only "Save diagram SVGs for
Word", kept for phase 3; the spike's PDF/print items are gone).
Webshell: `app.js` (`diagramErrors`, `firstDocumentShown`), `print.js` (`keepPages`, `KINDS`,
`measureStyles`), `cheatsheet.js` (`requestPrintHtml`, waits for light drawings, Ctrl+P →
Marqora Print), `cheatsheet.css/.html` (`.mq-cheatsheet` window rules vs `.mq-cheatsheet-doc`
paper rules).
Word: `ExportReport.MathWithoutPreview` — an equation with no preview MathML is now reported
(found by the census).
Tests: `ContentCensusTests.cs` (new), settings migration and import tests, layout tests.
Docs: `Architecture.md` decision 3 rewritten, `WebViewPrinting` summary, README PDF row,
`CLAUDE.md` report-window paragraph, `WordExport.md`, plan progress line.
Unrelated fixes made along the way (also uncommitted): no landing-page flash at startup and an
"Opening your documents…" message until the first document is painted (`MainViewModel`
`IsStartupSettled`/`ShowsStartupMessage`, `MainWindow.xaml`).

**Verified by Paul in the app:** page ranges to HP and MS PDF; Print dialog layout boxes; shared
layout across dialogs; title-page behavior; startup message.
**Not yet verified:** everything in §6.

## 6. Next steps, in order (do these and nothing else first)

1. **Gate run** — ask Paul to rebuild Debug, then:
   1. Export `docs/UltimateMarkdownContent.md` to PDF into the scratchpad. The status bar must
      **not** mention the classic engine. (The last run fell back: the separate WebView2
      environment was being overridden by `WEBVIEW2_USER_DATA_FOLDER`; fixed in
      `PagedPrintHost.EnvironmentAsync` but not yet tried.)
   2. In the cheatsheet press **Ctrl+P**: Marqora's two-column Print dialog must open (not
      Chromium's). Print to Microsoft Print to PDF.
   3. In a diagram pop-out, Export to PDF.
2. **Read the results:** in the log look for `Paged PDF ... pages`, `paper check, N kinds of
   element match the spec` (or `paper check, <difference>` warnings), and no `could not write`.
   Render the PDFs: the cheatsheet must have no browser date/URL in its margins; the diagram
   must be on one page.
3. **Fix only what that run shows.** Then rerun the Word-vs-PDF comparison with the new PDF and
   the existing `UltimateMarkdownContent.docx` (scratchpad, 2026-10-07 19:08) and update
   `docs/WordVsPdf-Comparison.md`'s scorecard.
4. **Ask Paul whether to commit phase 2** (suggest two commits: the phase 2 export work, and the
   startup landing-page/message fix).
5. **Phase 3** (plan §7), in this order: the line-1119 `\textcolor`/`\colorbox` equation (the
   corpus converts it, the live app did not — capture the preview's MathML for that line); Word
   drops the line-909 `<iframe>` without a report row; adjacent quotes merging into one panel and
   nested quotes not indenting in Word; HTML tables to Word tables; diagrams Word cannot draw
   (SVG allowlist from spike S6, labels in `foreignObject`).
6. **Phase 4:** report a footnote Paged.js cannot place at the page foot (plan O4).
7. **Deferred by Paul:** the `CLAUDE.md` "Paper spec" section — do not add until he asks.

## 7. Known open items (not bugs in phase 2's code paths)

- **`align` equation numbers print (0) on every line in the paged PDF** (found by the
  2026-10-08 rerun). KaTeX numbers through a CSS counter (`katexEqnNo`, reset on `body`) that
  does not survive Paged.js. The preview's own (1)(1)(1) is the older defect underneath, so a
  fix touches the preview too; Paul has not decided when.
- To test the cheatsheet's Ctrl+P, click into the cheatsheet first. It opens without focus on
  purpose (`CheatsheetService.OpenAsync`), so a Ctrl+P straight after opening it prints the
  main window's document.

- The classic engine and its Preferences switch are to be removed one release after the paged
  engine ships.
- Microsoft Print to PDF output looks a hair heavier than the direct PDF — the printer driver's
  rendering, accepted.
- Word's report says "Example image alt text" where the PDF report says the URL for the same
  picture — cosmetic; the link check has no alt text.

## 8. Where to look

- Plan: `docs/Export-Alignment-Plan.md` (§6 phase 2, §7 phase 3, §8 paper spec, §9 checks,
  §11 gates, §12 review log). Its progress line (line 5) is long; this file supersedes it.
- Comparison: `docs/WordVsPdf-Comparison.md`. Paper table: `docs/Paper-Design.md`.
- Memory notes in the Claude project memory: `hp-paged-print-stall`, `color-themes-design`,
  `commits-bear-pauls-name`, `no-desktop-input-automation`.
