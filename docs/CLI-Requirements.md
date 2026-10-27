# Marqora CLI: feature requirements

The requirements for `marqora-cli.exe`, the command-line companion that runs Marqora's document
commands over markdown files without opening a window.

This document turns `docs/CLI-Feasibility.md` (written against `dev` at `eab7281`, 2026-10-09)
into a list of trackable items. The detailed implementation plan is written from it: every code
change in the plan names the requirement IDs it serves, and every requirement here is met by at
least one change in the plan. The feasibility study stays the place for the reasoning; this
document says only what has to be true, and how to tell.

---

## 1. How to read this document

### 1.1 Requirement IDs

Every item has an ID of the form `CLI-<AREA>-<NN>`, or `CLI-VB-<VERB>-<NN>` for a verb.

| Area | Covers |
| --- | --- |
| `SP` | The spike: the go/no-go for the browser verbs |
| `CMD` | The command line itself: verbs, aliases, help, parsing |
| `IN` | Inputs: files, globs, stdin, encoding, read-only |
| `OUT` | Outputs and write modes: stdout, `--write`, `--check`, `--dry-run`, output files |
| `RPT` | Reporting: messages, exit codes, `--json`, verbosity, color |
| `SET` | Settings: hermetic defaults, `--settings` |
| `TH` | Color themes: `--theme`, `themes list` |
| `VB-<VERB>` | One verb's behavior: `FMT`, `NUM`, `UNN`, `RPL`, `REN`, `DGL`, `CHK`, `TOC`, `OUT`, `UNP`, `VER` |
| `EX` | Exports common to HTML, PDF and Word; `EX-HTML`, `EX-PDF`, `EX-WORD` for each |
| `FO` | Folio |
| `ARC` | Architecture and the refactor of the app's export code |
| `NF` | Non-functional: speed, offline, runtime, platform |
| `PKG` | Packaging and install |
| `RULE` | The repository's standing rules, applied to the CLI |
| `DOC` | Documentation |
| `TST` | Tests |

IDs are never reused or renumbered. A requirement that is dropped stays in place, marked
**Withdrawn** with the reason, so a plan or commit that names it still finds it. A new
requirement takes the next free number in its area, wherever it is written.

### 1.2 Fields

Each requirement has:

- **Requirement**: what must be true, as one or two sentences.
- **Why**: where the reason is not obvious, with a pointer into the feasibility study (§) or
  `CLAUDE.md`.
- **Acceptance**: a checklist. A requirement is done when every box can be ticked by a test or
  by a stated manual check.
- **Milestone** and **priority**, on one line.

### 1.3 Milestones

| Milestone | Contents | Gate |
| --- | --- | --- |
| **M0** | The spike (§9 phase 1 of the study) | Decides whether M2 goes ahead as designed |
| **M1** | The CLI skeleton, every text verb, `themes list`, `unpack`, and the folder and zip forms of `folio`. No browser, and no refactor of the app beyond moving one class | Shippable on its own |
| **M2** | The refactor (`Exporting`, `WebRender`), `export html/pdf/word`, the single-file Folio, and the app's switch to the detached Word harvest | M0 passed |

M1 can ship in a release before M2 is finished. The documentation requirements are tagged with
the milestone whose features they describe.

### 1.4 Priority

**Must**: the milestone does not ship without it. **Should**: expected, but the milestone can ship
without it if the plan says why. **Could**: done if cheap; otherwise moves to the backlog.

---

## 2. Decisions

The study's §1 decisions stand. The open questions of its §11 were settled on 2026-10-09:

| Study § | Question | Decision |
| --- | --- | --- |
| 11.1 | Verb names | **Short names are canonical** (`number`, `unnumber`, `renumber-lists`); the menu names (`number-headings`, `remove-heading-numbers`) are accepted as aliases |
| 11.2 | Export options as flags | **Page setup and theme only**: `--paper`, `--orientation`, `--margin`, `--[no-]backgrounds`, `--theme`. Everything else (title page, contents, furniture, notes, Word layout, formatter options) comes only from `--settings` |
| 11.3 | Two runs at once | **A WebView2 data folder per process**, created for the run and deleted on exit |
| 11.4 | Word harvest | **The app moves to the detached render as well**, tracked as its own requirement (`CLI-ARC-07`) and announced as a behavior change |
| 11.5 | Folio forms | **Inferred from `-o`**, with `--form` to override. A single file that would be too large fails with a suggestion; it never switches form on its own |
| 11.6 | `check` scope | **Links and images by default**; style diagnostics with `--style`, as hints that fail the run only with `--strict`. **No `--fix`** in v1 |
| 11.7 | Existing outputs | **Refused without `--overwrite`** (exit 4) |
| 11.8 | `replace` options | **Exactly the Find bar**: `--match-case`, `--whole-word`, `--regex`, through `FindQuery` and `DocumentReplacer`, treating code the way the app's Replace All does |
| 11.9 | A CLI settings file of its own | **None in v1.** `--settings app` and `--settings <file>` are enough |

Added beyond the study, on the same date: encoding and line endings are preserved by `--write`;
`--quiet`, `--verbose` and `--color`; a versioned `--json` schema.

Two changes to the study's verb table follow from the code:

- **`toc` prints; it does not insert.** The app has no command that writes a contents list into
  markdown. Contents exist only inside exports (`ContentsListing`, `PaperContents`). Inserting
  one would invent a format and an update-in-place rule with no app counterpart, so it is an
  open item (§5), not a v1 requirement.
- **The folder and zip Folios need no browser.** `FolioWriter.WriteFolderAsync` and
  `WriteZipAsync` are in the headless `Folio` library. Only the single-file form renders. The
  two source forms are therefore M1.

### 2.1 Amendments of 2026-10-16

Made while writing `docs/CLI-Implementation-Plan.md`, which checked each requirement against the
code. The plan's §3 has the finding behind each one. No ID was added, removed or renumbered.

| Requirement | Change |
| --- | --- |
| `CLI-SP-01`, `CLI-ARC-01` | The parent window is the spike's to choose; WebView2's WinRT projection is switched on and must activate without the Windows App SDK; target `net10.0-windows10.0.19041.0` |
| `CLI-ARC-01`, `CLI-ARC-02` | A fourth project, `PaulTechGuy.MQ.Imaging`, behind `IImageResizer`; `Exporting` exists from M1 to receive `FolioShrinker` |
| `CLI-SP-03`, `CLI-DOC-02` | No GitHub Actions is used: the spike's Actions run is recorded as not run, by decision, and the manual has a pre-commit hook and no Actions recipe. Amended again on 2026-10-26; the version of 2026-10-16 had the runs made by Paul in a scratch repository |
| `CLI-VB-UNP-01` | Single-file Folios only. Amended again on 2026-10-27 (A5c, which reverses A5b): `--overwrite` replaces what is in a non-empty target, except for the folders that are never emptied; without it the target is refused |
| `CLI-IN-01` | Amended on 2026-10-27 (A15): `folio` keeps the order its documents were named in, because that order is the Folio's reading order |
| `CLI-OUT-08` | Amended on 2026-10-27 (A5c): for `folio`'s folder form, `--overwrite` replaces what is in the folder, as for `unpack` |
| `CLI-VB-REN-01` | `--style sequential\|ones\|zeros`; the app's per-list suggestion by default |
| `CLI-OUT-06` | `format` decides line endings and the final newline through its own options |
| `CLI-SET-03` | A raw `settings.json` is accepted too |
| `CLI-EX-05` | Only the CLI fails a timed-out file; the app's degrade is unchanged |
| `CLI-ARC-07` | The app's Word export uses the same headless host the CLI uses |
| `CLI-VB-VER-01` | Compared with the shared `ProductVersion`, which replaces `AppVersion` |
| `CLI-ARC-05` | One suite runs over the shared bridge and transport |
| `CLI-VB-CHK-01` | Web images and pictures outside the folder stay errors; `--ignore-external-images` turns them into information (the proposal to demote them by default was rejected) |

---

## 3. Scope

### 3.1 In v1

Verbs: `format`, `number`, `unnumber`, `replace`, `renumber-lists`, `diagram-layout`, `check`,
`toc`, `outline`, `unpack`, `themes list`, `version` (M1); `folio` in the folder and zip forms
(M1); `export html`, `export pdf`, `export word`, `folio` in the single-file form (M2).

### 3.2 Out of v1

| Item | Status |
| --- | --- |
| `completion` (shell completion scripts) and `@file` response files | Deferred: not chosen for v1. The study listed `completion`; it moves to the backlog. Response files are switched off so they are not an undocumented feature |
| `toc --write` (inserting a contents list) | Open item (§5) |
| `check --fix` | Deferred (11.6); `format --write` is the fixer |
| `replace --skip-code` | Deferred (11.8); the app has no such option |
| A CLI settings file of its own | Deferred (11.9) |
| `print`, `check --spelling`, `watch`, a recipe file | Deferred (study §10) |
| Several verbs in one invocation; delegating to the running app | Rejected (study §10) |
| Checking web links | Banned by the network rule, not deferred |

---

## 4. Requirements

### 4.1 Spike (M0)

#### CLI-SP-01: A console process renders and prints headlessly

**Requirement.** A console executable opens a hidden WebView2 on an STA thread with a parent
window the spike chooses, loads the webshell, renders a document containing a mermaid diagram,
a KaTeX equation and a highlighted code block, writes a PDF, and exits cleanly. The plan records
which parent window kind laid out: a message-only window, or an off-screen one if Chromium will
not lay out against a message-only window (`PagedPrintHost` found that a hidden controller never
does).
**Why.** The highest risk in the study (§8); `PagedPrintHost` suggests it works but has not
proved it outside a window. It is also written against WebView2's WinRT projection, which a
project gets only when it asks for it, and the CLI must not need the Windows App SDK
(`CLI-NF-05`).
**Acceptance.**
- [ ] WebView2's WinRT projection activates in the console process with no Windows App SDK
  installed
- [ ] The PDF shows the diagram (light drawing), the equation and colored code
- [ ] The process exits with code 0 and leaves no `msedgewebview2.exe` behind
- [ ] Ctrl+C during the render exits 130 and leaves no browser process behind
- [ ] Output piped to another process (`| Out-Null`) does not hang the exit

**M0 · Must**

#### CLI-SP-02: Cold start is measured

**Requirement.** The spike records the time from process start to the first rendered document,
and to the second, on a developer machine.
**Acceptance.**
- [ ] Both timings recorded in the plan; the second shows the browser is reused

**M0 · Must**

#### CLI-SP-03: Non-interactive sessions are tried

**Requirement.** The spike runs over SSH on the developer machine and records whether it works.
A GitHub Actions runner is not tried: no GitHub Actions is used for this work, in this
repository or any other, so exports on CI stay untested and the manual says so.
**Why.** "CI-ready" is not claimed anywhere until it has been shown (`CLI-DOC-02`).
**Acceptance.**
- [ ] Result for the Actions runner recorded: **not run, by decision** (no GitHub Actions is used
  for this work), and exports on CI documented in the manual as untested
- [ ] Result over SSH recorded
- [ ] If either fails, the plan records whether M2 goes ahead with that environment documented
  as unsupported

**M0 · Must**

### 4.2 The command line

#### CLI-CMD-01: Verb shape

**Requirement.** Invocation is `marqora-cli <verb> [options] <inputs...>`, with nested verbs
`export html`, `export pdf`, `export word` and `themes list`. Built on `System.CommandLine`,
pinned to `2.0.x` through Central Package Management.
**Acceptance.**
- [ ] Every verb in §3.1 is reachable by its canonical name
- [ ] The package version is set in `Directory.Packages.props`, not in the project

**M1 · Must**

#### CLI-CMD-02: Aliases for the menu names

**Requirement.** `number-headings` is an alias of `number`, and `remove-heading-numbers` of
`unnumber`. Help lists the canonical name and shows the alias.
**Acceptance.**
- [ ] Each alias gives the same output as its canonical verb on the same input
- [ ] `--help` for each verb names its alias

**M1 · Must**

#### CLI-CMD-03: Help with examples

**Requirement.** `marqora-cli --help` lists the verbs; `<verb> --help` lists the verb's options
and two or three examples, the same examples the manual gives.
**Acceptance.**
- [ ] Every verb's help has an *Examples* section
- [ ] A test fails if a verb has no examples

**M1 · Must**

#### CLI-CMD-04: Usage errors

**Requirement.** An unknown verb, an unknown option, a missing required option, or conflicting
flags (`--write` with `--check`, `--write` with `--dry-run`, `-o` with `--out-dir`) exit 2 with a
message naming the problem and pointing at `--help`.
**Acceptance.**
- [ ] Each case above exits 2 with a one-sentence message on stderr
- [ ] A close misspelling of a verb suggests the right one

**M1 · Must**

#### CLI-CMD-05: Response files off

**Requirement.** `@file` arguments are not expanded in v1.
**Why.** Not chosen for v1 (§3.2); an undocumented feature becomes a compatibility promise.
**Acceptance.**
- [ ] `marqora-cli format @args.rsp` treats `@args.rsp` as a file name

**M1 · Should**

### 4.3 Inputs

#### CLI-IN-01: Files and globs

**Requirement.** Inputs are paths or glob patterns, expanded by the CLI with
`Microsoft.Extensions.FileSystemGlobbing`, so `*` and `**` behave the same in PowerShell, cmd and
bash. Matches are processed in a stable, ordinal-sorted order, each file once. `folio` is the
exception: its documents keep the order they were named in, each pattern's matches sorted where
the pattern stands, because that order is the Folio's reading order, as the app keeps the order
documents were ticked in.
**Acceptance.**
- [ ] `"docs/**/*.md"` matches the same files from all three shells
- [ ] A file named twice, or matched by two patterns, is processed once
- [ ] `folio b.md a.md` makes a Folio whose documents are in the order `b.md`, `a.md`
- [ ] A pattern matching nothing is an input error (exit 3) naming the pattern

**M1 · Must**

#### CLI-IN-02: Standard input

**Requirement.** `-` reads markdown from stdin. It may be the only input; it cannot be combined
with files.
**Acceptance.**
- [ ] `Get-Content x.md -Raw | marqora-cli format -` writes the formatted text to stdout
- [ ] `-` with another input exits 2

**M1 · Must**

#### CLI-IN-03: Base folder

**Requirement.** Relative links and images resolve against the input file's folder, or for stdin
against `--base-dir`, defaulting to the current folder. `check`, the exports and `--verbose` name
the base they used for stdin.
**Acceptance.**
- [ ] `check -` with `--base-dir docs` reports the same findings as `check docs/x.md`
- [ ] The base folder appears in `--json` output for stdin

**M1 · Must**

#### CLI-IN-04: Encoding

**Requirement.** Input is read as UTF-8, with or without a byte order mark. A file that is not
valid UTF-8 is refused with exit 3, not rewritten.
**Acceptance.**
- [ ] A UTF-8 file with BOM and one without both format correctly
- [ ] A Windows-1252 file with non-ASCII bytes exits 3 and is unchanged

**M1 · Must**

#### CLI-IN-05: Read-only files refused for writing

**Requirement.** With `--write`, every input is checked before any work starts. If any is
read-only, the run exits 3 naming each read-only file, and nothing is written.
**Why.** The CLI's form of *Read-only documents* in `CLAUDE.md`: refused up front, not partway.
**Acceptance.**
- [ ] Two writable files and one read-only file: exit 3, none of the three changed
- [ ] Without `--write`, a read-only file is read normally

**M1 · Must**

#### CLI-IN-06: Missing and unreadable files

**Requirement.** A missing or unreadable input is an input error (exit 3) naming the file; the
run continues with the other inputs unless `--fail-fast` is given (`CLI-RPT-03`).
**Acceptance.**
- [ ] One missing file among three: the other two are processed, exit 3

**M1 · Must**

### 4.4 Outputs and write modes

#### CLI-OUT-01: Default leaves the file alone

**Requirement.** Without a mode flag, a text verb writes its result to stdout and changes no file.
**Acceptance.**
- [ ] File timestamps and contents are unchanged after a run without `--write`

**M1 · Must**

#### CLI-OUT-02: Several inputs need a mode

**Requirement.** With more than one input and no `--write`, `--check` or `--dry-run`, a text verb
that would print documents exits 2 rather than concatenating them.
**Acceptance.**
- [ ] `format a.md b.md` exits 2 with a message suggesting `--write` or `--check`
- [ ] `check`, `outline` and `toc` are not affected; they print per-file results

**M1 · Must**

#### CLI-OUT-03: `--write`

**Requirement.** `--write` rewrites each input in place through the safe file writing in
`Services` (written to a temporary file and replaced), and prints one summary line per file to
stderr. A file whose result is identical to its input is not rewritten.
**Acceptance.**
- [ ] An unchanged file keeps its modification time
- [ ] Killing the process mid-write never leaves a truncated file

**M1 · Must**

#### CLI-OUT-04: `--check`

**Requirement.** `--check` writes nothing, lists the files that would change, and exits 1 if
there is at least one.
**Acceptance.**
- [ ] All files already in shape: exit 0, no output
- [ ] One file would change: exit 1, its path on stderr

**M1 · Must**

#### CLI-OUT-05: `--dry-run` for text verbs

**Requirement.** `--dry-run` writes nothing and prints a unified diff per file that would change.
**Acceptance.**
- [ ] The diff applies cleanly with `git apply` to the original file
- [ ] An unchanged file prints nothing

**M1 · Must**

#### CLI-OUT-06: Encoding and line endings preserved

**Requirement.** `--write` keeps each file's byte order mark (present or absent), its line endings
(CRLF or LF; a mixed file keeps the majority ending and says so with `--verbose`), and whether it
ends with a newline. Only changes the verb makes reach the file. stdout is UTF-8 without a BOM,
with the input's line endings. A verb whose own options rewrite line endings or the final newline
(`format`, through `LineEndings` and `EofNewline`) decides them itself; the preservation applies
to everything else.
**Why.** A CI gate on a repository with mixed endings must not report every file as changed.
**Acceptance.**
- [ ] A CRLF file with BOM, formatted with `--write`, is still CRLF with BOM
- [ ] An LF file without a final newline keeps it that way under `number --write`
- [ ] `format --check` on a formatted CRLF file exits 0

**M1 · Must**

#### CLI-OUT-07: Output files for exports and Folio

**Requirement.** Exports and `folio` take `-o <path>` for one input (for `folio`, the whole set)
or `--out-dir <folder>` for many, where each output is named after its input with the format's
extension. `--out-dir` is created if missing. Two inputs that would give the same output name
are an input error before any work starts.
**Acceptance.**
- [ ] `export pdf --out-dir dist a.md b.md` writes `dist/a.pdf` and `dist/b.pdf`
- [ ] `-o` with two inputs exits 2
- [ ] `docs/a.md` and `notes/a.md` into one `--out-dir` exits 3 before anything is written

**M1 (folio) / M2 (exports) · Must**

#### CLI-OUT-08: No overwrite without `--overwrite`

**Requirement.** An output file that already exists is not replaced unless `--overwrite` is
given; without it the run exits 4 with "Pass --overwrite to replace it". All outputs are checked
before any work starts. A folder output (`folio`'s folder form) with anything in it is treated the
same way: refused without `--overwrite`, its contents replaced with it, under the rules
`CLI-VB-UNP-01` gives, and never when it holds an input document or a picture the Folio collects.
**Why.** The conservative default: the default leaves the user's files alone (11.7).
**Acceptance.**
- [ ] Existing output, no flag: exit 4, file unchanged, no render started
- [ ] With `--overwrite`: replaced
- [ ] An output path that is an input file is refused even with `--overwrite`
- [ ] A folder output holding an input or a collected picture is refused even with `--overwrite`

**M1 (folio) / M2 (exports) · Must**

#### CLI-OUT-09: HTML to stdout

**Requirement.** `export html -o -` writes the page to stdout. PDF, Word and Folio require a
file, and `-o -` for them exits 2.
**Acceptance.**
- [ ] `export html -o - x.md > x.html` gives the same bytes as `export html -o x.html x.md`

**M2 · Should**

### 4.5 Reporting

#### CLI-RPT-01: Exit codes

**Requirement.** The exit codes are fixed by this table and documented in the manual. Once
released they do not change meaning.

| Code | Meaning |
| ---: | --- |
| 0 | Success |
| 1 | Findings: `check` found problems, or `--check` found a file that would change |
| 2 | Usage: unknown verb, bad option, conflicting flags, unknown theme |
| 3 | Input: missing, unreadable, not UTF-8, read-only with `--write`, colliding outputs |
| 4 | Output: cannot write, or exists without `--overwrite` |
| 5 | Render: WebView2 runtime missing, render timed out, PDF engine failed |
| 130 | Canceled (Ctrl+C) |

**Acceptance.**
- [ ] A test per code produces it from a real invocation
- [ ] The table in `docs/CLI.md` matches the `ExitCode` type, checked by a test

**M1 · Must**

#### CLI-RPT-02: Messages

**Requirement.** Messages go to stderr, never stdout. Each error names the file, says what went
wrong in one sentence and, where there is one, what to do next.
**Acceptance.**
- [ ] stdout carries only the verb's result (or `--json`), so a pipe never receives a message
- [ ] Every message in the code goes through one reporter type, so a test can list them

**M1 · Must**

#### CLI-RPT-03: Several files, one exit code

**Requirement.** A run continues past a failed file and exits with the most severe code it met
(ranked 130 > 5 > 4 > 3 > 2 > 1 > 0). `--fail-fast` stops at the first failure.
**Acceptance.**
- [ ] One input error and one finding: exit 3, both reported
- [ ] With `--fail-fast`, files after the first failure are not processed

**M1 · Must**

#### CLI-RPT-04: `--json`

**Requirement.** `--json` writes to stdout one JSON object per input, one per line, with
`schemaVersion`, `path`, `verb`, `outcome`, `outputs`, `findings` (each with `line`, `column`,
`code`, `severity` and `message`) and `message`. Human messages still go to stderr. The schema is
written in the manual.
**Acceptance.**
- [ ] Every line parses as JSON and carries `schemaVersion: 1`
- [ ] Golden tests cover each verb's JSON shape
- [ ] A partial failure is visible per file without reading stderr

**M1 · Must**

#### CLI-RPT-05: Schema versioning

**Requirement.** Adding a field keeps `schemaVersion`; removing or renaming one, or changing its
meaning, raises it. The rule is stated in the manual.
**Acceptance.**
- [ ] The golden tests fail on a removed or renamed field

**M1 · Must**

#### CLI-RPT-06: `--quiet` and `--verbose`

**Requirement.** `--quiet` suppresses the per-file summary lines and keeps errors. `--verbose`
adds timings, the resolved theme and its source, the settings source, the base folder, and the
line ending chosen for a mixed file. They cannot be combined.
**Acceptance.**
- [ ] `--quiet --write` on a clean run prints nothing
- [ ] `--verbose` on an export names the theme and where it came from

**M1 · Should**

#### CLI-RPT-07: Color

**Requirement.** `--color auto|always|never`, default `auto`: color only when stderr is a
terminal and `NO_COLOR` is not set. `--color always` overrides `NO_COLOR`. `--json` is never
colored.
**Acceptance.**
- [ ] Redirected stderr carries no escape sequences under `auto`
- [ ] `NO_COLOR=1` gives none under `auto`

**M1 · Should**

#### CLI-RPT-08: Cancellation

**Requirement.** Ctrl+C stops the run, leaves no partial output file and no browser process, and
exits 130.
**Acceptance.**
- [ ] Ctrl+C during `--write` leaves every file either original or fully written
- [ ] Ctrl+C during an export leaves no output file and no `msedgewebview2.exe`

**M1 (text) / M2 (exports) · Must**

### 4.6 Settings

#### CLI-SET-01: Hermetic by default

**Requirement.** Without `--settings`, every verb uses built-in defaults and the flags given. The
CLI reads nothing from the user's profile, the app's Preferences, or the environment (other than
`NO_COLOR`).
**Acceptance.**
- [ ] The same command on two machines with different Preferences gives identical bytes

**M1 · Must**

#### CLI-SET-02: `--settings app`

**Requirement.** `--settings app` adopts the desktop app's saved Preferences: formatter options,
heading numbering, export layout and themes. The CLI never writes to them.
**Acceptance.**
- [ ] A formatter option changed in Preferences changes `format --settings app` output
- [ ] The Preferences file is byte-identical after a run

**M1 · Must**

#### CLI-SET-03: `--settings <file>`

**Requirement.** `--settings <file.json>` adopts an exported Preferences file, read through
`PreferencesDocument`. A raw `settings.json` is accepted too, as the app's Import accepts one. A
missing or malformed file, or one naming a theme that does not exist, is a usage error (exit 2)
before any work starts.
**Acceptance.**
- [ ] A file exported from Preferences gives the same output as `--settings app` on that machine
- [ ] A malformed file exits 2 naming the file and the problem

**M1 · Must**

#### CLI-SET-04: Flags win over settings

**Requirement.** A flag given on the command line overrides the same setting from `--settings`.
**Acceptance.**
- [ ] `--paper A4` with a settings file naming Letter gives A4

**M1 · Must**

### 4.7 Color themes

#### CLI-TH-01: `--theme`

**Requirement.** `export html`, `export pdf`, `export word` and `folio` accept `--theme <id or
name>`, matching a theme's id or gallery name, ignoring case. Text verbs do not accept it.
**Acceptance.**
- [ ] `vintage-press`, `"Vintage Press"` and `VINTAGE-PRESS` resolve to the same theme
- [ ] `format --theme ocean` exits 2

**M1 (folio) / M2 (exports) · Must**

#### CLI-TH-02: Which theme wins

**Requirement.** `--theme`, then the export theme of the `--settings` source, then its screen
theme, then Default.
**Acceptance.**
- [ ] A test per rung of the ladder

**M1 · Must**

#### CLI-TH-03: An unknown theme fails

**Requirement.** An unknown theme, from the command line or from a settings source, exits 2
before any work starts, lists the valid ids, and suggests the closest one.
**Why.** A quiet fallback gives a wrongly colored file nobody notices until it is sent (study §2.7).
**Acceptance.**
- [ ] `--theme Oceanic` suggests `ocean` and lists every id
- [ ] No output file exists after the failure

**M1 · Must**

#### CLI-TH-04: Always the light palette, one theme per run

**Requirement.** Every output wears the resolved theme's light palette. Every file and every Folio
document in the run wears the same theme. Nothing is written to record it.
**Acceptance.**
- [ ] No dark palette color appears in an HTML export's stylesheet
- [ ] No file other than the requested outputs is created

**M2 · Must**

#### CLI-TH-05: `themes list`

**Requirement.** `themes list` prints each theme's id, name and description in gallery order,
Default first, reading `ThemeCatalog`. With `--json`, the same as an array.
**Acceptance.**
- [ ] The list matches `ThemeCatalog` exactly, checked by a test
- [ ] Adding a theme file adds a row with no CLI change

**M1 · Must**

#### CLI-TH-06: The resolved theme is shown

**Requirement.** `--dry-run` and `--verbose` on an export or `folio` name the resolved theme and
its source (`--theme`, the settings, or Default).
**Acceptance.**
- [ ] Each of the three sources is named correctly

**M1 · Must**

### 4.8 Text verbs

#### CLI-VB-FMT-01: `format`

**Requirement.** `format` applies `MarkdownFormatter` with the options from the settings source
(`FormatOptions` defaults when hermetic). It takes the modes of §4.4.
**Acceptance.**
- [ ] Output equals the app's Format Document for the same text and options
- [ ] `format` on already-formatted text is a no-op (idempotent)

**M1 · Must**

#### CLI-VB-NUM-01: `number`

**Requirement.** `number` applies `HeadingNumberRewriter.Apply` with `--from h1|h2|h3` (the
`HeadingNumbering` start) and `--style two-spaces|one-space|dot-space|paren-space`
(`HeadingNumberStyle`). Defaults come from the settings source. The result reports links moved
and uncertain headings.
**Acceptance.**
- [ ] Output equals the app's Number Headings for the same options
- [ ] Uncertain headings are reported as findings with line numbers

**M1 · Must**

#### CLI-VB-UNN-01: `unnumber`

**Requirement.** `unnumber` applies `HeadingNumberRewriter.Remove`.
**Acceptance.**
- [ ] `number` then `unnumber` returns the original text for a document without numbers

**M1 · Must**

#### CLI-VB-RPL-01: `replace`

**Requirement.** `replace --find <text> --with <text>` with `--match-case`, `--whole-word` and
`--regex`, built as a `FindQuery` and applied through `DocumentReplacer`, so code is treated
exactly as the app's Replace All treats it. The summary gives the count per file.
**Acceptance.**
- [ ] For each combination of the three options, output equals the app's Replace All
- [ ] An invalid regular expression exits 2 with the parser's message
- [ ] `--with` may be empty, and that deletes the matches

**M1 · Must**

#### CLI-VB-REN-01: `renumber-lists`

**Requirement.** `renumber-lists` applies `ListRenumber` to every ordered list in the document.
`--style sequential|ones|zeros` picks the numbering; without it each list takes the numbering
the app would suggest for that list. A list that cannot take `zeros` (the app only allows it for
a list that follows a break) is left alone and reported.
**Acceptance.**
- [ ] Output equals the app's Renumber List applied to each list in turn
- [ ] `--style` overrides the suggestion, and a list that cannot take `zeros` is reported

**M1 · Must**

#### CLI-VB-DGL-01: `diagram-layout`

**Requirement.** `diagram-layout --direction top-to-bottom|left-to-right` rewrites the direction
of every mermaid diagram that has one, through `DiagramLayout.EditFor`. `--diagram <n>` limits it
to the n-th diagram.
**Acceptance.**
- [ ] Output equals the app's Diagram Layout for each diagram
- [ ] A diagram type without a direction is left alone and reported

**M1 · Must**

#### CLI-VB-CHK-01: `check`, links and images

**Requirement.** `check` runs `MarkdownAnalyzer`'s link and image checks: dead local links and
anchors, missing images. Each finding is an error with file, line and column, and any error exits
1. Web links are listed as *not checked* under `--verbose`, never contacted. A picture loaded from
a web address, and a picture outside the document's folder, are errors like the rest, as the
source pane reports them. `--ignore-external-images` turns both off: they no longer fail the run
and are shown only under `--verbose`, as information.
**Why.** The app blocks remote pictures, so the squiggle is right; but a README with badge images
would otherwise fail every run with no way to accept it.
**Acceptance.**
- [ ] A dead anchor, a missing image and a dead relative link are each found
- [ ] A document with a web image exits 1, and exits 0 with `--ignore-external-images`, which
  lists it as information under `--verbose`
- [ ] Findings match the source pane's squiggles for the same document
- [ ] No socket is opened during a run (`CLI-NF-03`)

**M1 · Must**

#### CLI-VB-CHK-02: `check --style`

**Requirement.** `--style` adds the style diagnostics as hints. Hints do not change the exit code
unless `--strict` is given, when any hint exits 1.
**Acceptance.**
- [ ] A style-only problem exits 0 with `--style` and 1 with `--style --strict`
- [ ] `--strict` without `--style` exits 2

**M1 · Must**

#### CLI-VB-TOC-01: `toc`

**Requirement.** `toc` prints the document's contents as a nested markdown list of links, over the
heading levels `ContentsListing.Levels` gives for the numbering in force, with the anchors the
renderer produces. It never writes to the input.
**Acceptance.**
- [ ] Every link resolves to a heading in the preview
- [ ] `toc --write` exits 2 (see §5)

**M1 · Should**

#### CLI-VB-OUT-01: `outline`

**Requirement.** `outline` prints the heading tree, indented, with line numbers; `--json` gives
level, text, anchor and line per heading.
**Acceptance.**
- [ ] Matches the outline pane for the same document

**M1 · Should**

#### CLI-VB-UNP-01: `unpack`

**Requirement.** `unpack <folio> --out-dir <folder>` takes a single-file Folio apart into its
markdown and images through the `Folio` unpacker, as File > Open Folio does. The target folder
must be missing or empty unless `--overwrite` is given: without it a non-empty one is refused with
exit 4. With it, what is in the folder is replaced: the Folio is unpacked beside it first, and the
old contents are deleted only once the new ones are in place. Some folders are never emptied,
even with `--overwrite`: a drive root, the profile folder, the current folder or one that holds
it, and a folder holding the Folio being unpacked.
**Why.** A Folio arrives from someone else, and unpacking a newer copy of it over the last one is
the common case. The folders that are never emptied are each a slip of the keyboard away from one
nobody meant to lose. The folder and zip forms are already plain files, and the app's Open Folio
reads only the single-file form.
**Acceptance.**
- [ ] A single-file Folio unpacks; a folder or a `.zip` given to `unpack` exits 3 with a message
  saying it is already files, or to unzip it
- [ ] Without `--overwrite`, a non-empty target exits 4 and nothing in it is touched
- [ ] With `--overwrite`, a non-empty target holds exactly the Folio's files afterward, and
  nothing is left beside it
- [ ] With `--overwrite`, each folder that is never emptied exits 4 and is left as it was
- [ ] Unpacking a Folio made by the app gives the files the app would open

**M1 · Must**

#### CLI-VB-VER-01: `version`

**Requirement.** `version` and `--version` print the version, which is the app's version from the
same build.
**Acceptance.**
- [ ] Equals the product version `Marqora.exe` reports from the same build (the shared
  `ProductVersion`, which replaces the app-private `AppVersion`)

**M1 · Must**

### 4.9 Exports

#### CLI-EX-01: One browser per run

**Requirement.** The first export in a run starts the headless renderer; later files reuse it. A
run of text verbs never starts it.
**Acceptance.**
- [ ] Ten exports start one browser process tree
- [ ] `format` starts none (checked in a test by the renderer factory never being called)

**M2 · Must**

#### CLI-EX-02: A data folder per process

**Requirement.** The renderer's WebView2 data folder is created per run, apart from the preview's
and the print engine's, and deleted on exit, including after Ctrl+C. A stale folder from a
crashed run is cleaned up on a later run.
**Acceptance.**
- [ ] Two exports run at the same time both succeed
- [ ] No data folder remains after a normal exit or after Ctrl+C

**M2 · Must**

#### CLI-EX-03: The export report becomes findings

**Requirement.** What `ExportReportWindow` would show (missing images, unresolved links, content
that degraded) is reported as findings per file, on stderr or in `--json`. The window never opens.
Findings do not change the exit code; the export succeeded.
**Acceptance.**
- [ ] A document with a missing image exports, exits 0, and reports the image with its line

**M2 · Must**

#### CLI-EX-04: `--dry-run` for exports

**Requirement.** `--dry-run` on an export renders nothing and prints the planned outputs, the
resolved theme and its source, the page setup in force, and what the report would already know
from the markdown (missing images, unresolved local links).
**Acceptance.**
- [ ] No browser starts and no file is written

**M2 · Should**

#### CLI-EX-05: Render timeout

**Requirement.** A render that does not finish within a timeout (default 60 seconds per document,
`--timeout <seconds>` to change it) fails that file with exit 5 and moves on. The app's own render
timeout and its degrade to unfinished markup are unchanged; only the CLI fails the file.
**Acceptance.**
- [ ] A document that never finishes rendering (test hook) fails with exit 5 and a message

**M2 · Must**

#### CLI-EX-06: Diagrams leave as their light drawing

**Requirement.** Every export takes diagrams through `outputMarkup` / `outputSvgOf`, never the
on-screen SVG. A document's own mermaid theme is honored.
**Why.** `CLAUDE.md`, *Screen and output*.
**Acceptance.**
- [ ] The exported diagram SVG equals the light drawing for the same document in the app

**M2 · Must**

#### CLI-EX-HTML-01: `export html`

**Requirement.** `export html` writes the same page as File > Export > HTML, through the shared
`Exporting` code.
**Acceptance.**
- [ ] For `cheatsheet.md` and the alignment fixtures, CLI and app give identical bytes for the
  same theme and settings

**M2 · Must**

#### CLI-EX-PDF-01: `export pdf`

**Requirement.** `export pdf` writes the same PDF as File > Export > PDF through the paged engine,
with `--paper letter|a4|...` (every `PaperSize`), `--orientation portrait|landscape`,
`--margin <PageMargin>` and `--[no-]backgrounds`. Layout options beyond these come from
`--settings`.
**Acceptance.**
- [ ] The page size and margins of the output match each flag value
- [ ] The visible content matches the app's PDF of the same document, compared as images page by
  page

**M2 · Must**

#### CLI-EX-WORD-01: `export word`

**Requirement.** `export word` writes the same `.docx` as File > Export > Word, harvesting
diagrams, math and code colors from a detached render. Word layout comes from `--settings`.
**Acceptance.**
- [ ] CLI and app give identical `word/document.xml` and media parts for the same document,
  theme and settings, once `CLI-ARC-07` is done
- [ ] Diagrams and math appear as images, code in its colors

**M2 · Must**

### 4.10 Folio

#### CLI-FO-01: Form from the output path

**Requirement.** `folio -o <path>` chooses the form from the path: `.html` gives a single file,
`.zip` a zip, a path ending in a separator or naming an existing folder gives a folder.
`--form single|folder|zip` overrides; an extension that disagrees with `--form` exits 2.
**Acceptance.**
- [ ] Each of the three paths gives its form
- [ ] `-o x.html --form zip` exits 2

**M1 (folder, zip) / M2 (single) · Must**

#### CLI-FO-02: A single file that is too large

**Requirement.** When the single-file form's estimated size passes the limit the app warns at, the
run fails before writing, with exit 4 and a message suggesting `--form zip` or
`--shrink-images`. It never switches form on its own.
**Acceptance.**
- [ ] An over-limit set exits 4 with the suggestion, and no file is written

**M2 · Must**

#### CLI-FO-03: Shrinking images

**Requirement.** `--shrink-images <px>` downsizes images wider than the given width before any
form is written, as the preflight's checkbox does. `FolioShrinker` moves out of the app for it
(`CLI-ARC-02`).
**Acceptance.**
- [ ] The same set and width give the same image bytes as the app

**M1 · Should**

#### CLI-FO-04: Web pictures only when asked

**Requirement.** Pictures named by web address are collected only with `--fetch-web-images`. The
`HttpClient` is created only on that path. `--dry-run` lists the sites that would be contacted.
**Why.** `CLAUDE.md`, *What Marqora claims about the network*.
**Acceptance.**
- [ ] Without the flag, no socket is opened and web pictures stay as links, reported
- [ ] `--dry-run --fetch-web-images` lists each host once and contacts none

**M1 · Must**

#### CLI-FO-05: The preflight as `--dry-run`

**Requirement.** `folio --dry-run` prints the documents, the picture count, missing pictures,
outside links, the estimated size, the form and the theme, as the app's preflight shows them.
**Acceptance.**
- [ ] Each line of the app's preflight has its counterpart in the output and the JSON

**M1 · Must**

#### CLI-FO-06: The Folio report

**Requirement.** What the app's Folio report would list is reported as findings per document, as
`CLI-EX-03` does for exports.
**Acceptance.**
- [ ] Every finding names the document it belongs to

**M1 · Must**

### 4.11 Architecture and refactor

#### CLI-ARC-01: Projects and the dependency rule

**Requirement.** New projects `PaulTechGuy.MQ.Cli` (console exe) and `PaulTechGuy.MQ.WebRender`
(WebView2 Core only), both `net10.0-windows10.0.19041.0` with WebView2's WinRT projection
switched on; `PaulTechGuy.MQ.Imaging` (the same target, no WinUI), which holds the Windows
Imaging resizer behind an `IImageResizer` interface in `Abstractions`; and
`PaulTechGuy.MQ.Exporting` (`net10.0`). `Cli` never references `App`; `App` never references
`Cli`; none of `Exporting`, `WebRender` and `Imaging` references WinUI.
**Why.** The paged engine is written against the WinRT projection, which the package enables
only on request. `FolioShrinker` resizes through Windows Imaging, so a `net10.0` project cannot
hold it without that seam.
**Acceptance.**
- [ ] An architecture test fails on a forbidden reference

**M0 (WebRender seed) / M1 (Cli, Exporting, Imaging) / M2 (WebRender completed) · Must**

#### CLI-ARC-02: Move the export assembly out of the app

**Requirement.** `HtmlExporter`, `HtmlPageLayout`, `FolioHtmlWriter`, `FolioShrinker`,
`DiagramHtmlDocument`, `ColorThemePayloads`, `RenderedHtmlPackager` and the export report model
move to `Exporting`. Moved, not rewritten; `RenderedHtmlPackager` loses its `Windows.UI` color.
`FolioShrinker` moves in M1 for `CLI-FO-03`, resizing through `IImageResizer`
(`CLI-ARC-01`), so `Exporting` exists from M1.
**Acceptance.**
- [ ] The app's exports are unchanged (`CLI-ARC-06`)
- [ ] `Exporting` builds with no Windows reference

**M2 (M1 for `FolioShrinker`) · Must**

#### CLI-ARC-03: Move the paged engine

**Requirement.** `PagedPrintHost`, `PagedJobs` and `WebViewPrinting` move to `WebRender`, taking
a parent window handle from their host rather than from the app's window.
**Acceptance.**
- [ ] The app's PDF export and print are unchanged

**M2 · Must**

#### CLI-ARC-04: The rendering interface

**Requirement.** An `IDocumentRenderer` (name settled in the plan) carved out of `IPreviewHost`:
render markdown with a base folder and options to finished HTML; a diagram as PNG or SVG and a
math run as PNG from a rendered document; print a rendered document to PDF. The live preview and
the headless host both implement it.
**Acceptance.**
- [ ] Every export path in the app and the CLI calls the interface, not `IPreviewHost`

**M2 · Must**

#### CLI-ARC-05: One shell bridge

**Requirement.** The bridge protocol (request ids, message shapes, timeouts) lives once in
`WebRender` and is used by both implementations. The webshell gains at most a render-only entry
page, not a second pipeline.
**Acceptance.**
- [ ] One test suite runs over the shared bridge and transport that both implementations use
- [ ] No message shape is defined twice

**M2 · Must**

#### CLI-ARC-06: The app's exports are unchanged

**Requirement.** After the refactor, the app's HTML, PDF, Word and Folio outputs equal their
outputs before it, checked against the export-alignment baseline (`cheatsheet.md`), with
timestamps and generated ids masked where a format embeds them.
**Acceptance.**
- [ ] A before/after comparison over the baseline set is recorded in the plan, with no
  unexplained difference

**M2 · Must**

#### CLI-ARC-07: The app's Word export uses the detached render

**Requirement.** The app's Word export harvests from a detached render, as the CLI does, instead
of the live preview: through the same headless host the CLI uses, in a data folder of the app's
own. It no longer depends on what the preview last painted.
**Why.** Decision 11.4; study §4.3. This is a behavior change in the app.
**Acceptance.**
- [ ] App and CLI give identical Word content for the same input (`CLI-EX-WORD-01`)
- [ ] Exporting while the preview is busy or on another tab gives the same file as when it is idle
- [ ] Announced in `docs/releases/vNext.md` as a change, and `docs/WordExport.md` §7 updated

**M2 · Must**

### 4.12 Non-functional

#### CLI-NF-01: Text verbs are fast

**Requirement.** A text verb on one ordinary document (under 100 KB) finishes in under one second
from process start on a developer machine, warm disk.
**Acceptance.**
- [ ] Measured and recorded for `format` and `check`

**M1 · Should**

#### CLI-NF-02: Cold start is bounded

**Requirement.** The first export in a run adds no more than the cold start measured in M0 plus a
margin the plan sets.
**Acceptance.**
- [ ] Measured and recorded against the M0 figure

**M2 · Should**

#### CLI-NF-03: Offline by default

**Requirement.** The CLI does nothing on its own: no update check, no telemetry, no link check.
The only path that opens a connection is `--fetch-web-images`.
**Acceptance.**
- [ ] A test run of every verb under a network-denied process opens no socket, apart from
  `folio --fetch-web-images`
- [ ] No `HttpClient` is constructed outside the fetcher's method

**M1 · Must**

#### CLI-NF-04: Missing WebView2 runtime

**Requirement.** When the WebView2 runtime is missing, browser verbs exit 5 with a message saying
how to install it; text verbs work.
**Acceptance.**
- [ ] Simulated missing runtime: `export pdf` exits 5, `format` succeeds

**M2 · Must**

#### CLI-NF-05: Platform

**Requirement.** The CLI runs wherever `Marqora.exe` runs, from the same build, and does not need
the Windows App SDK.
**Acceptance.**
- [ ] The CLI starts on a machine with no Windows App SDK runtime installed

**M1 · Must**

### 4.13 Packaging and install

#### CLI-PKG-01: Beside the app

**Requirement.** `marqora-cli.exe` is built Release, self-contained, at the app's version, into
the app's install folder, sharing the runtime, `webshell/` and the themes.
**Acceptance.**
- [ ] The release build output contains both executables and one copy of the shared files
- [ ] The CLI finds `webshell/` and the themes relative to its own location

**M1 · Must**

#### CLI-PKG-02: PATH, opt-in

**Requirement.** `Install.ps1` offers to add the install folder to the user's `PATH`, per user,
without administrator rights, off unless chosen. `Uninstall.ps1` removes it if it was added.
**Acceptance.**
- [ ] Install with the option: `marqora-cli version` works in a new shell
- [ ] Uninstall removes the entry and leaves the rest of `PATH` as it was

**M1 · Must**

### 4.14 Repository rules

#### CLI-RULE-01: License headers

**Requirement.** Every new `.cs` file carries the header; `Add-FileHeaders.ps1 -Check` passes.
**Acceptance.**
- [ ] `pwsh ./build/Add-FileHeaders.ps1 -Check` exits 0

**M1, M2 · Must**

#### CLI-RULE-02: American spelling

**Requirement.** New code, help text and documents pass `Set-AmericanSpelling.ps1 -Check`.
**Acceptance.**
- [ ] The check exits 0

**M1, M2 · Must**

#### CLI-RULE-03: The network claim

**Requirement.** `docs/CLI.md` and the CLI's help text are added to `Test-NetworkClaim.ps1`'s
curated surfaces, and pass.
**Acceptance.**
- [ ] The script lists the new surfaces, and `-Check` exits 0

**M1 · Must**

#### CLI-RULE-04: Release notes

**Requirement.** A `##` section in `docs/releases/vNext.md` for the CLI, with M2's additions and
the Word export change (`CLI-ARC-07`) added when they land.
**Acceptance.**
- [ ] The section exists and is written for someone deciding whether to download

**M1, M2 · Must**

### 4.15 Documentation

#### CLI-DOC-01: The manual

**Requirement.** `docs/CLI.md`: install and PATH, a five-minute tour, every verb with its options
and examples, pipes and globs, dry-run/check/write, settings, themes, the exit-code table, the
`--json` schema and its versioning rule, troubleshooting. It reads well in Marqora's own preview.
**Acceptance.**
- [ ] Every verb and option in the code appears in the manual (checked against the parser by a
  test)
- [ ] Every example in it runs and gives the output shown

**M1 (text verbs), M2 (exports) · Must**

#### CLI-DOC-02: A pre-commit hook recipe

**Requirement.** The manual has a pre-commit hook for `format --check` and `check`. It has no
GitHub Actions recipe, or any other CI service's: none is used for this work, so none is
published. Exports in CI are documented only as far as `CLI-SP-03` showed them to work.
**Acceptance.**
- [ ] The hook, installed in this repository, passes on a clean tree and blocks a commit with an
  unformatted file or a dead link
- [ ] The manual contains no GitHub Actions workflow

**M1 · Should**

#### CLI-DOC-03: The rest of the docs

**Requirement.** `README.md` (a *Command line* section with three examples, and the new projects
in *Project layout*), `docs/index.html` (a feature card), `Welcome to Marqora.md` (a short
paragraph), `docs/Architecture.md` (the headless renderer, the shared bridge, the one export
pipeline), `docs/Installer.md` (the PATH option), `docs/Folio.md` (the CLI's forms).
**Acceptance.**
- [ ] Each file updated; the Architecture section reviewed with the M2 code

**M1, M2 · Must**

### 4.16 Tests

#### CLI-TST-01: CLI tests

**Requirement.** `tests/PaulTechGuy.MQ.Cli.Tests`: parsing, every exit code, every verb's text
and JSON output against golden files, and that each text verb gives the app's library result.
**Acceptance.**
- [ ] The project runs in the normal test run

**M1 · Must**

#### CLI-TST-02: Export tests

**Requirement.** `tests/PaulTechGuy.MQ.Exporting.Tests` for the moved code, and the bridge suite
of `CLI-ARC-05`.
**Acceptance.**
- [ ] The project runs in the normal test run

**M2 · Must**

#### CLI-TST-03: Fixtures stay untouched

**Requirement.** Tests that use `UltimateMarkdownContent.md` or other markdown fixtures read them
and never format them in place.
**Acceptance.**
- [ ] The fixtures are unchanged after a test run (`git status` clean)

**M1 · Must**

---

## 5. Open items

These do not block M1. Each is settled before the milestone it affects.

| # | Item | Suggestion | Affects |
| ---: | --- | --- | --- |
| 1 | `toc --write`: inserting a contents list into the markdown. The app has no such command, so a format and an update rule would be new | Leave it out of v1. If wanted, design it in the app first (marker comments around the list, replaced in place), and the CLI follows | Backlog |
| 2 | Exports on CI and over SSH | Decided by `CLI-SP-03` | M2 |
| 3 | The size limit for a single-file Folio (`CLI-FO-02`) | Use the figure the app's preflight warns at, so the CLI and the app agree | M2 |
| 4 | Whether `--strict` should also make export findings fail the run | Not in v1; add it if a CI user asks | Backlog |
| 5 | The render timeout default (`CLI-EX-05`) | 60 seconds, revisited with the M0 timings | M2 |

---

## 6. Index

The implementation plan copies this table and adds a *Code changes* column.

| ID | Title | M | Priority |
| --- | --- | :---: | :---: |
| CLI-SP-01 | A console process renders and prints headlessly | M0 | Must |
| CLI-SP-02 | Cold start is measured | M0 | Must |
| CLI-SP-03 | Non-interactive sessions are tried | M0 | Must |
| CLI-CMD-01 | Verb shape | M1 | Must |
| CLI-CMD-02 | Aliases for the menu names | M1 | Must |
| CLI-CMD-03 | Help with examples | M1 | Must |
| CLI-CMD-04 | Usage errors | M1 | Must |
| CLI-CMD-05 | Response files off | M1 | Should |
| CLI-IN-01 | Files and globs | M1 | Must |
| CLI-IN-02 | Standard input | M1 | Must |
| CLI-IN-03 | Base folder | M1 | Must |
| CLI-IN-04 | Encoding | M1 | Must |
| CLI-IN-05 | Read-only files refused for writing | M1 | Must |
| CLI-IN-06 | Missing and unreadable files | M1 | Must |
| CLI-OUT-01 | Default leaves the file alone | M1 | Must |
| CLI-OUT-02 | Several inputs need a mode | M1 | Must |
| CLI-OUT-03 | `--write` | M1 | Must |
| CLI-OUT-04 | `--check` | M1 | Must |
| CLI-OUT-05 | `--dry-run` for text verbs | M1 | Must |
| CLI-OUT-06 | Encoding and line endings preserved | M1 | Must |
| CLI-OUT-07 | Output files for exports and Folio | M1/M2 | Must |
| CLI-OUT-08 | No overwrite without `--overwrite` | M1/M2 | Must |
| CLI-OUT-09 | HTML to stdout | M2 | Should |
| CLI-RPT-01 | Exit codes | M1 | Must |
| CLI-RPT-02 | Messages | M1 | Must |
| CLI-RPT-03 | Several files, one exit code | M1 | Must |
| CLI-RPT-04 | `--json` | M1 | Must |
| CLI-RPT-05 | Schema versioning | M1 | Must |
| CLI-RPT-06 | `--quiet` and `--verbose` | M1 | Should |
| CLI-RPT-07 | Color | M1 | Should |
| CLI-RPT-08 | Cancellation | M1/M2 | Must |
| CLI-SET-01 | Hermetic by default | M1 | Must |
| CLI-SET-02 | `--settings app` | M1 | Must |
| CLI-SET-03 | `--settings <file>` | M1 | Must |
| CLI-SET-04 | Flags win over settings | M1 | Must |
| CLI-TH-01 | `--theme` | M1/M2 | Must |
| CLI-TH-02 | Which theme wins | M1 | Must |
| CLI-TH-03 | An unknown theme fails | M1 | Must |
| CLI-TH-04 | Always the light palette, one theme per run | M2 | Must |
| CLI-TH-05 | `themes list` | M1 | Must |
| CLI-TH-06 | The resolved theme is shown | M1 | Must |
| CLI-VB-FMT-01 | `format` | M1 | Must |
| CLI-VB-NUM-01 | `number` | M1 | Must |
| CLI-VB-UNN-01 | `unnumber` | M1 | Must |
| CLI-VB-RPL-01 | `replace` | M1 | Must |
| CLI-VB-REN-01 | `renumber-lists` | M1 | Must |
| CLI-VB-DGL-01 | `diagram-layout` | M1 | Must |
| CLI-VB-CHK-01 | `check`, links and images | M1 | Must |
| CLI-VB-CHK-02 | `check --style` | M1 | Must |
| CLI-VB-TOC-01 | `toc` | M1 | Should |
| CLI-VB-OUT-01 | `outline` | M1 | Should |
| CLI-VB-UNP-01 | `unpack` | M1 | Must |
| CLI-VB-VER-01 | `version` | M1 | Must |
| CLI-EX-01 | One browser per run | M2 | Must |
| CLI-EX-02 | A data folder per process | M2 | Must |
| CLI-EX-03 | The export report becomes findings | M2 | Must |
| CLI-EX-04 | `--dry-run` for exports | M2 | Should |
| CLI-EX-05 | Render timeout | M2 | Must |
| CLI-EX-06 | Diagrams leave as their light drawing | M2 | Must |
| CLI-EX-HTML-01 | `export html` | M2 | Must |
| CLI-EX-PDF-01 | `export pdf` | M2 | Must |
| CLI-EX-WORD-01 | `export word` | M2 | Must |
| CLI-FO-01 | Form from the output path | M1/M2 | Must |
| CLI-FO-02 | A single file that is too large | M2 | Must |
| CLI-FO-03 | Shrinking images | M1 | Should |
| CLI-FO-04 | Web pictures only when asked | M1 | Must |
| CLI-FO-05 | The preflight as `--dry-run` | M1 | Must |
| CLI-FO-06 | The Folio report | M1 | Must |
| CLI-ARC-01 | Projects and the dependency rule | M1/M2 | Must |
| CLI-ARC-02 | Move the export assembly out of the app | M2 | Must |
| CLI-ARC-03 | Move the paged engine | M2 | Must |
| CLI-ARC-04 | The rendering interface | M2 | Must |
| CLI-ARC-05 | One shell bridge | M2 | Must |
| CLI-ARC-06 | The app's exports are unchanged | M2 | Must |
| CLI-ARC-07 | The app's Word export uses the detached render | M2 | Must |
| CLI-NF-01 | Text verbs are fast | M1 | Should |
| CLI-NF-02 | Cold start is bounded | M2 | Should |
| CLI-NF-03 | Offline by default | M1 | Must |
| CLI-NF-04 | Missing WebView2 runtime | M2 | Must |
| CLI-NF-05 | Platform | M1 | Must |
| CLI-PKG-01 | Beside the app | M1 | Must |
| CLI-PKG-02 | PATH, opt-in | M1 | Must |
| CLI-RULE-01 | License headers | M1/M2 | Must |
| CLI-RULE-02 | American spelling | M1/M2 | Must |
| CLI-RULE-03 | The network claim | M1 | Must |
| CLI-RULE-04 | Release notes | M1/M2 | Must |
| CLI-DOC-01 | The manual | M1/M2 | Must |
| CLI-DOC-02 | A pre-commit hook recipe | M1 | Should |
| CLI-DOC-03 | The rest of the docs | M1/M2 | Must |
| CLI-TST-01 | CLI tests | M1 | Must |
| CLI-TST-02 | Export tests | M2 | Must |
| CLI-TST-03 | Fixtures stay untouched | M1 | Must |
