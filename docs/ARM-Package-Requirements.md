# Marqora ARM64 package: feature requirements

The requirements for shipping Marqora to Windows on ARM PCs: a second release zip,
`Marqora-<version>-win-arm64.zip`, published on GitHub beside the x64 zip on every release, with
the same contents, the same installer and the same steps for whoever downloads it.

Written 2026-11-04 against `dev` at `b32617a` (1.0.21). This document says what has to be true
and how to tell. An implementation plan is written from it, and every change in the plan names
the requirement IDs it serves.

---

## 1. Summary

Marqora can already be built for ARM64. A trial build on the x64 development PC produced both
executables with three one-line settings changes and no change to any C# file (section 2).

The work is therefore in the release pipeline, not the app: the scripts assume one architecture
and one zip. The one thing a build cannot prove is that the app runs, so the first ARM64 release
depends on a full check on a real Windows on ARM PC.

What someone downloading Marqora sees:

- Each GitHub release carries two zips, each with its checksum, and says which one you need.
- Both zips install the same way: extract, double-click `Install.cmd`.
- The ARM64 zip runs natively, rather than through Windows' x64 emulation.
- Picking the wrong zip is handled: it is either stopped with a clear message or noted.

---

## 2. What the trial build established

The trial ran on a throwaway copy of `b32617a`, on the x64 development PC. It published the app
and marqora-cli with the Release settings: self-contained and precompiled.

The three changes it needed:

| File | Change |
| --- | --- |
| `build/Publish.props` | `RuntimeIdentifier` from `win-x64` to `win-arm64` |
| `src/PaulTechGuy.MQ.Cli/PaulTechGuy.MQ.Cli.csproj` | `PlatformTarget` from `x64` to `arm64` |
| `src/PaulTechGuy.MQ.WebRender/PaulTechGuy.MQ.WebRender.csproj` | `PlatformTarget` from `x64` to `arm64` |

What it showed:

| Question | Result |
| --- | --- |
| Do the app and the CLI build and publish for ARM64 from an x64 PC? | Yes, with no errors |
| Does every native package have an ARM64 payload? | Yes: WebView2 and all the Windows App SDK components |
| Is anything native in the output not ARM64? | No. App: 223 ARM64 files, 82 CPU-neutral .NET files. CLI: 137 and 81 |
| Are the files the app and CLI share the same bytes? | Yes, so the check in `Add-CliToPublish` passes |
| Does building x64 and then ARM64 in one tree, without cleaning, mix them? | Publishes came out clean, but plain builds did not: WebRender shared one folder between the two, and an x64 test run then failed to load it. Found in review and fixed: ARM-BLD-07 |
| How large is it? | 670 files, 284 MB unpacked, about 88 MB zipped, against about 95 MB for the x64 zip of 1.0.21 |
| Does it run on the x64 development PC? | No: `cannot execute binary file` |

The zipped figure was measured on the merged app and CLI folders, without the installer scripts,
which add very little.

**Not established.** Nothing has been launched on ARM hardware. Section 6 is how that gets
answered.

**A trap in repeating the trial.** A working folder with a long path makes the build fail with
`MSB3030`, "Could not copy the file ... because it was not found", and no compiler error. The path
of `PaulTechGuy.MQ.Imaging.dll` under `obj` went past the 260-character Windows limit. That has
nothing to do with ARM, and the repository's usual location is not affected.

---

## 3. Decisions

### 3.1 Settled by Paul

| # | Decision | Chosen | Why |
| --- | --- | --- | --- |
| D1 | How ARM64 builds are tested | On a real Windows on ARM PC. One full check for the first release, then a short checklist each release | The development PC can build ARM64 but not run it. A real PC is what lets the zip be a fully supported download |
| D2 | Which zips a release carries | Always both. No draft is created unless both built | A version never exists for one kind of PC only, and the scripts never have to ask which zips a release has |
| D3 | The x64 zip's installer on an ARM PC | Installs normally, with a one-line note that an ARM version exists | Emulation should work, so blocking would refuse a working setup. Silence would hide the ARM build |
| D4 | Windows versions the ARM64 zip states | Windows 11 on ARM. Stated, not enforced | Claim only what is tested. Windows 10 on ARM is not blocked, it is just not promised |

### 3.2 Proposed, and confirmed by Paul on 2026-11-04

These follow from the decisions above or from how the repository already works.

| # | Decision | Proposed | Why |
| --- | --- | --- | --- |
| D5 | Asset name | `Marqora-<version>-win-arm64.zip` | The existing pattern with the runtime identifier swapped. The x64 name does not change, so existing links keep working |
| D6 | The default architecture | x64 stays the default everywhere nothing asks otherwise: Visual Studio, `dotnet build`, the tests | The daily inner loop does not change. ARM64 exists only when a script asks for it |
| D7 | Dev builds | `New-DevBuild.ps1` stays x64 unless asked for ARM64 | A dev build is a quick test zip, so it should not pay for two. The option has to exist, because it is how a change reaches the ARM PC before a release |
| D8 | Where the test suite runs | On the x64 development PC, as now | The tests exercise the same source. What is specific to ARM is covered by the checklist in section 6 |
| D9 | Where ARM64 is built | On the x64 development PC, with no ARM build machine | The trial shows it works, and it keeps one release machine and no GitHub Actions |
| D10 | How the installer knows its zip's architecture | It reads `app\Marqora.exe`'s own file header | The file cannot disagree with itself. A separate marker could |

---

## 4. Scope

### 4.1 In scope

- Building both zips in one release run, from one commit.
- Both zips on the GitHub draft and the published release.
- The checks that prove each zip is what its name says.
- The installer's architecture checks and messages.
- The reader-facing text that names an architecture or says there is one zip.
- The ARM checklist.

### 4.2 Out of scope

- **Any change to the app's features or to any C# file**, unless the first full check finds a
  fault on ARM. Such a fault is handled under ARM-TST-05.
- **MSIX, the Microsoft Store, or winget.** Marqora ships as a zip, as `docs/Installer.md`
  records.
- **Code signing.** Unchanged: the ARM64 zip is unsigned, as the x64 zip is.
- **One zip holding both architectures.** It would roughly double the download for everyone.
- **ARM64EC or 32-bit builds.**
- **GitHub Actions, in any form.**
- **An update check or anything else that reaches out on its own.** The installer's note and the
  release text only name where the other zip is. See *What Marqora claims about the network* in
  `CLAUDE.md`.

---

## 5. Requirements

Each ID has the form `ARM-<AREA>-<NN>`. IDs are never reused or renumbered: a dropped requirement
stays in place, marked **Withdrawn** with the reason.

| Area | Covers |
| --- | --- |
| `BLD` | Building for a chosen architecture |
| `GATE` | Proving a zip is what its name says |
| `REL` | The GitHub release |
| `INS` | The installer |
| `DOC` | Reader-facing text |
| `TST` | Testing |

### 5.1 Building

**ARM-BLD-01. One place chooses the architecture.** `build/Publish.props` takes the architecture
as an input, and `PlatformTarget` in the CLI and WebRender projects follows it rather than naming
x64 outright. The app and the CLI must never be able to build for different architectures.
*Check:* a publish asked for ARM64 changes no tracked file, and its output passes ARM-GATE-01.

**ARM-BLD-02. x64 is unchanged by default.** With nothing asked for, every project builds as it
does today (D6). This includes Visual Studio, `dotnet build`, the two test projects that name x64
and `build/Get-BaselineHashes.ps1`.
*Check:* the x64 zip's file list matches 1.0.21's, apart from intended changes.

**ARM-BLD-03. `New-Release.ps1` builds both zips.** One run produces both zips and their checksums
in `build/artifacts`, from one source tree. If either fails, the run fails.
*Check:* the run leaves four files named for the version.

**ARM-BLD-04. `New-DevBuild.ps1` can build either.** It stays x64 unless asked for ARM64 (D7). The
CLI is included either way, as it is in every build path.
*Check:* an ARM64 dev build produces a zip with `marqora-cli.exe` in it and the dev banner in its
README.

**ARM-BLD-05. No file from one build reaches the other.** Staging for one architecture is finished
and zipped, or is held apart, before the other begins. The second publish must not inherit the
first's `.stage` folder.
*Check:* ARM-GATE-01 passes on both zips from a single run.

**ARM-BLD-06. The release build gate covers ARM64.** The preflight compiles the app and CLI
projects for ARM64 as well as the solution for x64, so a project that breaks only for ARM64 fails
before anything is promoted or tagged. It does not build the solution for ARM64, because the test
projects stay x64 (D8).
*Check:* an ARM64-only build failure stops `Publish-Release.ps1` at its gates.

**ARM-BLD-07. Building one architecture never disturbs the other.** Every project with a set
architecture has its own `bin` and `obj` folders per architecture, so an ARM64 build leaves nothing
behind for a later x64 build, test run or Visual Studio session to pick up.
*Check:* the full x64 test suite passes straight after an ARM64 build, in the same tree.

### 5.2 Proving each zip

**ARM-GATE-01. Every native file matches the zip's architecture.** Before zipping, the script reads
the header of every `.exe` and `.dll` in the publish. A native file for another architecture fails
the build and is named. CPU-neutral .NET files are allowed. This is the proof the development PC
can give, because it cannot run the result.
*Check:* an x64 DLL placed in an ARM64 publish fails the run and names that file. Note that .NET
marks its CPU-neutral files as x86 in the header, so the test has to look at whether a file is
IL-only rather than at the machine type alone.

**ARM-GATE-02. The CLI version check works without running the CLI.** `Add-CliToPublish` runs
`marqora-cli.exe version`, which cannot work for an ARM64 build on an x64 PC. When the machine
cannot run the build, the check compares the version in the file's properties, as
`Get-PublishedVersion` already does, and says that it did. When it can, it runs the executable as
now.
*Check:* an ARM64 release run on the x64 PC passes this step, and a CLI at the wrong version
still fails it.

**ARM-GATE-03. The existing publish checks run for both.** The required-files check (`Marqora.pri`,
`App.xbf`, the web shell), the shared-files check, and the version check all run for each
architecture.
*Check:* removing `App.xbf` from the ARM64 publish fails the run.

### 5.3 The GitHub release

**ARM-REL-01. The draft carries four assets.** `Publish-Release.ps1` uploads both zips and both
checksum files in the one `gh release create`, and creates no draft if any of them is missing (D2).
*Check:* the draft lists four assets, and `-WhatIf` prints all four.

**ARM-REL-02. Both zips come from the tagged commit.** Both are built from `master` in the existing
build step, not built separately or carried over from an earlier run.
*Check:* each `.exe`'s version matches the tag.

**ARM-REL-03. `-Verify` checks both.** It downloads both zips and confirms, for each, the checksum
and the five root entries, and fails if either is missing. This applies to releases made after
this change: pointed at 1.0.21 or earlier, it reports the ARM64 zip as missing, and that is
accepted rather than special-cased.
*Check:* `-Verify` reports both, and a release lacking one fails.

**ARM-REL-04. `-Republish` replaces both.** A republish replaces all four assets, with no change to
what it deletes or how it confirms.
*Check:* after a republish, both zips carry the fix.

**ARM-REL-05. The release page helps someone choose.** `build/release-footer-template.md` lists both
zips, each with its download link, Windows requirement and SHA256, and says how to tell which you
need: **Settings > System > About > System type** reads "x64-based processor" or "ARM-based
processor". The template's placeholders cover both zips.
*Check:* the generated body has two download links that resolve and two checksums that match.

**ARM-REL-06. Existing links keep working.** The x64 asset name does not change, and the links to
`releases/latest` still lead to a page with both zips.
*Check:* the x64 name is unchanged in the 1.0.21 pattern.

### 5.4 The installer

The installer remains Windows PowerShell 5.1 compatible and needs no administrator
(`docs/Installer.md`).

**ARM-INS-01. An ARM64 zip stops on an x64 PC, before it changes anything.** The check runs before
`Copy-Payload`, which deletes the old install folder, so a working install is left alone. The
message says this zip is for ARM PCs and to download the x64 zip. There is no override.
*Check:* on an x64 PC with Marqora installed, the ARM64 zip's `Install.cmd` stops with the message,
and the installed copy still starts.

**ARM-INS-02. An x64 zip on an ARM PC installs, with a note.** It installs normally and shows one
line saying an ARM version is on the release page (D3). It asks nothing and adds no prompt, and
the line says nothing about speed (ARM-DOC-07).
*Check:* on the ARM PC, the x64 install completes, shows the note, and the app starts.

**ARM-INS-03. The installer asks Windows for the machine's real architecture.** It does not trust
`PROCESSOR_ARCHITECTURE` in its own process, which reports what an emulated process is pretending
to be, and it reads the zip's own architecture as D10 says.
*Check:* ARM-INS-01 and ARM-INS-02 both behave correctly on real hardware, with `Install.cmd` run by
double-click.

**ARM-INS-04. Switching between the two is an ordinary upgrade.** Installing the ARM64 zip over an
x64 install, or the reverse, replaces the app folder and keeps settings, recent files and snippets
in `%LOCALAPPDATA%\PaulTechGuy\Marqora`.
*Check:* on the ARM PC, install x64, change a setting, install ARM64: the setting survives and the
installed `Marqora.exe` is ARM64.

**ARM-INS-05. The WebView2 check works on ARM.** `Test-WebView2Runtime` finds the runtime on Windows
on ARM rather than warning falsely. Its registry paths have only been seen working on x64.
*Check:* on the ARM PC, the install shows no WebView2 warning.

**ARM-INS-06. The uninstaller and file associations work unchanged.**
*Check:* covered by the checklist in section 6.

### 5.5 Reader-facing text

**ARM-DOC-01. `build/installer/README.txt` is right for its zip.** The requirement line and the
file names in the checksum section match the zip it ships in: "Windows 11 on ARM" in the ARM64 zip
(D4), the existing line in the x64 zip. Its `win-x64` file names become a placeholder.
*Check:* each zip's README names that zip.

**ARM-DOC-02. `README.md` no longer says one zip.** The *Download* section says there are two and
how to choose, and the build section's zip name reflects both.

**ARM-DOC-03. The homepage does not claim less than is true.** `docs/index.html`'s download buttons
stay as they are, leading to the release page. A line near the main button says it covers x64 and
ARM PCs.

**ARM-DOC-04. The build documents are brought up to date.** `docs/Installer.md`, `docs/Releasing.md`
(including the smoke test and the asset count) and `build/ReadMe.md` describe two zips and the
ARM64 dev build.

**ARM-DOC-05. The release notes announce it.** A section in `docs/releases/vNext.md`, written as
the work lands, for someone deciding whether to download.

**ARM-DOC-06. The repository's text checks stay green.** `Set-AmericanSpelling.ps1 -Check`,
`Test-NetworkClaim.ps1 -Check`, and `Add-FileHeaders.ps1 -Check` if a `.cs` file was touched.

**ARM-DOC-07. No speed claims.** Nothing Marqora publishes says the ARM64 build is faster, or
compares its speed or battery use with the x64 build: not the installer, the release page, the
README, the homepage or the release notes. "Native" and "built for ARM" describe what it is, and
are the words to use.
*Check:* a read of the changed text before the first ARM64 release.

**ARM-DOC-08. Everything that states what Marqora runs on shows ARM64 support.** `README.md`: the
opening line, the *Download* section and the requirements. `docs/index.html`: the line under the
main download button, which says "one zip", and the footer's "Windows 11". The homepage is served
from `master`, so this text lands only after the full check has passed (ARM-TST-01): it goes live
when a release promotes `master`, before **Publish release** is clicked.
*Check:* a read of each place, and the text is not on `dev` before the full check has passed.

### 5.6 Testing

**ARM-TST-01. The first ARM64 release gets the full check.** All of section 6.1 and 6.2 passes on a
real Windows on ARM PC, using an ARM64 dev build, before the release is started, so a fault surfaces
while nothing is waiting on it. The draft asset then gets the short check under ARM-TST-02.

**ARM-TST-02. Every later release gets the short check.** Section 6.1 on the draft asset, before
publishing.

**ARM-TST-03. The checklist is a file.** It is kept as a markdown file with exact steps and sample
documents, not assembled in chat each time.

**ARM-TST-04. No test switch is added to the app.** The checks use the app as shipped and
marqora-cli's existing commands.

**ARM-TST-05. A fault found only on ARM holds the release.** Under D2 it holds both zips. It is
fixed on `dev` and the same version is republished, or the ARM64 work waits and the release goes
out without this feature.

**ARM-TST-06. The existing test suite stays on x64 and stays green** (D8).

---

## 6. The ARM checklist

Run on the real ARM PC. The short check uses the zip downloaded from the private draft, and the
full check uses an ARM64 dev build (ARM-TST-01).

### 6.1 Short check, every release

1. Run `Install.cmd` from the extracted ARM64 zip. No WebView2 warning.
2. Start Marqora from the Start menu. **Help > About** shows the version.
3. Task Manager's **Details** tab, with the **Architecture** column on, shows `Marqora.exe` as
   ARM64.
4. Open a document with a diagram and math. The source and the preview both draw.
5. `marqora-cli version` shows the version.
6. With marqora-cli, export one sample document to PDF and to Word, and open both.

### 6.2 Full check, first release only

The parts of the app that lean on Windows or on a native component:

1. **Editor and preview:** typing, find, a mode switch and a theme switch.
2. **Spell check:** a misspelling is underlined and offers corrections.
3. **Pictures:** a Folio with a large picture that has to be shrunk.
4. **Exports from the app:** PDF, Word, HTML, Folio, and Copy as PNG, SVG and rich text.
5. **Printing:** a paged print to a real or PDF printer, with a page range that does not start at
   page 1.
6. **Windows integration:** double-clicking a `.md` file, a second launch handing its file to the
   first, and the file and folder dialogs.
7. **Install paths:** ARM-INS-02 and ARM-INS-04, then Uninstall from **Settings > Apps**, and
   confirm user data is kept.

ARM-INS-01 is the one check that needs an x64 PC rather than the ARM one.

---

## 7. Files expected to change

| File | Why |
| --- | --- |
| `build/Publish.props` | ARM-BLD-01 |
| `src/PaulTechGuy.MQ.Cli/PaulTechGuy.MQ.Cli.csproj` | ARM-BLD-01 |
| `src/PaulTechGuy.MQ.WebRender/PaulTechGuy.MQ.WebRender.csproj` | ARM-BLD-01 |
| `build/New-Release.ps1` | ARM-BLD-03, ARM-BLD-05, ARM-GATE-01, ARM-GATE-03 |
| `build/New-DevBuild.ps1` | ARM-BLD-04 |
| `build/ReleaseCommon.ps1` | ARM-BLD-06, ARM-GATE-01, ARM-GATE-02, ARM-DOC-01 |
| `build/Publish-Release.ps1` | ARM-REL-01 to ARM-REL-05 |
| `build/release-footer-template.md` | ARM-REL-05 |
| `build/installer/Install.ps1` | ARM-INS-01 to ARM-INS-05 |
| `build/installer/README.txt` | ARM-DOC-01 |
| `README.md`, `docs/index.html` | ARM-DOC-02, ARM-DOC-03, ARM-DOC-08 |
| `docs/Installer.md`, `docs/Releasing.md`, `build/ReadMe.md` | ARM-DOC-04 |
| `docs/releases/vNext.md` | ARM-DOC-05 |

`src/PaulTechGuy.MQ.App/PaulTechGuy.MQ.App.csproj` needs no change: it takes the architecture from
`Publish.props`, and its target that trims language files already follows the architecture.

---

## 8. Open items

| # | Item | Needed by |
| --- | --- | --- |
| O1 | **Closed 2026-11-04.** Paul has access to an ARM64 laptop. It is not convenient, so ARM checks are batched rather than run as each change lands | |
| O2 | **Closed 2026-11-04.** Paul confirmed D5 to D10 | |
| O3 | **Closed 2026-11-04.** No speed wording anywhere: ARM-DOC-07 | |
| O4 | How much longer the release run takes with a second publish | **Closed.** The build step, tests included, took about four minutes on a warm tree. Recorded in `docs/Releasing.md` |
| O5 | The release footer still said "84 MB" in its checksum text | **Closed.** The size was removed, so it cannot go stale |
