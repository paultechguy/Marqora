# Marqora build scripts

Everything here is PowerShell 7 (`pwsh`) and everything is run from the repository root:

```powershell
pwsh .\build\<script>.ps1
```

All of them exit `0` on success and `1` on failure, so any one of them can gate a commit or a
CI step. Where a script takes `-Check`, that is the form CI wants: it writes nothing and exits
non-zero when there is something to report.

The one exception to "run from the root" is `ReleaseCommon.ps1`, which is a library and is
dot-sourced by the others rather than invoked.

---

## Checks

Run these before you commit. `Add-FileHeaders.ps1` and `Set-AmericanSpelling.ps1` rewrite the
tree when run with no arguments; `Test-ButtonStandards.ps1` and `Test-DocumentColors.ps1` only
ever report, and their `-Check` changes the exit code rather than the output.

| Script | What it does |
|---|---|
| `Add-FileHeaders.ps1` | Puts the copyright and SPDX license header on every hand-written `.cs` file under `src\` and `tests\`. Idempotent, so re-running is safe. `IDE0073` is a build warning, so a missing header costs you a build. |
| `Set-AmericanSpelling.ps1` | Rewrites British spellings to American ones. The word list is deliberately short and sits at the top of the script — add to it when a word actually turns up. |
| `Test-ButtonStandards.ps1` | Checks the app's buttons against `docs\Button-App-Standards.md`: inline sizes, unstyled buttons, hard-coded colors. The mechanical half only; the document's checklist covers the rest. |
| `Test-DocumentColors.ps1` | Checks that the callout colors and the highlight yellow agree between `webshell\app.css` and `src\PaulTechGuy.MQ.Domain`. The preview reads the stylesheet; the Word export builds a `.docx` with no browser in sight and cannot, so those few values are written twice. This is what keeps the copies honest. |

`Set-AmericanSpelling.ps1` reads `.cs`, `.xaml`, `.js`, `.css`, `.html`, `.json` and `.md`.
`.ps1` is not in that list, so the scripts in this folder — and their prose — are not covered
by it.

## Building something installable

Both produce the same zip: no wrapper folder, `Install.cmd` at its root, the .NET runtime and
the Windows App SDK inside it. The recipient extracts it and double-clicks `Install.cmd`. They
differ only in where the version comes from.

| Script | What it does |
|---|---|
| `New-DevBuild.ps1` | Builds the working tree as it stands under a version **you** name: `-Version 0.3.0-test4`. Writes no commits, no tags, and does not touch `Directory.Build.props`. For putting today's code on another machine. `-Version` is mandatory — without it the zip and the About box would be indistinguishable from the real release of the same number. |
| `New-Release.ps1` | The same build, taking its version from `Directory.Build.props`. This is the one command that produces something shippable, and it is step 2 of the release below. `-Test` gates it on a green test run. |

Both take `-ForceAssets`, `-OutputDirectory`, `-KeepStaging` and `-ShowBuildOutput`.

## Releasing

Two steps with your own work in between. The split is deliberate: nothing reaches GitHub until
the notes are written and committed.

| Script | What it does |
|---|---|
| `New-ReleaseNotes.ps1` | **Step 1.** Bumps `<Version>` in `Directory.Build.props` and scaffolds `docs\releases\v<version>.md` from the template. You then write the notes and commit both as an ordinary change. `-Check` asks whether a release could start, without starting one. |
| `Publish-Release.ps1` | **Step 2.** Promotes `master`, builds via `New-Release.ps1 -Test`, tags, and leaves a **draft** release on GitHub. Nothing it does is unrecoverable. `-Verify` re-checks a release you have already published; `-WhatIf` runs every gate for real and then prints the git and gh commands without running them. |

## Setup and one-offs

| Script | What it does |
|---|---|
| `Get-WebAssets.ps1` | Restores the third-party web assets the preview shell needs into `webshell\vendor\`, which is git-ignored. Run it after a fresh clone; the build scripts run it for you when it is missing. `-Force` re-downloads. |
| `New-AppIcon.ps1` | Rebuilds `MarqoraLogo.ico` from `MarqoraLogo.png` at seven sizes. Only after the logo changes. |
| `Register-FileAssociation.ps1` | Registers Marqora for `.md` and friends under `HKCU`. The installer does this for you; running it by hand is for working in the source tree. `-Unregister` undoes it. |

## Shared

| Script | What it does |
|---|---|
| `ReleaseCommon.ps1` | Not run directly. Dot-sourced by the four build and release scripts for the progress output, the `dotnet`/`git`/`gh` wrappers, the version helpers, the pre-flight gates, and the staging and zip steps. Defines functions and does nothing on its own. |

---

## Folders

Three of them, plus two loose templates.

| Folder | Checked in? | Holds |
|---|---|---|
| `installer\` | yes | The five files that ship inside every zip. `Install.cmd`, `Uninstall.cmd` and `README.txt` go to the zip's root; `Install.ps1` and `Uninstall.ps1` go to its `install\` folder, joined there by a copy of `Register-FileAssociation.ps1` from this folder. Windows PowerShell 5.1 compatible on purpose — pwsh 7 is not on a stock Windows install. |
| `artifacts\` | no | Release output. |
| `artifacts\dev\` | no | Dev build output, one floor below the release artifacts so a test zip is never picked up by hand in mistake for a real one. |

`release-notes-template.md` and `release-footer-template.md` sit beside the scripts. Both use
`{{TOKEN}}` substitution, the same idiom `installer\README.txt` uses for its version.

### What touches what

`.stage\` appears inside whichever artifacts folder is being written and is removed at the end
of the run unless `-KeepStaging` is passed.

| Script | `installer\` | `artifacts\` | `artifacts\dev\` | Outside `build\` |
|---|---|---|---|---|
| `Add-FileHeaders.ps1` | — | — | — | rewrites `.cs` under `src\`, `tests\` |
| `Get-WebAssets.ps1` | — | — | — | writes `webshell\vendor\` |
| `New-AppIcon.ps1` | — | — | — | writes `src\...\Assets\MarqoraLogo.ico` |
| `New-DevBuild.ps1` | reads all five | — | **writes** `Marqora-<version>-win-x64.zip` and `.zip.sha256`; `.stage\` while it runs | — |
| `New-Release.ps1` | reads all five | **writes** `Marqora-<version>-win-x64.zip` and `.zip.sha256`; `.stage\` while it runs | — | — |
| `New-ReleaseNotes.ps1` | — | — | — | writes `docs\releases\v<version>.md`; edits `Directory.Build.props` |
| `Publish-Release.ps1` | — | **writes** `release-body-<version>.md`; reads the zip and `.zip.sha256` that `New-Release.ps1` left | — | moves `master`, writes a tag, uploads a draft to GitHub. `-Verify` downloads to `%TEMP%` |
| `Register-FileAssociation.ps1` | — | — | — | writes `HKCU` only; no files |
| `ReleaseCommon.ps1` | reads, for its callers | writes, for its callers | writes, for its callers | — |
| `Set-AmericanSpelling.ps1` | — | excluded by design | excluded by design | rewrites the whole tree |
| `Test-ButtonStandards.ps1` | — | — | — | reads `src\PaulTechGuy.MQ.App\`, `webshell\` |
| `Test-DocumentColors.ps1` | — | — | — | reads `webshell\app.css`, `src\PaulTechGuy.MQ.Domain\` |

`Publish-Release.ps1` reaches `installer\` and both artifacts folders only through the scripts
it calls, which is why its own rows are otherwise empty. `Set-AmericanSpelling.ps1` skips
`installer\` because nothing in it carries an extension the script reads.
