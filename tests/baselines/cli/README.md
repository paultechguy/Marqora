# CLI export baseline

What the app exports before the CLI work changes any of its code, kept so that the refactor in
M2 can prove it changed nothing (`docs/CLI-Implementation-Plan.md`, WP-M0-05 and CLI-ARC-06),
and so the CLI's own exports can be compared with the app's.

Once made, nothing in this folder is edited or re-exported over; a new baseline is a new
decision.

The exports are git-ignored: they are large and they are evidence, not source. What is
committed is this file, the input in `inputs/`, and `MANIFEST.sha256`, which proves later that
the exports are still the ones that were made.

## Which app makes them: Marqora 1.0.13

The baseline is made with **the release tagged `v1.0.13`**, built from source, and not with the
app built from `feature/CLI`.

- **Why not `feature/CLI`.** It carries the CLI's own changes to the app (the shared web-asset
  copy and the shell's `requestRenderSettled` message). Neither should change an export, but a
  baseline is only worth having if it was made by code the CLI work has not touched at all.
- **Why not an older commit such as `eab7281`.** `dev` was merged into `feature/CLI` after it
  (`17a537e`), bringing about 670 lines of app and shell changes with it: among them the
  flowchart-label separation, which changes how diagrams come out. A baseline from before them
  would differ from every later export for reasons that have nothing to do with the CLI.
- **`v1.0.13` is both.** It is `dev`'s tip, the code `feature/CLI` was last merged with, and the
  only app code the two differ by is the CLI's. Being a released tag, it can be rebuilt exactly
  at any time.

If `dev` is merged into `feature/CLI` again before the baseline is made, use the newest commit
of `dev` that has been merged instead, for the same reason.

## Three folders, and what each is for

The old app runs from a separate folder, a **git worktree**, so that your own checkout never
leaves `feature/CLI`. That matters: this README, the input `inputs/pictures.md` and the hash
script exist only on `feature/CLI`. A checkout switched to `v1.0.13` would not have any of them.

| Folder | Holds | Used for |
| --- | --- | --- |
| `C:\Users\pcarver\Documents\MyData\Home\Software\dotnet\PaulTechGuy.Marqora` | your checkout, on `feature/CLI` throughout | the documents you open, **the folder every export is saved into**, recording the result |
| `C:\Users\pcarver\Documents\MyData\Home\Software\dotnet\PaulTechGuy.Marqora-baseline` | a worktree at `v1.0.13`, made for this and removed after | **running the 1.0.13 app**, and nothing else |
| `%LOCALAPPDATA%\PaulTechGuy\Marqora` | the app's settings and browser data, shared by every Marqora build | nothing to do: it is why only one Marqora may run at a time |

**Never save an export into the worktree.** It has no `tests\baselines` folder, and it is deleted
at the end.

## 1. Build Marqora 1.0.13 in a worktree

In PowerShell, from your own checkout:

```powershell
cd C:\Users\pcarver\Documents\MyData\Home\Software\dotnet\PaulTechGuy.Marqora
git fetch --tags
git worktree add --detach ..\PaulTechGuy.Marqora-baseline v1.0.13

cd ..\PaulTechGuy.Marqora-baseline
pwsh .\build\Get-WebAssets.ps1
dotnet build .\src\PaulTechGuy.MQ.App\PaulTechGuy.MQ.App.csproj -c Release
```

- `--detach` makes the worktree with no branch: it sits on the tag and nothing is created that
  needs deleting later besides the folder.
- `Get-WebAssets.ps1` is needed because `webshell\vendor` is git-ignored and a new worktree
  starts without it; the build stops with a message if it is missing.
- Your checkout is untouched by all of this: `git branch --show-current` there still says
  `feature/CLI`.

## 2. Run it

1. **Close any Marqora that is running.** Both builds use the same settings and browser data in
   `%LOCALAPPDATA%\PaulTechGuy\Marqora`, and two at once fight over it.
2. **Start the 1.0.13 build:**

   ```powershell
   & C:\Users\pcarver\Documents\MyData\Home\Software\dotnet\PaulTechGuy.Marqora-baseline\src\PaulTechGuy.MQ.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\Marqora.exe
   ```

3. **Check `Help > About Marqora` says 1.0.13.** It may reopen the documents you last had open,
   from your own settings; close them or leave them, they are not part of the baseline.

## 3. Prepare

1. **Open the three documents from your own checkout**, by their full paths, not the worktree's
   copies: the CLI later exports exactly these files, so the baseline must be made from them.
   They are inputs and must stay byte-identical: do not edit, format or save any of them. If the
   app asks to save, say no.
   - `C:\Users\pcarver\Documents\MyData\Home\Software\dotnet\PaulTechGuy.Marqora\webshell\cheatsheet.md`
   - `C:\Users\pcarver\Documents\MyData\Home\Software\dotnet\PaulTechGuy.Marqora\docs\UltimateMarkdownContent.md`
   - `C:\Users\pcarver\Documents\MyData\Home\Software\dotnet\PaulTechGuy.Marqora\tests\baselines\cli\inputs\pictures.md`
2. **Set the export theme to "Same as the screen"** in Preferences, so that `View > Color Theme`
   alone decides what each export wears.
3. **Leave every export dialog as it opens**, and make no change in any of them: the same page
   setup, cover, contents and Word layout for every file. The Preferences file exported at the
   end records what those were, and the CLI uses that file to make the same exports.
4. **Save every file into your own checkout's baseline folder**, under exactly the name given:

   `C:\Users\pcarver\Documents\MyData\Home\Software\dotnet\PaulTechGuy.Marqora\tests\baselines\cli\`

   The same folder as this README, not `inputs\` below it.

## 4. Export

Tick each as it is saved. `build/Get-BaselineHashes.ps1` reads the names from this list, so a
name typed wrongly shows up as missing.

### Color theme: Default

`View > Color Theme > Default`.

- [x] `cheatsheet.default.html` — cheatsheet, `File > Export > HTML`
- [x] `cheatsheet.default.pdf` — cheatsheet, `File > Export > PDF`
- [x] `cheatsheet.default.docx` — cheatsheet, `File > Export > Word`
- [x] `UltimateMarkdownContent.default.html` — UltimateMarkdownContent, HTML
- [x] `UltimateMarkdownContent.default.pdf` — UltimateMarkdownContent, PDF
- [x] `UltimateMarkdownContent.default.docx` — UltimateMarkdownContent, Word
- [x] `pictures.default.html` — pictures, HTML
- [x] `pictures.default.pdf` — pictures, PDF
- [x] `pictures.default.docx` — pictures, Word

Then `File > Share as Folio...`, with exactly the three documents ticked, nothing fetched from the
web, and "Shrink images wider than" **unticked**:

- [x] `folio.default.html` — the single-file form
- [x] `folio.default.zip` — the zip form
- [x] `folio.default.folder` — the folder form (a folder of that name)

And once more with "Shrink images wider than" **ticked**, at its default of 1600:

- [x] `folio.default.reduced.html` — the single-file form
- [x] `folio.default.reduced.zip` — the zip form
- [x] `folio.default.reduced.folder` — the folder form

### Color theme: Vintage Press

`View > Color Theme > Vintage Press`. One document is enough to show a second palette arriving
in every format; the content checks above do not need repeating.

- [x] `cheatsheet.vintage-press.html` — cheatsheet, HTML
- [x] `cheatsheet.vintage-press.pdf` — cheatsheet, PDF
- [x] `cheatsheet.vintage-press.docx` — cheatsheet, Word
- [x] `folio.vintage-press.html` — Folio of the three, single-file form, shrinking unticked

### The settings that made them

- [x] `preferences.json` — `Preferences > Export preferences...`, saved last, so it holds the
  dialogs' settings as they stood for every export above

Then close the 1.0.13 app. The color theme and the export-theme choice you changed are saved in
your own settings, which every build shares: set them back the next time you open your usual
Marqora.

## 5. Record

In your own checkout, still on `feature/CLI`, **while the worktree still exists**:

```powershell
cd C:\Users\pcarver\Documents\MyData\Home\Software\dotnet\PaulTechGuy.Marqora
pwsh .\build\Get-BaselineHashes.ps1
```

It checks every name above is present, writes `MANIFEST.sha256`, and prints the line for the
plan's §9.3 record. It reads the version and commit from the worktree's `Marqora.exe`, so the
line should say Marqora 1.0.13 built from `0568b2c`, the commit `v1.0.13` points at. If the
worktree is somewhere else, pass the app with `-App <path to Marqora.exe>`.

Commit `MANIFEST.sha256` and this file, with the boxes ticked. The exports themselves are not
committed.

## 6. Remove the worktree

Once the manifest is written:

```powershell
cd C:\Users\pcarver\Documents\MyData\Home\Software\dotnet\PaulTechGuy.Marqora
git worktree remove --force ..\PaulTechGuy.Marqora-baseline
git worktree prune
```

`--force` is needed because the build outputs and the restored web assets make the worktree
"dirty"; nothing in it is worth keeping. The 1.0.13 app can be rebuilt from the tag any time.

## Known gaps in this baseline

Made 2026-10-26 with Marqora 1.0.13, and kept as it is by decision, with two cases it does not
cover. `inputs/pictures.md` describes what was intended for both; it cannot be corrected, because
an input must stay byte-identical to what the baseline was made from.

- **No picture keeps its path.** `inputs/images/logo.png` is byte-for-byte the same file as
  `docs/assets/marqora-logo.png`. The Folio de-duplicates pictures by content, so it stored one
  copy, `media/marqora-logo.png`, and pointed both links at it. Whether a distinct picture beside
  the document keeps its path is not shown here; the Folio planner's own tests carry that.
- **Shrinking never happens.** The screenshot is 1888 pixels wide, and its copy reduced to 1600
  came out no smaller, so the shrinker kept the original, as it is meant to. The `reduced`
  Folios therefore match the plain ones, and prove only that the declined path is unchanged. A
  picture actually being reduced is proved by `FolioShrinkTests` (WP-M1-15), not by this folder.

## Keeping the exports safe afterwards

The exports live in your own checkout, git-ignored, and git leaves ignored files alone when you
switch branches: they are still here when you come back to `feature/CLI`. Three things would
lose them or muddle them:

- **`git clean -x`** (or `-X`) in this checkout deletes ignored files. `git clean -fd` without
  `-x` leaves them.
- **On another branch**, such as `dev`, the ignore rule for this folder does not exist yet, so
  the exports show as untracked files there. They are still safe, but `git add .` on that branch
  would commit them.
- **Re-exporting over them.** If one has to be made again, the baseline has changed: re-run step
  5 with `-Force` and say why in the commit.

Any time later:

```powershell
pwsh .\build\Get-BaselineHashes.ps1 -Check
```

exits non-zero if any export has changed, gone missing, or been added since the manifest was
written.
