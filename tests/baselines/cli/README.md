# CLI export baseline

What the app exports today, kept so that the refactor in M2 can prove it changed nothing
(`docs/CLI-Implementation-Plan.md`, WP-M0-05 and CLI-ARC-06), and so the CLI's own exports can
be compared with the app's.

**Made from the app before any M1 or M2 work package changes app code.** Once made, nothing in
this folder is edited or re-exported over; a new baseline is a new decision.

The exports are git-ignored: they are large and they are evidence, not source. What is
committed is this file, the input in `inputs/`, and `MANIFEST.sha256`, which proves later that
the exports are still the ones that were made.

## Before you start

1. **Build and run the app as it is at the commit you are baselining**, Release, the way you
   normally run it. Note the commit (`git rev-parse --short HEAD`).
2. **Open the three documents.** They are inputs and must stay byte-identical: do not edit,
   format or save any of them. If the app asks to save, say no.
   - `webshell/cheatsheet.md`
   - `docs/UltimateMarkdownContent.md`
   - `tests/baselines/cli/inputs/pictures.md`
3. **Set the export theme to "Same as the screen"** in Preferences, so that `View > Color Theme`
   alone decides what each export wears.
4. **Leave every export dialog as it opens**, and make no change in any of them: the same page
   setup, cover, contents and Word layout for every file. The Preferences file exported at the
   end records what those were, and the CLI uses that file to make the same exports.
5. **Save every file into this folder**, `tests/baselines/cli/`, under exactly the name given.

## Exports

Tick each as it is saved. `build/Get-BaselineHashes.ps1` reads the names from this list, so a
name typed wrongly shows up as missing.

### Color theme: Default

`View > Color Theme > Default`.

- [ ] `cheatsheet.default.html` — cheatsheet, `File > Export > HTML`
- [ ] `cheatsheet.default.pdf` — cheatsheet, `File > Export > PDF`
- [ ] `cheatsheet.default.docx` — cheatsheet, `File > Export > Word`
- [ ] `UltimateMarkdownContent.default.html` — UltimateMarkdownContent, HTML
- [ ] `UltimateMarkdownContent.default.pdf` — UltimateMarkdownContent, PDF
- [ ] `UltimateMarkdownContent.default.docx` — UltimateMarkdownContent, Word
- [ ] `pictures.default.html` — pictures, HTML
- [ ] `pictures.default.pdf` — pictures, PDF
- [ ] `pictures.default.docx` — pictures, Word

Then `File > Share as Folio...`, with exactly the three documents ticked, nothing fetched from the
web, and "Shrink images wider than" **unticked**:

- [ ] `folio.default.html` — the single-file form
- [ ] `folio.default.zip` — the zip form
- [ ] `folio.default.folder` — the folder form (a folder of that name)

And once more with "Shrink images wider than" **ticked**, at its default of 1600:

- [ ] `folio.default.reduced.html` — the single-file form
- [ ] `folio.default.reduced.zip` — the zip form
- [ ] `folio.default.reduced.folder` — the folder form

### Color theme: Vintage Press

`View > Color Theme > Vintage Press`. One document is enough to show a second palette arriving
in every format; the content checks above do not need repeating.

- [ ] `cheatsheet.vintage-press.html` — cheatsheet, HTML
- [ ] `cheatsheet.vintage-press.pdf` — cheatsheet, PDF
- [ ] `cheatsheet.vintage-press.docx` — cheatsheet, Word
- [ ] `folio.vintage-press.html` — Folio of the three, single-file form, shrinking unticked

### The settings that made them

- [ ] `preferences.json` — `Preferences > Export preferences...`, saved last, so it holds the
  dialogs' settings as they stood for every export above

Then put `View > Color Theme` back to whatever you use.

## Recording it

```powershell
pwsh ./build/Get-BaselineHashes.ps1
```

It checks every name above is present, writes `MANIFEST.sha256`, and prints the line that goes in
the plan's §9.3 record. Commit `MANIFEST.sha256` and this file, with the boxes ticked.

Later, at any point:

```powershell
pwsh ./build/Get-BaselineHashes.ps1 -Check
```

exits non-zero if any export has changed, gone missing, or been added since the manifest was
written.
