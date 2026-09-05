# Dialogs, themes, and why the print dialog is Marqora's own

A light Marqora on a dark Windows opened a dark print dialog. Chasing that turned up two
things: the dialog was never ours to theme, and it had been quietly discarding what the user
chose in it. This is the record of what was tried, what was true, and what was built instead.

Written at length because both dead ends look like good ideas from a standing start, and
because the second finding is a printing bug rather than a cosmetic one.

---

## Who draws what

| Surface | Drawn by | Follows |
|---|---|---|
| Preview, editor, tab strip, palettes | WinUI / XAML | `ThemeService` → `RequestedTheme` |
| Preferences, PDF export, About, shortcuts | WinUI `ContentDialog` | same, via `DialogExtensions.AnchorTo` |
| Preview and cheatsheet content | WebView2 | `IPreviewHost.SetThemeAsync` |
| **Print** | **Marqora**, since this change | the anchoring window's theme |
| Open / Save As / folder (`IFileDialog`) | Windows | the Windows setting, and nothing else |

`ThemeService` sets `FrameworkElement.RequestedTheme`, which reaches XAML and stops there.
Everything Windows draws for itself is outside its reach — as the file dialogs still are.

---

## What was tried: the process app mode

The standard lever. `uxtheme.dll` exports, by ordinal only, `SetPreferredAppMode` (#135), whose
`ForceLight` / `ForceDark` override the Windows color-mode setting for one process; the
neighbouring `RefreshImmersiveColorPolicyState` (#104) makes the process re-read the cached
color policy afterwards, and `FlushMenuThemes` (#136) does the same for menus.

It was implemented, called from `ThemeService` where the effective theme resolves, and logged
as applied. **It changed nothing.** On Windows 11 build 26200, with both calls in place:

| App theme | Windows | Print | Open | Save As |
|---|---|---|---|---|
| Light | Dark | dark | dark | dark |
| Dark | Light | light | light | light |

Every dialog followed Windows, in both directions. The code was reverted rather than shipped:
two calls into an undocumented private API that demonstrably change nothing are a maintenance
liability with no observable behaviour to justify them.

Should a future Windows honour it again, the mechanism is above and the gate to remember is
that `TargetPlatformMinVersion` is `10.0.17763.0` — on 1809 ordinal 135 is
`AllowDarkModeForApp(BOOL)`, a different function taking a different argument, so any such
attempt must check `Environment.OSVersion.Version.Build >= 18362` first.

---

## Why it could not have worked

The dialog a `PrintDlgW` call put on screen was **not `comdlg32`'s dialog at all**. Windows 11
substitutes its own modern print experience — printer, orientation, copies, color mode,
collation, pages, a preview pane — themed by the system.

The tell is in the screenshot that settled it: the orientation field read `Portra&it`. That is
the legacy dialog's keyboard mnemonic passing through Windows' shim into a control that does
not use `&`. The window is Windows' own, and no process-level theming reaches it.

It also announced `This app doesn't support print preview`, in a dialog Marqora could not
change, about a preview Marqora could not offer.

---

## The second finding: the settings were decorative

Worth stating separately, because it is not about color on screen but about ink on paper.

That dialog collects a full set of choices and hands them back in a DEVMODE. The old
`Win32PrintDialog` read five things out of it: printer name, copies, collate, orientation and
paper size. `dmColor` and `dmDuplex` were never looked at, and `WebViewPrinting` never set
`CoreWebView2PrintSettings.ColorMode` or `.Duplex`.

So a user who chose *High Quality CMYK Grayscale* got whatever the printer defaults to. Same
for duplex, tray and quality. The dialog looked like it was collecting settings; the printout
did not know about most of them.

That is what tipped the decision. A dialog that cannot be themed is annoying. A dialog that
appears to accept instructions and then drops them is wrong.

---

## What was built

`Views/PrintDialog.cs` — a `ContentDialog` of Marqora's own, in the shape of
`PdfExportDialog`, shown by all three print paths: `PrintDialogService` for the preview, and
`CheatsheetWindow` and `DiagramWindow` directly, each anchored to its own window's content so
the dialog wears that window's theme.

It asks: printer, copies, collate, pages, paper size, orientation, and — where the driver says
it can — color and sides.

**What it offers comes from the printer, not from a table.** `Services/Win32Printers.cs` asks
the spooler: `EnumPrintersW` for the list, `GetDefaultPrinterW` for the initial choice, and
`DeviceCapabilitiesW` for paper names and sizes, maximum copies, `DC_COLORDEVICE` and
`DC_DUPLEX`. The dialog this replaces carried a hard-coded table of eight `DMPAPER_` constants
and could size nothing outside it; a driver that names its own paper is now believed.

A capability the driver does not claim is **not shown**. A mono laser is not offered a color
choice it would ignore — which is the principle the old arrangement broke.

Two things stayed where they were:

- **Margins and backgrounds** still come from the PDF page setup in preferences. The Windows
  dialog had no field for either, and keeping one page setup means paper and PDF agree.
- **Nothing is remembered.** The dialog opens on the system default printer each time, as the
  Windows one did. No new settings, nothing to migrate.

`PageRange` in Domain checks the pages box while the user can still see it. The print call
rejects a range it cannot read by failing the job, which arrives long after the dialog has
gone; an unreadable range disables Print instead, with the reason under the box.

### What was given up

Windows' dialog offered *Add a printer* and a preview pane. Ours offers neither. The preview
is no loss — it said the app did not support one — and adding a printer is a Windows Settings
job that a print dialog is a strange place to start.

---

## The file dialogs are still Windows', and that was decided rather than left

Open, Save As and the folder picker go on following the Windows setting. Asked directly
whether that could be changed, the answer is no, for three separate reasons:

- **No supported API.** `IFileDialog` has nothing for it. `IFileDialogCustomize` adds
  controls to the dialog, not colors to it.
- **The app-mode lever does not move them either**, as the table above shows.
- **Per-HWND theming is the only route left, and it is a bad bet.** `IFileDialogEvents` plus
  `IOleWindow::GetWindow` would hand over the dialog's HWND, and `AllowDarkModeForWindow`,
  `SetWindowTheme` and `DwmSetWindowAttribute` could then be walked over its children. That
  dialog is shell-drawn - places bar, breadcrumb, list view, search box - so the likely
  outcome is a half-lit dialog that needs revisiting after every Windows update.

One question was left open rather than answered: whether the app mode Marqora set was still
in force at the moment a dialog opened. `Microsoft.ui.xaml.dll` imports UxTheme only by name -
`OpenThemeData` and the animation helpers - with no ordinal import of #135, so nothing
suggests WinUI overrides it, but a dynamic call cannot be ruled out from outside. If it is
ever worth settling, `SetPreferredAppMode` returns the *previous* mode: call it again
immediately before showing a dialog and log what comes back.

Replacing the dialogs is not on the table. A hand-built file browser would lose the places
list, the search, the network browsing and every shell integration the real one has.

A mismatched Open dialog is ordinary Windows behaviour. A print dialog that drops your
grayscale choice was not - which is the whole difference between accepting one and replacing
the other.

---

## Verification

No script covers this; `Test-ButtonStandards.ps1` cannot see it and neither can a diff.

1. Windows dark, Marqora light, Ctrl+P — the dialog is light. Windows light, Marqora dark — it
   is dark.
2. Choose *Black and white* on a color printer; the page comes out grey. This is the bug the
   old dialog hid.
3. Two-sided, on a printer that offers it; confirm the field is absent on one that does not.
4. Copies above one enables Collate; a nonsense page range disables Print and says why.
5. Ctrl+P from the cheatsheet and the diagram pop-out — each opens over its own window, in
   that window's theme, on the paper preferences names.
