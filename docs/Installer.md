# Marqora installer

Marqora ships as a zip that someone extracts and installs by double-clicking a `.cmd` file.
There is no MSIX, no certificate, no installer framework, and nothing that asks for an
administrator. This document records why it is shaped that way, because several of the
decisions look arbitrary until you know what forced them.

Everything lives in `build/`:

```
build/
    New-Release.ps1               the one command; produces the zip
    Register-FileAssociation.ps1  markdown file types (also useful on its own)
    installer/
        Install.cmd               double-clickable wrapper
        Uninstall.cmd             double-clickable wrapper
        Install.ps1               the actual installer
        Uninstall.ps1             the actual uninstaller
        README.txt                what the recipient reads
    artifacts/                    git-ignored output
```

---

## The one command

```powershell
pwsh .\build\New-Release.ps1          # → build\artifacts\Marqora-<version>-win-x64.zip
pwsh .\build\New-Release.ps1 -Test    # same, gated on a green test run
```

Measured on the 0.1.0 build: 632 files, about 225 MB published, 84 MB zipped.

Tests are opt-in rather than always-on. Repackaging is something you do repeatedly while
getting the installer right, and paying for the full suite on every iteration only teaches
you to stop running the script. `-Test` is for a release you are actually going to send
someone.

---

## What the recipient gets

The zip has no wrapper folder: its contents sit at the root, so extracting it into a folder
someone already made does not bury the release a level deeper. Explorer's **Extract All**
still proposes a destination named after the zip, which is where the double-click path
lands; only "Extract Here" in a third-party tool puts 632 files loose in Downloads.

```
README.txt        plain text, no markdown, because they cannot read markdown yet
Install.cmd       double-click this
Uninstall.cmd     or this
install/          Install.ps1, Uninstall.ps1, Register-FileAssociation.ps1
app/              the published application
```

After `Install.cmd`:

```
%LOCALAPPDATA%\Programs\Marqora\
    Marqora.exe
    Assets\web\...
    uninstall\
        Uninstall.ps1
        Register-FileAssociation.ps1
        install-manifest.json

%LOCALAPPDATA%\PaulTechGuy\Marqora\      settings, recent files, snippets, logs
                                          (written by the app; see AppPaths)
```

Plus a Start menu shortcut, a desktop shortcut, the markdown file associations, and an
entry under `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Marqora` so
**Settings > Apps** shows Marqora with a working Uninstall button.

---

## Decisions

| Decision | Chosen | Why not the alternative |
|---|---|---|
| Install location | `%LOCALAPPDATA%\Programs\Marqora` | `Program Files` needs elevation, and an *unsigned* installer asking for admin is the single scenario SmartScreen fights hardest. The associations are `HKCU` regardless, so a machine-wide install would buy the UAC prompt and none of the benefit. |
| Packaging | plain zip + scripts | MSIX needs a certificate. WiX/Inno add a build dependency and a second language for a job that is "copy a folder, write six registry keys". |
| Signing | none; strip the zone marker instead | A certificate is a few hundred dollars a year for a free tool. See **Mark of the web** below. |
| Shell | Windows PowerShell 5.1 | pwsh 7 is not on a stock Windows install. Needing to install a shell before you can install the app defeats the point. |
| User data on uninstall | kept unless `-RemoveUserData` | An upgrade is an uninstall followed by an install often enough that silently deleting hand-written snippets is a bad default. |
| What uninstall removes | read from a manifest | An install run with `-NoDesktopShortcut`, or into a non-default `-InstallDir`, has to uninstall correctly rather than by guesswork. |
| Association logic | one script, copied into the zip | `Register-FileAssociation.ps1` is useful on its own during development. Copying it at stage time means one source, and no chance of the shipped copy drifting from the one that gets exercised by hand. |

---

## Mark of the web

This is the part that earns its keep, and the part most likely to be removed by someone who
does not know what it is for.

Explorer stamps a `Zone.Identifier` alternate data stream on every file extracted from a
downloaded zip. **Both `Copy-Item` and `robocopy` carry that stream to the destination.** An
unsigned executable carrying one meets the full-screen "Windows protected your PC"
SmartScreen panel — not once, but on every launch.

`Install.ps1` therefore runs `Unblock-File` across the whole installed tree
(`Unblock-Payload`). The download is vetted once, at the zip, and the app then starts
normally forever after.

> This is not a security bypass performed behind the user's back. They chose to download
> the zip and chose to run the installer; what is removed is the marker on the copy *they
> just asked to be installed*. The source files in the extracted folder are untouched.

Because there is no signature to offer instead, `New-Release.ps1` writes a `.sha256`
alongside the zip. It is the only integrity signal this build can provide.

---

## New-Release.ps1

This produces the artifact. Getting that artifact onto GitHub — version bump, release notes,
promoting `dev` to `master`, tagging, the draft release — is
[`docs/Releasing.md`](Releasing.md), which calls this script as one of its steps.

Four steps, each one line of output:

1. **web assets** — runs `Get-WebAssets.ps1` if `webshell\vendor\monaco\vs\loader.js` is
   missing. That is deliberately the same sentinel the app project's `VerifyWebAssets`
   target checks, so the failure arrives at step one with an explanation instead of at the
   publish as an MSBuild error.
2. **publish** — `dotnet publish -c Release -o <staging>\app`.
3. **stage** — reads the version, assembles the release folder, expands `{{VERSION}}` in
   `README.txt`.
4. **zip** — `ZipFile.CreateFromDirectory` with `includeBaseDirectory: false`, so the
   staged folder's *contents* are the zip, not the folder itself. The staged folder is
   still named `Marqora-<version>-win-x64`, which is what `-KeepStaging` leaves behind.

Points worth keeping:

**The version comes from the built executable, not from `Directory.Build.props`.** The exe
is ground truth for what was actually produced; the props file is only consulted if the exe
carries no version at all. Build metadata after a `+` is trimmed — Settings > Apps shows
the string verbatim, and a commit hash in the version column helps nobody.

**Staging moves the publish output, it does not copy it.** Same volume, so it is a rename.
Copying 225 MB twice to produce one zip is time spent for nothing.

**The publish is sanity-checked before it is zipped.** Four files must exist:

```
Marqora.pri                            resolves every ms-appx:/// URI
App.xbf                                the compiled App.xaml
Assets\web\shell.html                  the preview shell
Assets\web\vendor\monaco\vs\loader.js  the editor
```

> The first two are carried into a publish folder by the MSIX packaging targets, which an
> unpackaged app does not import. `PublishWinUIResources` in the app project adds them by
> hand — see `docs/Architecture.md`. Without them the first `InitializeComponent` throws
> and the process dies before a window appears, **with no clue in the publish folder as to
> why**. Checking here turns a zip that fails on the recipient's machine into a build that
> fails on yours.

**dotnet output is captured, not streamed**, so the step lines stay one line each. The whole
log is printed on failure, which is the only time anyone wants it. `-ShowBuildOutput`
overrides this.

---

## Install.ps1

Five steps: prepare, copy, unblock, shortcuts and associations, register.

**The target directory is checked before anything is deleted.** An upgrade removes the old
directory rather than copying over it — overlaying a new build on an old one leaves
assemblies nothing references any more, and a self-contained .NET app that finds two
versions of the same assembly fails in ways that look nothing like the actual cause. Since
that means a recursive delete, `Resolve-InstallDirectory` proceeds only if the directory is
empty, holds a `Marqora.exe`, or holds an install manifest. Anything else is a typo in
`-InstallDir`, and deleting it would be an unpleasant way to find that out.

**robocopy, not `Copy-Item`.** 632 files move several times faster with `/MT:8`, and
robocopy is in `System32` on every supported Windows, so it is not a new dependency.

> robocopy speaks in a bit field: 0–7 are success, 8 and above are failures. It leaves
> `$LASTEXITCODE` non-zero on a perfectly good copy, so `Copy-Payload` normalises it.
> Anything downstream that checks `$LASTEXITCODE` breaks otherwise.

**A running Marqora is asked to close, never killed outright** — see **Closing a running
app** below.

**The association step verifies its outcome, not its exit code.** `Register-FileAssociation.ps1`
signals failure by exiting 1, and a script invoked with `&` does not surface that to its
caller. So `Register-Associations` checks that the ProgId's `shell\open\command` key exists
and points at the installed exe. That is both a better check and a check of the thing
actually wanted.

**The WebView2 runtime is probed and warned about, not installed.** Windows 11 ships it and
Windows 10 gets it through Windows Update, so it is nearly always present — but when it is
missing the app fails at the first preview, and a warning at install time is far cheaper
than that diagnosis.

---

## Uninstall.ps1

Four steps: close the app, remove shortcuts and associations, remove the Settings entry,
remove the app. Then user data, if asked.

### The sharp edge: deleting the directory you are running from

The Uninstall button in Settings runs a script that lives *inside* the directory it is about
to delete. Two facts matter:

- A `.ps1` is read into memory before it executes, so deleting the script file mid-run is
  harmless.
- **A process holding that directory as its working directory is not harmless.** `cmd.exe`
  in particular holds its working directory for its entire lifetime, and the delete then
  fails with a sharing violation that reads like a permissions problem.

`Uninstall.ps1` therefore copies its own folder to `%TEMP%` and re-runs from there whenever
`$PSScriptRoot` is under the install directory (`Invoke-Relaunch`). The child runs in the
same console, is waited on, and its exit code is passed back, so the caller sees one window
and one result. The parent removes the staging copy afterwards, since the staged copy cannot
delete itself.

The `Uninstall.cmd` shipped in the zip sidesteps the problem from the other direction: it
runs the copy of `Uninstall.ps1` in the *extracted folder*, not the installed one, and
`cd`s to `%TEMP%` first. Nothing is ever executing from inside the doomed directory.

> This is also why there is no `Uninstall.cmd` inside the install folder. `cmd.exe` reads a
> batch file incrementally rather than into memory, so a batch file that deletes its own
> directory errors out on its next line.

### Manifest-driven removal

`install-manifest.json` records what the install actually created:

```json
{
  "schema": 1,
  "product": "Marqora",
  "version": "0.1.0",
  "installedUtc": "2026-08-26T17:44:27.1564483Z",
  "installDir": "...",
  "shortcuts": ["...Start Menu\\Programs\\Marqora.lnk", "...Desktop\\Marqora.lnk"],
  "fileAssociations": true,
  "arpKey": "HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Marqora",
  "dataDirectory": "...\\PaulTechGuy\\Marqora"
}
```

The default shortcut locations are removed alongside the recorded ones, so an entry an
older installer never captured does not survive. Every manifest read goes through
`Get-ManifestValue`, because under `Set-StrictMode` reading a property that is not there is
an error, and meeting a manifest written by an older installer is a normal thing.

### One deliberate duplication

If `Register-FileAssociation.ps1` is missing, `Remove-Associations` deletes the association
keys inline instead. This is the only place in the tree where that duplication is tolerated:
an uninstaller that cannot find its helper must still finish. Registry entries left pointing
at a deleted executable make Explorer offer a broken handler for every markdown file, which
is worse than repeating fifteen lines.

---

## Closing a running app

Both scripts share this, and it was got wrong the first time.

`CloseMainWindow()` is sent first — the same request the window's close button sends — so
the app runs its own shutdown and gets to ask about unsaved documents. After fifteen
seconds, anything still running is either waiting on a save prompt or wedged, and **only
`-Force` decides which**.

> The first version made `-Quiet` imply a hard kill. Windows prefers `QuietUninstallString`
> where it can, so an uninstall started from Settings could have killed the app and
> discarded unsaved work with nothing on screen to say so. `-Quiet` controls output. It
> must never control whether the user's work survives.

---

## Constraints that will bite

- **Everything under `build/installer/` and `Register-FileAssociation.ps1` must run on
  Windows PowerShell 5.1.** No ternaries, no `??`, no `-File` niceties added in 7. Only
  `New-Release.ps1` may assume pwsh 7, because it runs on a developer machine.
- **Windows does not allow an app to make itself the default handler.** That choice is
  sealed with a per-user hash only the Settings UI can produce. The installer makes Marqora
  *available* and keeps its command line correct; choosing it is a one-time manual step by
  the user. Do not add code that claims to automate this.
- **`MultiSelectModel = Player` on the open verb.** Without it Explorer starts one process
  per selected file. See `Register-FileAssociation.ps1` for the full reasoning.
- **The `.cmd` files must stay BOM-free ASCII.** A UTF-8 BOM prefixes the first command and
  `@echo off` stops working.
- **The ARP key name and the ProgId are fixed.** Renaming either strands entries already
  written on a user's machine.

---

## Verified behaviour

The whole cycle was exercised against a real install on a real machine, not reasoned about:

| Scenario | Result |
|---|---|
| `Install.cmd` from an extracted zip | 632 files, shortcuts, associations, Settings entry |
| Installed app launched with a document | window opened, WebView2 rendered side-by-side, no errors logged |
| Zone markers stamped on payload, then installed | streams gone from the installed copy |
| Install run a second time over the first | stale file removed, user data kept, shortcuts not duplicated |
| Settings' exact `UninstallString`, app running | app closed gracefully, everything removed including the directory the script ran from |
| `Uninstall.cmd -RemoveUserData` | data removed only when asked |
| `Register-FileAssociation.ps1` under PowerShell 5.1 | register and unregister both correct |

---

## Deliberately left out

- **Code signing.** The cost is hard to justify for a free tool. Revisit if SmartScreen
  friction becomes a real complaint; the `Unblock-File` step would then be redundant but
  harmless.
- **A machine-wide install.** Would need elevation and would still leave associations
  per-user.
- **MSIX.** Needs a certificate, and the app is deliberately unpackaged — see
  `docs/Architecture.md`.
- **Auto-update.** An upgrade is "run the new `Install.cmd`", which is honest and has no
  background service, no update endpoint and no signing story to get wrong.
- **Delta updates.** The payload is 84 MB zipped. Not worth a patch format.
- **A `marqora` command on `PATH`.** Nothing has asked for it; adding to the user `PATH`
  from an installer is more intrusive than it looks.
- **A Start menu uninstall shortcut.** Windows users look in Settings > Apps, and the zip's
  `Uninstall.cmd` covers the rest.
