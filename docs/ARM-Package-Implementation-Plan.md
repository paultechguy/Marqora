# Marqora ARM64 package: implementation plan

The plan for building what `docs/ARM-Package-Requirements.md` asks for, and the place where
progress against those requirements is tracked.

Written 2026-11-04 against `dev` at `b32617a`, and revised the same day after the review logged in
section 11. Work packages are tracked in the todo list in section 2.4, and individual requirements
in the matrix in section 8.

---

## 1. Context

When this plan is finished, `New-Release.ps1` writes four files where it now writes two:

```
build\artifacts\Marqora-<version>-win-x64.zip
build\artifacts\Marqora-<version>-win-x64.zip.sha256
build\artifacts\Marqora-<version>-win-arm64.zip
build\artifacts\Marqora-<version>-win-arm64.zip.sha256
```

`Publish-Release.ps1` calls `New-Release.ps1`, so the release steps in `docs/Releasing.md` stay the
same commands, and the draft carries all four files. There is no switch that leaves ARM64 out of a
release (D2). `New-DevBuild.ps1` builds one zip, x64 unless asked for ARM64 (D7).

No C# file changes, unless the check on the ARM laptop finds a fault.

---

## 2. How to use this plan

### 2.1 Work packages

Work packages run in the order given. WP-1 to WP-7 need no ARM hardware. WP-8 is the one full
session on the ARM laptop, batched after them because the laptop is inconvenient. WP-9 waits for
WP-8, because it states publicly what WP-8 proves. WP-10 is the first release.

### 2.2 Status values

`Open`, `Built` (done and proven on the x64 PC), `Proven` (also passed on the ARM laptop),
`Withdrawn`.

### 2.3 Standing rules

- Nothing is committed or pushed until Paul asks. Each work package is left pending for review.
- The work stays on the branch Paul is on.
- `dev` is pulled before each work package starts, because other agents merge to it.
- No GitHub Actions, and no test switch in the app.
- Installer scripts stay Windows PowerShell 5.1 compatible. Build scripts are pwsh 7.
- No speed claims in any text (ARM-DOC-07).
- Every new release note goes in `docs/releases/vNext.md`.
- A new finding outside the named work goes to section 10, not into the change.

### 2.4 Todo

- [x] Requirements written, decisions D1 to D10 confirmed, amendments A1 to A3 applied
- [x] Review of this plan (section 11)
- [x] **WP-1** Choosing the architecture *(built, uncommitted, awaiting Paul's review)*
- [x] **WP-2** The shared release functions *(built, uncommitted)*
- [x] **WP-3** `New-Release.ps1` builds both *(built, uncommitted)*
- [x] **WP-4** `New-DevBuild.ps1` can build either *(built, uncommitted)*
- [x] **WP-5** The GitHub release *(built, uncommitted; `-WhatIf` still to run, section 3)*
- [x] **WP-6** The installer *(built, uncommitted; double-click checks are in WP-8)*
- [x] **WP-7** Build documents and the checklist *(built, uncommitted)*
- [x] **WP-8** The full check on the ARM laptop *(Paul: passed 2026-11-06, steps 1-12)*
- [x] **WP-9** Documents showing ARM64 support *(written, uncommitted; goes live when a release promotes `master`)*
- [ ] **WP-10** The first ARM64 release *(Paul)*

---

## 3. What the code says

Findings from reading the code and from trial builds, which the requirements did not know.

**The architecture can be chosen with one build option.** A single property, defaulted to `x64`
and read in three places. These are the final results, in the real tree and on scratch copies, with
the WebRender fix below in place.

| Check | Result |
| --- | --- |
| Default publish, against the unchanged code | 662 of 664 app files and 529 of 531 CLI files identical. The other two are `PaulTechGuy.MQ.WebRender.dll` and its `.pdb`, whose code is unchanged (below) |
| ARM64 publish, app and CLI | All native files ARM64, shared files identical |
| ARM64 gate form, warnings as errors, no incremental, app and CLI | 0 warnings, 0 errors |
| Flipping x64, ARM64, x64, ARM64 in one tree | Each repeat byte-for-byte identical to the first of its kind |
| `WebRender.dll` in every Release folder, after all of that | Each one is the architecture of its folder. Two copies in WebRender's own `bin` and `obj`, left from before the fix, are x64, and nothing reads them |
| Full x64 suite, straight after an ARM64 build, real tree | **2,372 of 2,372 pass** |
| Existing x64 release gate and a Debug build, real tree | Both clean |

**WebRender needs its own folders per architecture.** It names a platform target but had no runtime
identifier, so both architectures shared one `bin` and `obj`. The compiler does not treat a changed
platform target as a reason to rebuild, so an x64 build reused the ARM64 library: 43 of 355 CLI tests
failed with `Could not load file or assembly 'PaulTechGuy.MQ.WebRender'`. A publish hides this,
because it precompiles into a folder per architecture, which is why the first trials looked clean.
Left in, a release would have broken the tests it runs, and could have left a Release run from
Visual Studio unable to load WebRender. WP-1 gives WebRender a runtime identifier.

Adding that identifier changes the bytes of `WebRender.dll`. Before precompiling, the two compiled
libraries are the same size and differ by 120 of 122,880 bytes, in nine short runs, none of them in
the code: the header, the identity and debug fields and the `.pdb` path. The larger difference in
the shipped, precompiled file follows from those.

**Timings from those runs, on a warm tree.** ARM64 app publish 53 seconds, ARM64 CLI publish 21
seconds. That answers open item O4: expect a release run to take about two to three minutes longer,
counting the extra build gate. The real scripts have not been timed yet.

**WebRender does not import `Publish.props`.** So the shared default cannot live there. It goes in
`Directory.Build.props`, which every project reads.

**The solution cannot simply be built for ARM64.** `Test-GateBuild` builds the whole solution. Two
test projects name x64 outright and reference WebRender, so a solution-wide ARM64 option would pull
WebRender to ARM64 under an x64 test project. The ARM64 gate therefore builds the app and CLI
projects by name.

**`Add-CliToPublish` already takes `-ExtraArguments`,** and `Get-PublishedVersion` reads an ARM64
executable's version from its file properties on the x64 PC. Tested: it returned `1.0.21`.

**All 305 `.exe` and `.dll` files in the ARM64 publish read cleanly with .NET's PE reader,** so
ARM-GATE-01 can treat a file it cannot read as a failure rather than skip it.

**`Expand-Template` throws on any placeholder left unfilled, but only the footer uses it.** A
forgotten footer token fails the release run. `New-StagedRelease` fills `README.txt` with a plain
`.Replace`, so a forgotten token there would ship in the zip with no error. WP-2 closes that.

**`Install.cmd` ends in `pause`.** A refusal message stays on screen after a double-click.

**The zip name appears in several places in `Publish-Release.ps1`:** the confirmation panel, the
`-WhatIf` hints, the build check, the release body, the upload, and `-Verify`.

**`-Republish` needs no change of its own.** It deletes the draft and the tag, and the rest of the
run builds and uploads as normal. ARM-REL-04 is met by WP-5 and proven by running it.

**Results of WP-2 to WP-7,** from real runs on the x64 PC, written to temp folders so that
`build\artifacts` was not touched.

| Check | Result |
| --- | --- |
| `New-Release.ps1`, one run | Four files in 127 seconds on a warm tree. x64 zip 95 MB, ARM64 zip 88 MB, 677 files each. Both executables in each zip are that zip's architecture, and each README names its own zip |
| Header check on a real publish | 226 native and 82 CPU-neutral files, for both architectures |
| Injected into an ARM64 publish: an x64 DLL, a real x86 DLL, an x64 `WebRender.dll`, a text file named `.dll`, an empty `.exe` | Each fails, by name |
| `App.xbf` removed from the ARM64 publish, last run's four files present | Exit 1 naming `App.xbf`. New x64 zip, and no ARM64 zip left behind |
| `Add-CliToPublish` for ARM64 against a wrong app version | Fails, naming both versions and saying only the file's properties were read |
| ARM64 and default dev builds | Both pass, with the CLI in them and the dev banner in the README |
| `Test-GateBuild`, called directly | Clean for x64 and ARM64, in 52 seconds |
| Full x64 suite straight after that gate | **2,372 of 2,372 pass**, in 92 seconds |
| `Install.ps1` in Windows PowerShell 5.1, on copies stopped straight after the check | The x64 zip passes it. The ARM64 zip stops with the message and exit 1 |
| `-Verify` with a stand-in for `gh`, serving the real zips | Passes with all four. With the ARM64 zip missing, it names it and exits 1 |
| Footer filled from the real checksums | Reads correctly, and a missing token fails |
| Longest staged path under `build\artifacts` | 221 characters |

**Not yet proven, and why.** `Publish-Release.ps1 -WhatIf` refuses a dirty tree and a version
already released, so it can run only once this work is committed and the version bumped. Its
confirmation panel, build check and upload list have so far been read, not run. The same goes for
the real draft and `-Republish`, which WP-10 proves. The installer's double-click behavior is in the
checklist.

**A release run now takes about four minutes for the build step** (127 seconds of builds and 92 of
tests), which closes open item O4. A whole `Publish-Release.ps1` run also repeats the tests and the
build in its gates, so allow about six.

**Where the build differs from the text in section 6:**

- The README's zip placeholder holds the full name, `{{ZIP}}`, not a base name.
- `Test-GateBuild` takes the two projects as `-Executables`, so `Publish-Release.ps1` changed in WP-2.
- `New-Release.ps1` now names the zips from `Directory.Build.props` and fails if an executable
  reports another version. P4 needs the name before anything is built.
- The header check also refuses a .NET file that asks for 32 bits, which the IL-only flag alone
  would pass.
- The README's "95 MB" became "A large file", because the ARM64 zip is 88 MB.

**A bug the fault injection found.** A file that was not a real DLL failed the run with a confusing
error rather than its name, because PowerShell swallows what a property getter throws. Fixed in
`Assert-PublishArchitecture`.

---

## 4. Decisions taken while planning

| # | Decision | Why |
| --- | --- | --- |
| P1 | The property is `MarqoraArch`, with the values `x64` and `arm64` | Those exact words work as both the `PlatformTarget` and the end of the runtime identifier, so nothing has to translate between them |
| P2 | The list of architectures is stated once, in `ReleaseCommon.ps1` | `New-Release.ps1`, `Publish-Release.ps1` and the gates read one list, so they cannot disagree about what a release contains |
| P3 | Each architecture stages in its own folder, `.stage\win-x64` and `.stage\win-arm64` | Meets ARM-BLD-05 by construction, and `-KeepStaging` keeps both for inspection |
| P4 | `New-Release.ps1` deletes this version's zips and checksums before it starts | A failed run cannot leave a new x64 zip beside an old ARM64 one for `Publish-Release.ps1` to pick up |
| P5 | The tests run once, not once per architecture | They run on x64 either way (D8) |
| P6 | The installer reads the real architecture from the registry, at `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment`, value `PROCESSOR_ARCHITECTURE` | It needs no compiled helper. This is the one technical choice that only the ARM laptop can confirm. The fallback is `Win32_Processor`, and if WP-8 shows the registry is wrong, that is the change |
| P7 | If the installer cannot tell what the machine is, it carries on | An unexpected answer must not stop an install that would work. The check exists to catch one known mistake |
| P8 | The installer's note appears at the end of the install, beside "Installed to" | Shown first, it scrolls away during the install |
| P9 | The checklist is `docs/ARM-Release-Checklist.md` | ARM-TST-03 asks for a kept file. `docs/cli-tour/Guide.md` ships beside the app and has a diagram, so it serves as the sample document |
| P10 | WebRender gets separate folders per architecture, rather than a forced recompile when the architecture changes | Separate folders cannot be stale, and they match how the two executables already work. A forced recompile would rebuild on every switch and depend on nothing else ever sharing the folder |

---

## 5. Target shape

```
Directory.Build.props      MarqoraArch, default x64
    |
    +-- build/Publish.props             RuntimeIdentifier = win-$(MarqoraArch)
    +-- Cli and WebRender projects      PlatformTarget   = $(MarqoraArch)

New-Release.ps1
    web assets, tests            once
    remove this version's old zips and checksums
    for each architecture:
        publish app, publish CLI, compare shared files,
        check every native file, stage, zip, checksum

Publish-Release.ps1
    gates: build x64 solution, then app and CLI for arm64
    promote, New-Release.ps1 -Test, check all four files,
    tag, release body, draft with four assets
```

---

## 6. Work packages

### WP-1: Choosing the architecture

**Status:** Built, uncommitted.

**Serves:** ARM-BLD-01, ARM-BLD-02, ARM-BLD-07.

**Files:** `Directory.Build.props`, `build/Publish.props`,
`src/PaulTechGuy.MQ.Cli/PaulTechGuy.MQ.Cli.csproj`,
`src/PaulTechGuy.MQ.WebRender/PaulTechGuy.MQ.WebRender.csproj`.

**Changes:**

- `Directory.Build.props` gets `MarqoraArch`, defaulting to `x64` when nothing sets it, with a
  comment saying who reads it.
- `Publish.props` sets `RuntimeIdentifier` to `win-$(MarqoraArch)`.
- Both projects set `PlatformTarget` to `$(MarqoraArch)`.
- The comment in the WebRender project that says "x64 named outright" is rewritten. Its warning
  still holds: AnyCPU would ship the wrong native component.
- WebRender also sets `RuntimeIdentifier` to `win-$(MarqoraArch)`, with a comment saying why (P10).

The two test projects keep naming x64. They are not changed.

**Verified by:** the table at the top of section 3.

### WP-2: The shared release functions

**Status:** Built, uncommitted.

**Serves:** ARM-GATE-01, ARM-GATE-02, ARM-GATE-03, ARM-BLD-06, ARM-DOC-01.

**Files:** `build/ReleaseCommon.ps1`, `build/installer/README.txt`.

**Changes:**

- A single list of the release architectures, and a helper that gives a zip's name from a version
  and an architecture (P2).
- `Assert-PublishArchitecture -PublishDir -Architecture`, new. It reads every `.exe` and `.dll` with
  `System.Reflection.PortableExecutable.PEReader`. A file passes if its machine type matches, or if
  it is CPU-neutral, meaning machine type x86 **and** the IL-only flag set. A real x86 DLL has the
  same machine type and no such flag, so looking at the machine type alone would pass it. A file it
  cannot read fails. Failures are listed by name. It also catches the WebRender fault if it ever
  returns, because an ARM64-flagged library in an x64 publish has the wrong machine type.
- `Add-CliToPublish` gets `-Architecture`. It passes the option to the publish, and it runs
  `marqora-cli.exe version` only when this machine can run that architecture. Otherwise it compares
  the file-property version alone, and its summary says so.
- `Test-GateBuild` builds the solution as it does now, then the app and CLI projects for ARM64 with
  the same `-warnaserror --no-incremental`.
- `New-StagedRelease` fills two new README placeholders: the zip's base name and its Windows
  requirement line. Its default `win-x64` value is removed so that no caller can forget it.
- `New-StagedRelease` fills `README.txt` through `Expand-Template`, so an unfilled placeholder fails
  the build rather than reaching a zip.
- `README.txt` uses those placeholders in the requirements and checksum sections. The requirement
  placeholder covers both of today's lines, the Windows version and "64-bit (x64)".

**Verified by:** a real x64 and ARM64 build through WP-3, then fault injection on a staged folder:
an x64 DLL, and a real x86 DLL, each placed in the ARM64 publish, both failing by name.

### WP-3: `New-Release.ps1` builds both

**Status:** Built, uncommitted.

**Serves:** ARM-BLD-03, ARM-BLD-05, ARM-GATE-01, ARM-GATE-03.

**Files:** `build/New-Release.ps1`.

**Changes:**

- The publish, CLI, stage, zip and checksum steps move into a loop over the architecture list,
  each with its own staging folder (P3).
- Old zips and checksums for this version are removed first (P4).
- `Assert-PublishArchitecture` runs after the CLI is merged in, so one pass covers both executables.
- The required-files and welcome-document checks stay inside the loop.
- Progress counts follow the new number of steps. The closing output lists both zips and both
  hashes, and says which zip goes to which kind of PC.
- The comment block at the top says two zips.

**Verified by:** one run leaving four files, and each zip's `app\Marqora.exe` being the right
architecture. A forced failure in the second architecture leaves no zip claiming to be finished for
that architecture, and the script exits 1.

### WP-4: `New-DevBuild.ps1` can build either

**Status:** Built, uncommitted.

**Serves:** ARM-BLD-04.

**Files:** `build/New-DevBuild.ps1`.

**Changes:** a `-Architecture` parameter accepting `x64` or `arm64`, defaulting to `x64`. It passes
the option to both publishes and to `Add-CliToPublish`, and runs `Assert-PublishArchitecture`.

**Verified by:** an ARM64 dev build with the CLI in it and the dev banner in its README, and a
default dev build behaving as it does today.

### WP-5: The GitHub release

**Status:** Built, uncommitted. `-WhatIf` waits for a commit and a version bump (section 3).

**Serves:** ARM-REL-01 to ARM-REL-06.

**Files:** `build/Publish-Release.ps1`, `build/release-footer-template.md`.

**Changes:**

- `$zipName`, `$zipPath` and `$shaPath` become a list built from the shared architecture list.
- The confirmation panel names both zips. The check after the build requires all four files.
- The upload passes all four, and its progress line counts them.
- `-Verify` loops over both zips, checking the checksum and the five root entries of each. A
  release without an ARM64 zip fails it (see amendment A2).
- The footer gets a **Which one do I need?** section, using **Settings > System > About > System
  type**, and two download lines: x64 with the existing Windows line, ARM64 with "Windows 11 on
  ARM". Its tokens become `ZIP_X64`, `ZIP_ARM64`, `SHA256_X64`, `SHA256_ARM64`, `URL_X64` and
  `URL_ARM64`.
- The footer's "84 MB" goes, closing open item O5. The size is removed rather than updated, so it
  cannot go stale again.
- `Publish-Release.ps1`'s comment block and "both assets" wording are updated, as is the `-WhatIf`
  hint that says the build takes `~3 minutes`.

**Verified by:** `-WhatIf`, which runs every gate and prints all four files with no change to
anything. The real draft, `-Verify` and `-Republish` are proven at the first release, in WP-8.

### WP-6: The installer

**Status:** Built. The check is proven on x64 (section 3), and the double-click passed as step 12 of
the checklist.

**Serves:** ARM-INS-01 to ARM-INS-06.

**Files:** `build/installer/Install.ps1`.

**Changes:**

- `Get-PayloadArchitecture` reads the two-byte machine type from the header of `app\Marqora.exe`
  (D10). That file is always native, so the CPU-neutral case does not arise here.
- `Get-MachineArchitecture` reads the registry value in P6, whose expected values are `AMD64` and
  `ARM64`.
- A check runs straight after the version is read, ahead of the WebView2 check and well ahead of
  `Copy-Payload`:
  - **ARM64 zip, x64 machine:** stops with a non-zero exit. "This is the version of Marqora for ARM
    PCs, and this PC is x64. Download the win-x64 zip from
    https://github.com/paultechguy/Marqora/releases/latest. Nothing has been changed."
  - **x64 zip, ARM64 machine:** carries on and remembers to show the note.
  - **Anything else:** carries on (P7).
- The note appears beside "Installed to" (P8): "This PC has an ARM processor. A version of Marqora
  built for ARM PCs is available: the win-arm64 zip at
  https://github.com/paultechguy/Marqora/releases/latest".

`Uninstall.ps1` and the file associations do not change.

**Verified by:** on the x64 PC, an ARM64 dev zip's `Install.cmd` stops with the message, and the
installed Marqora is untouched and starts. The ARM-side behavior is proven in WP-8. Paul runs this
one, because it is pointed at the Marqora installed on his own PC.

### WP-7: Build documents and the checklist

**Status:** Built, uncommitted.

**Serves:** ARM-DOC-04, ARM-DOC-06, ARM-TST-03.

**Files:** `docs/Installer.md`, `docs/Releasing.md`, `build/ReadMe.md`, and a new
`docs/ARM-Release-Checklist.md`.

**Changes:**

- `docs/Installer.md`: the one command, two zips, the installer's architecture check, and why it
  sits ahead of `Copy-Payload`.
- `docs/Releasing.md`: four assets, the ARM laptop step in the smoke test, and the measured run time.
- `build/ReadMe.md`: the file tables and `-Architecture`.
- `docs/ARM-Release-Checklist.md`: section 6 of the requirements as exact steps. `Guide.md` has a
  diagram and no math, so the checklist says to add one line of math to a scratch copy.

**Verified by:** `Set-AmericanSpelling.ps1 -Check` and `Test-NetworkClaim.ps1 -Check`.

### WP-8: The full check on the ARM laptop

**Serves:** ARM-TST-01, ARM-TST-05, ARM-INS-02 to ARM-INS-06.

**This is Paul's, and with WP-10 it is the only hand work in the plan.**

1. Build `pwsh .\build\New-DevBuild.ps1 -Version <n>-arm1 -Architecture arm64`, plus an x64 dev
   build, and take both zips to the laptop.
2. **One session, using the checklist:** the full check, plus installing x64 first to prove the note
   and then ARM64 over it to prove the switch.
3. **Faults** are fixed on `dev` and only the affected steps are rerun, not the whole list.

Rough size of Paul's time, as an estimate: about an hour for step 2, and ten minutes per release
after that. Step 2's length depends mostly on how long a paged print and the exports take.

### WP-9: Documents showing ARM64 support

**Serves:** ARM-DOC-02, ARM-DOC-03, ARM-DOC-05, ARM-DOC-06, ARM-DOC-07, ARM-DOC-08.

**Depends on WP-8 passing.** The homepage is served from `master/docs`, so this text goes live when
a release promotes `master`, before **Publish release** is clicked. It must not reach `dev` until
the laptop has proven it true.

**Files and changes:**

| Where | Today | Becomes |
| --- | --- | --- |
| `README.md` line 7, opening line | "for Windows 11" | Says x64 and ARM64 |
| `README.md`, *Download* | "one zip, around 95 MB" | Two zips, and how to tell which you need |
| `README.md`, a new line in *Download* | Nothing | What each zip requires: Windows 10 1809 or later for x64, Windows 11 on ARM for ARM64 |
| `README.md`, build section, around line 113 | Names the x64 zip | Names both |
| `docs/index.html` line 231, under the main button | "one zip, about 95 MB" | "x64 and ARM64", with the size wording corrected |
| `docs/index.html` line 516, footer | "Windows 11" | "Windows 11 · x64 and ARM64" |
| `docs/releases/vNext.md` | Nothing | A section announcing the ARM64 download |

The *Getting started* requirements in `README.md` are for building from source, so they stay as they
are.

**Checked and left alone,** because they stay true: `Welcome to Marqora.md` and the About dialog
("for Windows 11"), `docs/CLI.md` (states no platform), and `docs/index.html`'s meta description and
"Download for Windows" buttons. `docs/Sponsors.md` and `docs/cli-tour` state no platform.

**Verified by:** `Set-AmericanSpelling.ps1 -Check`, `Test-NetworkClaim.ps1 -Check`, and a read for
speed wording.

### WP-10: The first ARM64 release

**Serves:** ARM-TST-02, and the real-draft proof of ARM-REL-01 to ARM-REL-05.

**This is Paul's.** The usual release steps, then the short check on the actual draft asset on the
laptop, then **Publish release**, then `-Verify`.

---

## 7. Amendments to the requirements

Paul accepted all three on 2026-11-04, and they are applied to `docs/ARM-Package-Requirements.md`.

| # | Requirement | Change | Why |
| --- | --- | --- | --- |
| A1 | ARM-TST-01 | The full check runs on an ARM64 **dev build** before the release, and only the short check runs on the draft | Written as it stands, the full check happens on the draft, so a fault found there means a fix, a republish and a second trip to the laptop with the release held open. Run early, faults surface while nothing is waiting on them. The draft still gets the short check, which is what proves the shipped file |
| A2 | ARM-REL-03 | State that `-Verify` applies to releases made after this change. Pointed at 1.0.21 or earlier, it reports the ARM64 zip as missing | Those releases do not have one. Keeping a special case for old versions would be code with no future use |
| A3 | ARM-BLD-06 | "Compiles the ARM64 target" means the app and CLI projects, not the solution | Section 3: the test projects stay x64 |

---

## 8. Traceability matrix

| Requirement | Work package | Verified by | Status |
| --- | --- | --- | --- |
| ARM-BLD-01 | WP-1 | ARM64 publish leaves no tracked file changed | Built |
| ARM-BLD-02 | WP-1 | Default publish matches current output; tests green | Built |
| ARM-BLD-03 | WP-3 | One run writes four files | Built |
| ARM-BLD-04 | WP-4 | ARM64 and default dev builds | Built |
| ARM-BLD-05 | WP-3 | Separate staging; architecture check on both zips | Built |
| ARM-BLD-06 | WP-2 | `-WhatIf` runs the ARM64 gate | Built; gate run directly, `-WhatIf` to come |
| ARM-BLD-07 | WP-1 | Full x64 suite straight after an ARM64 build | Built |
| ARM-GATE-01 | WP-2, WP-3 | Fault injection, x64 and real x86 DLLs | Built |
| ARM-GATE-02 | WP-2 | ARM64 run passes; wrong CLI version fails | Built |
| ARM-GATE-03 | WP-2, WP-3 | Removing `App.xbf` fails the ARM64 run | Built |
| ARM-REL-01 | WP-5, WP-10 | `-WhatIf`, then the real draft | Open: built, not yet run |
| ARM-REL-02 | WP-5, WP-10 | Both `.exe` versions match the tag | Open |
| ARM-REL-03 | WP-5, WP-10 | `-Verify` after the first release | Built; run with a stand-in for `gh` |
| ARM-REL-04 | WP-5, WP-10 | First `-Republish`, whenever one occurs | Open |
| ARM-REL-05 | WP-5, WP-10 | Generated body read; links resolve at the draft | Built; links resolve at the draft |
| ARM-REL-06 | WP-5 | x64 name unchanged | Built |
| ARM-INS-01 | WP-6 | ARM64 zip on the x64 PC | Passed |
| ARM-INS-02 | WP-6, WP-8 | x64 zip on the laptop | Passed |
| ARM-INS-03 | WP-6, WP-8 | Both of the above, by double-click | Passed |
| ARM-INS-04 | WP-8 | x64 then ARM64 on the laptop | Passed |
| ARM-INS-05 | WP-8 | No false warning on the laptop | Passed |
| ARM-INS-06 | WP-8 | Uninstall and associations on the laptop | Passed |
| ARM-DOC-01 | WP-2 | Each zip's README read | Built |
| ARM-DOC-02 | WP-9 | Read | Built |
| ARM-DOC-03 | WP-9 | Read | Built |
| ARM-DOC-04 | WP-7 | Read | Built |
| ARM-DOC-05 | WP-9 | Read | Built |
| ARM-DOC-06 | WP-7, WP-9 | The check scripts | Built |
| ARM-DOC-07 | WP-9 | Read for speed wording | Built |
| ARM-DOC-08 | WP-9 | Read each place; lands after WP-8 | Built |
| ARM-TST-01 | WP-8 | Checklist record | Passed |
| ARM-TST-02 | WP-10 | Checklist record, each release | Open |
| ARM-TST-03 | WP-7 | The file exists | Built |
| ARM-TST-04 | All | No test switch in the diff | Built, to WP-7 |
| ARM-TST-05 | WP-8 | Applies if a fault is found | No fault found |
| ARM-TST-06 | WP-1, each WP | Test suite green | Built, to WP-7 |

---

## 9. Risks

| Risk | What happens | Answer |
| --- | --- | --- |
| The app has a fault that appears only on ARM | The feature waits, or a C# fix joins the work | WP-8 runs before any release, so nothing is held. The riskiest parts are listed in the checklist's full section |
| The registry value in P6 is wrong under emulation | The note or the refusal misfires | WP-8 covers it, and `Win32_Processor` is the known fallback |
| A later package drops its ARM64 payload | The publish or ARM-GATE-01 fails | Fails on the x64 PC at build time, which is the intent |
| A future dependency brings native files with only an x64 copy | ARM-GATE-01 fails and names the file | Same |
| A release leaves ARM64 as the last build, so `project.assets.json` in the shared `obj` folders names `win-arm64` | Visual Studio could report `NETSDK1047` on its next build if it does not restore first. Not seen and not tested. `dotnet` restores every time, and the x64 suite passed after an ARM64 build | If it appears, a restore clears it. Listing both runtime identifiers would be the lasting fix |
| The ARM laptop is not available at release time | Under D2, the release waits | Accepted when D1 and D2 were chosen. The short check is kept short for this reason |

---

## 10. Backlog

Findings that are outside this plan, for Paul to pick from.

- `New-DevBuild.ps1` has an "84 MB" comment that is out of date. Harmless.
- `New-Release.ps1` and `New-DevBuild.ps1` repeat most of their publish steps. Worth merging one day,
  but not as part of this work.
- Help > About shows the .NET runtime but not the processor architecture. Showing it would help when
  someone reports a problem, but it is a C# change, so it stays out of this plan.
- `PaulTechGuy.MQ.Exporting.Tests` fails 9 of 19 with `The shell did not report ready` in a fresh copy
  of the repository under the temp folder, and passes 19 of 19 in the real tree. It failed the same
  way with no ARM64 build involved, run alone or with the other tests. The cause was not looked for,
  and it was not tried on unchanged code, so whether it predates this work is not known. It would
  matter to anyone running the tests from a fresh clone.
- `CLI` tests need the git-ignored `tests/baselines/cli/preferences.json`, so 2 of them fail in a
  copy made with `git archive`, and presumably in a fresh clone.

---

## 11. Review log

A review on 2026-11-04, attacking the plan's evidence first and then its design. It was done
directly, with no separate reviewers, as Paul asked for a light one.

| # | Finding | Outcome |
| --- | --- | --- |
| R1 | **The claim that x64 and ARM64 builds do not mix was wrong.** It rested on publishes alone. A plain x64 build or test run after an ARM64 build reused the ARM64 WebRender library, and 43 CLI tests failed | Fixed in WP-1 (P10). Added ARM-BLD-07. Section 3 and the requirements corrected |
| R2 | "Matches the current output" had never been compared with a real baseline | Compared. Identical apart from WebRender, whose code is unchanged |
| R3 | `README.txt` is filled by a plain replace, so a forgotten placeholder would ship silently. The plan had implied the safety net covered it | WP-2 moves it to `Expand-Template` |
| R4 | The `-WhatIf` hint in `Publish-Release.ps1` says `~3 minutes` and would go stale | Added to WP-5 |
| R5 | The homepage is served from `master`, so a support claim goes live when a release promotes it, before **Publish release** | WP-9 split out, to land after WP-8 passes. Added ARM-DOC-08 |
| R6 | The requirements said the checklist's CLI steps go first. They are last | Sentence removed |
| R7 | A solution-wide ARM64 build gate would pull WebRender to ARM64 under x64 test projects | Already covered by A3, and the gate form was tested |

**Claims attacked that held:** nothing else depends on the zip name. No script passes `--no-build` or
`--no-restore`. The longest staged path is 221 characters, or about 233 with a long dev version,
against the 260 limit. The installer reads version information from the files and never runs them.

**During the review** the PC stopped with a blue screen (`0x3B`) as the first test run began. Paul
attributes it to a known fault in the AMD graphics driver. It nulled files in that scratch copy, which
was discarded. The real tree was checked and undamaged. The later runs completed without a crash.
