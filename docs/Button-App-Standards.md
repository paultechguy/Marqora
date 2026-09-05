# Marqora button standards

Buttons in this app were built surface by surface, and for a while that was fine — the toolbar
had a shared style, and everything else was a handful of dialogs. It stopped being fine somewhere
around the fifth button height. This document is the answer to "what should this button look
like?", so that question stops being answered from scratch each time.

It is a standard for a **professional Windows desktop application**, which mostly means: use what
Windows already does, state the few places Marqora differs, and never paint a button by hand.

`docs/Architecture.md` covers how the app is put together. This covers only how its buttons look
and behave.

---

## The problem this replaces

Recorded so the standard reads as a correction rather than an arbitrary preference. Every row
below was measured in the tree, not guessed:

| What diverged | What was there |
|---|---|
| Height | 34 (`MqToolButtonStyle`), 32 (`FindAllWindow`'s private `ControlHeight`, which then *overrode* the 34 on a button that had asked for the shared style), 30 (`MqSegmentStyle`), 38 (recent-file pin and remove), unset on every dialog button |
| Padding | `10,6` on tool buttons, `20,8` on the empty state, `14,10` on a recent-file card, `0` on icon squares, unset elsewhere |
| Font size | 13 in the chrome styles, 14 in `FindAllWindow` via a second private constant, framework default everywhere else |
| Minimum width | `96` in exactly two places out of about twenty |
| Right alignment | done three ways: `HorizontalAlignment.Right` on a `StackPanel`, a `*`/`Auto` two-column `Grid`, and left to the `ContentDialog` template |
| Emphasis | `AccentButtonStyle` on the main window's *Reload*, *Save Now* and *Browse files* — but not on Preferences' **OK**, the most prominent commit button in the app |
| Destructive layout | the Discard-changes flyout put `Discard` first, unstyled, in a left-aligned row, with the safe choice neither default nor emphasised |
| Web buttons | `min-width: 30px; height: 26px; padding: 0 9px; border-radius: 4px` — a shape found nowhere else in the app |

One thing was already consistent, and it survives into the standard unchanged: **`Spacing = 8`**
on every horizontal button group.

---

## 1. Six tiers

Every button in Marqora is exactly one of these. Naming the tier is how you decide what a new
button looks like; if a button does not fit a tier, that is a design question, not a licence to
invent metrics.

| Tier | What it is | Style | Metrics |
|---|---|---|---|
| **Command** | Commits or dismisses a dialog, window or notice — OK, Cancel, Export, Reload, Find All, Browse files | `MqCommandButtonStyle`, `MqPrimaryCommandButtonStyle` | MinWidth 96, MinHeight 32, framework padding `11,5,11,6`, radius 4, font 14 |
| **Chrome** | The main window's toolbar and format bar. Quiet until the pointer arrives | `MqToolButtonStyle`, `MqToolToggleStyle`, `MqDropDownStyle` | MinWidth 36, Height 34, padding `10,6`, radius 6 (`MqPillRadius`), font 13 |
| **Compact chrome** | The same idea inside a palette window's toolbar | `MqCompactToolButtonStyle`, and its CSS twin in `webshell/diagram.css` | MinWidth 32, Height 28, padding `10,0`, radius 6, font 13 |
| **Icon** | Square, glyph only — tab list, search history, pin, remove | `MqIconButtonStyle` | 34 × 34, padding 0, radius 6, glyph 15 (`MqIconStyle`) |
| **Link** | Reads as text and only grows a hit target on hover | `MqPathLinkStyle` | padding `5,1`, radius 4, negative left margin |
| **Segment** | The source / split / preview switcher | `MqSegmentStyle` | 30 tall, font 12.5 — carries its own `ControlTemplate` on purpose. See §7 |

All of these live in `src/PaulTechGuy.MQ.App/App.xaml`, in one block, beside the metrics they use.

### Three heights, and each has a job

This is the part that has to be argued rather than asserted, because "one height everywhere" is
the tempting answer and it is wrong.

| Height | Where | Why that number |
|---|---|---|
| **34** | Every control row — the main window's chrome *and* a form row such as Find All's. Icon squares too | Marqora's own number, sized to the 48-tall toolbar it sits in |
| **32** | The **minimum** height of a command button | It is the framework's own `ContentDialogButtonHeight`, so a hand-built footer and a `ContentDialog` footer land on the same line. A command button inside a 34 control row simply stretches to it |
| **28** | Palette toolbars, including the diagram window's HTML one | A palette window's floor is 320 × 240. At 34 the toolbar strip would eat a fifth of the window |

Anything else is drift. The one standing exception is `MqSegmentStyle` at 30, and it has a written
reason (§7).

---

## 2. Emphasis and color

**Exactly one accented button per surface, and only if it commits.** Everything else is a neutral
button. That is the whole rule.

The accent is **the user's Windows accent**. Marqora used to state a teal of its own, `#51A8B1`,
and override every stock accent key with it. That is gone, and the comment at the top of
`App.xaml` records why: a `ContentDialog` is hosted in the popup root rather than under the
window's content, and the accent keys are reached there through aliases the framework resolves
once — so dialog buttons and list selection kept the Windows accent whatever the dictionary said.
The app was two-toned, teal in the window and the user's own color in every dialog, and the seam
showed wherever the two met.

The consequence for buttons is direct, and it retires an argument that used to be made in
`FindAllWindow`: **`AccentButtonStyle` now works in every window in the app.** There is no
override left for a second window's tree to miss.

What this forbids:

- No `Background` or `Foreground` set on a button, ever. Not a hex, not a named brush, not a
  `ThemeResource` lookup done in code.
- No hover, pressed or disabled colors. Those are the framework's, in all three tiers.
- No red for destructive actions. See §6 — Windows does not do this, and WinUI ships no critical
  button style to do it with.

The one thing a button may carry beyond its tier's style is a `MinWidth` that its own content
demands — `FindAllWindow`'s scope drop-down is 150 wide because its longest label is.

---

## 3. Order and placement

**Commit first, alternatives next, cancel last**, left to right. This is what a `ContentDialog`
already does — Primary, Secondary, Close — and what a hand-built footer must match:

```
[ Save ]  [ Discard ]  [ Cancel ]
   ^          ^            ^
 commit   alternative    cancel
 (accent)  (neutral)    (neutral)
```

- **Footers** are right-aligned, `Spacing = 8`, built by `CommandFooter.Row`.
- **Content-level groups** — the Export/Import pairs on the Preferences Advanced page — are
  left-aligned under their label with the same spacing. They are not command buttons and do not
  take the command MinWidth; they are actions on the content, the same way `ShortcutsDialog`'s
  copy button *"is an action on the content, not a way out of the dialog"*.
- **A confirmation is never a lone button.** If a flyout asks a question, it offers both answers.
  A single button the user has to click away from to decline is not a choice.

---

## 4. Two footer shapes, and why they differ

Marqora has two kinds of modal surface, and their footers do not look identical. This is
deliberate; it is written down here so nobody "fixes" it.

**A `ContentDialog` footer belongs to the framework**, and more completely than it first looks.
Its `CommandSpace` is a five-column grid whose button columns are `Width="*"` **written as
literals in the template**, with each button `HorizontalAlignment="Stretch"`. That is where the
equal-width look comes from, and nothing in a resource dictionary changes it. The only two
resources the template actually reads are `ContentDialogButtonSpacing` (8) and
`ContentDialogPadding` (24).

Worth knowing, because it is a trap that costs an afternoon: `ContentDialogButtonMinWidth` (130),
`ContentDialogButtonMaxWidth` (202), `ContentDialogButtonHeight` and
`ContentDialogButtonMinHeight` (32) **are defined in the theme dictionaries and referenced
nowhere**. They are UWP leftovers. Overriding them does nothing at all.

Nor can the default button be restyled. `PrimaryButtonStyle`, `SecondaryButtonStyle` and
`CloseButtonStyle` are real and template-bound, but the `DefaultButtonStates` group sets
`PrimaryButton.Style` to `AccentButtonStyle` directly — a visual-state setter writes a local
value, which outranks a `TemplateBinding`. So a style pushed in from outside would take effect on
every button *except* the one it most wanted to reach.

**Therefore: Marqora sets no style on a `ContentDialog` footer.** Set the button *text* and the
`DefaultButton`; the layout is not ours, and half-styling it is worse than not styling it.
Buttons inside a dialog's `Content` are ordinary app buttons and do take the shared styles.

**A window footer is right-aligned.** A `Window` has no template buttons to borrow, so
`PreferencesWindow` builds real ones: MinWidth 96, `Spacing = 8`, `HorizontalAlignment.Right`.

Both are correct Windows patterns — a task dialog stretches its buttons, a settings window
right-aligns them. Making the second match the first would put two 430-pixel buttons at the foot
of Preferences, which no Windows application does; making the first match the second is not
possible for the default button. What makes them read as one application is therefore not
geometry but what they genuinely share: order, emphasis, verb, the same 8 between them, and a 96
floor wherever the container does not decide the width. **The container decides the geometry;
the app decides the meaning.**

---

## 5. Keyboard

Every dialog-like surface answers Enter and Escape.

**WinUI has no `Button.IsDefault`** — that is a WPF property, and looking for it is a recurring
waste of an afternoon. What exists instead:

- A `ContentDialog` sets `DefaultButton`. Its Close button is the Escape path automatically.
- A `Window` handles it on the content root, as `PreferencesWindow` does. A handler attached that
  way does not see an event a control has already handled, which is what lets Enter inside a
  number box commit the number rather than the window.

Escape may be intercepted by something nearer when that is more useful — `ShortcutsDialog` empties
its filter box on the first Escape and closes on the second. That is a feature, and it belongs to
the control, not to the button standard.

---

## 6. Destructive actions

A destructive action is one that loses work the user cannot get back.

1. **An explicit verb, never "OK".** "Discard changes and reload", not "OK". The button label
   should make sense read on its own, without the message above it.
2. **Neutral fill.** No red. Windows 11 has no red-button convention, WinUI ships no critical
   button style, and a hand-built red needs a brush pair per theme plus a contrast check against
   whatever accent the user has chosen. The verb carries the warning.
3. **Never the default.** The *safe* option is the accented, default one. A stray Enter must not
   destroy anything.

```
Discard the changes you have made? Your preferences go back to
how they were when this dialog opened.

                        [ Discard ]  [ Keep editing ]
                          neutral       accent, default
```

For prompts raised through `IDialogService`, this is what the `defaultIsCancel` argument on
`ConfirmAsync` is for. Reaching for it is the point at which you should be sure the prompt is
really destructive: "Save changes?" is not — there, Primary *is* the safe answer.

---

## 7. Traps worth knowing

Each of these is already load-bearing somewhere in the tree, and each one cost a session to find.

**A `ThemeResource` looked up in code resolves against the *application's* theme.** That is the
operating system's theme, not the one the user chose in Marqora. With Windows dark and Marqora set
to light, a code lookup of `ApplicationPageBackgroundThemeBrush` paints a black page under light
controls. `PaletteWindow.SurfaceBrush` exists because of this. **Fetching a `Style` from
`Application.Current.Resources` is safe** — the `ThemeResource` references *inside* it resolve
against the element's own tree. Fetching a `Brush` is not. Use `MqStyles` for the former; never do
the latter.

**A `ContentDialog` inherits no theme.** It is hosted in the popup root, a sibling of the window's
content, so the `RequestedTheme` the theme service sets never reaches it and it falls back to the
framework default, which is dark. Every dialog goes through `DialogExtensions.AnchorTo`, which
sets `XamlRoot` and `RequestedTheme = anchor.ActualTheme`, so a new one cannot quietly miss it.
The window-side twin for flyouts is `PreferencesWindow.Themed`.

**`MqSegmentStyle` carries a full `ControlTemplate` on purpose.** The stock `ToggleButton`
animates on press and on check, and while an animation runs the control's content is composited
into its own layer — which drops the label from ClearType to greyscale antialiasing. The text
visibly softens. The custom template changes state by swapping brushes, so no layer is ever
created. Do not simplify it into a plain style.

**`AccentButtonStyle` declares no `BasedOn`.** In the Windows App SDK it sets six properties —
`Foreground`, `Background`, `BackgroundSizing`, `BorderBrush`, `CornerRadius`, `Template` — and
nothing else. A style derived from it should restate padding, font and border thickness rather
than assume they arrive. Assuming is how an accent button ends up a different size from the
neutral one beside it.

**Everything here is in DIPs.** `app.manifest` declares `PerMonitorV2`, WinUI scales for us, and
no button in the app computes a pixel size. The two places that convert between DIPs and physical
pixels are both in `MainWindow.xaml.cs` and are about title-bar insets, not buttons.

---

## 8. The web tier

Almost the entire app is native. The one exception is the diagram window's zoom toolbar, which is
HTML inside a WebView (`webshell/diagram.html`, styled by `webshell/diagram.css`) — `app.css` has
no button rules at all, and neither does the cheatsheet.

Those five buttons are **Compact chrome**, and the CSS states the tier's numbers once, at the top
of the file:

```css
:root {
  --mq-btn-height: 28px;
  --mq-btn-min-width: 32px;
  --mq-btn-padding-x: 10px;
  --mq-btn-radius: 6px;
  --mq-btn-font-size: 13px;
}
```

A CSS pixel here is a device-independent pixel — WebView2 takes the window's rasterization scale
— so these mean what the XAML numbers mean at any DPI, and nothing converts.

There is deliberately **no accent button on that toolbar**. Its five controls are peer zoom
actions and none of them commits anything, so the one-accent-per-surface rule has nothing to
place. Do not add one for the sake of consistency.

**On keeping the two sides in step.** Marqora's rule for a value shared between C# and the web is
that one side owns it and pushes it to the other: `MatchColors` holds the search-match colors,
`WebViewPreviewHost` sends them across, `app.js` installs them as custom properties, and `app.css`
deliberately never names them. That is the right shape for a *color*, which changes with theme
and selection.

These are metrics, and five zoom buttons do not justify a runtime bridge. So they are duplicated
on purpose, and `build/Test-ButtonStandards.ps1` compares the two files and fails if they drift.
A test is the cheaper source of truth here; a copy nobody checks is what this document exists to
stop.

---

## 9. Checklist for a new button

1. Which tier is it? Apply that tier's style and set nothing else about its appearance.
2. If it is a command button, does the surface already have an accented one? There is only ever
   one.
3. Is the accented one the button that **commits**?
4. Is the order commit → alternatives → cancel?
5. Is the footer right-aligned with `Spacing = 8`, built by `CommandFooter.Row`?
6. Does Enter commit and Escape cancel?
7. If anything here is destructive: explicit verb, neutral, and not the default?
8. Does the label say what happens, read on its own?
9. Does it have a tooltip or an `AutomationProperties.Name` if it is icon-only?
10. Run `pwsh ./build/Test-ButtonStandards.ps1 -Check`.

---

## What adopting it changed

The tree matches this document. What moved, for anyone reading a diff:

| Where | Change |
|---|---|
| `App.xaml` | The vocabulary: three named heights, the 96 floor, the 8 gap, and the styles — command, primary command, command drop-down, icon, compact tool, page action, card |
| `Views/MqStyles.cs`, `Views/CommandFooter.cs` | New. The typed lookup and the shared action row; `MqStyles.Verify()` runs at launch so a renamed key fails there rather than in a rarely-opened flyout |
| `IDialogService`, `DialogService`, `MainViewModel` | `ConfirmAsync` gained `destructivePrimary`. Reload-from-disk and clear-recent-files now put Enter on Cancel. The reload prompt's comment had claimed this for some time; the code did not do it |
| `PreferencesWindow` | OK is accented and the footer comes from `CommandFooter`; the Discard flyout is reordered with the accent on *Keep editing*; Restore-defaults gained the Cancel it never had; the six Advanced-page buttons take the shared style |
| `FindAllWindow` | *Find All* is the accented commit, *Clear* is neutral, and both take their size from the shared styles instead of this window's private constants. Two comments arguing from the removed teal override were rewritten |
| `MainWindow.xaml` | The change-notice row, the empty state and the recent-file cards all take named styles. **Pixels unchanged in the empty state on purpose** — the row folds below `EmptyStateStackWidth`, and narrowing it would fold a row that still fits |
| `webshell/diagram.css` | The zoom strip onto the Compact chrome tokens: 26 → 28 tall, radius 4 → 6, min-width 30 → 32 |

### Still open

- **The recent-file card's 38 × 38 icon buttons.** Every other icon button is 34. The card is a
  two-line row rather than a control row, so 38 may well be right — the number is now stated once
  in `MqCardIconButtonStyle` rather than six times in the markup, but the question has not been
  settled by eye.
- **`FindAllWindow`'s `_history`, `_term` and `_scope`** still carry an explicit height. They are
  form controls rather than buttons and have no shared style to take; 32 is the framework's own
  number for them, so nothing is visibly wrong, but they are not covered by anything.
