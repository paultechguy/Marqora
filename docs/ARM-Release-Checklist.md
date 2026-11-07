# ARM release checklist

The hand checks for the ARM64 zip, on the real ARM PC. The x64 PC builds that zip and can read every
file's header, but it cannot run it, so this list is the only proof that it works.

- **Full check:** once, on an ARM64 dev build, before the first release that carries ARM64. Repeat
  only the affected steps after a fix.
- **Short check:** every release, on the zip downloaded from the private draft, before clicking
  **Publish release**.

Fill in the record at the bottom each time.

---

## Getting the files

**For the full check,** on the x64 PC:

```powershell
pwsh .\build\New-DevBuild.ps1 -Version <n>-arm1 -Architecture arm64
pwsh .\build\New-DevBuild.ps1 -Version <n>-x64check
```

Take both zips from `build\artifacts\dev` to the ARM PC.

**For the short check,** on the ARM PC: sign in to GitHub in a browser, open the draft from the
repository's Releases page, and download the win-arm64 zip.

Extract each zip into a folder of its own. The sample document is
`%LOCALAPPDATA%\Programs\Marqora\docs\cli-tour\Guide.md`, installed with the app. It has a diagram
and no math, so once Marqora is installed, copy it to a scratch folder and add one line of math:

```markdown
$$ e^{i\pi} + 1 = 0 $$
```

---

## Short check, every release

- [ ] 1. Run `Install.cmd` from the extracted ARM64 zip by double-click. No WebView2 warning.
- [ ] 2. Start Marqora from the Start menu. **Help > About** shows the version and ARM64.
- [ ] 3. In Task Manager's **Details** tab, with the **Architecture** column on, `Marqora.exe` shows
      ARM64.
- [ ] 4. Open the sample document. The source and the preview both draw, with the diagram and the
      math.
- [ ] 5. `marqora-cli version` shows the version.
- [ ] 6. With marqora-cli, export the sample to PDF and to Word, and open both:

      marqora-cli export pdf Guide.md
      marqora-cli export word Guide.md

---

## Full check, first release only

Do the install paths first, because they start from a clean PC. Run each `Install.cmd` by
double-click, as someone downloading it would.

### Install paths

- [ ] 1. Install the **x64** dev zip. It completes, says at the end that this PC has an ARM processor
      and where the ARM version is, and shows no WebView2 warning. Marqora starts.
- [ ] 2. Change a setting in Preferences.
- [ ] 3. Install the **ARM64** dev zip over it. No note this time. The setting survived, and Task
      Manager shows `Marqora.exe` as ARM64.
- [ ] 4. All six steps of the short check above.

### The app

The parts that lean on Windows or on a native component:

- [ ] 5. **Editor and preview:** typing, find, a Light/Dark switch and a color theme switch.
- [ ] 6. **Spell check:** a misspelling is underlined and offers corrections.
- [ ] 7. **Pictures:** a Folio with a large picture that has to be shrunk.
- [ ] 8. **Exports from the app:** PDF, Word, HTML, Folio, and Copy as PNG, SVG and rich text.
- [ ] 9. **Printing:** a paged print to a real or PDF printer, with a page range that does not start
      at page 1.
- [ ] 10. **Windows integration:** double-clicking a `.md` file, a second launch handing its file to
      the first, and the file and folder dialogs.

### Removing it

- [ ] 11. Uninstall from **Settings > Apps**. The app, shortcuts and file associations are gone, and
      `%LOCALAPPDATA%\PaulTechGuy\Marqora` is still there.

### On the x64 PC

- [ ] 12. With Marqora installed, run the ARM64 dev zip's `Install.cmd` by double-click. It stops,
      says this zip is for ARM PCs and to download the win-x64 zip, and the window stays open.
      Marqora still starts afterward.

---

## If something fails

Fix it on the branch the build came from, build again, and rerun only the steps it could affect. A
fault that needs a change to how Windows reports the processor goes to `Get-MachineArchitecture` in
`build/installer/Install.ps1`: its registry read is the one choice only this PC could confirm, and
`Win32_Processor` is the fallback.

---

## Record

| Date | Build | Check | Windows build | Result | Notes |
| --- | --- | --- | --- | --- | --- |
| 2026-11-06 | ARM64 and x64 dev builds (`1.0.21-arm1`) | Full, steps 1-12 | Laptop: 25H2, 26200.9711 | Passed | Step 12: the window stayed open, the installed files were unchanged, and Marqora started afterward |
