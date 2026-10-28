# Marqora CLI: implementation plan

The plan for building `marqora-cli.exe` from `docs/CLI-Requirements.md`, and the place where
progress against those requirements is tracked and verified.

Written against `feature/CLI` at `6c2801d` (2026-10-09), and revised the same day after a
two-reviewer adversarial pass whose findings were each checked against the source before being
taken. On approval this file is saved as `docs/CLI-Implementation-Plan.md`, and from then on
that file is the one that gets updated.

---

## 1. Context

`docs/CLI-Feasibility.md` showed the CLI can be built. `docs/CLI-Requirements.md` turned it into
92 trackable requirements across three milestones: M0 spike, M1 text verbs, M2 exports. This plan:

1. Orders the work into **work packages** (`WP-M1-07`). Each names the requirements it serves,
   what it depends on, the files it touches, the existing code it reuses, and how it is verified.
2. Records what the code says that the requirements did not know (§3). Where the two disagree,
   it lists **amendments** (§7). Paul accepted all but two on 2026-10-16, and those were applied to
   the requirements document, with two replacements (A5b, A13b) for the ones he rejected.
3. Carries the **traceability matrix** (§8): every requirement ID, its work packages, the test or
   manual record that verifies it, and its status. Status is ticked there and nowhere else.

---

## 2. How to use this plan

### 2.1 Work packages

Each WP has the same fields:

- **Serves**: requirement IDs. A WP that covers a requirement only in part says which part.
- **Depends on**: WPs that must be done first.
- **Changes**: the files created, moved or edited. Moves are marked *move*. A move is not a
  rewrite: the diff should be namespaces, visibility and the one named seam.
- **Reuses**: existing code to call rather than write again.
- **Tests**: what the WP adds to the test suite. Test class names are the ones the matrix cites.
- **Done when**: the check that closes the WP.

### 2.2 Status values (matrix only)

| Status      | Meaning                                                                                                      |
| ----------- | ------------------------------------------------------------------------------------------------------------ |
| `todo`      | Not started                                                                                                  |
| `wip`       | A WP serving it is in progress                                                                               |
| `done`      | Every WP serving it is merged to `feature/CLI`                                                               |
| `verified`  | Every acceptance box in the requirements has a passing test or a completed §9 record, named in *Verified by* |
| `blocked`   | Waiting on a decision or a manual check; *Verified by* says on what                                          |
| `withdrawn` | Withdrawn by an accepted amendment (§7)                                                                      |

A requirement is `verified` only when every acceptance box is covered. A test that is skipped
does not count, so anything that cannot run everywhere (bash, Sandbox, SSH) is a §9 record, not a
skippable test.

### 2.3 Standing rules for every WP

These come from `CLAUDE.md` and the session memory. They apply to every package and are not
repeated in each one.

- **Before starting a WP**, fetch and fast-forward `dev` into view (autostash). Other agents
  merge mid-session. Work stays on `feature/CLI`: no branches are created, switched or merged.
- **No commits unless Paul asks.** Each WP is left as a reviewable diff. Commits carry Paul's
  name and no Claude trailer.
- **After any `.cs` edit**, run `pwsh ./build/Add-FileHeaders.ps1 -Check` (`CLI-RULE-01`). New
  files start with the two-line SPDX header.
- **After any text edit**, run `pwsh ./build/Set-AmericanSpelling.ps1 -Check` (`CLI-RULE-02`).
- **Never format a markdown fixture.** `UltimateMarkdownContent.md` and `cheatsheet.md` are only
  ever read by tests (`CLI-TST-03`). Golden inputs are copied to a temp folder before any
  `--write` test.
- **No desktop input automation.** Ctrl+C tests send a console control event to a child process.
  They never send keys.
- **On a planned list, do the named items only.** Anything new goes to §11 for Paul to pick from.
- **Release notes** go in `docs/releases/vNext.md` only (`CLI-RULE-04`).

---

## 3. What the code says

Facts that shape the work, each checked in the source. Each one is acted on in the WP named.

|   # | Finding                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   | Consequence                                                                                                                                                                                                                                                                                                                                                                 | WP           |
| --: | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------ |
|  F1 | A controller with `IsVisible = false` never lays out. Chromium stops animation frames for a hidden page, so only `PagedHostMode.OffScreen` works (`PagedPrintHost.cs:27-44`). A message-only parent may fail the same way.                                                                                                                                                                                                                                                                                | The spike tries three parent kinds in order and records which one lays out.                                                                                                                                                                                                                                                                                                 | M0-01        |
|  F2 | The paged engine is written against WebView2's **WinRT projection** (`IAsyncOperation`, `TypedEventHandler`, `CoreWebView2ControllerWindowReference`, `Windows.Foundation.Rect`). The package picks that projection by `WebView2EnableCsWinRTProjection` (not `WebView2UseWinRT`, which is for native and WinMD consumers), which it sets only for UAP or Windows App SDK projects (`Common.targets:88-107`). The target framework does not decide it. Without them a project gets the Win32 .NET API, which has different types under the same names. | `WebRender` and `Cli` set `WebView2EnableCsWinRTProjection=true` explicitly. The spike's first question is whether the native WinRT component activates in a console process with no Windows App SDK, either manifest-free or through an `activatableClass` manifest. If neither works, falling back to the Win32 API means rewriting the paged engine, and that is Paul's decision (§10). | M0-01        |
|  F3 | `PagedPrintHost` keeps a **static environment of its own** in `<WEBVIEW2_USER_DATA_FOLDER>-Print`, read from the variable `App/Program.cs:49` sets.                                                                                                                                                                                                                                                                                                                                                       | When it moves, the environment (or folder) is injected. The CLI passes `HeadlessRenderHost`'s environment, so a run has one browser tree and one data folder. The app keeps `-Print`.                                                                                                                                                                                       | M2-03        |
|  F4 | HTML and PDF export use the **live** preview: `GetRenderedHtmlAsync` and the private `RequestPrintMarkupAsync` (`WebViewPreviewHost.cs:1389`). Word takes the live markup if there is any, and asks for diagrams by hash and math by line. Folio uses `RenderForExportAsync` / `requestExportHtml`, which needs no tab. Word with no markup still exports, without pictures.                                                                                                                              | The headless host loads `shell.html` and opens each document as its one tab, then uses the same messages as the preview. That makes HTML, PDF and Word equal by construction rather than by keeping two renders in step. Folio's single form uses `requestExportHtml` as the app does.                                                                                      | M0-01, M2-05 |
|  F5 | The export orchestration lives in `MainViewModel` (`WriteHtmlExportAsync` :7566, `WriteFolioCoreAsync` :7690, `WriteWordExportAsync` :8348, `WritePdfExportAsync` :8580; `PdfPictureIssuesAsync`). PDF falls back to a **classic engine** (`ExportClassicPdfAsync`) when the paged one cannot finish.                                                                                                                                                                                                     | It is lifted into `Exporting` as jobs over an `IDocumentRenderer`. `MainViewModel` keeps the dialogs and the report window. The CLI keeps the classic fallback too, reported as an advisory finding, so CLI and app make the same file.                                                                                                                                     | M2-06        |
|  F6 | `RenderForExportAsync` (`WebViewPreviewHost.cs:915`) returns the unfinished markup after 30 s.                                                                                                                                                                                                                                                                                                                                                                                                            | The shared bridge returns a typed outcome. The app keeps degrading; the CLI exits 5 (decision).                                                                                                                                                                                                                                                                             | M2-04        |
|  F7 | `FolioShrinker` resizes through `ClipboardImage.ResizeForFileAsync` (WIC). No test covers `FolioShrinker` today.                                                                                                                                                                                                                                                                                                                                                                                          | An `IImageResizer` seam, a new `Imaging` project, and `FolioShrinker` moved into `Exporting`, which is created in M1. A new shrink test is written.                                                                                                                                                                                                                         | M1-15        |
|  F8 | `ListRenumber` is `internal` and works on one list in an `EditContext`. `Describe` only ever suggests `Sequential` or `Ones` (`ListRenumber.cs:42`). `Apply` with `Zeros` returns `EditResult.None` unless the list follows a break (:58).                                                                                                                                                                                                                                                                | A public whole-document entry point in `Editing`. `--style zeros` on a list that cannot take it leaves that list alone and reports it.                                                                                                                                                                                                                                      | M1-09        |
|  F9 | `FolioUnpacker.Unpack` reads only the single-file form and refuses a non-empty target.                                                                                                                                                                                                                                                                                                                                                                                                                    | `unpack` is single-file only; a non-empty target exits 4 unless `--overwrite` replaces its contents (A4, A5c).                                                                                                                                                                                                                                                                                            | M1-14        |
| F10 | The Folio preflight's figures are private to `FolioWindow`: `Weight(plan)` (:836), `HeavyPageBytes` 50 MB, `MailLimitBytes` 25 MB, `ReducibleBytes` 5 MB, and the advice lines (:850-905).                                                                                                                                                                                                                                                                                                                | A `FolioPreflight` model holding the estimate, the three limits and the advice moves into `Folio`. The window and the CLI both use it.                                                                                                                                                                                                                                      | M1-16        |
| F11 | `FormatOptions.Default` has `EofNewline = true` and `LineEndings = true` (Detect).                                                                                                                                                                                                                                                                                                                                                                                                                        | With default options, `format` adds a final newline on purpose. Amendment A7.                                                                                                                                                                                                                                                                                               | M1-03        |
| F12 | `AppVersion` is `internal` to App and reads its own assembly.                                                                                                                                                                                                                                                                                                                                                                                                                                             | It moves to `Services` as `ProductVersion`. Amendment A11 renames the requirement's reference.                                                                                                                                                                                                                                                                              | M1-13        |
| F13 | There is no GitHub Actions workflow, and none is added (decision).                                                                                                                                                                                                                                                                                                                                                                                                                                        | No GitHub Actions is used for this work, here or elsewhere: the spike's Actions run is not made, and the manual has a pre-commit hook and no Actions recipe (A14, replacing A3).                                                                                                                                                                                                                                                                                                     | M0-04, M1-19 |
| F14 | `PreferencesTransferService.ImportAsync(path, current)` is already pure: it merges a file over `current`, returns the result, writes nothing, and already accepts a raw `settings.json`. `JsonSettingsRepository` loading is **not** read-only: its `JsonFileStore` moves a damaged file to `settings.json.corrupt` (`TryQuarantine`) and falls back to defaults. Corrected in M1-05; the plan first said loading never writes.                                                                                                                                                                                                                                                                   | `--settings <file>` is `ImportAsync(path, new AppSettings())`. `--settings app` is `ImportAsync` too, on the app's own `settings.json` from `AppPaths`, never the repository, so a damaged file is reported and left where it is. No refactor is needed.                                                                                                                                                                                                              | M1-05        |
| F15 | The Folio steps run **Plan → Fetch → Plan again with the fetched files → Shrink** (`MainViewModel.cs:7999`, pinned by `FolioRemoteImageTests.cs:277`).                                                                                                                                                                                                                                                                                                                                                    | The CLI follows that order exactly.                                                                                                                                                                                                                                                                                                                                         | M1-16        |
| F16 | `MarkdownAnalyzer` takes `AnalysisRequest { Text, DocumentPath, Links, Outline, Anchors, … }`, which a render produces. `CheckBlockedImages` defaults to true, so every web image (`RemoteMedia`) and every picture outside the folder (`OutsideFolder`) becomes a finding. Missing images come from `LinkChecks`; `ImageChecks` only checks alt text.                                                                                                                                                    | `check` renders first (`MarkdigMarkdownRenderer`) and maps finding kinds to severity. Dead local links, dead anchors and missing local images are errors. `RemoteMedia` and `OutsideFolder` are errors too, as the squiggles report them. `--ignore-external-images` turns both into information shown under `--verbose` (A13b), so a README with badges can pass. Alt text goes with `--style`. For stdin, `DocumentPath` is a virtual `<base-dir>\stdin.md`.                  | M1-11        |
| F17 | `HeadingNumberRewriter.Apply` reports `UncertainHeadings` as a count only. `Read` returns no uncertainty. `LinksMoved` exists.                                                                                                                                                                                                                                                                                                                                                                            | An additive library change: `Result.UncertainLines` (`IReadOnlyList<int>`), filled where the count is.                                                                                                                                                                                                                                                                      | M1-07        |
| F18 | `MarkdownHeadingReader` and `GitHubHeadingSlug` are `internal`. The outline pane is fed by `MarkdigMarkdownRenderer.Render(…).Outline` (:78).                                                                                                                                                                                                                                                                                                                                                             | `toc` and `outline` use `Render(…).Outline` and its anchors.                                                                                                                                                                                                                                                                                                                | M1-12        |
| F19 | The App publishes with `PublishReadyToRun` in Release (`App.csproj:47`), self-contained, `win-x64`.                                                                                                                                                                                                                                                                                                                                                                                                       | The publish settings move into a shared `build/Publish.props` imported by both executables, so their shared libraries are the same bytes.                                                                                                                                                                                                                                   | M1-18        |
| F20 | `IPreviewHost` has six export-shaped members (`GetRenderedHtmlAsync`, `RenderForExportAsync`, `RequestDiagramPngAsync`, `RequestDiagramSvgAsync`, `RequestMathPngAsync`, `ExportPdfAsync`). `GetRenderedHtmlAsync` and `RequestDiagramSvgAsync` are also used outside exports (Copy as SVG, `MainViewModel.DiagramSvgs.cs:46`).                                                                                                                                                                           | `IDocumentRenderer` covers the export uses only, and the route test checks only the export job classes.                                                                                                                                                                                                                                                                     | M2-05        |
| F21 | `DiagramLayout.EditFor` returns null both for "already that way" and "no direction".                                                                                                                                                                                                                                                                                                                                                                                                                      | The verb calls `DiagramLayout.Current` to tell them apart.                                                                                                                                                                                                                                                                                                                  | M1-10        |

---

## 4. Decisions taken while planning

In addition to the requirements' §2, settled on 2026-10-09:

| Question                 | Decision                                                                                                                                                                                                                                          |
| ------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| How progress is tracked  | Work packages plus one traceability matrix. Ticking happens in the matrix only                                                                                                                                                                    |
| CI                       | **No GitHub Actions at all**, here or in a scratch repository: CI checks are local commands or manual records (A14)                                                                                                                                                                      |
| Spike code               | **Seeds `WebRender`**, reshaped in M2 rather than thrown away. The spike command is compiled only under the `MQ_SPIKE` define, so it never ships in M1                                                                                            |
| Requirement changes      | **Decided in §7.** Accepted amendments were applied to `CLI-Requirements.md` on 2026-10-16 (its §2.1 records them)                                                                                                                                                                               |
| `unpack` forms           | **Single-file only** (A4)                                                                                                                                                                                                                         |
| `unpack --overwrite`     | **Replaces the target's contents**, written beside it first, never for the folders A5 named (A5c, 2026-10-27, reversing A5b)                                                                                                                                                                                    |
| `renumber-lists`         | **`--style sequential\|ones\|zeros`**, defaulting to the app's suggestion per list (A6)                                                                                                                                                           |
| Render timeout           | **Typed outcome in the shared bridge**; the app is unchanged, the CLI exits 5 (A9)                                                                                                                                                                |
| ARC-07 mechanism         | **The app's Word export uses the headless host** (A10)                                                                                                                                                                                            |
| Image resizing           | **New `PaulTechGuy.MQ.Imaging` project** behind `IImageResizer` (A2)                                                                                                                                                                              |
| Rendering interface name | `IDocumentRenderer`, in `Abstractions/Rendering`                                                                                                                                                                                                  |
| Command-line library     | `System.CommandLine` 2.0.x, pinned in `Directory.Packages.props`                                                                                                                                                                                  |
| CLI data folder          | `%TEMP%\Marqora-cli\<pid>-<start ticks>\`: per process, deleted on exit after the browser has exited, stale ones swept by PID. Nothing goes in the profile                                                                                        |
| WebView2 variables       | The CLI clears `WEBVIEW2_*` from its own environment at startup. WebView2 would read them otherwise, and a run must be hermetic (`CLI-SET-01`)                                                                                                    |
| Test hooks               | In-process fakes wherever possible. Out-of-process hooks (pause mid-write, never-finishing render, missing runtime) use a hidden option compiled only under the `MQ_TEST_HOOKS` define, which Release never sets. No environment variable is read |
| Test TFM                 | `Cli.Tests` and `Exporting.Tests` target `net10.0-windows10.0.19041.0`                                                                                                                                                                            |
| App export checks in M2  | **One short app session, after M2-06**, stands for the app comparisons of M2-02 to M2-06; from M2-09 the CLI makes the sets and Claude compares them. No test switch is added to the app (Paul, 2026-10-27). See the note at the head of M2 |

---

## 5. Target shape

### 5.1 Projects

```
src/
  PaulTechGuy.MQ.Cli        net10.0-windows10.0.19041.0  Exe (console)  M0 seed, M1  marqora-cli.exe
  PaulTechGuy.MQ.WebRender  net10.0-windows10.0.19041.0  Library       M0 seed, M2  headless host, shell bridge, paged engine
  PaulTechGuy.MQ.Exporting  net10.0                      Library       M1 seed, M2  page assembly, packaging, Folio HTML, export jobs
  PaulTechGuy.MQ.Imaging    net10.0-windows10.0.19041.0  Library       M1           IImageResizer over WIC
tests/
  PaulTechGuy.MQ.Cli.Tests        M1   parsing, exit codes, golden text/JSON, architecture, offline
  PaulTechGuy.MQ.Exporting.Tests  M2   moved code, bridge suite
build/
  Publish.props                   M1   ReadyToRun, RID, self-contained: one copy for both executables
  WebAssets.targets               M0   the MarqoraWebAsset copy, imported by App and Cli
```

`WebRender` and `Cli` set `WebView2EnableCsWinRTProjection=true` (F2).

Dependency rule, enforced by `ArchitectureTests`:

- `Cli` → libraries, `WebRender`, `Exporting`, `Imaging`. Never `App`.
- `App` → the same. Never `Cli`.
- `Exporting` → no Windows or WinUI reference (it targets `net10.0`, so the compiler enforces it).
- `WebRender` and `Imaging` → no `Microsoft.WindowsAppSDK.*` reference.

### 5.2 Key types (new)

| Type                                                             | Project               | Role                                                                                                                                                                         |
| ---------------------------------------------------------------- | --------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ExitCode`                                                       | Cli                   | The fixed exit-code enum (`CLI-RPT-01`). The manual's table is tested against it                                                                                             |
| `Reporter`                                                       | Cli                   | Every human message goes through it: stderr only, color, quiet and verbose. It takes an injected `ITerminal` for the redirect check                                          |
| `ResultWriter`                                                   | Cli                   | The only writer to stdout: verb results and `--json` lines                                                                                                                   |
| `JsonLine`                                                       | Cli                   | `schemaVersion: 1`; the schema is snapshotted in `Golden/schema-v1.json`                                                                                                     |
| `InputSet`                                                       | Cli                   | Globs, `-`, dedupe, ordinal order, base folder                                                                                                                               |
| `TextFile`                                                       | Cli                   | Strict UTF-8 read; BOM, line endings, final newline kept and restored                                                                                                        |
| `TextVerb`                                                       | Cli                   | Template for text verbs: default / `--write` / `--check` / `--dry-run`                                                                                                       |
| `UnifiedDiff`                                                    | Cli                   | Line diff for `--dry-run`: a small Myers implementation, no package                                                                                                          |
| `SettingsSource`                                                 | Cli                   | Hermetic, `app`, or file; flags override                                                                                                                                     |
| `ThemeResolver`                                                  | Cli                   | id/name match, the precedence ladder, unknown → exit 2 with a suggestion                                                                                                     |
| `Suggest`                                                        | Cli                   | Edit-distance "did you mean", shared by verbs and themes                                                                                                                     |
| `OutputPlan`                                                     | Cli                   | `-o` / `--out-dir`, collision, input-equals-output and overwrite checks, all before any work                                                                                 |
| `FolioPreflight`                                                 | Folio                 | The estimate, the three limits and the advice lines, moved out of `FolioWindow` (F10)                                                                                        |
| `DocumentLists`                                                  | Editing               | `RenumberAll(text, ListNumbering?)` (F8)                                                                                                                                     |
| `ProductVersion`                                                 | Services              | `Of(Assembly)`, moved from `AppVersion` (F12)                                                                                                                                |
| `IImageResizer`, `WicImageResizer`                               | Abstractions, Imaging | The resize seam (F7)                                                                                                                                                         |
| `IDocumentRenderer`                                              | Abstractions          | Render; diagram PNG/SVG; math PNG; PDF with the classic fallback                                                                                                             |
| `IShellTransport`, `CoreWebView2Transport`                       | WebRender             | Post and receive shell messages over any `CoreWebView2`. Both hosts use the same projection, so one transport class serves the preview's control and the headless controller |
| `ShellBridge`                                                    | WebRender             | Request ids, message shapes, timeouts, `BridgeOutcome<T>`, written once over `IShellTransport`                                                                               |
| `HeadlessRenderHost`                                             | WebRender             | STA thread, parent window, environment in the run's data folder, `shell.html`, one tab                                                                                       |
| `HtmlExportJob`, `PdfExportJob`, `WordExportJob`, `FolioPageJob` | Exporting             | The orchestration lifted out of `MainViewModel`                                                                                                                              |

---

## 6. Work packages

### M0: the spike

Gate: M2 goes ahead as designed only if `CLI-SP-01` passes on a developer machine. If SSH or the
Actions runner fails, M2 still goes ahead and that environment is documented as unsupported.

#### WP-M0-01: Headless host seed in `WebRender`

- **Serves**: CLI-SP-01, CLI-ARC-01 (WebRender), CLI-NF-05 (WinRT projection without the Windows
  App SDK).
- **Depends on**: none.
- **Changes**:
  - **Project.** New `src/PaulTechGuy.MQ.WebRender/`, targeting `net10.0-windows10.0.19041.0`, with
    `WebView2EnableCsWinRTProjection=true`. It references `Microsoft.Web.WebView2`, `Rendering`, `Domain` and
    `Abstractions`, and is added to `PaulTechGuy.MQ.slnx`.
  - **Activation.** Native WinRT activation is tried manifest-free (CsWinRT probing
    `Microsoft.Web.WebView2.Core.dll`) and, failing that, through an app manifest with
    `activatableClass` entries. The one that works is recorded in §9.1.
  - **Web assets.** New `build/WebAssets.targets`, holding the `MarqoraWebAsset` copy factored out
    of `App.csproj` (moved, not duplicated) and imported by App and Cli, so `Assets\web` lands
    beside both executables.
  - **`HeadlessRenderHost`.** A dedicated STA thread with a Win32 message loop and a
    `SynchronizationContext` that posts to it; a parent window; a `CoreWebView2Environment` in the
    run's data folder; virtual-host mapping of `Assets\web`, as `PagedPrintHost.cs:606` does;
    navigate to `shell.html` and wait for the shell's ready message; open the document as a tab.
  - **Parent window**, tried in order, each result recorded in §9.1:
    - (a) message-only (`HWND_MESSAGE`) with the controller visible;
    - (b) a never-shown `WS_POPUP` tool window (`WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`) with the
      controller's bounds off-screen, as `PagedHostMode.OffScreen` does;
    - (c) the same window shown at off-screen coordinates and cloaked with `DwmSetWindowAttribute`.
  - **Paged engine, linked rather than moved.** `PagedPrintHost.cs`, `PagedJobs.cs` and
    `WebViewPrinting.cs` are compiled in through `<Compile Include="…\App\Services\…" Link=…/>`,
    so the app is untouched. Whatever else from App they need is linked the same way and listed in
    §9.1. `InternalsVisibleTo("marqora-cli")` is added for M0 only and removed in M2-03.
- **Reuses**: `MarkdigMarkdownRenderer`, `WebAssetProvider`, and the shell's `openTab` and
  `requestPrintHtml` messages, read from `WebViewPreviewHost`.
- **Done when**: a fixture with a mermaid diagram, a KaTeX equation and a code block renders in the
  hidden host, and its print markup comes back.

#### WP-M0-02: Spike command in `Cli`

- **Serves**: CLI-SP-01, CLI-ARC-01 (Cli seed).
- **Depends on**: M0-01.
- **Changes**:
  - New `src/PaulTechGuy.MQ.Cli/`: a console Exe with `AssemblyName` `marqora-cli`, the same
    target framework, and `WebView2EnableCsWinRTProjection=true`.
  - Under `#if MQ_SPIKE`, `Program.Main` runs `marqora-cli __spike-pdf <in.md…> -o <dir>`. It
    renders through `HeadlessRenderHost`, prints through the linked `PagedPrintHost.ExportAsync`,
    and exits.
  - Cancellation goes through `PosixSignalRegistration` (SIGINT for Ctrl+C, SIGQUIT for
    Ctrl+Break). On cancel it ends the browser, waits for `BrowserProcessExited`, deletes the data
    folder (retrying while locked) and exits 130.
- **Done when**: the four acceptance boxes of CLI-SP-01 hold, checked by hand. Ctrl+C is sent by a
  launcher that shares the child's console and raises `CTRL_C_EVENT` to process group 0. Results
  go in §9.1.

#### WP-M0-03: Measurements

- **Serves**: CLI-SP-02.
- **Depends on**: M0-02.
- **Changes**: the spike logs to stderr the time from process start to each document rendered.
- **Done when**: first and second document timings are in §9.1, and the second shows the browser
  was reused.

#### WP-M0-04: Non-interactive sessions

- **Serves**: CLI-SP-03.
- **Depends on**: M0-02.
- **Steps**:
  1. Over SSH into the dev machine (OpenSSH server): run the spike and record the result.
  2. Actions runner: **not run, by decision** (A14). No GitHub Actions is used for this work, so
     exports on CI stay untested and the manual says so. Recorded in §9.1.
- **Done when**: both results and the go / unsupported decision for each are recorded. Until then
  the requirement is `blocked`.

#### WP-M0-05: Export baseline, before anything in the app changes

- **Serves**: CLI-ARC-06 (before half), CLI-ARC-02 (FolioShrinker counts as part of the refactor).
- **Depends on**: none. It must run before M1-05, M1-13, M1-15, M1-16 and M1-18 change app code.
- **Steps**: from the app as it is at the start of this plan, Paul exports HTML, PDF, Word, and a
  single-file, folder and zip Folio (with and without "reduce pictures") of the baseline set. The
  set is `webshell/cheatsheet.md`, `docs/UltimateMarkdownContent.md` and the alignment fixtures of
  `docs/Export-Alignment-Handoff.md` §10, each in Default and one other theme, with the settings
  file used kept beside them. The outputs go to `tests/baselines/cli/` (git-ignored); the file
  list and hashes go in §9.3.
- **Done when**: the baseline and its hashes are recorded.

### M1: skeleton and text verbs

#### WP-M1-00: Test project scaffold

- **Serves**: CLI-TST-01 (in part), CLI-TST-03.
- **Depends on**: M0-02 (the Cli project exists).
- **Changes**: `tests/PaulTechGuy.MQ.Cli.Tests` (xunit v3 and Shouldly, as the other test
  projects; `net10.0-windows10.0.19041.0`), added to the solution. It contains:
  - `CliRunner.InProcess`, which drives the command tree with injected stdout, stderr and terminal
    and returns the exit code;
  - `CliRunner.OutOfProcess`, which starts the built exe and is used for signals, shells, real
    redirection and kills;
  - golden files under `Golden/`, refreshed only when `MQ_UPDATE_GOLDEN=1` is set in the *test*
    process;
  - `FixtureGuardTests`, which hash every markdown fixture before the run and compare after;
  - tests that touch the process console in a non-parallel collection.
- **Done when**: `dotnet test` over the solution runs the project.

#### WP-M1-01: Command tree

- **Serves**: CLI-CMD-01..05, CLI-ARC-01 (Cli), CLI-NF-05.
- **Depends on**: M1-00.
- **Changes**:
  - **Package.** `Directory.Packages.props` gains `System.CommandLine` 2.0.12 under a new
    *Command line* label.
  - **Command tree.** `Commands/` holds one file per verb. `RootCommandFactory` builds the tree,
    with `export {html,pdf,word}` and `themes list` nested, and `number-headings` and
    `remove-heading-numbers` as aliases.
  - **Help examples.** `VerbExamples` holds each verb's examples once, and a custom help action
    prints them as an *Examples* section.
  - **Parser settings.** The response-file token replacer is switched off.
  - **Global options.** `--json`, `--quiet`, `--verbose`, `--color`, `--fail-fast`, `--settings`.
  - **Conflicts.** `--write` with `--check` or `--dry-run`, `-o` with `--out-dir`, `--quiet` with
    `--verbose`, and `-` with other inputs each give exit 2, with one sentence that points at
    `--help`.
  - **Unknown verb.** Handled through `Suggest`.
  - **Export verbs in M1.** They exist but are hidden. Until M2 they exit 2, saying they arrive in
    a later release.
- **Tests**:
  - `CommandTreeTests`: every canonical verb is reachable.
  - `AliasTests`: an alias gives byte-identical output to its verb, and help names the alias.
  - `HelpExamplesTests`: fails if any verb has no examples.
  - `UsageErrorTests`: each conflict exits 2, a misspelled verb gets a suggestion, and
    `format @args.rsp` treats the argument as a file name.
- **Done when**: the tests pass.

#### WP-M1-02: Run core

- **Serves**: CLI-RPT-01..07, CLI-RPT-08 (text part).
- **Depends on**: M1-01.
- **Changes**:
  - **`ExitCode`.** The codes 0, 1, 2, 3, 4, 5 and 130, ranked 130 > 5 > 4 > 3 > 2 > 1 > 0.
  - **`RunAggregator`.** Continues past a failed file and keeps the worst code; `--fail-fast`
    stops at the first.
  - **`Reporter`.** Takes `ITerminal` for the "is stderr a terminal" check, plus `--color` and
    `NO_COLOR`.
  - **`ResultWriter`.**
  - **`JsonLine`.** `schemaVersion`, `path`, `verb`, `outcome`, `outputs`, and `findings[]` with
    `{line, column, code, severity, message}`, plus `message`. Two additive fields keep it at
    version 1: `baseDir` for stdin, and `theme {id, source}` on exports and Folio.
  - **Cancellation.** Through `PosixSignalRegistration` (SIGINT, SIGQUIT).
- **Tests**:
  - `ExitCodeTests`: a real invocation for each code.
  - `RunAggregatorTests`: one input error plus one finding gives exit 3, and `--fail-fast` skips
    the files after the failure.
  - `JsonGoldenTests`: a golden per verb and outcome; every line parses with `schemaVersion: 1`; a
    partial failure shows per file in JSON alone; a removed or renamed field fails the comparison
    with `Golden/schema-v1.json`.
  - `ReporterTests`: out of process, redirected stderr under `auto` carries no escape sequences,
    and neither does `NO_COLOR=1`; `--quiet --write` on a clean run prints nothing.
  - `OutputDisciplineTests`: a source scan that fails on `Console.Out`, `Console.Error` or
    `Console.Write*` outside `Reporter` and `ResultWriter`.
  - `CancelTests`: Ctrl+C during a large `--write`, through a console-sharing launcher, exits 130
    and leaves every file original or complete.
- **Done when**: the tests pass.

#### WP-M1-03: Inputs and text files

- **Serves**: CLI-IN-01..04, CLI-IN-06, CLI-OUT-06.
- **Depends on**: M1-02.
- **Changes**:
  - **`InputSet`.** Built on `Microsoft.Extensions.FileSystemGlobbing`, with a new central version.
    Literal paths are taken as given and patterns are expanded. Results are deduplicated by full
    path and sorted ordinally, and a pattern matching nothing is exit 3.
  - **stdin.** `-` reads stdin; `--base-dir` applies to stdin only.
  - **`TextFile.Read`.** Uses `UTF8Encoding(false, true)`; invalid bytes give exit 3 and nothing is
    written. It records the BOM, the majority line ending (a mixed file is noted under
    `--verbose`) and the final newline.
  - **`TextFile.Restore`.** Puts back the BOM and the ending. The final newline is restored unless
    the verb changed it on purpose (A7). stdout is UTF-8 without a BOM.
- **Tests**:
  - `InputSetTests`:
    - a file named twice is processed once;
    - an empty pattern exits 3;
    - one missing file among three: exit 3, and the other two are processed.
  - `ShellGlobTests`: the same quoted `**` pattern from `pwsh` and from `cmd` gives the same list.
    bash is a §9.3 record.
  - `StdinTests`: `Get-Content x.md -Raw | marqora-cli format -` writes the formatted text, run
    out of process through `pwsh`; `-` together with a file exits 2.
  - `TextFileTests`:
    - BOM and no-BOM inputs both format;
    - a Windows-1252 file exits 3 and stays byte-identical;
    - CRLF with a BOM survives `format --write`;
    - LF without a final newline survives `number --write`;
    - `format --check` on an already formatted CRLF file exits 0.
- **Done when**: the tests pass.

#### WP-M1-04: Write modes

- **Serves**: CLI-OUT-01..05, CLI-IN-05.
- **Depends on**: M1-03.
- **Changes**: the `TextVerb` base, holding `Transform(text, VerbContext) → VerbResult(text,
  findings)`, and the modes:
  - **Default.** The result goes to stdout. With several inputs and no mode, exit 2; `check`,
    `outline` and `toc` opt out of that rule.
  - **`--write`.** A pre-pass refuses read-only inputs: exit 3, all of them named, nothing written.
    After that, `SafeFileWriter.WriteAsync` runs only where the bytes differ.
  - **`--check`.** Paths on stderr, exit 1.
  - **`--dry-run`.** `UnifiedDiff` with `a/` and `b/` prefixes, in the file's own line endings.
- **Reuses**: `SafeFileWriter`, and `ClearStaleTemporaries` per target folder at start.
- **Tests**:
  - `WriteModeTests`:
    - timestamps are unchanged without `--write`;
    - an unchanged file keeps its mtime under `--write`;
    - one read-only file among three: exit 3, and none of the three changed;
    - a read-only file is read normally without `--write`;
    - `--check` exit codes and output;
    - `format a.md b.md` exits 2 and suggests `--write`/`--check`.
  - `DryRunDiffTests`: `git apply --check` accepts the diff against the original, and an unchanged
    file prints nothing.
  - `KillDuringWriteTests`: an `MQ_TEST_HOOKS` pause after the temp file is written, then a kill,
    leaves the file original.
- **Done when**: the tests pass.

#### WP-M1-05: Settings

- **Serves**: CLI-SET-01..04.
- **Depends on**: M1-02; M0-05 recorded.
- **Changes**: `SettingsSource` in `Cli`.
  - **Hermetic.** `new AppSettings()`.
  - **App.** `JsonSettingsRepository` over `AppPaths.SettingsFilePath`. The load is read-only
    (F14).
  - **File.** `PreferencesTransferService.ImportAsync(path, new AppSettings())`. A problem from
    the import result, or an unknown theme (M1-06), gives exit 2 naming the file.
  - **Exposed settings.** `Formatting`, `HeadingNumbering`, the number style, `PdfDefaults`,
    `DocxDefaults`, `LayoutDefaults`, `PreviewMaxWidth`, `ColorTheme` and `ExportColorTheme`.
  - **Flags.** Applied on top of whichever source is in use.
- **Tests**: `SettingsSourceTests`, with `IAppPaths` injected to point at a temp folder:
  - hermetic output is the same with and without a crafted settings file present;
  - `--settings app` follows a changed formatter option;
  - the settings file is byte-identical after a run;
  - an exported file gives the same output as `app` for the same content;
  - a malformed file exits 2 naming it;
  - `--paper A4` beats a file naming Letter, asserted on the resolved setup (and on the PDF in
    M2-10).
- **Done when**: the tests pass.

#### WP-M1-06: Themes

- **Serves**: CLI-TH-01 (`folio`, and text verbs refusing it), CLI-TH-02, CLI-TH-03, CLI-TH-05,
  CLI-TH-06 (resolution).
- **Depends on**: M1-05.
- **Changes**:
  - **`ThemeResolver`.** Over `ThemeCatalog.Load()`, it matches a theme's id or name, ignoring
    case. The ladder is `--theme`, then the settings' `ExportColorTheme`, then the settings'
    `ColorTheme`, then Default.
  - **Unknown theme.** From any rung, exit 2, every id, and a `Suggest` hint, all before any
    output is planned.
  - **`themes list`.** In catalog order: Default first, then by name.
- **Tests**:
  - `ThemeResolverTests`:
    - three spellings of one theme resolve the same;
    - `format --theme ocean` exits 2;
    - one test per rung of the ladder;
    - `Oceanic` suggests `ocean` and lists all ids;
    - `folio --theme Oceanic -o out.zip` leaves no `out.zip`;
    - `--verbose` names the theme's source for each of the three sources.
  - `ThemesListTests`: the list equals `ThemeCatalog` in order, and `FromSources` with an extra
    theme adds a row with no CLI change.
- **Done when**: the tests pass.

#### WP-M1-07: `format`, `number`, `unnumber`

- **Serves**: CLI-VB-FMT-01, CLI-VB-NUM-01, CLI-VB-UNN-01.
- **Depends on**: M1-04, M1-05.
- **Changes**:
  - **Library.** Additive, in `Formatting`: `HeadingNumberRewriter.Result.UncertainLines` (F17),
    with a unit test in `Formatting.Tests`.
  - **`format`.** `MarkdownFormatter.Format(text, settings.Formatting)`.
  - **`number`.** `number --from h1|h2|h3 --style two-spaces|one-space|dot-space|paren-space` calls
    `HeadingNumberRewriter.Apply`. `UncertainLines` become findings, and `LinksMoved` goes in the
    summary.
  - **`unnumber`.** `HeadingNumberRewriter.Remove`.
- **Reuses**: the arguments the app's commands pass, read from their call sites in
  `MainViewModel`.
- **Tests**:
  - `FormatVerbTests`: goldens; equals a direct library call; idempotent.
  - `NumberVerbTests`: equals `Apply` / `Remove` called directly; uncertain headings come with line
    numbers; `number` then `unnumber` round-trips an unnumbered document.
- **Done when**: the tests pass.

#### WP-M1-08: `replace`

- **Serves**: CLI-VB-RPL-01.
- **Depends on**: M1-04.
- **Changes**: `replace --find --with [--match-case] [--whole-word] [--regex]` builds a
  `ReplaceQuery` and calls `DocumentReplacer.Replace` with one `FindDocument` per input. An invalid
  regex exits 2 with the parser's message. `--with ""` is allowed. The summary gives a count per
  file.
- **Tests**: `ReplaceVerbTests`: all eight option combinations against a direct `DocumentReplacer`
  call; code is treated as the app treats it; an empty `--with` deletes.
- **Done when**: the tests pass.

#### WP-M1-09: `renumber-lists`

- **Serves**: CLI-VB-REN-01.
- **Depends on**: M1-04.
- **Changes**: in `Editing`, `DocumentLists.RenumberAll(text, ListNumbering?)`.
  - For each ordered list it builds an `EditContext` and calls `ListRenumber.Apply`, using
    `Describe`'s suggestion when the argument is null.
  - It returns the edits and the lists it left alone: those where `Zeros` was asked for and the
    list does not follow a break.
  - The verb takes `--style sequential|ones|zeros` and reports each list left alone.
- **Tests**:
  - `RenumberListsTests`: equals `MarkdownEditor` applied list by list; `--style` overrides the
    suggestion; an unbreakable `zeros` list is reported and left unchanged.
  - `Editing.Tests` covers `DocumentLists`.
- **Done when**: the tests pass.

#### WP-M1-10: `diagram-layout`

- **Serves**: CLI-VB-DGL-01.
- **Depends on**: M1-04.
- **Changes**: `--direction top-to-bottom|left-to-right [--diagram <n>]`.
  - It walks `DiagramLayout.Fences` from last to first.
  - `DiagramLayout.Current` returning null means the diagram has no direction: it is reported
    (info) and left alone.
  - Otherwise `EditFor` is applied when the direction differs.
- **Tests**: `DiagramLayoutVerbTests`: equals `EditFor` applied by hand per diagram; `--diagram 2`
  touches only the second; a pie chart is left alone and reported.
- **Done when**: the tests pass.

#### WP-M1-11: `check`

- **Serves**: CLI-VB-CHK-01, CLI-VB-CHK-02, CLI-IN-03.
- **Depends on**: M1-04.
- **Changes**: `check [--style [--strict]] [--ignore-external-images]`.
  - **Render first.** `MarkdigMarkdownRenderer.Render`, then `MarkdownAnalyzer.Analyze` with
    `AnalysisRequest { Text, DocumentPath, Links, Outline, Anchors }` (F16). `DocumentPath` is the
    file, or `<base-dir>\stdin.md` for stdin.
  - **Severity map.**
    - Errors (exit 1): dead local link, dead anchor, missing local image, `RemoteMedia` (a web
      image) and `OutsideFolder` (a picture outside the document's folder).
    - Info, under `--verbose` only: web links "not checked", and with `--ignore-external-images`
      also `RemoteMedia` and `OutsideFolder`.
    - Hints, only with `--style` (exit 1 only with `--strict`): style diagnostics and alt text.
  - **Flag rules.** `--strict` without `--style` exits 2. `--ignore-external-images` is one flag
    for both external kinds (A13b).
- **Tests**: `CheckVerbTests`:
  - a dead anchor, a missing image and a dead relative link are each found, with line and column;
  - a README with a badge image exits 1, and exits 0 with `--ignore-external-images`, which lists
    the badge as information under `--verbose`;
  - findings equal the analyzer's own result for the same request, which is the source of the
    squiggles;
  - `check - --base-dir docs` equals `check docs/x.md`, and `baseDir` appears in its JSON;
  - the style exit codes.
- **Done when**: the tests pass.

#### WP-M1-12: `toc` and `outline`

- **Serves**: CLI-VB-TOC-01, CLI-VB-OUT-01.
- **Depends on**: M1-04, M1-05.
- **Changes**: both go through `MarkdigMarkdownRenderer.Render(text, numbering).Outline` (F18).
  - **`toc`.** Prints a nested list of `[text](#anchor)` over
    `ContentsListing.Levels(numbering)`. `toc --write` exits 2 and points to the open item.
  - **`outline`.** Prints an indented tree with line numbers; with `--json`, level, text, anchor
    and line for each heading.
- **Tests**:
  - `TocVerbTests`: every anchor is an `id` in the rendered HTML; `--write` exits 2.
  - `OutlineVerbTests`: equals `Render(…).Outline`, which is what the outline pane shows.
- **Done when**: the tests pass.

#### WP-M1-13: `version`

- **Serves**: CLI-VB-VER-01.
- **Depends on**: M1-01; M0-05 recorded.
- **Changes**: *move* `AppVersion` to `Services` as `ProductVersion.Of(Assembly)`. The app passes
  its own assembly and the CLI passes its own. `version` and `--version` print it. A generated
  `BuildInfo.Version` constant in the test project comes from the same `Directory.Build.props`
  property.
- **Tests**: `VersionTests`: the CLI's output equals `BuildInfo.Version`. `New-Release.ps1` asserts
  that `FileVersionInfo.ProductVersion` of `Marqora.exe` and `marqora-cli.exe` are equal (M1-18).
- **Done when**: both checks pass.

#### WP-M1-14: `unpack`

- **Serves**: CLI-VB-UNP-01 (as amended by A4 and A5c).
- **Depends on**: M1-04.
- **Changes**: `unpack <folio.html> --out-dir <folder> [--overwrite]`.
  - **Not a single-file Folio.** A folder, a `.zip`, or a file where `FolioPayload.TryDecode`
    fails gives exit 3 with guidance.
  - **Non-empty target.** Without `--overwrite`, exit 4, and nothing in it is touched. With it
    (A5c), `FolderReplacement` unpacks into a sibling, moves the old folder aside, moves the new
    one into its name and only then deletes the old one, putting it back if the move fails. A
    drive root, the profile folder, the current folder or one holding it, and a folder holding the
    Folio are refused, exit 4. A missing or empty target is handed to `FolioUnpacker.Unpack`
    directly, which enforces the empty rule itself.
- **Tests**: `UnpackVerbTests`, using a single-file Folio **made by the app and committed as a
  fixture** (`FolioHtmlWriter` is still in App in M1). The unpacked files equal the documents the
  app would open. Also tested: a non-empty target exits 4 and is unchanged without `--overwrite` and replaced with it, a target holding the Folio is refused, and the folder and zip
  refusals.
- **Done when**: the tests pass.

#### WP-M1-15: `Imaging`, `Exporting` seed, `FolioShrinker` move

- **Serves**: CLI-ARC-01 (Exporting, Imaging), CLI-ARC-02 (FolioShrinker), CLI-FO-03.
- **Depends on**: M0-05 recorded.
- **Changes**:
  - **Seam.** `IImageResizer.ResizeForFileAsync(byte[], int maxWidth)` in `Abstractions`.
  - **`Imaging` project.** New `src/PaulTechGuy.MQ.Imaging/` with `WicImageResizer`, the resize
    half of `ClipboardImage` *moved*. `ClipboardImage` keeps the clipboard half and calls the
    resizer.
  - **`Exporting` project.** New `src/PaulTechGuy.MQ.Exporting/` (`net10.0`), with `FolioShrinker`
    *moved* in and taking `IImageResizer`.
  - **Wiring.** App DI is updated.
- **Tests**: a new `FolioShrinkTests` in `Folio.Tests` or `Exporting`: an oversized PNG is reduced,
  a JPEG that would grow is kept, and the same input and width give the same bytes.
- **Done when**: the tests pass, including a reduction that really happens, which the baseline
  cannot show (§9.3), and the app's Folio "reduce pictures" output equals the M0-05 baseline.

#### WP-M1-16: `folio`, folder and zip forms

- **Serves**: CLI-FO-01 (folder, zip), CLI-FO-03, CLI-FO-04, CLI-FO-05, CLI-FO-06, CLI-OUT-07 and
  CLI-OUT-08 (folio), CLI-TH-06 (folio), CLI-NF-03 (fetch path).
- **Depends on**: M1-06, M1-15.
- **Changes**:
  - **Command.** `folio -o <path> [--form single|folder|zip][--theme][--shrink-images <px>]
    [--fetch-web-images][--dry-run][--overwrite] <inputs…>`.
  - **Form inference.** `.html` gives single, `.zip` gives zip, and a trailing separator or an
    existing folder gives folder. A `--form` that disagrees exits 2. Single exits 2 ("a later
    release") until M2-12.
  - **`OutputPlan`.** Refuses an output that is one of the inputs, even with `--overwrite`, and
    refuses an existing output without `--overwrite` (exit 4) before planning.
  - **Order (F15).** `FolioPlanner.Plan` → `FolioFetcher` (only under `--fetch-web-images`) →
    `Plan` again with the fetched files → `FolioShrinker` into a run-scoped scratch folder →
    `FolioWriter.WriteFolderAsync` / `WriteZipAsync`.
  - **Preflight (F10).** *Move* the preflight figures into `FolioPreflight` in `Folio`.
    `FolioWindow` and `--dry-run` both use it. `--dry-run` prints the documents, the picture
    count, missing pictures, outside links, the estimate with its advice, the form, and the theme
    with its source. With `--fetch-web-images` it adds each host once, and contacts none.
  - **Report.** The Folio report becomes findings, each naming its document.
- **Reuses**: `MainViewModel.WriteFolioCoreAsync`'s sequence, read and followed.
- **Tests**:
  - `FolioFormTests`: each path gives its form; `-o x.html --form zip` exits 2.
  - `OutputPlanTests`: an output that is an input is refused even with `--overwrite`; an existing
    output exits 4 before planning; two inputs colliding in one `--out-dir` exit 3 (driven through
    `OutputPlan` directly, ready for M2).
  - `FolioDryRunTests`: each preflight line appears in text and in JSON.
  - `FolioFetchTests`: `--dry-run --fetch-web-images` lists hosts and contacts none; without the
    flag, web pictures stay links and are reported.
  - `FolioFindingsTests`: every finding names its document.
  - `FolioPreflight` unit tests in `Folio.Tests`.
- **Done when**: the tests pass, and the folder and zip outputs equal the M0-05 baseline (§9.3).

#### WP-M1-17: Architecture and offline tests

- **Serves**: CLI-ARC-01 (enforcement), CLI-NF-03, CLI-TST-01 (completion).
- **Depends on**: M1-16.
- **Changes**:
  - **`ArchitectureTests`.** Read each `.csproj` and fail on a forbidden reference.
  - **`OfflineTests`.** In a non-parallel collection, an `EventListener` on `System.Net.Sockets`
    and `System.Net.Http` runs every M1 verb in-process (all text verbs, `folio` without fetch,
    `unpack`, `themes list`, `version`) and asserts no connect starts. A source scan asserts
    `new HttpClient` appears only inside `FolioFetcher`'s fetch method.
  - **Firewall record.** A §9.3 record: every verb run under a Windows Firewall outbound block for
    `marqora-cli.exe`, with no failure caused by it. M2-13 extends this to the exports.
- **Done when**: the tests pass, and every M1 WP's tests run in the normal run.

#### WP-M1-18: Packaging and install

- **Serves**: CLI-PKG-01, CLI-PKG-02, CLI-NF-05.
- **Depends on**: M1-01; M0-05 recorded.
- **Changes**:
  - **`build/Publish.props`.** Holds `SelfContained`, `RuntimeIdentifier win-x64` and
    `PublishReadyToRun` for Release, *moved* from `App.csproj` and imported by App and Cli (F19).
  - **`build/New-Release.ps1`.** Publishes the CLI into the same `app/` folder after the App.
    It fails if a shared file differs between the two publishes, and asserts equal
    `ProductVersion` (M1-13).
  - **`build/installer/Install.ps1`.** An opt-in switch and prompt add the install folder to the
    user `PATH` with `[Environment]::SetEnvironmentVariable(…, 'User')`, and record a marker
    beside the install. `Uninstall.ps1` removes exactly that entry when the marker says it was
    added.
- **Tests / checks**:
  - The `New-Release.ps1` assertions.
  - `AssetLocationTests`: the built CLI finds `Assets\web` and the themes relative to its own
    folder, run from a different working directory.
  - §9.3 records:
    - install with PATH, then `marqora-cli version` works in a new shell;
    - uninstall leaves PATH as it was;
    - the CLI starts in Windows Sandbox, which has no Windows App SDK.
- **Done when**: the checks pass and the records are complete.

#### WP-M1-19: M1 documentation and rules

- **Serves**: CLI-DOC-01 (M1), CLI-DOC-02, CLI-DOC-03 (M1), CLI-RULE-01..04 (M1), CLI-NF-01.
- **Depends on**: M1-01..M1-18.
- **Changes**:
  - **`docs/CLI.md`.**
    - install and PATH;
    - a five-minute tour;
    - every M1 verb with its options and examples;
    - pipes and globs;
    - dry-run, check and write;
    - settings and themes;
    - the exit-code table;
    - the `--json` schema and its versioning rule;
    - a pre-commit hook for `format --check` and `check`, and no CI workflow of any kind (A14);
    - troubleshooting.

    It should read well in Marqora's own preview.
  - **The rest of the docs.**
    - `README.md`: a *Command line* section and the new projects.
    - `docs/index.html`: a feature card.
    - `Welcome to Marqora.md`: a paragraph.
    - `docs/Installer.md`: the PATH option.
    - `docs/Folio.md`: the CLI's forms.
  - **`build/Test-NetworkClaim.ps1`.** `docs/CLI.md` and the Cli `Commands/` files, where the help
    text lives, join `$surfaces`.
  - **`docs/releases/vNext.md`.** A `## Command line` section.
- **Tests**:
  - `ManualCoverageTests`: every verb, alias and option in the command tree appears in `CLI.md`;
    every `VerbExamples` entry appears in it.
  - `ManualTableTests`: the exit-code table equals `ExitCode`.
  - `ManualExampleTests`: every fenced block in `CLI.md` that starts with `marqora-cli` must carry
    an output block, or be marked `<!-- cli-example: no-output -->`, so an untagged example fails.
    Each one is run against a temp copy of the fixtures and compared with its output.
- **Checks**:
  - `Add-FileHeaders.ps1 -Check`, `Set-AmericanSpelling.ps1 -Check` and
    `Test-NetworkClaim.ps1 -Check` all exit 0.
  - NF-01: the `format` and `check` timings go in §9.2.
  - DOC-02: the hook, installed here, passes on a clean tree and blocks a commit with an
    unformatted file or a dead link; the manual has no Actions workflow (A14, §9.3).
- **Done when**: the tests and checks pass.

### M2: refactor and exports

Gate: CLI-SP-01 passed (§9.1).

**How the app's exports are checked (Paul, 2026-10-27).** Every export by hand from the app is
slow and easy to get wrong; the second export of the baseline took hours, and a save dialog that
remembered the baseline folder wrote over one of its files. So the "matches the baseline" of
M2-02, M2-03, M2-04 and M2-06 is one app session after M2-06, not one per package: the
cheatsheet in HTML, PDF and Word and its Folio in each form, all in Forest, one print to
Microsoft Print to PDF, and Copy as PNG and SVG on a diagram. Until then each package is proven by
the build, the tests and headless runs. A difference found then is traced through the report,
which names the format and the place. From M2-09 the CLI makes the full set and the comparisons
need no hand work. A command-line switch that drove the app's exports was considered and
rejected: the app gains no option for testing.

**The baseline's "default" exports, nine files and six Folios, are Forest** (found 2026-10-27, renamed
`*.forest.*` with every recorded hash kept; `tests/baselines/cli/README.md` has the account).
Every comparison of those rows exports in Forest.

#### WP-M2-01: Comparison script

- **Serves**: CLI-ARC-06 (tooling).
- **Depends on**: M0-05.
- **Changes**: `build/Compare-ExportBaseline.ps1` compares a fresh set against the M0-05 baseline.
  - Before comparing it masks docx `core.xml` dates and `w:rsid` values, PDF `/CreationDate`,
    `/ModDate` and `/ID`, and zip entry timestamps.
  - It renders PDFs to PNG per page with `Windows.Data.Pdf`, and compares pixels within a stated
    tolerance.
  - It writes a difference report.
- **Done when**: the script, run against the baseline set exported a second time from the
  unchanged app, reports no difference.

#### WP-M2-02: Move the export assembly into `Exporting`

- **Serves**: CLI-ARC-02.
- **Depends on**: M2-01.
- **Changes**:
  - **Moves.** `HtmlExporter`, `HtmlPageLayout`, `FolioHtmlWriter`, `DiagramHtmlDocument`,
    `ColorThemePayloads`, `DiagramViewer` and `RenderedHtmlPackager` *move*.
  - **One seam.** `RenderedHtmlPackager` swaps `Windows.UI.Color` for a plain RGB record.
  - **Unchanged.** `ExportIssueReport` stays in Abstractions and `ExportIssue` in Domain.
  - **Wiring.** App DI is updated.
- **Done when**: `Exporting` builds with no Windows reference, and the HTML and Folio comparison
  shows no difference (§9.3).

#### WP-M2-03: Move the paged engine into `WebRender`

- **Serves**: CLI-ARC-03, CLI-EX-01 (one browser tree).
- **Depends on**: M2-01.
- **Changes**:
  - **Moves.** `PagedPrintHost`, `PagedJobs` and `WebViewPrinting` *move*, replacing the M0 links.
    The M0 `InternalsVisibleTo` is removed.
  - **Environment injection (F3).** The static environment becomes an injected
    `Func<Task<CoreWebView2Environment>>`. The app passes today's `-Print` environment, unchanged,
    and the CLI passes `HeadlessRenderHost`'s.
  - **Visibility.** `public` only where App or Cli need it.
- **Done when**: the app's PDF matches the baseline (§9.3), and one print to Microsoft Print to PDF
  is checked by hand.

#### WP-M2-04: One shell bridge

- **Serves**: CLI-ARC-05, CLI-EX-05 (mechanism).
- **Depends on**: M2-03.
- **Changes**:
  - **`IShellTransport` and `CoreWebView2Transport`.** One class over a `CoreWebView2`, used by
    both hosts. Both use the WinRT projection.
  - **`ShellBridge`.** Holds the requests by `Guid`, the message records, per-request timeouts,
    and `BridgeOutcome<T> { Rendered | TimedOut | Failed }`.
  - **The preview's delegation.** `WebViewPreviewHost` hands the export-shaped requests and
    `RequestPrintMarkupAsync` to the bridge. It maps `TimedOut` to today's degrade, so the app is
    unchanged.
  - **`render.html`.** Added to the webshell only if the headless page needs one. It loads the
    same `app.js` without the editor; it is not a second pipeline.
- **Tests**: `BridgeShapeTests`: a source scan fails if a message-name literal appears outside
  `ShellBridge`.
- **Done when**: the test passes and the app behaves as before.

#### WP-M2-05: `IDocumentRenderer` and its two implementations

- **Serves**: CLI-ARC-04, CLI-EX-06 (route).
- **Depends on**: M2-04.
- **Changes**:
  - **`IDocumentRenderer`** (F20):
    - `RenderAsync(markdown, baseFolder, options) → BridgeOutcome<RenderedDocument>`;
    - `DiagramPngAsync(hash)`;
    - `DiagramSvgAsync(hash)`, the light drawing through `outputSvgOf`;
    - `MathPngAsync(line, ordinal)`;
    - `RenderDetachedAsync(html)`, for Folio;
    - `PrintPdfAsync(path, setup, title, furniture) → PdfOutcome`, with the classic fallback.
  - **Live implementation.** `WebViewPreviewHost` implements it for the active tab.
  - **Headless implementation.** `HeadlessRenderHost` implements it: the document is opened as
    its one tab, the theme set with `SetExportColorThemeAsync`, light palette always.
- **Tests**: `RendererRouteTests`: a source scan shows the export job classes reach rendering only
  through `IDocumentRenderer`.
- **Done when**: the test passes.

#### WP-M2-06: Export jobs out of `MainViewModel`

- **Serves**: CLI-ARC-04 (app side), CLI-EX-03 (issue collection).
- **Depends on**: M2-02, M2-05.
- **Changes**:
  - **Jobs.** `HtmlExportJob`, `PdfExportJob`, `WordExportJob` and `FolioPageJob` in `Exporting`.
    `PdfExportJob` absorbs `PdfPictureIssuesAsync` and `DiagramIssues`. Each job returns
    `ExportOutcome(outputs, issues)`.
  - **`MainViewModel`.** Its `Write*ExportAsync` methods become dialogs → job → report window.
- **Done when**: all four app exports, **Word included**, match the baseline (§9.3). This runs
  before M2-07, so drift from the moves is not confused with ARC-07's intended change.

#### WP-M2-07: The app's Word export through the headless host

- **Serves**: CLI-ARC-07, CLI-RULE-04 (the announcement).
- **Depends on**: M2-06.
- **Changes**:
  - **Host.** The app registers a lazy `HeadlessRenderHost` with its own data folder
    (`…\WebView2-Render`), ended at shutdown alongside `PagedPrintHost.EndBrowser`.
  - **Word export.** `WriteWordExportAsync` passes that host to `WordExportJob`.
  - **Docs.** `docs/WordExport.md` §7 is rewritten.
  - **Release note.** `docs/releases/vNext.md` announces the change.
- **Checks**: §9.3 records:
  - the app's Word export is identical whether the preview is busy, on another tab, or idle;
  - the Word difference from the baseline is explained;
  - the first Word export's added cold start goes in §9.2.
- **Done when**: the records are complete.

#### WP-M2-08: CLI export infrastructure

- **Serves**: CLI-EX-01..05, CLI-NF-04, CLI-OUT-07 and CLI-OUT-08 (exports), CLI-RPT-06 (export
  verbosity), CLI-RPT-08 (exports), CLI-TH-01 (exports), CLI-TH-06 (exports).
- **Depends on**: M2-05, M1-16 (`OutputPlan`).
- **Changes**:
  - **`RendererFactory`.** One lazy `HeadlessRenderHost` per run, sharing its environment with the
    paged engine (M2-03).
  - **Data folder.** Deleted in `finally` and on cancel, after `BrowserProcessExited`; stale
    folders are swept by PID at start.
  - **Timeout.** `--timeout <s>`, default 60. `TimedOut` gives that file exit 5, and the run moves
    on.
  - **Missing runtime.** `GetAvailableBrowserVersionString` failing gives exit 5 with the install
    link.
  - **`--dry-run`.** No browser. It prints the planned outputs, the theme and its source, the page
    setup, and what `check`'s analysis already knows: missing images and unresolved local links.
  - **Findings.** Export issues become findings and do not change the exit code.
  - **Outputs.** `OutputPlan` handles them.
- **Tests**:
  - `RendererFactoryTests`: a `format` run never calls a fake factory; ten exports leave exactly
    one `msedgewebview2` browser process (the root of the tree) parented under the run, counted
    by parent PID.
  - `DataFolderTests`: two concurrent runs both succeed; no folder remains after a normal exit or
    after Ctrl+C, sent by the launcher.
  - `RenderTimeoutTests`: an `MQ_TEST_HOOKS` render that never finishes gives exit 5 and a
    message.
  - `MissingRuntimeTests`: an injected runtime probe makes `export pdf` exit 5 while `format`
    succeeds.
  - `ExportFindingsTests`: a missing image exports, exits 0 and is reported with its line.
  - `ExportDryRunTests`: no browser starts and no file is written.
  - `CancelTests` (exports): Ctrl+C during an export leaves no output file and no browser process.
  - `OutputPlanTests` (exports): `export pdf --out-dir dist a.md b.md` writes `dist/a.pdf` and
    `dist/b.pdf`; `-o` with two inputs exits 2; existing output without `--overwrite` exits 4
    with no browser started.
  - `ReporterTests`: `--verbose` on an export names the theme and its source.
- **Done when**: the tests pass.

#### WP-M2-09: `export html`

- **Serves**: CLI-EX-HTML-01, CLI-OUT-09.
- **Depends on**: M2-08.
- **Changes**: the verb over `HtmlExportJob`, with the page width from the settings source
  (`PreviewMaxWidth`). `-o -` writes to stdout; PDF, Word and Folio refuse it with exit 2.
- **Tests**: `ExportHtmlTests`: `-o -` gives the same bytes as `-o x.html`; the CLI output for the
  baseline set equals the app's after the refactor. The reference is the app's set, re-exported
  after M2-06, compared by `Compare-ExportBaseline.ps1` and recorded in §9.3.
- **Done when**: the test passes and the record is complete.

#### WP-M2-10: `export pdf`

- **Serves**: CLI-EX-PDF-01, CLI-SET-04 (PDF).
- **Depends on**: M2-08.
- **Changes**:
  - **Flags.** `--paper` (every `PaperSize`), `--orientation`, `--margin` (every `PageMargin`) and
    `--[no-]backgrounds`. The rest of the layout comes from `--settings`.
  - **Fallback.** If the classic engine took over, an advisory finding says so.
  - **Spike retired.** `MQ_SPIKE` and its command are removed.
- **Tests**: `ExportPdfTests`: MediaBox and margins match each flag value; `--paper A4` over a
  settings file naming Letter gives A4. The page images match the app's PDF through the comparison
  script (§9.3).
- **Done when**: the tests pass and the record is complete.

#### WP-M2-11: `export word`

- **Serves**: CLI-EX-WORD-01.
- **Depends on**: M2-07, M2-08.
- **Changes**: the verb over `WordExportJob`, with the Word setup and layout from the settings
  source.
- **Tests**: `ExportWordTests`: diagrams and math are images and code is in its colors. CLI and
  app give identical `word/document.xml` and media parts for the same input, theme and settings
  (§9.3).
- **Done when**: the tests pass and the record is complete.

#### WP-M2-12: `folio`, single-file form

- **Serves**: CLI-FO-01 (single), CLI-FO-02, CLI-TH-04 (one theme across documents).
- **Depends on**: M2-08.
- **Changes**:
  - **Rendering.** The single form goes through `FolioPageJob`, with each document rendered by
    `RenderDetachedAsync`.
  - **Size.** If `FolioPreflight`'s estimate passes `HeavyPageBytes`, the run exits 4 before
    writing and suggests `--form zip` or `--shrink-images`.
- **Tests**: `FolioFormTests`: `.html` gives a single file; an over-limit set exits 4 and writes
  nothing.
- **Done when**: the tests pass, and the single-file output matches the app's (§9.3).

#### WP-M2-13: Export verification suite

- **Serves**: CLI-ARC-06 (after half), CLI-EX-06, CLI-TH-04, CLI-TST-02, CLI-ARC-05 (one suite),
  CLI-NF-03 (exports).
- **Depends on**: M2-09..M2-12.
- **Changes**:
  - **`tests/PaulTechGuy.MQ.Exporting.Tests`** holds the moved code's tests plus `BridgeSuite`.
    That suite runs `ShellBridge` over `CoreWebView2Transport` on a headless controller hosting
    `shell.html`. The preview uses that same bridge and transport over its control's
    `CoreWebView2`, so this is the one suite over both implementations. Amendment A12 says so.
  - **`DiagramOutputTests`.** The exported SVG equals the shell's `outputSvgOf` drawing.
  - **`ExportPaletteTests`.**
    - No dark-palette value from the theme JSON appears in an HTML export's stylesheet.
    - Every document of a Folio carries the same theme.
    - No file other than the requested outputs is created, with the run's data folder exempt and
      shown to be gone at exit.
  - **`OfflineTests` (exports).** The headless host's `WebResourceRequested` filter counts any
    request outside the virtual host, and must count zero. The §9.3 firewall record is extended to
    the export verbs.
  - **ARC-06.** The comparison over the full set is run and recorded in §9.3.
- **Done when**: the project runs in the normal test run, and §9.3 shows no unexplained difference.

#### WP-M2-14: M2 documentation, measurements and close-out

- **Serves**: CLI-DOC-01 (exports), CLI-DOC-03 (Architecture), CLI-NF-02, CLI-RULE-01..04 (M2).
- **Depends on**: M2-13.
- **Changes**:
  - **`docs/CLI.md`.** The export verbs, the Folio single form, `--timeout`, and render
    troubleshooting. Exports in CI are described only as far as §9.1 showed them to work. Where
    an export's output goes: `-o`, `--out-dir`, or beside its document when neither is given (A16).
  - **`docs/CLI-Recipes.md`.** Export recipes in PowerShell, cmd and bash, the everyday one being
    an export written beside its document; the shell code hand-run as M1's was.
  - **`docs/Architecture.md`.** A *Headless rendering* section: the host, the shared bridge, and
    one export pipeline with two front ends.
  - **`docs/releases/vNext.md`.** The export verbs.
  - **Help.** The export verbs become visible.
  - **NF-02.** The first-export cost goes in §9.2.
- **Checks**: the three repository scripts with `-Check`, and the manual tests.
- **Done when**: every requirement in §8 is `verified` or `withdrawn`.

### Order at a glance

```
M0-05 (baseline; first, before any app change)
M0-01 → M0-02 → M0-03, M0-04                                     gate
M1-00 → M1-01 → M1-02 → M1-03 → M1-04 → { M1-05 → M1-06, M1-07..M1-14 }
        M1-15 → M1-16 → M1-17;  M1-18;  M1-19 last
M2-01 → { M2-02, M2-03 → M2-04 → M2-05 } → M2-06 → M2-07
      → M2-08 → M2-09, M2-10, M2-11, M2-12 → M2-13 → M2-14
```

---

## 7. Amendments to the requirements

Walked through one at a time with Paul on 2026-10-16. **Accepted** (A1–A4, A6–A12) were applied to
`docs/CLI-Requirements.md`, whose §2.1 records them. **Rejected** (A5, A13) were replaced by A5b
and A13b, also applied. **A14**, added on 2026-10-16 and accepted on 2026-10-26, replaces A3: no
GitHub Actions is used for this work. **A5c** and **A15** were decided on 2026-10-27, after M1-16
was built: A5c adopts A5 after all, for `unpack` and for `folio`'s folder form, and A15 makes
`folio` keep the order its documents were named in. The table keeps the rejected and superseded
rows so the reasoning stays findable.

|   # | Requirement            | Change                                                                                                                                                                                                                                                                               | Reason                                                                                          |
| --: | ---------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------- |
|  A1 | CLI-ARC-01, CLI-SP-01  | `Cli` and `WebRender` target `net10.0-windows10.0.19041.0` with WebView2's WinRT projection switched on. SP-01's "message-only parent window" becomes "a parent window the spike chooses, recorded in the plan". Add to SP-01: "WinRT activation works without the Windows App SDK". | F1, F2                                                                                          |
|  A2 | CLI-ARC-01, CLI-ARC-02 | Add `PaulTechGuy.MQ.Imaging` (Windows TFM, no WinUI), holding the WIC resizer behind `IImageResizer`. `Exporting` is created in M1 to receive `FolioShrinker`.                                                                                                                       | F7                                                                                              |
|  A3 | CLI-SP-03, CLI-DOC-02  | **Superseded by A14.** The Actions results are manual checks by Paul in a scratch repository. DOC-02's box becomes "runs green in a scratch repository".                                                                                                                                                    | F13                                                                                             |
|  A4 | CLI-VB-UNP-01          | The first box becomes "A single-file Folio unpacks; a folder or `.zip` exits 3 with guidance."                                                                                                                                                                                       | F9                                                                                              |
|  A5 | CLI-VB-UNP-01          | **Rejected, replaced by A5b; adopted after all as A5c on 2026-10-27.** Add: "`--overwrite` empties a non-empty target and then unpacks; without it a non-empty target exits 4. Refused even with `--overwrite` for a drive root, the profile folder, the current folder or an ancestor of it, or a folder containing the input."                            | F9                                                                                              |
|  A6 | CLI-VB-REN-01          | Add `--style sequential\|ones\|zeros`, defaulting to the app's suggestion per list. A list that cannot take `zeros` is left alone and reported.                                                                                                                                      | F8                                                                                              |
|  A7 | CLI-OUT-06             | Add: "A verb whose own options rewrite line endings or the final newline (`format` with `LineEndings`/`EofNewline`) decides them itself." The no-final-newline box names `number` rather than `format`.                                                                              | F11                                                                                             |
|  A8 | CLI-SET-03             | Add: "A raw `settings.json` is accepted too, as the app's Import accepts one."                                                                                                                                                                                                       | F14; this records behavior that already exists                                                  |
|  A9 | CLI-EX-05              | Add: "The app's own render timeout and degrade are unchanged; only the CLI fails the file."                                                                                                                                                                                          | F6                                                                                              |
| A10 | CLI-ARC-07             | "…through the same headless host the CLI uses, in a data folder of the app's own."                                                                                                                                                                                                   | Decision                                                                                        |
| A11 | CLI-VB-VER-01          | "Equals `AppVersion.Current`" becomes "equals the product version `Marqora.exe` reports from the same build" (`ProductVersion`).                                                                                                                                                     | F12                                                                                             |
| A12 | CLI-ARC-05             | "One test suite runs over both implementations" becomes "one suite runs over the shared bridge and transport that both implementations use".                                                                                                                                         | Hosting the WinUI preview in a test process is not practical; both hosts use the same transport |
| A5b | CLI-VB-UNP-01          | **Superseded by A5c.** The target must be missing or empty; a non-empty one exits 4, and `unpack` takes no `--overwrite`. Follows the app's own rule, which never writes over what is already there. | F9 |
| A13b | CLI-VB-CHK-01         | Web images and pictures outside the folder stay errors. One flag, `--ignore-external-images`, makes both information, shown only under `--verbose`. | F16 |
| A13 | CLI-VB-CHK-01          | **Rejected, replaced by A13b.** Add: "Web images and pictures outside the folder are information, shown with `--verbose`; they do not fail the run."                                                                                                                                                                 | F16                                                                                             |
| A14 | CLI-SP-03, CLI-DOC-02 | **Accepted 2026-10-26; replaces A3.** SP-03: the Actions-runner box becomes "Not run, by decision; exports on CI are documented as untested". DOC-02: the manual has a pre-commit hook and no GitHub Actions recipe at all; the hook is checked by installing it here. | No Actions minutes are to be spent, and a public repository would leave a permanent public record |
| A5c | CLI-VB-UNP-01, CLI-OUT-08 | **Accepted 2026-10-27; reverses A5b.** `--overwrite` replaces what is in a non-empty folder, for `unpack` and for `folio`'s folder form; without it the folder exits 4. The new contents are written beside it and swapped in, so a failure never leaves nothing. Refused even with `--overwrite` for a drive root, the profile folder, the current folder or one holding it, and a folder holding anything the run reads (an input, the Folio being unpacked, a picture the Folio collects); Paul confirmed keeping that last guard, 2026-10-27. | Paul's call: replacing last week's copy with this week's is the common case |
| A15 | CLI-IN-01              | **Accepted 2026-10-27.** `folio` keeps the order its documents were named in, each pattern's matches sorted where the pattern stands; every other verb keeps the ordinal sort. | §9.3: the order was the only difference from the app's Folios, which keep the ticked order |
| A16 | CLI-OUT-07             | **Accepted 2026-10-28.** An export given neither `-o` nor `--out-dir` writes each output beside its document, named after it with the format's extension; standard input still needs `-o`. An existing file is still refused without `--overwrite`. `docs/CLI.md` and `docs/CLI-Recipes.md` describe it when the export verbs are documented (M2-14). | Paul's call: `export pdf notes.md` should just make `notes.pdf`, as File > Export offers |

---

## 8. Traceability matrix

All 92 IDs from the requirements' §6 index, each exactly once. *WPs* lists every work package that
serves the ID, owner first. *Verified by* names the test class or the §9 record.

| ID             | Title                                 |   M   |  Pri   | WPs                        | Verified by                                               | Status |
| -------------- | ------------------------------------- | :---: | :----: | -------------------------- | --------------------------------------------------------- | ------ |
| CLI-SP-01      | Console renders and prints headlessly |  M0   |  Must  | M0-02, M0-01               | §9.1                                                      | verified |
| CLI-SP-02      | Cold start measured                   |  M0   |  Must  | M0-03                      | §9.1                                                      | verified |
| CLI-SP-03      | Non-interactive sessions tried        |  M0   |  Must  | M0-04                      | §9.1 (SSH run; Actions not run, A14) | verified |
| CLI-CMD-01     | Verb shape                            |  M1   |  Must  | M1-01                      | `CommandTreeTests` | verified |
| CLI-CMD-02     | Aliases                               |  M1   |  Must  | M1-01                      | `AliasTests` | verified |
| CLI-CMD-03     | Help with examples                    |  M1   |  Must  | M1-01, M1-19               | `HelpExamplesTests`, `ManualCoverageTests`                | verified (M1, M2-14) |
| CLI-CMD-04     | Usage errors                          |  M1   |  Must  | M1-01                      | `UsageErrorTests` | verified |
| CLI-CMD-05     | Response files off                    |  M1   | Should | M1-01                      | `UsageErrorTests` | verified |
| CLI-IN-01      | Files and globs                       |  M1   |  Must  | M1-03                      | `InputSetTests`, `FolioVerbTests`, `ShellGlobTests`, §9.3 (bash) | verified (M1) |
| CLI-IN-02      | Standard input                        |  M1   |  Must  | M1-03                      | `StdinTests` | verified |
| CLI-IN-03      | Base folder                           |  M1   |  Must  | M1-11, M1-03               | `ReportVerbTests` (check) | verified |
| CLI-IN-04      | Encoding                              |  M1   |  Must  | M1-03                      | `TextFileTests` | verified |
| CLI-IN-05      | Read-only refused for writing         |  M1   |  Must  | M1-04                      | `WriteModeTests` | verified |
| CLI-IN-06      | Missing and unreadable files          |  M1   |  Must  | M1-03                      | `InputSetTests` | verified |
| CLI-OUT-01     | Default leaves file alone             |  M1   |  Must  | M1-04                      | `WriteModeTests` | verified |
| CLI-OUT-02     | Several inputs need a mode            |  M1   |  Must  | M1-04                      | `WriteModeTests` | verified |
| CLI-OUT-03     | `--write`                             |  M1   |  Must  | M1-04                      | `WriteModeTests`, `KillDuringWriteTests` | verified |
| CLI-OUT-04     | `--check`                             |  M1   |  Must  | M1-04                      | `WriteModeTests` | verified |
| CLI-OUT-05     | `--dry-run` for text verbs            |  M1   |  Must  | M1-04                      | `DryRunDiffTests` | verified |
| CLI-OUT-06     | Encoding and line endings preserved   |  M1   |  Must  | M1-03                      | `TextFileTests` | verified |
| CLI-OUT-07     | Output files for exports and Folio    | M1/M2 |  Must  | M1-16, M2-08               | `OutputPlanTests` | verified (M1, M2-08) |
| CLI-OUT-08     | No overwrite without `--overwrite`    | M1/M2 |  Must  | M1-16, M2-08               | `OutputPlanTests`, `FolderReplacementTests` | verified (M1, M2-08) |
| CLI-OUT-09     | HTML to stdout                        |  M2   | Should | M2-09                      | `ExportHtmlTests` | verified |
| CLI-RPT-01     | Exit codes                            |  M1   |  Must  | M1-02, M1-19               | `ExitCodeTests`, `ManualTableTests`                       | verified (M1) |
| CLI-RPT-02     | Messages                              |  M1   |  Must  | M1-02                      | `OutputDisciplineTests` | verified |
| CLI-RPT-03     | Several files, one exit code          |  M1   |  Must  | M1-02                      | `RunAggregatorTests` | verified |
| CLI-RPT-04     | `--json`                              |  M1   |  Must  | M1-02                      | `JsonGoldenTests` | verified |
| CLI-RPT-05     | Schema versioning                     |  M1   |  Must  | M1-02                      | `JsonGoldenTests` | verified |
| CLI-RPT-06     | `--quiet` and `--verbose`             |  M1   | Should | M1-02, M2-08               | `ReporterTests` | verified (M1, M2-08) |
| CLI-RPT-07     | Color                                 |  M1   | Should | M1-02                      | `ReporterTests` | verified |
| CLI-RPT-08     | Cancellation                          | M1/M2 |  Must  | M1-02, M2-08               | `ExitCodeTests`, `CancelTests`, §9.1 (Ctrl+C) | verified (M1, M2-08) |
| CLI-SET-01     | Hermetic by default                   |  M1   |  Must  | M1-05                      | `SettingsSourceTests` | verified |
| CLI-SET-02     | `--settings app`                      |  M1   |  Must  | M1-05                      | `SettingsSourceTests` | verified |
| CLI-SET-03     | `--settings <file>`                   |  M1   |  Must  | M1-05                      | `SettingsSourceTests` | verified |
| CLI-SET-04     | Flags win over settings               |  M1   |  Must  | M1-05, M2-10               | `SettingsSourceTests`, `ExportPdfTests` | verified (M1, M2-10) |
| CLI-TH-01      | `--theme`                             | M1/M2 |  Must  | M1-06, M2-08               | `ThemeTests`, `ExportDryRunTests` | verified (M1, M2-08) |
| CLI-TH-02      | Which theme wins                      |  M1   |  Must  | M1-06                      | `ThemeTests` | verified |
| CLI-TH-03      | Unknown theme fails                   |  M1   |  Must  | M1-06                      | `ThemeTests` | verified |
| CLI-TH-04      | Light palette, one theme per run      |  M2   |  Must  | M2-13, M2-12               | `ExportPaletteTests` | verified (M2-13) |
| CLI-TH-05      | `themes list`                         |  M1   |  Must  | M1-06                      | `ThemeTests` | verified |
| CLI-TH-06      | Resolved theme shown                  |  M1   |  Must  | M1-06, M1-16, M2-08        | `ThemeTests`, `FolioDryRunTests`, `ReporterTests`, `ExportDryRunTests` | verified (M1, M2-08) |
| CLI-VB-FMT-01  | `format`                              |  M1   |  Must  | M1-07                      | `TextVerbTests` | verified |
| CLI-VB-NUM-01  | `number`                              |  M1   |  Must  | M1-07                      | `TextVerbTests` | verified |
| CLI-VB-UNN-01  | `unnumber`                            |  M1   |  Must  | M1-07                      | `TextVerbTests` | verified |
| CLI-VB-RPL-01  | `replace`                             |  M1   |  Must  | M1-08                      | `TextVerbTests` | verified |
| CLI-VB-REN-01  | `renumber-lists`                      |  M1   |  Must  | M1-09                      | `TextVerbTests` | verified |
| CLI-VB-DGL-01  | `diagram-layout`                      |  M1   |  Must  | M1-10                      | `TextVerbTests` | verified |
| CLI-VB-CHK-01  | `check`, links and images             |  M1   |  Must  | M1-11, M1-17               | `ReportVerbTests`, `OfflineTests` | verified |
| CLI-VB-CHK-02  | `check --style`                       |  M1   |  Must  | M1-11                      | `ReportVerbTests` | verified |
| CLI-VB-TOC-01  | `toc`                                 |  M1   | Should | M1-12                      | `ReportVerbTests` | verified |
| CLI-VB-OUT-01  | `outline`                             |  M1   | Should | M1-12                      | `ReportVerbTests` | verified |
| CLI-VB-UNP-01  | `unpack`                              |  M1   |  Must  | M1-14                      | `UnpackVerbTests`, `FolderReplacementTests` | verified |
| CLI-VB-VER-01  | `version`                             |  M1   |  Must  | M1-13, M1-18               | `VersionTests`, `New-Release.ps1` assertion               | verified |
| CLI-EX-01      | One browser per run                   |  M2   |  Must  | M2-08, M2-03               | `RendererFactoryTests` | verified |
| CLI-EX-02      | Data folder per process               |  M2   |  Must  | M2-08                      | `DataFolderTests`, `CancelTests` | verified |
| CLI-EX-03      | Export report becomes findings        |  M2   |  Must  | M2-08, M2-06               | `ExportFindingsTests`, `ExportJobTests` | verified |
| CLI-EX-04      | `--dry-run` for exports               |  M2   | Should | M2-08                      | `ExportDryRunTests` | verified |
| CLI-EX-05      | Render timeout                        |  M2   |  Must  | M2-08, M2-04               | `RenderTimeoutTests` | verified |
| CLI-EX-06      | Diagrams leave as light drawing       |  M2   |  Must  | M2-13, M2-05               | `DiagramOutputTests` | verified (M2-13) |
| CLI-EX-HTML-01 | `export html`                         |  M2   |  Must  | M2-09                      | `ExportHtmlTests`, §9.3 | verified |
| CLI-EX-PDF-01  | `export pdf`                          |  M2   |  Must  | M2-10                      | `ExportPdfTests`, §9.3 | verified (M2-10; paler emoji accepted, §9.3) |
| CLI-EX-WORD-01 | `export word`                         |  M2   |  Must  | M2-11                      | `ExportWordTests`, §9.3 | verified (M2-11) |
| CLI-FO-01      | Form from output path                 | M1/M2 |  Must  | M1-16, M2-12               | `FolioFormTests`, `FolioVerbTests`, §9.3 | verified (M1, M2-12) |
| CLI-FO-02      | Single file too large                 |  M2   |  Must  | M2-12                      | `FolioFormTests` | verified (M2-12) |
| CLI-FO-03      | Shrinking images                      |  M1   | Should | M1-15, M1-16               | `FolioShrinkTests`, §9.3 | verified |
| CLI-FO-04      | Web pictures only when asked          |  M1   |  Must  | M1-16, M1-17               | `FolioFetchTests`, `OfflineTests` | verified |
| CLI-FO-05      | Preflight as `--dry-run`              |  M1   |  Must  | M1-16                      | `FolioDryRunTests` | verified |
| CLI-FO-06      | Folio report                          |  M1   |  Must  | M1-16                      | `FolioFindingsTests` | verified |
| CLI-ARC-01     | Projects and dependency rule          | M1/M2 |  Must  | M1-17, M0-01, M0-02, M1-15 | `ArchitectureTests` | verified (M1) |
| CLI-ARC-02     | Move export assembly                  |  M2   |  Must  | M2-02, M1-15               | `ArchitectureTests`, §9.3 (M2-02, M2-06, M2-13) | verified (M2) |
| CLI-ARC-03     | Move paged engine                     |  M2   |  Must  | M2-03                      | §9.3 (M2-03, M2-06, M2-10) | verified (M2) |
| CLI-ARC-04     | Rendering interface                   |  M2   |  Must  | M2-05, M2-06               | `RendererRouteTests`, `BridgeSuite`, §9.3 (M2-05, M2-06) | verified (M2) |
| CLI-ARC-05     | One shell bridge                      |  M2   |  Must  | M2-04, M2-13               | `BridgeSuite`, `BridgeShapeTests`, `ShellBridgeTests` | verified (M2-13) |
| CLI-ARC-06     | App's exports unchanged               |  M2   |  Must  | M0-05, M2-01, M2-13        | §9.3 | verified (M2-06, M2-07, M2-13) |
| CLI-ARC-07     | App Word via detached render          |  M2   |  Must  | M2-07                      | §9.3                                                      | verified |
| CLI-NF-01      | Text verbs fast                       |  M1   | Should | M1-19                      | §9.2                                                      | verified |
| CLI-NF-02      | Cold start bounded                    |  M2   | Should | M2-14                      | §9.2                                                      | verified (M2-14) |
| CLI-NF-03      | Offline by default                    |  M1   |  Must  | M1-17, M1-16, M2-13        | `OfflineTests` (CLI and exports), §9.3 Sandbox | verified (M1, M2-13) |
| CLI-NF-04      | Missing WebView2 runtime              |  M2   |  Must  | M2-08                      | `MissingRuntimeTests` | verified |
| CLI-NF-05      | Platform                              |  M1   |  Must  | M1-18, M1-01, M0-01        | §9.1, §9.3 Sandbox                                        | verified (M1, M2-14) |
| CLI-PKG-01     | Beside the app                        |  M1   |  Must  | M1-18                      | `New-Release.ps1` assertion, `AssetLocationTests`         | verified |
| CLI-PKG-02     | PATH, opt-in                          |  M1   |  Must  | M1-18                      | §9.3                                                      | verified (M1) |
| CLI-RULE-01    | License headers                       | M1/M2 |  Must  | M1-19, M2-14               | `Add-FileHeaders.ps1 -Check`                              | verified (M1) |
| CLI-RULE-02    | American spelling                     | M1/M2 |  Must  | M1-19, M2-14               | `Set-AmericanSpelling.ps1 -Check`                         | verified (M1) |
| CLI-RULE-03    | Network claim                         |  M1   |  Must  | M1-19                      | `Test-NetworkClaim.ps1 -Check`                            | verified (M1) |
| CLI-RULE-04    | Release notes                         | M1/M2 |  Must  | M1-19, M2-07, M2-14        | Review of `vNext.md`                                      | verified (M1, M2-07) |
| CLI-DOC-01     | The manual                            | M1/M2 |  Must  | M1-19, M2-14               | `ManualCoverageTests`, `ManualExampleTests`, `ManualTableTests`, `ManualCommandTests` (with `docs/CLI-Recipes.md`) | verified (M1) |
| CLI-DOC-02     | A pre-commit hook recipe |  M1   | Should | M1-19                      | §9.3 (scratch repository; installed here by Paul), `ManualCoverageTests` | verified (M1; scratch repository, not installed here by Paul's decision) |
| CLI-DOC-03     | The rest of the docs                  | M1/M2 |  Must  | M1-19, M2-14               | Review, §9.3 (M2-14) | verified (M1, M2-14) |
| CLI-TST-01     | CLI tests                             |  M1   |  Must  | M1-00, M1-17               | `dotnet test` | verified |
| CLI-TST-02     | Export tests                          |  M2   |  Must  | M2-13                      | `dotnet test` | verified (M2-13) |
| CLI-TST-03     | Fixtures untouched                    |  M1   |  Must  | M1-00                      | `FixtureGuard` (assembly fixture) | verified |

---

## 9. Records

Filled in as the work happens. A requirement verified by a record is `verified` only when its row
here is complete.

### 9.1 Spike (M0)

| Item | Result | Date |
| --- | --- | --- |
| WinRT activation without the Windows App SDK: manifest-free / manifest / neither | **Manifest-free.** With `WebView2EnableCsWinRTProjection=true` the package copies the native `Microsoft.Web.WebView2.Core.dll` beside the executable and C#/WinRT activates it from there; no manifest was added. Mid-run, the process had loaded that DLL and the projection from its own folder and not one module from a Windows App SDK package, although several runtimes are installed on this machine. Windows Sandbox (§9.3, NF-05) is the confirming run on a machine with none. | 2026-10-16 |
| App files the linked paged engine needed beyond the three named | **Fewer, not more:** `PagedPrintHost.cs` and `ColorThemePayloads.cs` only. `PagedJobs` and `WebViewPrinting` were not needed: the spike calls `PagedPrintHost.ExportAsync` directly, without the classic fallback | 2026-10-16 |
| Parent window kind that lays out (a / b / c) | **(a) message-only**, first try, with Chromium's throttling left on. (b) and (c) were not needed; both remain in `ParentWindowKind`, and `--no-throttle` (`WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS`) was not needed either | 2026-10-16 |
| PDF shows light diagram, equation, colored code | **Yes.** `cheatsheet.md` → 9 pages: flowchart and sequence diagram drawn light, inline and display math typeset by KaTeX, the C# block highlighted, header and page numbers from the paged engine. Pages rendered with `build/ConvertTo-PdfPages.ps1` and inspected | 2026-10-16 |
| Exit 0, no `msedgewebview2.exe` left | **Yes.** Counted by command line (`Marqora-cli` in the user data path): peak 13 browser processes during a two-document run (two trees, render and print, F3), 0 after; run folder removed | 2026-10-16 |
| Ctrl+C → 130, no browser left | **The cancel path, yes:** canceled mid-render and mid-print through the `--cancel-after` hook, which cancels the same token the signal handler does: exit 130, 0 browsers, 0 run folders, no partial PDF. **A real Ctrl+C keystroke, by Paul in his own terminal during a run of `UltimateMarkdownContent.md`: exit 130, no browser left** | 2026-10-16 |
| `\| Out-Null` does not hang | **Yes:** exit 0 in 2.7 s, nothing left | 2026-10-16 |
| Start → first document rendered (ms) | **1,166–1,202 ms warm; 2,018 ms on the first run after a build** (shell ready at ~700 ms warm, 1,411 ms cold). `cheatsheet.md`, Debug build, dev machine | 2026-10-16 |
| Start → second document rendered (ms) | **2,404 ms** for `cheatsheet.md` a second time: 48 ms after the first finished printing, so the browser is reused. Printing it took 0.9 s against 1.2 s the first time. A large second document (`UltimateMarkdownContent.md`, 72 KB, 28 diagrams, 35 equations) took 3.5 s to render and 3.9 s to print, 54 pages | 2026-10-16 |
| Over SSH | **Fails: browser verbs are unsupported over SSH.** Run by Paul over OpenSSH Server into this machine (installed for the test with `dism`, since `Add-WindowsCapability` fails under PowerShell 7 with "Class not registered", then removed). The CLI starts, but WebView2 refuses the controller: `Invalid window handle (0x80070578)` with the message-only and the hidden-popup parent alike, at `CreateCoreWebView2ControllerAsync`. An SSH session runs on a non-interactive desktop, which WebView2 cannot attach to. Exit 5, no browser left. The data folder outlived the run's cleanup because the failed start never ended its browser; `HeadlessRenderHost.StartAsync` now ends it, and the next run swept the folder anyway | 2026-10-16 |
| Actions runner (Paul, scratch repo; log link) | **Not run, by decision:** no Actions minutes are to be spent, and a public scratch repository would leave a permanent public record of itself. Exports on CI are untested, and `docs/CLI.md` says so. A14, accepted 2026-10-26 | 2026-10-16 |
| Go / unsupported decision per environment | **Developer machine: go.** M2 may proceed as designed (gate passed). **SSH: unsupported for the browser verbs** (export and the single-file Folio); M2 goes ahead with that documented in `docs/CLI.md`, and the text verbs, which never start a browser, are unaffected. **Actions: not run (A14); exports on CI are untested** | 2026-10-16 |

Also from the spike, for M2:

- **The shell had no "render settled" message.** It records the moment (`state.renderSettled`) but never told the host, because a person prints a preview they have already watched settle. `webshell/app.js` gained `requestRenderSettled` / `renderSettled`, which answers once the current render and the output drawings are done. The app never sends it. WP-M2-04's `ShellBridge` must carry it.
- **`build/WebAssets.targets`** now holds the shell copy for both executables; `App.csproj` imports it. The App builds and its `Assets\web` is unchanged.

### 9.2 Timings

| Measure                                    | Figure | Bound                      | Date |
| ------------------------------------------ | ------ | -------------------------- | ---- |
| `format`, < 100 KB, start to exit (NF-01)  | **100 ms** median of 15 (98-102), `UltimateMarkdownContent.md` (72 KB), Release publish with ReadyToRun, warm disk, Ryzen 9 5900X; `cheatsheet.md` (8.5 KB) 91 ms | < 1 s                      | 2026-10-27 |
| `check`, < 100 KB, start to exit (NF-01)   | **144 ms** median of 15 (141-147), same document and machine; `cheatsheet.md` 127 ms | < 1 s                      | 2026-10-27 |
| First export added cost (NF-02)            | **1.44 to 1.82 s**: `export html` of `cheatsheet.md` 1,589 ms, `export pdf` 1,914 ms, `export word` 1,541 ms, against `format` of it at 99 ms, medians of 11, so the first export adds 1.49, 1.82 and 1.44 s: starting the browser, rendering, writing, and ending the browser. A second document in the same run adds little: `UltimateMarkdownContent.md` alone 3,023 ms, it and the cheatsheet 3,093 ms. Release build (`New-Release.ps1` of the working tree), Ryzen 9 5900X. The M0 figure is 1.17 to 1.20 s warm, start to first document rendered, so the bound is about 2.2 s, and all three are inside it | M0 figure + max(50 %, 1 s) | 2026-10-28 |
| App's first Word export added cost (M2-07) | 0.49 s to start the render browser (the app's log, Release, first Word export of the session); the export after it took 1.13 s. The spike's browser was ready in 0.7 s | report only                | 2026-10-28 |

### 9.3 Manual checks and comparisons

| Check                                                                                     | Result | Date |
| ----------------------------------------------------------------------------------------- | ------ | ---- |
| M0-05 baseline captured: file list and hashes | **Done.** Made by Paul with Marqora 1.0.13 (tag `v1.0.13`, `0568b2c`) from a detached worktree, `preferences.json` confirming the version: 20 exports, 30 files, 20,650,958 bytes, `MANIFEST.sha256` `4af2b21e0c999d77`; `Get-BaselineHashes.ps1 -Check` passes. **Two gaps, kept by decision** (README, *Known gaps*): no picture keeps its path, because `inputs/images/logo.png` is byte-identical to `docs/assets/marqora-logo.png` and the Folio de-duplicated them; and shrinking never happens, because the 1888-pixel screenshot's reduced copy was no smaller, so the shrunk Folios equal the plain ones | 2026-10-26 |
| Folio "reduce pictures" equals baseline after M1-15 | **Equal.** `folio -o red/ --shrink-images 1600` over the three inputs: every document and picture byte-identical to `folio.default.reduced.folder`, the screenshot declined as in the app. Proves only that the declined path is unchanged (see the M0-05 row); a picture actually being reduced is `FolioShrinkTests`' to prove | 2026-10-27 |
| Folio folder and zip, CLI vs baseline (M1-16) | **Equal.** Every document and picture in the folder and in the zip is byte-identical to `folio.default.folder` and `folio.default.zip`, and the findings are the app report's rows. First run: the CLI sorted its inputs by path, so the document order differed from the app's ticked order (cheatsheet, UltimateMarkdownContent, pictures). After A15, named in that order, the manifest matches apart from its time stamp, and the zip's entries come in the same order. No kept-path case in the baseline (see the M0-05 row); `FolioPlanner`'s tests carry that | 2026-10-27 |
| Unpack of the app-made `folio.default.html` (M1-14) | **Equal, two expected differences.** The documents the app packed, back out: `UltimateMarkdownContent.md` differs only by its byte order mark, which the app's Folio drops, and `pictures.md` has its picture links repointed to `media/`, as the Folio rewrote them | 2026-10-27 |
| Globs from bash give the same list (IN-01) | **Same.** `outline --json "docs/**/*.md"` from the repository root in PowerShell 7, Windows PowerShell 5.1, cmd, Git Bash and bash in WSL: 37 files from each, the same list in all five and the same as `git ls-files` for those paths. Bash's own expansion of an unquoted `docs/*.md` was run in WSL with the recipes | 2026-10-27 |
| Every verb under a firewall outbound block (M1: text verbs; M2: exports)                  | **M1 passed**, in Windows Sandbox with networking turned off (stronger than a firewall rule; `example.com` did not resolve), from the Release zip of this branch. All 13 verbs, 19 runs, each under a second, each with the expected exit code: 0, or 1 for `format --check` and `check` on a document with a dead link and a web picture. Folio zip and folder written, `folio.default.html` unpacked (3 documents, 2 pictures). `folio --fetch-web-images` gave up in 0.8 s with a warning, `Could not be fetched: ... the site could not be reached. [remote-image-failed]`, and still made the Folio, exit 0. **M2 passed** the same way on 2026-10-28, from a release zip of the working tree (`New-Release.ps1`, output kept out of `build\artifacts`): WebView2 runtime 155.0.4283.62 already in the Sandbox, `export html`, `export pdf` and `export word` of two documents each (a diagram, a picture, a web picture, math, code) and a three-document single-page Folio, all exit 0 in 3 to 7 s, `unpack` of that page exit 0; the HTML holds its drawn diagram, embedded picture, typeset math and highlighting, the page all three; the web picture reported, never fetched; no data folder left in `%TEMP%\Marqora-cli` | 2026-10-27, 2026-10-28 |
| Release output: two executables, one set of shared files, equal versions                  | **Done.** `New-Release.ps1` publishes `marqora-cli` separately and merges it into `app\`: 518 shared files byte-identical, 8 of its own (the exe, its dll, deps, runtimeconfig and pdb, `WebRender`, `System.CommandLine`), and both at v1.0.12 by file version and by `marqora-cli version`. The staged CLI runs from another folder | 2026-10-27 |
| Install with PATH; `marqora-cli version` in a new shell; uninstall restores PATH          | **Passed.** In Windows Sandbox, from the Release zip of this branch: `Install.cmd -AddToPath` exit 0, the user PATH gained exactly the install folder, stayed `ExpandString` and kept `%USERPROFILE%` unexpanded, and the manifest recorded `pathEntry`. A shell opened before the install did not find marqora-cli; a new one started by Explorer printed `1.0.12`. `Uninstall.ps1` exit 0, the folder gone, the entry removed, `ExpandString` and `%USERPROFILE%` kept, and a new shell no longer finds it. **The difference:** the PATH was `...\WindowsApps;` before and `...\WindowsApps` after, the trailing `;` lost (§11). **Run again with the fix**, the same release with the new `UserPath.ps1`: `...\WindowsApps;` became `...\WindowsApps;<install folder>;` and came back as `...\WindowsApps;`, exactly, `ExpandString` kept; the rest as before. Not on Paul's machine, whose installed Marqora is 1.0.13 while this branch builds 1.0.12 | 2026-10-27 |
| CLI starts in Windows Sandbox (no Windows App SDK)                                        | **Passed, by a different proof.** It starts in Windows Sandbox, but this Sandbox image has the Windows App SDK runtime as two packages that are part of Windows and cannot be removed, so the Sandbox alone does not show a machine without it. Instead: while running, the Release `marqora-cli.exe` loads 45 modules, all from `C:\Windows` or the release folder, none from an installed package and none of the Windows App SDK, and its `deps.json` names no Windows App SDK package. Holds for the M1 verbs, and for the exports (2026-10-28): part way through an `export pdf` of `UltimateMarkdownContent.md`, the Release build had 84 modules loaded, none from an installed package and none of the Windows App SDK, and the only one from outside Windows and the release folder was the WebView2 runtime's own `EmbeddedBrowserWebView.dll`, which every export needs | 2026-10-27, 2026-10-28 |
| DOC-02 pre-commit hook passes clean, blocks a bad commit (A14) | **Passed in a scratch repository**, not this one: the hook copied out of `docs/CLI.md`, with a Release publish on PATH. A formatted file with good links committed; an unformatted list (exit 1, "would change") and a dead link (exit 1, `broken-link`) were refused; a commit with no markdown, and a file named with a space, went through. **Not installed in this repository, by decision (Paul, 2026-10-27):** 32 of its 47 markdown files would fail `format --check` and 15 have `check` findings, among them `UltimateMarkdownContent.md` and `docs/cli-tour/Guide.md`, which are unformatted on purpose and must never be formatted, so the hook would refuse nearly every commit that touches a document and its message would point at `format --write`. `ManualCoverageTests` holds the manual to no CI workflow | 2026-10-27 |
| `docs/CLI-Recipes.md`, added at Paul's request after M1-19: the shell code run by hand | **Run, every recipe.** Every recipe was run in a scratch Git repository with a Debug build on PATH: in PowerShell 7.6, in Windows PowerShell 5.1 (after the UTF-8 line the page gives), in cmd and in Git Bash. Each gave what its text says, including accented headings, CRLF files, a file name with a space, an empty file list, and exit 4 on a second dated Folio the same day. **WSL, run 2026-10-27** (Ubuntu, WSL 2, Debug build on the Windows PATH): the `marqora-cli.exe` link ran by the short name and inside `xargs`; on `/mnt/c`, a quoted `**` pattern, bash's own glob, pipes into `grep`, standard input, exit codes, LF output, an accented heading, the git-and-`xargs` recipe on a name with a space, the `awk` contents recipe and a reading order from a CRLF file all as the page says; `folio -o "$(wslpath -w ...)"` wrote to `\\wsl.localhost\Ubuntu\...`, and documents kept on the Linux side worked by relative path. One sentence was wrong: a Linux absolute path is not meaningless but read as a Windows one, so `/tmp/x.zip` was written to `C:\tmp\x.zip` without an error; the page now says so (§11). **Clipboard, run by Paul 2026-10-27** in a PowerShell 7.6 console: `Get-Clipboard -Raw | marqora-cli format - | Set-Clipboard` formatted the text but pasted `Café` as `Caf├⌐`, and `toc README.md | Set-Clipboard` was right (no accents). The cause is PowerShell, not marqora-cli, whose bytes are `C3 A9` whatever the code page: PowerShell 7 decodes a native program's output with `[Console]::OutputEncoding`, `ibm437` here, whenever it goes into a command or a variable. After `[Console]::OutputEncoding = [Text.UTF8Encoding]::new()`, `Café` pasted correctly. The page had said PowerShell 7 needs nothing, true only for `>`; the earlier PowerShell 7 runs went through a session with no console and could not show it. The recipes' *Accented letters* and the manual's troubleshooting now give the line for PowerShell 7 too. **`chcp 65001`, run by Paul 2026-10-27** in cmd with the Debug build (1.0.12): `toc` on a file with `## Café` and `## Résumé` showed `Caf├⌐` and `R├⌐sum├⌐` in code page 437, and the accented letters correctly after `chcp 65001`, as the page says. **Task Scheduler, run 2026-10-27:** a temporary task with a weekly trigger starting `pwsh.exe -NoProfile -File Make-Handbook.ps1`, run on demand and then deleted. With marqora-cli found by full path, Last Run Result 0 and the dated zip made, the missing picture in `folio.log`; run again the same day, 4 and "It already exists". With marqora-cli not on PATH, the page's first script gave **0, no zip and no log**, because a command that is not found leaves `$LASTEXITCODE` empty; the page now wraps the command in `try`, and the same run gives 1 with the reason in `folio.log`. `ManualCommandTests` parses every marqora-cli command in the page's shell blocks; three options broken on purpose were each caught | 2026-10-27 |
| M2-01: `Compare-ExportBaseline.ps1` reports no difference for a second export of the baseline from the unchanged app | **Passed for the rows a second export could test.** Paul exported the whole set again from 1.0.13 on 2026-10-27. It showed three things. (1) The baseline's "default" rows had been made in **Forest**: every one carries Forest's colors, the second set Default's; they were renamed `*.forest.*`, hashes kept (head of M2). (2) The script missed two things a real export varies in: mermaid's session counters (`mq-diagram-N`, `actorN`, `root-N`), now renumbered by first appearance, and the Folio's dated `<title>`, now masked like its folder name. After that the three Vintage Press exports, the one part made in the same theme both times, compared the same, the PDF as pictures. (3) The second set's Folios held a fourth document, the README open in a tab; the README now says to close every other tab. During that run a save dialog wrote a fresh `cheatsheet.forest.pdf` over the baseline's; it was restored from Paul's copy outside the repo, checked against every recorded hash, and the script now runs `Get-BaselineHashes.ps1 -Check` before it compares. The Forest rows and the Folio masks are checked by the first Forest set (head of M2). Before that, the script was built and tested (README, *Comparing a fresh set*). Against copies of the baseline: itself, 20 the same; a copy carrying what a second export changes (new Word relationship ids, a new name for its properties part and new times; new PDF dates; a new Folio export time inside the single-file sources, in the zip's and the folder's manifests, a new dated folder name and new zip entry times; a new Preferences export time), 13 files different in bytes and all 20 reported the same; the same copy with one real change of each kind (a title in HTML, a word in Word, the other theme's PDF, two zip entries swapped, a picture gone from the folder form, the version in the single-file Folio's sources, a setting in Preferences), each reported, with the place. A PDF differing only in its producer name compared the same as pictures. The same PDF rendered twice gives identical pages, so the PDF tolerance (a channel may move 16 of 255, a page may have 0.05% of its pixels differ) is for real exports, not for the renderer. | 2026-10-27 |
| M2-02: HTML and Folio vs baseline                                                         | **Moved; matches the baseline** (the app session of 2026-10-28, recorded under M2-06; it was to be the session after M2-06) (head of M2). The seven classes are in `Exporting` (renamed, so their history follows), which builds as plain `net10.0` with no Windows reference. The `Windows.UI.Color` seam needed no new type: `RenderedHtmlPackager` used `Windows.UI` only for `AccentColors.LightHex`, which is `DocumentAccent.LightHex` in Domain, so it reads that. `HtmlPageLayout`, `DiagramHtmlDocument`, `ColorThemePayloads` and `DiagramViewer` became `public`, as the app and `WebRender` use them; `WebRender` references `Exporting` instead of linking `ColorThemePayloads.cs`. App, CLI and solution build with no new warning, and every test passes. | 2026-10-27 |
| M2-03: PDF vs baseline; one print to Microsoft Print to PDF                               | **Moved; the PDF matches the baseline and the print is right** (the app session of 2026-10-28, recorded under M2-06; it was to be the session after M2-06) (head of M2). `PagedPrintHost`, `PagedJobs` and `WebViewPrinting` are in `WebRender` (renamed, so their history follows), and the M0 link and `InternalsVisibleTo` are gone. The static environment became `PagedPrintHost.Configure(Func<Task<CoreWebView2Environment>>)`, called once: the app passes `BesideUserDataFolderAsync`, which is the old `-Print` code unchanged, at the top of `Program.Main` where it sets the preview's folder, and the spike passes the same; marqora-cli will pass the headless host's in M2-08. Public only where the app or the CLI uses a type: `PagedPrintHost`, `PagedHostMode`, `PagedPrintResult`, `PagedJobs`, `PdfJob`, `WebViewPrinting`. The app references `WebRender`; `ArchitectureTests` still finds no path from the CLI to the app or from `WebRender` to WinUI. Solution builds with no new warning; every test passes. `New-Release.ps1` still finds every shared file identical: the CLI now has 6 files of its own and 520 shared, `WebRender.dll` and its pdb among them | 2026-10-27 |
| M2-04: the app behaves as before with the shared bridge | **Bridge built; the app behaves as before** (the app session of 2026-10-28, recorded under M2-06; it was to be the session after M2-06) (head of M2). `ShellBridge`, `IShellTransport` and `CoreWebView2Transport` in `WebRender` own the eight request-and-reply pairs both hosts make of the shell (rendered and finished markup, print markup, diagram PNG and SVG, equation PNG, print readiness, render settled), their payloads, reply fields and matching by id; `BridgeOutcome<T>` is `Rendered`, `TimedOut` or `Failed`. The preview keeps each request's timeout, degrade and warning (10 s and empty markup, 30 s and the unfinished markup, 20 s and nothing); a post with no shell to post to now fails at once instead of waiting out the timeout, with the same result. The headless host throws on a timeout as before. `BridgeShapeTests` scan every file that loads `ShellUri` and fail on a request name or a reply `case` outside the bridge (a reply case put back into the preview was caught); the cheatsheet and diagram windows load pages of their own and are outside it. `ShellBridgeTests`, 7, over a recording transport. The spike printed `cheatsheet.md` (9 pages) and `UltimateMarkdownContent.md` (54 pages) through the bridge and the moved engine, exit 0, no browser left. | 2026-10-27 |
| M2-05: both renderers implement `IDocumentRenderer`; every app export goes through it | **Built; the app exports through it unchanged** (the app session of 2026-10-28, recorded under M2-06; it was to be the session after M2-06) (head of M2). `IDocumentRenderer`, `RenderRequest` and `RenderedDocument` are in `Abstractions/Rendering`, and `BridgeOutcome` moved there from `WebRender` so that export code built for any platform can read it. The preview implements it for the document on screen, handing over the markup it already has, as the exports always took it; `IPreviewHost` lost the three members only an export used (`RenderForExportAsync`, `RequestMathPngAsync`, `ExportPdfAsync`), and the app's four exports and the Word export's picture requests call the renderer, each keeping its old degrade (empty markup, the unfinished Folio document, a missing picture). The headless host renders a document as its one tab and keeps it open for the diagrams, equations and PDF asked of it next; it renders the markdown itself (`IMarkdownRenderer`), sets the request's theme for screen and export, serves the document's own pictures, and prints through the same paged engine with the classic engine as fallback. Shared with the preview rather than copied: `DocumentFiles` (serving a document's folder) and `ShellAnswers` (reading a diagram or equation picture, and the PDF's report rows). `RendererRouteTests`, 10: the app's export methods name `_documentRenderer` and never `_host`, `IPreviewHost` offers nothing only an export asks for, both hosts implement the interface, nothing in `Exporting` names `IPreviewHost`. The spike now goes through `RenderAsync` and `PrintPdfAsync`: `cheatsheet.md`, `UltimateMarkdownContent.md` (its 2 drawn-as-error diagrams reported) and `pictures.md`, paged engine, exit 0, no browser left. The headless PDF of `pictures.md` shows the picture beside the document, which the spike's earlier PDFs could not, and the two outside the document's folder as "not shown", exactly as the app's own PDF does | 2026-10-27 |
| M2-06: all four app exports vs baseline (before ARC-07)                                   | **Passed: all six exports match the baseline, and the print and both copies are right** (Paul's app session, 2026-10-28, Release build of `feature/CLI` with M2-01 to M2-06 uncommitted, Forest). `cheatsheet.forest.html`, `.docx` and the three Folio forms are the same, and `cheatsheet.forest.pdf` the same as pictures; report `tests/baselines/fresh/after-M2-06.comparison.md`. The print to Microsoft Print to PDF has the same nine pages as the export, light, with its diagrams drawn; Copy as PNG and Copy as SVG were checked by Paul. This stands for M2-02 to M2-05 as well (head of M2). **The comparison script learned five things on the way**, all from the Folio's single page, the first time two renders of `UltimateMarkdownContent.md` were compared (Run A had made no `folio.vintage-press.html`): mermaid places rough.js's control points along a shape's sides at random, a gantt chart draws its "today" line where today is, and a gitGraph commit with no id, a state diagram's divider and an Iconify icon each get random ids. `Compare-ExportBaseline.ps1` masks those in HTML and keeps every endpoint; a copy with one diagram label changed and one with one endpoint moved are both still reported. The Word and HTML exports of the same document will need them when the CLI exports it (M2-09). In building it: `HtmlExportJob`, `PdfExportJob`, `WordExportJob` and `FolioPageJob` are in `Exporting`, each handed its renderer per run (so M2-07 can hand Word another) and answering with `ExportOutcome(Outputs, Issues)`, plus the PDF's engine and, when nothing was written, how the render ended. `MainViewModel`'s four methods are now dialogs, the job, and the report or status line, worded as before. Three choices made in building it: **(1)** the diagram rows stay with the renderer (`ShellAnswers.PdfIssues`, shared by both hosts since M2-05), because they are read from the markup it printed; the job adds the picture rows it took over from `PdfPictureIssuesAsync` and sorts both by line. **(2)** The PDF job renders before it prints, which the headless host needs; the preview hands over its markup (one extra fetch) and prints whatever it shows even when that render does not answer, as the app always has. **(3)** The Word export's picture requests come back to the context the job started on, as the view model's dispatcher posts did, so the preview is still spoken to only from the UI thread. HTML's pictures too large to embed are now rows; the app's dialog lists them as before. `ExportJobTests`, 6, over a recording renderer; `RendererRouteTests`, 12: each app export runs its job with `_documentRenderer`, the view model keeps none of the moved work, every job takes `IDocumentRenderer` per run and no host in its constructor. The spike now prints through `PdfExportJob`: the same three inputs, paged engine, exit 0, no browser left, with the rows in line order (`UltimateMarkdownContent.md` 5: two remote, one not found, two drawn as errors; `pictures.md` 3) | 2026-10-28 |
| M2-07: Word identical with preview busy / other tab / idle; baseline difference explained | **Passed** (Paul's app run, 2026-10-28, Release build with M2-07 uncommitted: `cheatsheet.md`, Forest, the first Word export of the session). The app started its own browser (0.49 s, §9.2) and the file matches the baseline in its text, styles and two of its three pictures; the third, the flowchart's PNG, differs in 28 of its 107,880 pixels, two one-pixel columns at the back edges of its two arrowheads, and looks the same. The spike's file, made with the baseline's preferences rather than the reader's, matched that PNG exactly; the difference was not pursued further. `ExportRenderHost` (App) starts a `HeadlessRenderHost` on the first Word export, in `WebView2-Render` beside the preview's data folder and a message-only window, keeps it for the session, replaces it if its browser has gone, and ends it at shutdown beside the paged engine's; if it cannot start, the export logs why and uses the preview, as before. The preview's preferences reach it through `ShellPreferences`, now the one `applyPreferences` message for both hosts. **Busy, other tab, idle:** identical by construction, since the export no longer asks the preview anything. The renderer renders the exported text itself, so nothing the preview is doing is read; not run by hand. **Baseline difference:** the spike, given the baseline's `preferences.json` and Forest (`--settings`, `--theme`), made the three documents' Word files through `WordExportJob` and a `HeadlessRenderHost`, as the app now does: `cheatsheet` and `pictures` are the same as the baseline. `UltimateMarkdownContent` differs in three parts, all explained: a sequence diagram's stick-figure ids count from 0 in a fresh browser where the 1.0.13 session's preview had reached 6 (the change this package makes), and the PNGs of the gantt chart, whose "today" line has moved since 2026-10-26, and of the diagram whose SVG differs only in Iconify's random ids. `Compare-ExportBaseline.ps1` now masks the random parts in a Word package's SVGs as in HTML. `docs/WordExport.md` §7 and the release note are written | 2026-10-28 |
| M2-08: CLI export infrastructure (its tests; recorded for the choices made in building it) | **Built; the tests pass** (2026-10-28). `export pdf` runs through the shared export run (`Verbs/ExportVerb.cs`, `PdfFormat`); `export html` and `export word` keep refusing until M2-09 and M2-11, and all three stay hidden from help until their comparisons are recorded. **One browser:** `RendererFactory` starts a `HeadlessRenderHost` on the first export, on a render thread of its own (STA, pumping), and hands its environment to the paged engine, so a run that prints is one browser tree; a browser that has gone is replaced for the next file. **Data folder:** `%TEMP%\Marqora-cli\<pid>-<start>-<8 letters>`, the letters so two runs in one process never share one; removed after the browser exits, Ctrl+C included, and swept by a later run once its process is gone. **Timeout:** `--timeout`, default 60; a render that does not finish fails its file with exit 5 and the run moves on. The CLI wraps the renderer (`StrictRenderer`) rather than changing the jobs, so the app keeps its degrade. **Runtime:** looked for before anything starts; missing, it is exit 5 with the install link, once for the run. **Outputs:** `-o` for one document, `--out-dir` for any number, or neither, when each goes beside its document (A16, Paul's call after the build); each is written beside its name and moved onto it only when complete, and anything a canceled print finishes writing is removed after the browser has gone. **Findings:** export report rows become warnings (advisories information) on their lines, code from the row's problem (`not-found`). **Dry run:** no browser; prints each output, the theme and its source, the page setup and layout, and as findings the missing pictures and unresolved links `check` finds. The PDF's title and furniture now come from `PaperFurnitureFor` (`Rendering`), which the app's two helpers call too, so both read a document's front matter the same way; the PDF is titled by the file name with its extension, as the app titles it. **Tests**, 15 new (CLI 317): `RendererFactoryTests` (a `format` run never makes a factory; ten exports start one browser, gone after), `DataFolderTests` (none left, a stale one swept; two runs at once as two processes), `CancelTests`, `RenderTimeoutTests`, `MissingRuntimeTests`, `ExportFindingsTests`, `ExportDryRunTests`, and export cases in `OutputPlanTests` and `ReporterTests`. **Departures from the package as written:** the never-finishing render and the missing runtime are in-process fakes (the test-hooks row prefers them), so no `MQ_TEST_HOOKS` option exists; Ctrl+C is the run's own cancellation token, which the signal handler trips, rather than a signal sent by a launcher, since a test host cannot send a console Ctrl+C to one process alone. Found in building it: the test project had the classic .NET `Microsoft.Web.WebView2.Core.dll` where the CLI has the native WinRT component of that name, and in-process the runtime looked missing ("class not registered"); the test project now asks for the WinRT projection too | 2026-10-28 |
| M2-09: `export html` vs the app's HTML: `cheatsheet` in Forest and Vintage Press, `UltimateMarkdownContent` and `pictures` in Forest | **Passed** (2026-10-28; the CLI from the working tree, `--settings tests/baselines/cli/preferences.json`; report `tests/baselines/fresh/m2-09-cli.comparison.md`). Both cheatsheet pages are byte for byte the app's. `UltimateMarkdownContent` is the same once a sequence diagram's stick-figure ids are renumbered, a session counter like the diagram ids the script already renumbers (6 in the app's session, 0 in a fresh browser); `Compare-ExportBaseline.ps1` now renumbers them too. `pictures` differs in one attribute: the app's page carries `class="mq-active-block"` on the paragraph its cursor was in, the preview's caret bar, which a browser draws beside that paragraph when the page is opened (it is hidden only in print). The CLI's page has no cursor and is otherwise identical; the app's leak is in §11. The page is the app's job and packager; the packager reads the export theme from the settings, so the CLI hands it a read-only settings object (`FixedSettings`) whose export theme is the one the run resolved, and `--theme` wins for the palette as it does for the render. The page width is the settings source's `PreviewMaxWidth`. `-o -` writes the page to stdout through a file in the temp folder, the same bytes as the file (`ExportHtmlTests`), and under `--json` the page is the line's `text`; PDF refuses `-o -` with exit 2. **Found on the way:** a run whose document asked for any picture, served or missing, waited 5 seconds for its browser to exit after closing it, then ended it with a warning, on every such run. `HeadlessRenderHost` now waits one second and ends it without a word; `pictures.md` went from 6.4 s to 2.2 s. Why the browser stays is not known; it is in §11 | 2026-10-28 |
| M2-10: `export pdf` vs the app's PDF, as pictures: `cheatsheet` in Forest and Vintage Press, `UltimateMarkdownContent` and `pictures` in Forest | **Verified; one difference accepted** (2026-10-28; the CLI from the working tree, `--settings tests/baselines/cli/preferences.json`; report `tests/baselines/fresh/m2-10-cli.comparison.md`). Both cheatsheets and `pictures` are the same as pictures. `UltimateMarkdownContent` differs on 3 of its 54 pages: page 32 is the gantt chart's "today" line, which has moved since 2026-10-26 and is expected; pages 6 and 52 draw some color emoji paler (on page 6 the test tube, the rainbow flag and the astronaut; on page 52 the check and cross marks across the flavor table), 0.12% and 0.84% of their pixels, and nothing else on them differs. **Not explained.** It is not the CLI's paged engine sharing the render browser: printing in a browser of its own gave the same pages. It is not the WebView2 runtime having moved since the baseline either, as far as one comparison shows: the M0 spike's PDF of the same document, made this morning by a headless host with a print browser of its own, matches the baseline on both pages, apart from the header it printed and the baseline's settings leave off. Paul accepted it as a known difference on 2026-10-28; the cause stays in §11 in case it comes back. **Flags:** `--paper letter|a4|legal`, `--orientation portrait|landscape`, `--margin normal|narrow|moderate|wide|none`, `--backgrounds` and `--no-backgrounds` (never both), each ignoring case and laid over the settings source's PDF setup, so `--dry-run`, `--verbose` and the export see one setup; the cover, contents and header and footer stay the settings' alone. If the classic engine prints the file, an advisory finding says so, as the app's status line does. `ExportPdfTests`, 11, through a real browser and read back with Windows' own PDF engine: the page size for each paper and for A4 landscape, where the text starts for each margin, `--paper A4` over a settings file naming Letter, and the two background flags together exiting 2; the classic-engine advice in `ExportFindingsTests`. **Spike retired:** `Spike.cs`, its `__spike-pdf` command and the `MQ_SPIKE` define are gone; `Program.Main` is `CliApp.Run` alone. Finding codes now take the report row's first clause without punctuation (`the-diagram-could-not-be-drawn`, `outside-the-documents-folder-so-not-shown`) | 2026-10-28 |
| M2-11: `export word` vs the app's Word file: `cheatsheet` in Forest and Vintage Press, `UltimateMarkdownContent` and `pictures` in Forest | **Verified** (2026-10-28; the CLI from the working tree, `--settings tests/baselines/cli/preferences.json`; report `tests/baselines/fresh/m2-11-cli.comparison.md`). Against the 1.0.13 baseline, `word/document.xml`, every other XML part and every SVG are the same in all four, and `pictures` is the same throughout. The only differences are in diagram PNGs: the cheatsheet's flowchart, and six pictures in `UltimateMarkdownContent`. Against the app as it is today (`m2-07-app`, the app's Word export through its own headless host), the cheatsheet is the same, PNGs included, so those are the drift M2-07 already recorded and not the CLI's. In `UltimateMarkdownContent`, against M2-07's headless export, five of the six PNGs differ by 0 to 56 pixels; the sixth is the gantt chart, whose "today" line has moved (0.48% of its pixels), as on page 32 of the PDF. **Math:** Word's file has the equation as Word's own (`m:oMath`, from the MathML KaTeX wrote), and a picture only of an equation Word cannot write, which is the app's behavior; CLI-EX-WORD-01's "math appear as images" is read that way. **No flags:** the page (`docxSetup`) and the layout come from the settings source alone, as the requirement says. `ExportWordTests`, 3: a real export carrying a diagram picture, a Word equation and a colored keyword; an A4 landscape `docxSetup` giving that page; the dry run's setup line. `PendingVerb` is gone, with nothing left pending. Finding codes now stop at a colon too, so Word's "Raw HTML: its text ..." findings are `raw-html` | 2026-10-28 |
| M2-12: `folio`'s single page vs the app's: the three documents in Forest, Forest with shrinking at 1600, and Vintage Press | **Verified for Forest; Vintage Press compared in part** (2026-10-28; the CLI from the working tree, `--settings tests/baselines/cli/preferences.json`, the documents in the app's order; report `tests/baselines/fresh/m2-12-cli.comparison.md`). `folio.forest.html` and `folio.forest.reduced.html` are the same as the app's. `folio.vintage-press.html` differs only by ids the app numbers across its session, as far as the script was taken: the app made that page late in a session of exports, the CLI in a fresh browser. The script learned three of them (a diagram's light drawing, `mq-output-diagram-34` against `-1`; a class diagram's `classId-Document-8` against `-0`; a state diagram's divider count), and the masks leave the M2-09 report unchanged. The next, at line 4408 of 5458, is a mindmap's `node-` ids, which do not renumber cleanly as a session counter; the rest of that file was left uncompared (§11) rather than hunted, as the theme is the only difference from the Forest page that matches. **Changes:** `folio -o x.html` goes through the app's `FolioPageJob`, each document finished by `RenderDetachedAsync` in the run's own browser, in the resolved theme, at `PreviewMaxWidth`, numbered by the settings; written beside its name and moved onto it. Over `HeavyPageBytes` (50 MB as the page carries it, after fetching and shrinking) the run exits 4 before a browser starts, naming the zip's size, `--form zip` and `--shrink-images`. **New `folio --timeout`** (default 60): CLI-EX-05 says every render, so a document that does not finish fails the page with exit 5 and nothing is written, where the app puts it in unfinished. `FolioFormTests`: the "later release" test replaced by three: a real page with a drawn diagram and an embedded picture that `unpack` takes back apart; a 40 MB picture refused with exit 4, no browser made and no file; a never-finishing document giving exit 5 and no leftovers. `docs/CLI.md`'s folio section and troubleshooting, and the vNext notes, no longer say the page comes later | 2026-10-28 |
| M2-13: the export verification suite | **Built** (2026-10-28). **`tests/PaulTechGuy.MQ.Exporting.Tests`** now runs a real browser: x64, the WebView2 projection asked for by name and the shell's files imported through `WebAssets.targets`, as marqora-cli has them. A `ShellFixture` starts one `HeadlessRenderHost` for the collection, as marqora-cli starts it, on the `RenderThread` that moved from the CLI to `WebRender/Win32` for it (one copy, two users). **`BridgeSuite`** (A12), 5: a render finished with its diagram, math and highlighting; a diagram by hash as SVG and PNG; an equation by line as a picture; an unknown hash answered with nothing rather than left waiting; markup finished detached, leaving the open document's diagram in place. **`DiagramOutputTests`**: the drawing in the rendered markup (`outputMarkup`) is the one the SVG request hands over (`outputSvgOf`): same id, labels and shapes; with no screen, the headless host's screen and output drawings are one, so a dark window's choice between them stays the shell's own. **`OfflineTests` (exports)**: `HeadlessRenderHost` now sees every request its page makes and counts any outside `marqora.assets` and `marqora.document` (logged as a warning, left to the browser as the preview leaves it); rendering and finishing detached `UltimateMarkdownContent`, the cheatsheet and `pictures` counts zero. The shell's content policy is what stops such requests, so no document can drive the count up and the test can only show it stays at zero. **`ExportPaletteTests`** (CLI), 16: every theme's HTML export, fixed to `data-theme="light"`, declares each light slot with its value and carries no dark value its shell stylesheets do not also hold (a first, value-only version failed on app.css's own grays, which some themes share as dark text: `#e6e6e6` in its dark screen rules, since stripped, and `#8a8a8a`, its light tertiary text); a Folio's page declares each slot once, with the run's theme; an HTML, PDF or Word export and a single-page Folio add their output and nothing else. Firewall record and ARC-06 above. All suites pass: CLI 354, Exporting 19, every other project as before | 2026-10-28 |
| M2-13 (Paul's follow-up): `New-DevBuild.ps1` publishes `marqora-cli` | **Done** (2026-10-28). Found building the Sandbox run: a dev zip had no CLI. Paul: a full local build must build the CLI too, as it now shares much of the app's code and a CLI that stops compiling has to be noticed. The CLI publish, the shared-file comparison and the version check moved out of `New-Release.ps1` into `ReleaseCommon.ps1`'s `Add-CliToPublish`, which both scripts call; the dev build passes its own version to the CLI too. A dev build of the working tree: `[3/5] publish marqora-cli ... ok (v1.0.13-cli.m2, 6 files of its own, 525 shared)` | 2026-10-28 |
| M2-13 (Paul's follow-up): pages leaving the app no longer carry app.css's dark rules | **Done** (2026-10-28). Found by `ExportPaletteTests`: every HTML export, Folio page and rich-text copy carried app.css's dark-screen rules though each is fixed to `data-theme="light"`. Paul: strip them, since nothing is exported in dark. `RenderedHtmlPackager.WithoutDarkScreen` takes out the dark root named in the print block's selector list, the dark root's own rules, and the `@media screen` block left empty; a diff of the cheatsheet's page before and after shows only those lines gone. `ExportPaletteTests` now also asserts no dark rule remains and the print block still applies to the light root. `Compare-ExportBaseline.ps1` removes the same rules from both sides (`Get-WithoutDarkScreen`), so the 1.0.13 baseline still compares: the full set re-run gives the same seven explained differences as before. `docs/Architecture.md`'s HTML paragraph says so | 2026-10-28 |
| M2-14: M2 documentation, measurements and close-out | **Done** (2026-10-28). **Help:** the export verbs are visible; `CommandTreeTests` now holds them in `--help` and `export --help` (it held them out until M2). **`docs/CLI.md`:** the introduction and tour name the exports; *Exports: where they go, and what they need* (beside the document, `-o`, `--out-dir`, exists and `--overwrite`, the report as findings, `--dry-run` with real output, the run's browser, `--timeout`, a desktop needed, SSH exit 5, not tried as a scheduled task or on CI, what the settings decide); a section each for `export html`, `export pdf` and `export word`; Settings, Themes, `themes list` and the JSON fields and outcomes widened to the exports; troubleshooting for exit 5 in its four forms and for an export that differs from the app's. Its tables were aligned by `format --write`, and `check` finds nothing. **`docs/CLI-Recipes.md`:** an *Exporting* section: a PDF beside every document, exports in a dated folder of their own, and export only what has changed, each run by hand in PowerShell 7.6, Windows PowerShell 5.1, cmd and Git Bash with the Debug build on PATH: all exit 0, the second plain run exit 4 as the recipe says, the change-only pass finding exactly the two documents made stale and nothing on a second pass; the dated folder in a `.cmd` file (the prompt form is the same line as M1's dated Folio, which was run at a prompt). **`docs/Architecture.md`:** *Headless rendering* (the host, the one bridge, one pipeline with two front ends, a run's browser); *Exporting* no longer names `IPreviewHost` calls that are gone, and says Word's browser artifacts come from the export's own browser; the Layers table gains `Exporting`, `Imaging`, `WebRender` and `Cli`. **DOC-03:** README's *Command line* (exports, a PDF example) and project layout, the site's card, the Welcome document's paragraph, `docs/Folio.md` (the single page, the size refusal); `docs/Installer.md` needed nothing. **`vNext.md`:** the exports, and no "later release". **NF-02** and **NF-05** measured, §9.2 and above. Every row of §8 is `verified` or `withdrawn` | 2026-10-28 |
| CLI vs app: HTML, PDF pages, Word parts, single-file Folio (M2-09..M2-12)                 | **No unexplained difference** (WP-M2-13, ARC-06; 2026-10-28; the full set made by the CLI from the working tree in one folder, 19 exports in 90 s, none failing; report `tests/baselines/fresh/m2-13-cli.comparison.md`). Same: every HTML but `pictures`, all three Folio forms in Forest plain and reduced, `pictures.forest.docx`, and the cheatsheet's and `pictures`' PDFs as pictures. Each difference is one recorded above: the cheatsheet's Word PNG in both themes is the app's own drift since 1.0.13 (M2-07; the CLI equals the app today, M2-11); `UltimateMarkdownContent`'s PDF pages 6 and 52 are the paler emoji Paul accepted and page 32 the gantt chart's "today" (M2-10); its Word PNGs are the same "today" and a few anti-aliased pixels (M2-11); `pictures.forest.html` is the app's leaked `mq-active-block` (M2-09, §11); `folio.vintage-press.html` is the mindmap ids past line 4408 (M2-12, §11). `preferences.json` is listed as missing because it is the settings, not an export | 2026-10-28 |
| ARC-06 final comparison, differences explained (M2-13)                                    | **Done**: the "CLI vs app" row above is that comparison, rerun after the dark rules came out of exported pages with the same result | 2026-10-28 |

---

## 10. Risks

| Risk                                                              | Weight        | Where it is answered                                                                                                                                                                         |
| ----------------------------------------------------------------- | ------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| WinRT WebView2 will not activate outside the Windows App SDK (F2) | High          | M0-01. The fallback is the Win32 .NET API, which means rewriting the paged engine and, because both ship a `Microsoft.Web.WebView2.Core.dll`, resolving a name clash in `app/`. Paul decides |
| No parent window kind lays out headlessly (F1)                    | High          | M0-01; if a–c all fail, M2 stops and Paul decides                                                                                                                                            |
| WebView2 on CI or over SSH                                        | High, unknown | M0-04; documented as unsupported if it fails                                                                                                                                                 |
| App exports drift during the moves                                | Medium        | Baseline before any app change (M0-05); each move WP closes on the comparison                                                                                                                |
| Live and headless shells give different markup                    | Medium        | Both open the document as a tab and use the same messages (F4); compared in M2-09..M2-13                                                                                                     |
| Second browser in the app for Word (ARC-07)                       | Low           | Lazy, in its own data folder, ended at shutdown; cost in §9.2                                                                                                                                |
| Shared files differ between the two publishes                     | Low           | `build/Publish.props` (F19); `New-Release.ps1` fails on a difference                                                                                                                         |

---

## 11. Backlog

Out of v1, or turned up while planning. Paul picks from these. None is done along the way.

- `completion` (shell completion scripts) and `@file` response files (requirements §3.2).
- `toc --write`, designed in the app first (requirements §5 item 1).
- `--strict` making export findings fail the run (requirements §5 item 4).
- `check --fix`, `replace --skip-code`, and a CLI settings file of its own.
- `print`, `check --spelling`, `watch`, and a recipe file.
- Moving the app's HTML and PDF exports to the headless host too, if M2-09..M2-13 find the live
  and headless markup differ.
- **The app's HTML export leaves pictures outside the document's folder as relative links**
  (`../../../../docs/assets/...` in `pictures.default.html`), while it embeds the one beside the
  document. The file shows them only where it was saved; moved or sent, they break. Seen in the
  M0-05 baseline, 2026-10-26. Possibly intended, and an app question rather than a CLI one; the
  CLI's `export html` matches whatever the app does.
- **`--settings` is not checked by a command that reads no settings.** `check`, `replace` and the
  others that never consult the settings accept `--settings nosuch.json` and exit 0, so a typo
  is noticed only on a command that does read them (`format`, `number`, `toc`, `outline`, `folio`), which stops
  with exit 2. Seen while writing the manual, 2026-10-27. Checking the file on every command
  would cost each one a read it does not need; the manual says which commands use settings.
- **"N lines changed" counts by position, not by difference.** `MarkdownFormatter.CountChangedLines`
  compares line i with line i, so one blank line inserted near the top counts every line after
  it: formatting `docs/CLI.md` reported "684 lines changed" for about 30 real changes. The app
  shows the same figure; `format --write` repeats it. A count from the diff the CLI already makes
  for `--dry-run` would be right in both. Seen while writing the manual, 2026-10-27.
- **An option whose value goes missing takes the next option as its value.** `--with=` with
  nothing after the `=`, in any shell, and `--with ''` in Windows PowerShell 5.1, which drops an
  empty argument, both leave `--with` to take the next token: `replace --find x --with=
  --dry-run a.md` replaces with the text "--dry-run" and, no longer a dry run, prints the
  document. With `--write` next, the file is left alone and the document printed with
  "--write" in place of each match, so nothing is lost but the run did not do what was asked. The recipes
  give `'""'` for 5.1; refusing an option-shaped value for `--with`, or `--with=` with nothing
  after it, would close it. Seen while writing the recipes, 2026-10-27.
  **Done 2026-10-27:** `--with=` (any option written `--name=`) is now an empty value, and an
  option that needs a value followed by one of the command's own options as a separate word stops
  with exit 2 and says to write `--with=--write` if that text is meant. Tried in Windows
  PowerShell 5.1: `--with ''` is refused, `--with=` deletes. The recipes now give `--with=`.
- **`check` reports every link that leaves the document's folder as broken**, `../README.md`
  included, because the app's own link check (`LinkChecks.Exists`, through
  `PathContainment.ResolveWithin`) only looks inside the folder. The source pane underlines the
  same links. In a repository whose documents link up and across folders, `check` over all of
  them reports each such link, so it cannot gate a commit there. An app question first; the CLI
  follows whatever the app decides. The manual and the recipes say so. Seen while writing the
  recipes, 2026-10-27.
  **Done 2026-10-27, in the app:** a link (not a picture) is checked wherever it leads, as the
  preview follows it there, so `../README.md` is fine when the file is there, in the source pane
  and in `check` alike. Pictures keep the folder rule and their `outside-folder` finding.
- **From WSL, a Linux absolute path is written on the Windows drive without a word.**
  `folio -o /tmp/x/Linux.zip`, run from a folder under `/mnt/c`, made `C:\tmp\x\Linux.zip` and
  exited 0, because to Windows `/tmp/...` is a path from the root of the current drive. The
  recipes now say so and give `wslpath -w`. Refusing a path that starts with a single `/` would
  catch it, but `\folder\file` is a real, if rare, Windows form, so it is a question for Paul
  rather than a fix. Seen in the WSL run, 2026-10-27. **Decided 2026-10-27 (Paul):** left as the
  recipes describe it; no change to the CLI.
- **Uninstalling drops a trailing `;` from the user PATH.** `%USERPROFILE%\...\WindowsApps;`
  came back as `%USERPROFILE%\...\WindowsApps`. It works the same, since the `;` only ends an
  empty entry, but it is not the value the user had. Seen in the Sandbox install run,
  2026-10-27. **Done 2026-10-27 (Paul asked for it now):** the install, not the uninstall, was at
  fault: `Add-UserPathEntry` took the user's trailing `;` as its separator, so `A;` became
  `A;dir` and removing `dir` left `A`. It now keeps the ending, `A;dir;`, and removing gives back
  `A;`. Add then remove gave back the exact value for six shapes of PATH (a trailing `;`, none,
  `;;` in the middle and at the end, empty) and left one already holding the folder alone, in
  Windows PowerShell 5.1 and PowerShell 7, against an in-memory PATH; the old script failed the
  three that end in `;`.
- **The app's HTML export carries the preview's caret bar.** The page is the preview's markup,
  and the block the cursor was in keeps `class="mq-active-block"`, which app.css draws as a teal
  bar beside it in a browser (print hides it). Seen in the baseline's `pictures.forest.html`,
  2026-10-28 (WP-M2-09); the CLI's page, from a browser with no cursor, has none. Stripping the
  class on the way out, or moving the app's HTML export to the headless host as Word moved, would
  each answer it.
- **The render browser does not exit after closing when the document asked for a picture**,
  served or not: the headless host waited out its 5 seconds and ended it, on every such run. It
  now waits one second and ends it quietly (WP-M2-09), so the cost is gone, but the cause is not
  known. Seen 2026-10-28.
- **The CLI's PDF draws some color emoji paler than the app's**: three on page 6 of
  `UltimateMarkdownContent.forest.pdf` and the check and cross marks on page 52, nothing else.
  Not the shared print browser and, as far as one comparison shows, not a newer WebView2 runtime
  (WP-M2-10, §9.3). Accepted by Paul as a known difference; an app PDF made today would say whether
  the app now draws them the same way.
  Seen 2026-10-28.
- **`folio.vintage-press.html` is compared only to line 4408 of 5458**: past it, a mindmap's
  `node-` ids are numbered from the app's session and do not renumber cleanly, so
  `Compare-ExportBaseline.ps1` stops there. Forest, which matches, is the same page in another
  theme (WP-M2-12, §9.3). Seen 2026-10-28.
- **`InputSetTests.One_missing_file_among_three_the_others_are_processed_and_the_run_exits_3`
  failed once** in a full run of the CLI tests and passed in the four runs after it, alone and in
  full; the assertion that failed was the exit code, and it does not say which it got. Seen
  2026-10-28, during WP-M2-12.
- **`export --dry-run --out-dir` does not catch two documents with the same name**: it lists both
  going to one file, where the real run refuses with exit 3 before writing. The dry run skips the
  output check altogether. Seen 2026-10-28, writing WP-M2-14's recipes.
- **The collision message says "share them separately" for an export**, the Folio's wording:
  `docs\a\Notes.md and docs\b\Notes.md would both be written to ...\Notes.html. Rename one, or
  share them separately.` Seen 2026-10-28.
- **The CLI test project builds with an MSB3277 warning**: two versions of `WindowsBase`
  conflict, one from .NET and one wanted by the WebView2 package's WPF assembly, which reaches
  the test project through `marqora-cli`. It is there at `f225114`, before M2, so nothing in M2
  caused it. The tests run; it is noise in every build's output. Seen 2026-10-27.
