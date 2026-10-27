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
|  F9 | `FolioUnpacker.Unpack` reads only the single-file form and refuses a non-empty target.                                                                                                                                                                                                                                                                                                                                                                                                                    | `unpack` is single-file only and takes no `--overwrite`: a non-empty target exits 4 (A4, A5b).                                                                                                                                                                                                                                                                                            | M1-14        |
| F10 | The Folio preflight's figures are private to `FolioWindow`: `Weight(plan)` (:836), `HeavyPageBytes` 50 MB, `MailLimitBytes` 25 MB, `ReducibleBytes` 5 MB, and the advice lines (:850-905).                                                                                                                                                                                                                                                                                                                | A `FolioPreflight` model holding the estimate, the three limits and the advice moves into `Folio`. The window and the CLI both use it.                                                                                                                                                                                                                                      | M1-16        |
| F11 | `FormatOptions.Default` has `EofNewline = true` and `LineEndings = true` (Detect).                                                                                                                                                                                                                                                                                                                                                                                                                        | With default options, `format` adds a final newline on purpose. Amendment A7.                                                                                                                                                                                                                                                                                               | M1-03        |
| F12 | `AppVersion` is `internal` to App and reads its own assembly.                                                                                                                                                                                                                                                                                                                                                                                                                                             | It moves to `Services` as `ProductVersion`. Amendment A11 renames the requirement's reference.                                                                                                                                                                                                                                                                              | M1-13        |
| F13 | There is no GitHub Actions workflow, and none is added (decision).                                                                                                                                                                                                                                                                                                                                                                                                                                        | Actions results are manual checks by Paul in a scratch repository (A3).                                                                                                                                                                                                                                                                                                     | M0-04, M1-19 |
| F14 | `PreferencesTransferService.ImportAsync(path, current)` is already pure: it merges a file over `current`, returns the result, writes nothing, and already accepts a raw `settings.json`. `JsonSettingsRepository` loading never writes.                                                                                                                                                                                                                                                                   | `--settings <file>` is `ImportAsync(path, new AppSettings())`. `--settings app` loads through `JsonSettingsRepository` with `AppPaths`. No refactor is needed.                                                                                                                                                                                                              | M1-05        |
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
| CI                       | **No workflow is added.** Actions checks are manual, by Paul, elsewhere (A3)                                                                                                                                                                      |
| Spike code               | **Seeds `WebRender`**, reshaped in M2 rather than thrown away. The spike command is compiled only under the `MQ_SPIKE` define, so it never ships in M1                                                                                            |
| Requirement changes      | **Decided in §7.** Accepted amendments were applied to `CLI-Requirements.md` on 2026-10-16 (its §2.1 records them)                                                                                                                                                                               |
| `unpack` forms           | **Single-file only** (A4)                                                                                                                                                                                                                         |
| `unpack --overwrite`     | **None.** `unpack` refuses a non-empty target and takes no `--overwrite` (A5b; the proposal to empty it was rejected)                                                                                                                                                                                            |
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
  2. Actions runner (A3): Paul runs a `MQ_SPIKE` build on `windows-latest` in a scratch repository
     and records the log link in §9.1.
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

- **Serves**: CLI-VB-UNP-01 (as amended by A4 and A5b).
- **Depends on**: M1-04.
- **Changes**: `unpack <folio.html> --out-dir <folder>`. There is no `--overwrite`.
  - **Not a single-file Folio.** A folder, a `.zip`, or a file where `FolioPayload.TryDecode`
    fails gives exit 3 with guidance.
  - **Non-empty target.** Exit 4, and nothing in it is touched. A missing or empty target is
    handed to `FolioUnpacker.Unpack`, which enforces the same rule itself.
- **Tests**: `UnpackVerbTests`, using a single-file Folio **made by the app and committed as a
  fixture** (`FolioHtmlWriter` is still in App in M1). The unpacked files equal the documents the
  app would open. Also tested: a non-empty target exits 4 and is unchanged, and the folder and zip
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
    - CI recipes: GitHub Actions YAML and a pre-commit hook for `format --check` and `check`;
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
  - DOC-02: the recipe runs green in Paul's scratch repository (§9.3).
- **Done when**: the tests and checks pass.

### M2: refactor and exports

Gate: CLI-SP-01 passed (§9.1).

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
    troubleshooting. Exports in CI are described only as far as §9.1 showed them to work.
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
and A13b, also applied. The table keeps the rejected rows so the reasoning stays findable.

|   # | Requirement            | Change                                                                                                                                                                                                                                                                               | Reason                                                                                          |
| --: | ---------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------- |
|  A1 | CLI-ARC-01, CLI-SP-01  | `Cli` and `WebRender` target `net10.0-windows10.0.19041.0` with WebView2's WinRT projection switched on. SP-01's "message-only parent window" becomes "a parent window the spike chooses, recorded in the plan". Add to SP-01: "WinRT activation works without the Windows App SDK". | F1, F2                                                                                          |
|  A2 | CLI-ARC-01, CLI-ARC-02 | Add `PaulTechGuy.MQ.Imaging` (Windows TFM, no WinUI), holding the WIC resizer behind `IImageResizer`. `Exporting` is created in M1 to receive `FolioShrinker`.                                                                                                                       | F7                                                                                              |
|  A3 | CLI-SP-03, CLI-DOC-02  | The Actions results are manual checks by Paul in a scratch repository. DOC-02's box becomes "runs green in a scratch repository".                                                                                                                                                    | F13                                                                                             |
|  A4 | CLI-VB-UNP-01          | The first box becomes "A single-file Folio unpacks; a folder or `.zip` exits 3 with guidance."                                                                                                                                                                                       | F9                                                                                              |
|  A5 | CLI-VB-UNP-01          | **Rejected, replaced by A5b.** Add: "`--overwrite` empties a non-empty target and then unpacks; without it a non-empty target exits 4. Refused even with `--overwrite` for a drive root, the profile folder, the current folder or an ancestor of it, or a folder containing the input."                            | F9                                                                                              |
|  A6 | CLI-VB-REN-01          | Add `--style sequential\|ones\|zeros`, defaulting to the app's suggestion per list. A list that cannot take `zeros` is left alone and reported.                                                                                                                                      | F8                                                                                              |
|  A7 | CLI-OUT-06             | Add: "A verb whose own options rewrite line endings or the final newline (`format` with `LineEndings`/`EofNewline`) decides them itself." The no-final-newline box names `number` rather than `format`.                                                                              | F11                                                                                             |
|  A8 | CLI-SET-03             | Add: "A raw `settings.json` is accepted too, as the app's Import accepts one."                                                                                                                                                                                                       | F14; this records behavior that already exists                                                  |
|  A9 | CLI-EX-05              | Add: "The app's own render timeout and degrade are unchanged; only the CLI fails the file."                                                                                                                                                                                          | F6                                                                                              |
| A10 | CLI-ARC-07             | "…through the same headless host the CLI uses, in a data folder of the app's own."                                                                                                                                                                                                   | Decision                                                                                        |
| A11 | CLI-VB-VER-01          | "Equals `AppVersion.Current`" becomes "equals the product version `Marqora.exe` reports from the same build" (`ProductVersion`).                                                                                                                                                     | F12                                                                                             |
| A12 | CLI-ARC-05             | "One test suite runs over both implementations" becomes "one suite runs over the shared bridge and transport that both implementations use".                                                                                                                                         | Hosting the WinUI preview in a test process is not practical; both hosts use the same transport |
| A5b | CLI-VB-UNP-01          | The target must be missing or empty; a non-empty one exits 4, and `unpack` takes no `--overwrite`. Follows the app's own rule, which never writes over what is already there. | F9 |
| A13b | CLI-VB-CHK-01         | Web images and pictures outside the folder stay errors. One flag, `--ignore-external-images`, makes both information, shown only under `--verbose`. | F16 |
| A13 | CLI-VB-CHK-01          | **Rejected, replaced by A13b.** Add: "Web images and pictures outside the folder are information, shown with `--verbose`; they do not fail the run."                                                                                                                                                                 | F16                                                                                             |
| A14 | CLI-SP-03, CLI-DOC-02 | **Proposed 2026-10-16, not yet decided.** SP-03: the Actions-runner box becomes "Not run, by decision; exports on CI are documented as untested". DOC-02: "runs green in a scratch repository" becomes "each command in the recipe runs green locally; the workflow file itself is untested, and the manual says so". | No Actions minutes are to be spent, and a public repository would leave a permanent public record |

---

## 8. Traceability matrix

All 92 IDs from the requirements' §6 index, each exactly once. *WPs* lists every work package that
serves the ID, owner first. *Verified by* names the test class or the §9 record.

| ID             | Title                                 |   M   |  Pri   | WPs                        | Verified by                                               | Status |
| -------------- | ------------------------------------- | :---: | :----: | -------------------------- | --------------------------------------------------------- | ------ |
| CLI-SP-01      | Console renders and prints headlessly |  M0   |  Must  | M0-02, M0-01               | §9.1                                                      | verified |
| CLI-SP-02      | Cold start measured                   |  M0   |  Must  | M0-03                      | §9.1                                                      | verified |
| CLI-SP-03      | Non-interactive sessions tried        |  M0   |  Must  | M0-04                      | §9.1 (Actions: Paul, A3)                                  | blocked: A14 pending (SSH recorded) |
| CLI-CMD-01     | Verb shape                            |  M1   |  Must  | M1-01                      | `CommandTreeTests`                                        | todo   |
| CLI-CMD-02     | Aliases                               |  M1   |  Must  | M1-01                      | `AliasTests`                                              | todo   |
| CLI-CMD-03     | Help with examples                    |  M1   |  Must  | M1-01, M1-19               | `HelpExamplesTests`, `ManualCoverageTests`                | todo   |
| CLI-CMD-04     | Usage errors                          |  M1   |  Must  | M1-01                      | `UsageErrorTests`                                         | todo   |
| CLI-CMD-05     | Response files off                    |  M1   | Should | M1-01                      | `UsageErrorTests`                                         | todo   |
| CLI-IN-01      | Files and globs                       |  M1   |  Must  | M1-03                      | `InputSetTests`, `ShellGlobTests`, §9.3 (bash)            | todo   |
| CLI-IN-02      | Standard input                        |  M1   |  Must  | M1-03                      | `StdinTests`                                              | todo   |
| CLI-IN-03      | Base folder                           |  M1   |  Must  | M1-11, M1-03               | `CheckVerbTests`                                          | todo   |
| CLI-IN-04      | Encoding                              |  M1   |  Must  | M1-03                      | `TextFileTests`                                           | todo   |
| CLI-IN-05      | Read-only refused for writing         |  M1   |  Must  | M1-04                      | `WriteModeTests`                                          | todo   |
| CLI-IN-06      | Missing and unreadable files          |  M1   |  Must  | M1-03                      | `InputSetTests`                                           | todo   |
| CLI-OUT-01     | Default leaves file alone             |  M1   |  Must  | M1-04                      | `WriteModeTests`                                          | todo   |
| CLI-OUT-02     | Several inputs need a mode            |  M1   |  Must  | M1-04                      | `WriteModeTests`                                          | todo   |
| CLI-OUT-03     | `--write`                             |  M1   |  Must  | M1-04                      | `WriteModeTests`, `KillDuringWriteTests`                  | todo   |
| CLI-OUT-04     | `--check`                             |  M1   |  Must  | M1-04                      | `WriteModeTests`                                          | todo   |
| CLI-OUT-05     | `--dry-run` for text verbs            |  M1   |  Must  | M1-04                      | `DryRunDiffTests`                                         | todo   |
| CLI-OUT-06     | Encoding and line endings preserved   |  M1   |  Must  | M1-03                      | `TextFileTests`                                           | todo   |
| CLI-OUT-07     | Output files for exports and Folio    | M1/M2 |  Must  | M1-16, M2-08               | `OutputPlanTests`                                         | todo   |
| CLI-OUT-08     | No overwrite without `--overwrite`    | M1/M2 |  Must  | M1-16, M2-08               | `OutputPlanTests`                                         | todo   |
| CLI-OUT-09     | HTML to stdout                        |  M2   | Should | M2-09                      | `ExportHtmlTests`                                         | todo   |
| CLI-RPT-01     | Exit codes                            |  M1   |  Must  | M1-02, M1-19               | `ExitCodeTests`, `ManualTableTests`                       | todo   |
| CLI-RPT-02     | Messages                              |  M1   |  Must  | M1-02                      | `OutputDisciplineTests`                                   | todo   |
| CLI-RPT-03     | Several files, one exit code          |  M1   |  Must  | M1-02                      | `RunAggregatorTests`                                      | todo   |
| CLI-RPT-04     | `--json`                              |  M1   |  Must  | M1-02                      | `JsonGoldenTests`                                         | todo   |
| CLI-RPT-05     | Schema versioning                     |  M1   |  Must  | M1-02                      | `JsonGoldenTests`                                         | todo   |
| CLI-RPT-06     | `--quiet` and `--verbose`             |  M1   | Should | M1-02, M2-08               | `ReporterTests`                                           | todo   |
| CLI-RPT-07     | Color                                 |  M1   | Should | M1-02                      | `ReporterTests`                                           | todo   |
| CLI-RPT-08     | Cancellation                          | M1/M2 |  Must  | M1-02, M2-08               | `CancelTests`                                             | todo   |
| CLI-SET-01     | Hermetic by default                   |  M1   |  Must  | M1-05                      | `SettingsSourceTests`                                     | todo   |
| CLI-SET-02     | `--settings app`                      |  M1   |  Must  | M1-05                      | `SettingsSourceTests`                                     | todo   |
| CLI-SET-03     | `--settings <file>`                   |  M1   |  Must  | M1-05                      | `SettingsSourceTests`                                     | todo   |
| CLI-SET-04     | Flags win over settings               |  M1   |  Must  | M1-05, M2-10               | `SettingsSourceTests`, `ExportPdfTests`                   | todo   |
| CLI-TH-01      | `--theme`                             | M1/M2 |  Must  | M1-06, M2-08               | `ThemeResolverTests`                                      | todo   |
| CLI-TH-02      | Which theme wins                      |  M1   |  Must  | M1-06                      | `ThemeResolverTests`                                      | todo   |
| CLI-TH-03      | Unknown theme fails                   |  M1   |  Must  | M1-06                      | `ThemeResolverTests`                                      | todo   |
| CLI-TH-04      | Light palette, one theme per run      |  M2   |  Must  | M2-13, M2-12               | `ExportPaletteTests`                                      | todo   |
| CLI-TH-05      | `themes list`                         |  M1   |  Must  | M1-06                      | `ThemesListTests`                                         | todo   |
| CLI-TH-06      | Resolved theme shown                  |  M1   |  Must  | M1-06, M1-16, M2-08        | `ThemeResolverTests`, `FolioDryRunTests`, `ReporterTests` | todo   |
| CLI-VB-FMT-01  | `format`                              |  M1   |  Must  | M1-07                      | `FormatVerbTests`                                         | todo   |
| CLI-VB-NUM-01  | `number`                              |  M1   |  Must  | M1-07                      | `NumberVerbTests`                                         | todo   |
| CLI-VB-UNN-01  | `unnumber`                            |  M1   |  Must  | M1-07                      | `NumberVerbTests`                                         | todo   |
| CLI-VB-RPL-01  | `replace`                             |  M1   |  Must  | M1-08                      | `ReplaceVerbTests`                                        | todo   |
| CLI-VB-REN-01  | `renumber-lists`                      |  M1   |  Must  | M1-09                      | `RenumberListsTests`                                      | todo   |
| CLI-VB-DGL-01  | `diagram-layout`                      |  M1   |  Must  | M1-10                      | `DiagramLayoutVerbTests`                                  | todo   |
| CLI-VB-CHK-01  | `check`, links and images             |  M1   |  Must  | M1-11, M1-17               | `CheckVerbTests`, `OfflineTests`                          | todo   |
| CLI-VB-CHK-02  | `check --style`                       |  M1   |  Must  | M1-11                      | `CheckVerbTests`                                          | todo   |
| CLI-VB-TOC-01  | `toc`                                 |  M1   | Should | M1-12                      | `TocVerbTests`                                            | todo   |
| CLI-VB-OUT-01  | `outline`                             |  M1   | Should | M1-12                      | `OutlineVerbTests`                                        | todo   |
| CLI-VB-UNP-01  | `unpack`                              |  M1   |  Must  | M1-14                      | `UnpackVerbTests`                                         | todo   |
| CLI-VB-VER-01  | `version`                             |  M1   |  Must  | M1-13, M1-18               | `VersionTests`, `New-Release.ps1` assertion               | todo   |
| CLI-EX-01      | One browser per run                   |  M2   |  Must  | M2-08, M2-03               | `RendererFactoryTests`                                    | todo   |
| CLI-EX-02      | Data folder per process               |  M2   |  Must  | M2-08                      | `DataFolderTests`                                         | todo   |
| CLI-EX-03      | Export report becomes findings        |  M2   |  Must  | M2-08, M2-06               | `ExportFindingsTests`                                     | todo   |
| CLI-EX-04      | `--dry-run` for exports               |  M2   | Should | M2-08                      | `ExportDryRunTests`                                       | todo   |
| CLI-EX-05      | Render timeout                        |  M2   |  Must  | M2-08, M2-04               | `RenderTimeoutTests`                                      | todo   |
| CLI-EX-06      | Diagrams leave as light drawing       |  M2   |  Must  | M2-13, M2-05               | `DiagramOutputTests`                                      | todo   |
| CLI-EX-HTML-01 | `export html`                         |  M2   |  Must  | M2-09                      | `ExportHtmlTests`, §9.3                                   | todo   |
| CLI-EX-PDF-01  | `export pdf`                          |  M2   |  Must  | M2-10                      | `ExportPdfTests`, §9.3                                    | todo   |
| CLI-EX-WORD-01 | `export word`                         |  M2   |  Must  | M2-11                      | `ExportWordTests`, §9.3                                   | todo   |
| CLI-FO-01      | Form from output path                 | M1/M2 |  Must  | M1-16, M2-12               | `FolioFormTests`                                          | todo   |
| CLI-FO-02      | Single file too large                 |  M2   |  Must  | M2-12                      | `FolioFormTests`                                          | todo   |
| CLI-FO-03      | Shrinking images                      |  M1   | Should | M1-15, M1-16               | `FolioShrinkTests`, §9.3                                  | todo   |
| CLI-FO-04      | Web pictures only when asked          |  M1   |  Must  | M1-16, M1-17               | `FolioFetchTests`, `OfflineTests`                         | todo   |
| CLI-FO-05      | Preflight as `--dry-run`              |  M1   |  Must  | M1-16                      | `FolioDryRunTests`                                        | todo   |
| CLI-FO-06      | Folio report                          |  M1   |  Must  | M1-16                      | `FolioFindingsTests`                                      | todo   |
| CLI-ARC-01     | Projects and dependency rule          | M1/M2 |  Must  | M1-17, M0-01, M0-02, M1-15 | `ArchitectureTests`                                       | todo   |
| CLI-ARC-02     | Move export assembly                  |  M2   |  Must  | M2-02, M1-15               | `ArchitectureTests`, §9.3                                 | todo   |
| CLI-ARC-03     | Move paged engine                     |  M2   |  Must  | M2-03                      | §9.3                                                      | todo   |
| CLI-ARC-04     | Rendering interface                   |  M2   |  Must  | M2-05, M2-06               | `RendererRouteTests`                                      | todo   |
| CLI-ARC-05     | One shell bridge                      |  M2   |  Must  | M2-04, M2-13               | `BridgeSuite`, `BridgeShapeTests`                         | todo   |
| CLI-ARC-06     | App's exports unchanged               |  M2   |  Must  | M0-05, M2-01, M2-13        | §9.3                                                      | todo   |
| CLI-ARC-07     | App Word via detached render          |  M2   |  Must  | M2-07                      | `ExportWordTests`, §9.3                                   | todo   |
| CLI-NF-01      | Text verbs fast                       |  M1   | Should | M1-19                      | §9.2                                                      | todo   |
| CLI-NF-02      | Cold start bounded                    |  M2   | Should | M2-14                      | §9.2                                                      | todo   |
| CLI-NF-03      | Offline by default                    |  M1   |  Must  | M1-17, M1-16, M2-13        | `OfflineTests`, §9.3 firewall                             | todo   |
| CLI-NF-04      | Missing WebView2 runtime              |  M2   |  Must  | M2-08                      | `MissingRuntimeTests`                                     | todo   |
| CLI-NF-05      | Platform                              |  M1   |  Must  | M1-18, M1-01, M0-01        | §9.1, §9.3 Sandbox                                        | todo   |
| CLI-PKG-01     | Beside the app                        |  M1   |  Must  | M1-18                      | `New-Release.ps1` assertion, `AssetLocationTests`         | todo   |
| CLI-PKG-02     | PATH, opt-in                          |  M1   |  Must  | M1-18                      | §9.3                                                      | todo   |
| CLI-RULE-01    | License headers                       | M1/M2 |  Must  | M1-19, M2-14               | `Add-FileHeaders.ps1 -Check`                              | todo   |
| CLI-RULE-02    | American spelling                     | M1/M2 |  Must  | M1-19, M2-14               | `Set-AmericanSpelling.ps1 -Check`                         | todo   |
| CLI-RULE-03    | Network claim                         |  M1   |  Must  | M1-19                      | `Test-NetworkClaim.ps1 -Check`                            | todo   |
| CLI-RULE-04    | Release notes                         | M1/M2 |  Must  | M1-19, M2-07, M2-14        | Review of `vNext.md`                                      | todo   |
| CLI-DOC-01     | The manual                            | M1/M2 |  Must  | M1-19, M2-14               | `ManualCoverageTests`, `ManualExampleTests`               | todo   |
| CLI-DOC-02     | CI recipes                            |  M1   | Should | M1-19                      | §9.3 (scratch repo, A3)                                   | todo   |
| CLI-DOC-03     | The rest of the docs                  | M1/M2 |  Must  | M1-19, M2-14               | Review                                                    | todo   |
| CLI-TST-01     | CLI tests                             |  M1   |  Must  | M1-00, M1-17               | `dotnet test`                                             | todo   |
| CLI-TST-02     | Export tests                          |  M2   |  Must  | M2-13                      | `dotnet test`                                             | todo   |
| CLI-TST-03     | Fixtures untouched                    |  M1   |  Must  | M1-00                      | `FixtureGuardTests`                                       | todo   |

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
| Actions runner (Paul, scratch repo; log link) | **Not run, by decision:** no Actions minutes are to be spent, and a public scratch repository would leave a permanent public record of itself. Exports on CI are untested, and `docs/CLI.md` says so. Proposed as A14 | 2026-10-16 |
| Go / unsupported decision per environment | **Developer machine: go.** M2 may proceed as designed (gate passed). **SSH: unsupported for the browser verbs** (export and the single-file Folio); M2 goes ahead with that documented in `docs/CLI.md`, and the text verbs, which never start a browser, are unaffected. **Actions: not run (A14); exports on CI are untested** | 2026-10-16 |

Also from the spike, for M2:

- **The shell had no "render settled" message.** It records the moment (`state.renderSettled`) but never told the host, because a person prints a preview they have already watched settle. `webshell/app.js` gained `requestRenderSettled` / `renderSettled`, which answers once the current render and the output drawings are done. The app never sends it. WP-M2-04's `ShellBridge` must carry it.
- **`build/WebAssets.targets`** now holds the shell copy for both executables; `App.csproj` imports it. The App builds and its `Assets\web` is unchanged.

### 9.2 Timings

| Measure                                    | Figure | Bound                      | Date |
| ------------------------------------------ | ------ | -------------------------- | ---- |
| `format`, < 100 KB, start to exit (NF-01)  |        | < 1 s                      |      |
| `check`, < 100 KB, start to exit (NF-01)   |        | < 1 s                      |      |
| First export added cost (NF-02)            |        | M0 figure + max(50 %, 1 s) |      |
| App's first Word export added cost (M2-07) |        | report only                |      |

### 9.3 Manual checks and comparisons

| Check                                                                                     | Result | Date |
| ----------------------------------------------------------------------------------------- | ------ | ---- |
| M0-05 baseline captured: file list and hashes | **Done.** Made by Paul with Marqora 1.0.13 (tag `v1.0.13`, `0568b2c`) from a detached worktree, `preferences.json` confirming the version: 20 exports, 30 files, 20,650,958 bytes, `MANIFEST.sha256` `4af2b21e0c999d77`; `Get-BaselineHashes.ps1 -Check` passes. **Two gaps, kept by decision** (README, *Known gaps*): no picture keeps its path, because `inputs/images/logo.png` is byte-identical to `docs/assets/marqora-logo.png` and the Folio de-duplicated them; and shrinking never happens, because the 1888-pixel screenshot's reduced copy was no smaller, so the shrunk Folios equal the plain ones | 2026-10-26 |
| Folio "reduce pictures" equals baseline after M1-15 | Proves only that the declined path is unchanged (see the M0-05 row); a picture actually being reduced is `FolioShrinkTests`' to prove | |
| Folio folder and zip, CLI vs baseline (M1-16) | No kept-path case in the baseline (see the M0-05 row); `FolioPlanner`'s tests carry that | |
| Globs from bash give the same list (IN-01)                                                |        |      |
| Every verb under a firewall outbound block (M1: text verbs; M2: exports)                  |        |      |
| Release output: two executables, one set of shared files, equal versions                  |        |      |
| Install with PATH; `marqora-cli version` in a new shell; uninstall restores PATH          |        |      |
| CLI starts in Windows Sandbox (no Windows App SDK)                                        |        |      |
| DOC-02 recipe green in scratch repository                                                 |        |      |
| M2-02: HTML and Folio vs baseline                                                         |        |      |
| M2-03: PDF vs baseline; one print to Microsoft Print to PDF                               |        |      |
| M2-06: all four app exports vs baseline (before ARC-07)                                   |        |      |
| M2-07: Word identical with preview busy / other tab / idle; baseline difference explained |        |      |
| CLI vs app: HTML, PDF pages, Word parts, single-file Folio (M2-09..M2-12)                 |        |      |
| ARC-06 final comparison, differences explained (M2-13)                                    |        |      |

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
