# Marqora CLI: feasibility study

A conceptual study of `marqora-cli.exe`, a command-line companion to Marqora that runs the
app's document commands — export, format, heading numbers, replace, Folio — over one or more
markdown files without opening a window.

This is not the plan. It records what is feasible, what it costs, the decisions already taken,
and the questions a detailed plan has to close. It is written against `dev` at `eab7281`
(2026-10-09).

> **Verdict: feasible.** Every text command already lives in a plain `net10.0` library and needs
> little more than a front end. The exports are the real work: they come from the *rendered*
> preview, so the CLI has to host the webshell in a hidden WebView2 — which `PagedPrintHost`
> already proves can be done — and the export code that sits in the WinUI project today has to
> move out of it.

---

## 1. Decisions already taken

| Question | Decision |
| --- | --- |
| How exports render | **Headless WebView2.** The CLI hosts the same webshell in a WebView2 that is never shown. One rendering pipeline, so a CLI export and an app export are the same document. |
| How it ships | **Beside the app.** `marqora-cli.exe` goes in the same install folder and shares `webshell/`, the themes and the .NET runtime. The installer offers to add the folder to the user's `PATH`. |
| What a text command does by default | **Leaves the file alone.** Without `--write` the result goes to stdout. `--write` rewrites in place, `--check` fails if anything would change, `--dry-run` prints a diff. |
| Whose settings | **Hermetic, with opt-in.** Built-in defaults plus flags, so a CI box and a desk give the same bytes. `--settings app` adopts the desktop Preferences; `--settings <file.json>` adopts an exported Preferences file. |
| Color theme | **`--theme <id or name>`** on every export and on `folio`. The named theme's light palette is what the output wears. Without it: Default, or the theme `--settings` names. An unknown name fails. `themes list` shows what there is. (2.7) |
| Several commands, several files | **Pipes and globs.** One verb per invocation, many files per verb. Text transforms chain through stdin/stdout. |
| Reporting | **Readable by default, `--json` for machines.** Messages on stderr, a documented exit-code table, color that respects `NO_COLOR`. |
| Invocation style | **Verbs.** `marqora-cli <verb> [options] <files...>`. |

---

## 2. What the CLI does

### 2.1 Verbs

Named after the menu items they mirror, so the manual and the app use the same words.

| Verb | Mirrors | Changes markdown | Needs the browser |
| --- | --- | :---: | :---: |
| `format` | Format > Format Document | yes | no |
| `number` | Format > Number Headings | yes | no |
| `unnumber` | Format > Remove Heading Numbers | yes | no |
| `replace` | Edit > Replace All | yes | no |
| `renumber-lists` | Source menu > Renumber List (whole document) | yes | no |
| `diagram-layout` | Format > Diagram Layout | yes | no |
| `check` | The analysis behind the source pane's squiggles: dead local links and anchors, missing images, style diagnostics | no | no |
| `toc` | Contents listing: print it, or insert it with `--write` | with `--write` | no |
| `outline` | The outline pane: print the heading tree | no | no |
| `export html` | File > Export > HTML | no | **yes** |
| `export pdf` | File > Export > PDF | no | **yes** |
| `export word` | File > Export > Word | no | **yes** (diagrams, math, code colors) |
| `folio` | File > Share as Folio | no | **yes** |
| `unpack` | File > Open Folio (take a Folio apart into files) | no | no |
| `themes list` | The color theme gallery: each theme's id, name and description, for `--theme` | no | no |
| `version`, `completion` | — | no | no |

Half the list never starts a browser. That matters for speed: a text verb should finish in well
under a second, while the first export in a run pays WebView2's cold start.

### 2.2 Shape of an invocation

```powershell
# Format a whole folder in place
marqora-cli format --write "docs/**/*.md"

# CI gate: fail the build if anything is unformatted, or a local link is dead
marqora-cli format --check "docs/**/*.md"
marqora-cli check "docs/**/*.md"

# What would numbering do? A diff, nothing written
marqora-cli number --dry-run Guide.md

# Pipe: number, then format, then export, without a temporary file
Get-Content Guide.md -Raw | marqora-cli number - | marqora-cli format - > Guide.out.md

# Exports, many files to one folder, a named theme, A4
marqora-cli export pdf --out-dir dist --theme sweetheart --paper A4 docs/*.md
marqora-cli export word --out-dir dist --theme "Vintage Press" README.md

# Which themes are there?
marqora-cli themes list

# Replace across files
marqora-cli replace --find "Marqora 1.0" --with "Marqora 1.1" --match-case --write docs/*.md

# Share a set as one Folio
marqora-cli folio -o Handbook.html docs/handbook/*.md

# Machine-readable results
marqora-cli export html --json --out-dir site *.md
```

### 2.3 Inputs

- **Files, globs, and `-`.** Windows shells do not expand globs, so the CLI does
  (`Microsoft.Extensions.FileSystemGlobbing`), and `**` works the same in PowerShell, cmd and
  bash. `-` reads markdown from stdin.
- **stdin has no folder.** Relative links and images resolve against `--base-dir`, defaulting
  to the current directory. `check` and the exports say which base they used.
- **Read-only files are refused for `--write`**, with an exit code and a message naming the
  file. That is the CLI's form of the app's read-only rule: the command is refused before any
  work starts rather than failing partway.

### 2.4 Outputs

- Text verbs write to stdout unless `--write` is given. With several input files and no
  `--write`, output is refused, because concatenated documents are never what was meant.
- Exports take `-o <file>` for one input or `--out-dir <folder>` for many. `export html -o -`
  writes the page to stdout; PDF and Word require a file.
- An existing output is **not** overwritten without `--overwrite`. *(Open question 7.)*
- Everything that leaves the app is light, as on screen: the CLI has no dark mode, and the
  export palette is always the theme's light palette (`CLAUDE.md`, *Screen and output*).

### 2.5 Dry run, check and write

| Flag | Writes | Prints | Exit when there is work to do |
| --- | :---: | --- | --- |
| *(none)* | no | the result (text verbs) | 0 |
| `--dry-run` | no | unified diff for text verbs; for exports and Folio, the planned outputs, the theme and paper in force, and what the export report would already know (missing images, unresolved links) | 0 |
| `--check` | no | the files that would change | **1** |
| `--write` | yes | a one-line summary per file | 0 |

`--dry-run` on `folio` is the preflight: it lists the documents, the pictures, and — if
`--fetch-web-images` was given — **the sites it would contact**. See section 6.

### 2.6 Exit codes and messages

Proposed. The table goes in the manual and does not change once released.

| Code | Meaning |
| ---: | --- |
| 0 | Success |
| 1 | Findings: `check` found problems, or `--check` found a file that would change |
| 2 | Usage: unknown verb, bad option, conflicting flags |
| 3 | Input: file missing, unreadable, not markdown, read-only with `--write` |
| 4 | Output: cannot write, or exists without `--overwrite` |
| 5 | Render: WebView2 runtime missing, render timed out, PDF engine failed |
| 130 | Canceled (Ctrl+C) |

With several files the run continues past a failure by default and exits with the most severe
code it met; `--fail-fast` stops at the first. Every error message names the file, says what
went wrong in a sentence, and where there is one, says what to do
(`Install the WebView2 runtime from …`, `Pass --overwrite to replace it`).

`--json` writes one object per input file to stdout — path, verb, outcome, outputs, findings
(with line and column), and the message — so a script can act on a partial failure without
parsing prose.

### 2.7 Color theme

The exports follow the theme the command line names, the way the app's exports follow the
screen's theme or the export theme in Preferences.

- **`--theme <id or name>`** is accepted by `export html`, `export pdf`, `export word` and
  `folio`. It matches a theme's id or its gallery name, ignoring case: `--theme vintage-press`,
  `--theme "Vintage Press"` and `--theme VINTAGE-PRESS` are the same theme. The text verbs do not
  take it, since they never produce colored output.
- **Which theme wins**, highest first:
  1. `--theme`;
  2. with `--settings`, the export theme that Preferences file names;
  3. with `--settings`, that file's screen theme;
  4. Default.

  That is the app's own rule (*exports use the screen's theme unless Preferences names
  another*), with `--theme` on top and Default standing in for the screen. A run without
  `--settings` is hermetic: Default on every machine.
- **An unknown name fails before any work starts**, with exit 2 and the valid names in the
  message, plus a suggestion when the typo is close:

  ```text
  marqora-cli: No color theme named 'Oceanic'. Did you mean 'ocean'?
  Themes: default, ember, forest, nordic, ocean, orchid, pastel, slate, sunset,
          sweetheart, vintage-press. Run 'marqora-cli themes list' for details.
  ```

  The app falls back to Default for a theme that is no longer there, which is right for a
  setting saved by an earlier version. A name typed on a command line is different: a quiet
  fallback would produce a wrongly colored PDF that nobody notices until it has been sent.
  The same applies to a theme named by a `--settings` file: it fails rather than falling back.
- **Always the light palette.** Output is light whatever the theme (`CLAUDE.md`, *Screen and
  output*). Word reads the same palette through `DocxColors`; HTML, PDF and Folio take it
  through the stylesheet's `--mq-theme-<slot>` variables; diagrams follow the theme's
  `diagrams` setting, and a document's own mermaid theme is still honored.
- **One theme per run.** Every file in the run, and every document in a Folio, wears the same
  theme, as in the app, where one theme is in force for everything. Nothing is written to
  record it: no sidecar, nothing in the document, nothing in the app's Preferences.
- **`themes list`** prints each theme's id, name and description in gallery order (Default
  first), and the same as an array with `--json`. It reads `ThemeCatalog`, the list the gallery
  shows, so it can never offer a theme the exports would refuse. Shell completion offers the
  ids after `--theme`.
- **`--dry-run`** names the theme it resolved and where it came from (`--theme`, the settings
  file, or Default), so a script can confirm the theme before a long export.

---

## 3. What exists today

### 3.1 Already headless

These are `net10.0` with no WinUI or WebView2 reference, and the CLI can call them as they are:

| Library | Gives the CLI |
| --- | --- |
| `Formatting` | `MarkdownFormatter`, `HeadingNumberDetector` / `HeadingNumberRewriter`, contents anchors |
| `Editing` | `ListRenumber` |
| `Domain` | `DiagramLayout` (the mermaid direction rewrite), `FormatOptions`, `PreferencesDocument` |
| `Finding` | `DocumentFinder`, `DocumentReplacer` |
| `Analysis` | `MarkdownAnalyzer`: link, image and style checks |
| `Rendering` | the Markdig pipeline, front matter, heading slugs, `WebAssetProvider` |
| `Docx` | the whole Word writer, including `PreviewHarvest` |
| `Folio` | planner, writer, links, unpacker, fetcher |
| `Themes`, `Abstractions`, `Services` | palettes, settings documents, paths, safe file writing |

### 3.2 Bound to the window

The export path assembles its pieces inside `PaulTechGuy.MQ.App`:

| App file | Lines | What ties it to the app |
| --- | ---: | --- |
| `HtmlExporter`, `HtmlPageLayout`, `FolioHtmlWriter`, `FolioShrinker`, `DiagramHtmlDocument`, `ColorThemePayloads` | ~630 | Nothing but location — they can move as they are |
| `RenderedHtmlPackager` | 552 | `Windows.UI` (a color type) |
| `PagedPrintHost`, `PagedJobs`, `WebViewPrinting` | ~1,160 | WebView2 Core and a parent window handle; no XAML |
| `WebViewPreviewHost` | 2,535 | XAML and WebView2: the live preview. Owns the bridge, including the detached render the Folio uses |
| `ExportReportService` | 84 | Opens `ExportReportWindow` |

`IPreviewHost` is the seam. It is the screen preview's whole interface, but an export needs only
four of its members: `RenderForExportAsync`, `RequestDiagramPngAsync`, `RequestMathPngAsync`
and `ExportPdfAsync`.

### 3.3 The precedent that makes this feasible

Two things already exist that the CLI needs:

1. **A detached render.** The Folio (`docs/Folio.md`, *Rendering documents that are not on
   screen*) asks the shell to render markdown into a container nobody sees, run mermaid, KaTeX
   and highlight.js over it, and send the HTML back. That is exactly a CLI export's render.
2. **A hidden WebView2.** `PagedPrintHost` lays out and prints PDFs in a controller that is never
   shown, in a browser environment of its own. It needs a parent window handle and nothing
   else from WinUI. A console app can supply that with a message-only window on an STA thread
   with its own message loop.

---

## 4. Proposed shape

### 4.1 Projects

```
src/
  PaulTechGuy.MQ.Exporting      net10.0           NEW  HTML page assembly, packaging, Folio HTML,
                                                       export orchestration and the export report
                                                       model; moved out of App
  PaulTechGuy.MQ.WebRender      net10.0-windows   NEW  the headless render host: hidden WebView2,
                                                       the shell bridge, PagedPrintHost/PagedJobs;
                                                       WebView2 Core only, no WinUI
  PaulTechGuy.MQ.Cli            net10.0-windows   NEW  marqora-cli.exe: verbs, globbing, output,
                                                       exit codes; a console-subsystem Exe
  PaulTechGuy.MQ.App            (existing)             keeps the live preview and the windows;
                                                       calls Exporting and WebRender
tests/
  PaulTechGuy.MQ.Exporting.Tests                 NEW
  PaulTechGuy.MQ.Cli.Tests                       NEW  parsing, exit codes, golden output
```

The dependency rule: `Cli` references the libraries and never `App`; `App` never references
`Cli`. Both front ends call the same `Exporting` code, so an export cannot differ between them.

### 4.2 The narrow rendering interface

Carve an `IDocumentRenderer` (name to be settled) out of `IPreviewHost`:

- render markdown, with a base folder and the render options, to finished HTML;
- a diagram as PNG or SVG, and a math run as PNG, from a rendered document;
- lay out and print a rendered document to PDF.

Two implementations: the live preview (the app, as now) and the headless host (the CLI). Both
speak the **same webshell bridge**, so the protocol code — request ids, the message shapes,
timeouts — should move into `WebRender` and be shared rather than written twice. The JavaScript
side already has the detached render, so it needs a *render-only* entry page at most, not a
second pipeline.

### 4.3 One consequence worth taking deliberately

Word export today harvests diagrams, math and code colors from the *live* preview
(`docs/WordExport.md`, §7), and degrades rather than failing when the preview is busy. The CLI
has no live preview, so it harvests from a detached render. Once that path exists, the app
could use it too: Word export would then never depend on what the preview last painted, and the
app and the CLI would give byte-identical `.docx` files. Recommended, but it is a behavior
change in the app and the plan should treat it as one.

### 4.4 Command-line framework

Two candidates, compared as of 2026-10-09:
[`System.CommandLine`](https://www.nuget.org/packages/System.CommandLine) and
[`CommandLineParser`](https://www.nuget.org/packages/CommandLineParser).

| | System.CommandLine | CommandLineParser |
| --- | --- | --- |
| Owner | Microsoft (.NET team); powers the `dotnet` CLI | Community |
| Latest release | 2.0.12, 2026-09-08; monthly servicing since 2.0 went stable. A 3.0 release candidate is out | 2.9.1, 2022-05-17. Nothing since; 262 open issues, 60 open PRs |
| License | MIT | MIT |
| Downloads | 106M | 265M (most of it from before 2022) |
| Targets | `netstandard2.0`, `net8.0` | `netstandard2.0`, .NET Framework 4.x |
| Style | Builder: `Command`, `Option<T>`, `Argument<T>`, an action per command | Attributes on an options class per verb: `[Verb]`, `[Option]`, `[Value]` |
| **Nested verbs** (`export pdf`, `themes list`) | Yes: commands contain commands | **No.** Verbs are one level deep, so these become `export-pdf` and `list-themes`, or `export --to pdf` |
| Help | Generated per command; a custom help action adds an *Examples* section | Generated; `[Usage]` attributes give examples built in |
| Validation | Per option and per command, with custom messages | Required options, sets and mutually exclusive groups by attribute |
| Completion | Completion sources per option (theme ids after `--theme`) and a `[suggest]` directive a PowerShell completer can call | None |
| Response files (`@args.rsp`) | Yes | No |
| Exit codes, Ctrl+C | An action returns the exit code; async actions get a `CancellationToken` that Ctrl+C cancels | `MapResult` returns an int; cancellation is ours to write |
| Trimming and AOT | Designed for both in 2.0 | Reflection-based; neither |

**Recommendation: System.CommandLine.** Two things decide it, and both are requirements from
section 1. It supports the verb shape this study proposes (`export pdf`, `themes list`), while
CommandLineParser would force the CLI's names to follow the library. And it is maintained:
CommandLineParser has had no release for over four years, which is a poor base for something
meant to be modern and long-lived. CommandLineParser's real advantages are its compact
attribute style and built-in `[Usage]` examples. An examples section in System.CommandLine's
help costs one shared help action, written once.

Pin `2.0.x` through Central Package Management and let the 3.0 release settle before
moving to it. Command definitions are a small, contained surface, so a later major upgrade
is cheap.

`Spectre.Console.Cli` was also considered: richer terminal output, but heavier dependencies and
its own conventions. Output formatting stays small and hand-written in every case, so `--json`
and `NO_COLOR` remain under our control.

Not AOT or trimmed: WebView2 interop and the DI setup do not trim cleanly, and the startup
saving is not worth it next to WebView2's own.

---

## 5. Packaging and install

- `marqora-cli.exe` is built Release and self-contained, like `Marqora.exe`, into the **same
  folder**. Both are built from one solution at one version, so they share the runtime files
  without conflict. The CLI does not need the Windows App SDK.
- `Install.ps1` gains an opt-in, per-user `PATH` entry (no administrator, in keeping with
  `docs/Installer.md`). `Uninstall.ps1` removes it.
- The CLI gets **its own WebView2 data folder** (beside the preview's and the print engine's),
  for the reason `PagedPrintHost` documents: a hung browser that holds a folder blocks every
  later user of it. Two CLI runs at once is open question 3.
- The WebView2 runtime is present on every supported Windows 11. When it is missing, the
  browser verbs exit 5 with a message and a link, and the text verbs still work.

---

## 6. Rules the CLI inherits

| Rule (`CLAUDE.md`) | What it means here |
| --- | --- |
| *Offline by default; nothing happens that you didn't decide* | The CLI contacts nothing on its own: no update check, no telemetry. `check` verifies **local** links and anchors only and reports web links as not checked. `folio` fetches web pictures only with `--fetch-web-images`, and `--dry-run` names the sites first. Add the CLI manual and `--help` text to `Test-NetworkClaim.ps1`'s curated surfaces. |
| *Screen and output* | Output is always the light palette; diagrams take the light drawing through `outputMarkup`/`outputSvgOf`, never the on-screen SVG. |
| *Color themes* | One theme per run, chosen by `--theme`, the settings file or Default (2.7). Nothing is written to record it, and nothing is stored per document. `themes list` reads `ThemeCatalog`, as the gallery does. |
| *Read-only documents* | `--write` refuses a read-only file before doing any work. |
| *American spelling*, licence headers | New code and docs go through `Set-AmericanSpelling.ps1` and `Add-FileHeaders.ps1` like everything else. |
| *Release notes* | A `##` section in `docs/releases/vNext.md`. |

The window rules (`PaletteWindow`, button tiers, nothing modal) do not apply: the CLI has no
windows. It never opens the export report window; the report becomes the run's findings on
stderr or in `--json`.

---

## 7. Documentation

| Where | What |
| --- | --- |
| `docs/CLI.md` **(new)** | The manual: install and PATH, a five-minute tour, every verb with its options and two or three examples, pipes and globs, dry-run/check/write, settings, the exit-code table, the `--json` shape, CI recipes (GitHub Actions, a pre-commit hook), troubleshooting. Readable in the app itself, so it should look good in Marqora's own preview. |
| `README.md` | A *Command line* feature section with three examples and a link to the manual; the new projects in *Project layout*. |
| `docs/index.html` | A feature card or section on the homepage. |
| `Welcome to Marqora.md` | A short paragraph: the CLI exists, where it is, one example. |
| `docs/Architecture.md` | A section on the headless renderer, the shared bridge, and why there is one export pipeline. |
| `docs/Installer.md` | The PATH option. |
| `docs/releases/vNext.md` | The release note. |
| `--help` | Generated per verb, with examples, consistent with the manual. |

---

## 8. Risks

| Risk | Weight | Mitigation |
| --- | --- | --- |
| WebView2 from a console process: message loop, STA, a parent window, clean shutdown when output is piped or Ctrl+C arrives | Medium | Week-one spike (section 9). `PagedPrintHost` already does most of it. |
| WebView2 on CI runners and in non-interactive sessions (services, scheduled tasks, SSH) | **High, unknown** | Spike on a GitHub Actions Windows runner and under SSH before committing to "CI-ready" in the docs. |
| Two implementations of the shell bridge drifting apart | Medium | Move the protocol into `WebRender` and share it; one test suite over both. |
| The refactor moving export code the app depends on | Medium | Move, don't rewrite; the app's exports must be byte-identical before and after, checked against the export-alignment baseline (`cheatsheet.md`). |
| Cold start: WebView2 adds 1–2 seconds before the first export | Low | One browser per run, reused across files; text verbs never start it. |
| Word harvest moving from the live preview to a detached render | Low–Medium | Decide explicitly (4.3); the content guards in `PreviewHarvest` already protect against mismatches. |
| Scope creep: every menu item wants a verb | Low | The verb table in 2.1 is the v1 scope; anything else goes to a backlog. |

---

## 9. Suggested phasing

1. **Spike (a few days).** A console exe opens a hidden WebView2, loads the webshell, renders
   one document with a diagram and an equation, writes a PDF, and exits cleanly. Measure cold
   start. Try it on a CI runner and over SSH. This answers the high risk before anything else
   is built.
2. **Skeleton and text verbs.** `PaulTechGuy.MQ.Cli`, `System.CommandLine`, globbing, stdin,
   `--write/--check/--dry-run`, exit codes, `--json`. Verbs: `format`, `number`, `unnumber`,
   `replace`, `renumber-lists`, `diagram-layout`, `check`, `toc`, `outline`, `unpack`,
   `themes list`, `version`. No refactor needed for this phase, and it is useful on its own.
3. **Refactor.** Create `Exporting` and `WebRender`; move the export assembly and the paged
   engine out of `App`; carve the rendering interface; the app works exactly as before.
4. **Exports.** `export html`, `export pdf`, `export word`, then `folio`.
5. **Docs and packaging.** The manual, README, homepage, Welcome, installer PATH, completion,
   release note.

As rough whole-effort sizes: the spike is small; phase 2 is medium; phase 3 is the largest and
riskiest; phase 4 is medium once 3 is done; phase 5 is medium, because the manual is meant to
be excellent rather than adequate. Phase 2 can ship ahead of the rest if the spike goes badly.

---

## 10. Considered and deferred

- **`print`** to a printer: possible through the paged engine, but printing from a script is rare
  and printer selection needs a design of its own.
- **Spell check**: `WindowsSpellingEngine` lives in the app and uses the Windows API; feasible
  later as a `check --spelling` option.
- **`watch`** (re-export on save): user-started, so it does not break the network or "does
  nothing on its own" rules, but it is a long-running mode with its own design.
- **A recipe file** (`run steps.json`): pipes and globs cover v1; a recipe is the natural
  answer if one browser start across many exports turns out to matter.
- **Multi-verb chains in one invocation** (`format number export ...`): rejected as
  non-standard and hard to give per-verb options.
- **Delegating to the running app**: rejected; a CLI that launches the desktop app is not a CLI.
- **Web link checking**: banned by the network rule, not deferred.

---

## 11. Open questions for the detailed plan

1. **Verb names.** `number`/`unnumber`, or `number-headings`/`remove-heading-numbers` to
   match the menus exactly, with the short forms as aliases?
2. **Export options surface.** Which of the export dialogs' options become flags (paper,
   margins, title page, contents, furniture, notes as endnotes), and which only come from
   `--settings`?
3. **Concurrency.** Two `marqora-cli` runs at once: a WebView2 data folder per process (slower,
   isolated) or a shared one with a lock (faster, serialized)?
4. **The Word harvest** (4.3): move the app to the detached render as well, or keep it on the
   live preview?
5. **Folio forms.** The app offers three forms; does `folio` take all three (`--form`), and
   how does a large set fall back to `.zip`?
6. **`check` scope.** Which style diagnostics are on by default, and can `--fix` hand them to
   the formatter?
7. **Overwriting outputs.** Refuse without `--overwrite` (conservative), or overwrite exports
   by default, since an export never touches the source?
8. **`replace`** across files: plain and regex only, or whole-word and case options matching
   the Find bar exactly? Does it skip fenced code, as the app does?
9. **Where the CLI's settings file lives**, if it ever has one of its own, or is `--settings`
   enough?
