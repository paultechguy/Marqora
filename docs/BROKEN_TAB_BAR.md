# Tab bar: design and rationale

Previously a handoff note for a partly broken strip. The strip is now implemented in full;
this records how it works and why it is built the way it is, because the way it is built
was arrived at through several failed attempts whose lessons are easy to lose.

Where the pieces live:

| file | what it holds |
| --- | --- |
| `Views/MainWindow.xaml` | the `TabView`, its item template, header and footer |
| `Views/MainWindow.xaml.cs` | the fitting, visibility, positioning and passthrough passes |
| `Views/TabTitleFitter.cs` | middle-ellipsis shortening and all title measuring |
| `App.xaml` | `TabViewItemMaxWidth` and every tab-related brush |

## The model

Modelled on Visual Studio's document tabs.

1. **Capped-width tabs.** A tab never grows past `TabViewItemMaxWidth` (220, in `App.xaml`).
2. **Middle ellipsis on long names** (`TabTitleFitter`), so the extension and the
   distinguishing tail of the name both survive.
3. **No icon on tabs** — every document is markdown, the glyph would say nothing.
4. **Close button on the active tab only**; other tabs close with a middle click.
5. **A `…` button pinned to the far right of the strip**, just before the caption buttons,
   listing **all** open documents alphabetically with the active one bolded. It never moves.
6. **No scroll arrows and no partially drawn tabs.** The strip shows whole tabs or none.
7. **The active tab is always visible.** Picking a hidden document from the `…` list, or
   opening a file from Explorer with the strip full, puts it in the last visible slot.
8. **A 6px gap between tabs** (`TabSpacing`, mirrored by the `Margin` on the tab template),
   so two elided names side by side do not read as one run of text.
9. Shrinking the window hides the rightmost tabs; widening it brings them back.
10. **A tab never changes width.** Selecting or deselecting one moves nothing else on the
    strip. This costs about 30 pixels of empty space on every inactive tab and is worth it —
    see "Two things that must not move" below.

## How it works

All in `MainWindow.xaml.cs`, driven from `DocumentTabs.LayoutUpdated`. The order is
load-bearing: the titles decide how wide each tab wants to be, that decides which tabs are
shown, and only then is it known where the shown ones sit.

1. `EnsureTabStripDoesNotScroll` — one-time: finds the `ScrollViewer` inside the strip's
   ListView and disables horizontal scrolling. This is what removes the scroll arrows and
   makes a clipped half-tab unreachable even transiently.
2. `UpdateTabTitles` — fits every title to the width cap (middle ellipsis), **pins the tab
   to the width that title needs** by setting `MinWidth` and `MaxWidth` to the same value,
   and records that width in `_tabWidths`, rebuilt from scratch each pass. Chrome is booked
   at `ClosableTabChrome` (50) for **every** tab, active or not — see below for why.
3. `UpdateVisibleTabs` — books room for the active tab first, then admits the leading run
   of the tab order until the room runs out, and collapses the rest.
4. `PositionTabListButton` — keeps the `…` button clear of the caption buttons; the inset
   is physical pixels, so the margin is computed against the rasterization scale.
5. `UpdateTabPassthroughRegions` — re-registers the tab rectangles as client area (the
   strip doubles as the title bar).

`ApplySelectionFromViewModel` and `OnTabDragCompleted` run steps 2–3 (and 5) directly,
because a selection change or a finished reorder is not guaranteed to cause a layout pass
on its own. The visibility pass sits out an in-flight drag (`_isDraggingTab`); the
passthrough pass sits out a drag and a closing window (`_isClosing`).

## Two things that must not move: tab width and title weight

Selecting a tab used to shift every tab after it by a pixel or two, and a name sitting near
the fitting limit gained an ellipsis on the way in and lost it on the way out. There were
**two independent causes**, and both had to be answered — fixing either alone leaves the
drift.

**The close button, worth about 30 pixels.** Answered by booking `ClosableTabChrome` for
every tab and pinning the tab to that width. A tab that always reserves the close button's
room cannot change size when the button arrives. The empty space this leaves on an inactive
tab is the price of a strip that holds still, not an oversight. Booking chrome per state
instead (18 plain / 50 closable) uses the strip more efficiently and is what an earlier
revision did — but a tab that books different chrome in different states is a tab that
changes width when its state changes, which is the whole problem.

**The title's weight.** WinUI draws a selected tab's title heavier — 600 against 400
elsewhere, same family and size — and the same string measures about three per cent wider
that way. On size-to-content tabs that difference passes straight downstream. It parts
company from the close-button effect because it is *proportional to the name*: the tab losing
the selection and the tab gaining it shed and gain different amounts, and the difference
lands on every tab after both. Answered by `TabTitleFitter.MeasuringWeight`, a constant
`SemiBold` that every title is measured at whatever weight it is currently drawn at.

Pinning alone would not have been enough: what gets pinned is derived from a title width
that was itself moving.

The measuring weight is deliberately the *heavier* of the two. A title measured at the weight
it will have when its tab is active fits in both states; measured at the lighter one it fits
only until it is clicked.

`container.Width` is not ours to hold — `TabView` writes that itself while managing tab
sizing. Clamping `MinWidth` and `MaxWidth` to the same value is how the tab is made to
measure to exactly what was booked for it.

## Why the fitting is reactive rather than a custom panel

The original note declared the react-to-`LayoutUpdated` approach unsound and recommended a
custom `Panel` in the strip's `ItemsPanel`. Reading TabView's template (`generic.xaml` in
the WinUI NuGet package) settled it the other way: the strip is a `TabViewListView` — a
`ListView` — and ListView's built-in drag-reorder, which writes back to `ViewModel.Tabs`,
only works over an `ItemsStackPanel`/`ItemsWrapGrid`. A custom panel would have traded a
layout bug for losing reorder.

The reactive approach was not inherently unsound; it had **two specific self-references**,
and removing them makes the pass a pure function of stable inputs, which converges:

- **The room was measured from the footer's `ActualWidth`.** The footer sits in a star
  column, so its actual width *is* whatever the tabs left over — the pass concluded that
  exactly the currently visible tabs fit, every time, which is why widening the window
  never brought a hidden tab back. The room is now
  `strip width − leading strip − add button − TabStripTrailing.MinWidth` — nothing the
  pass's own output can move.
- **Tab widths were measured from containers.** A collapsed tab measures as zero, so the
  measurement depended on the decision. Widths now come only from the title fitter
  (`_tabWidths`), identical for hidden and visible tabs. `WidthOf` falls back to the cap for
  a tab not yet fitted, which errs towards hiding — the harmless direction, corrected on the
  next pass.

With both inputs stable, re-running the pass writes nothing (every write is guarded), so
the layout it runs inside settles immediately.

The partial tabs were the strip's own `ScrollViewer` clipping underneath the visibility
logic; scrolling is now disabled outright (`EnsureTabStripDoesNotScroll`), and the fit
guarantees the visible content never exceeds the viewport anyway.

## Chrome and theming

The hairline along the foot of the strip is drawn by TabView's own template
(`LeftBottomBorderLine` / `RightBottomBorderLine`, `Height="1"`) from `TabViewBorderBrush`.
Marqora owns that key now, in both theme dictionaries in `App.xaml`, and **it must stay equal
to `MqChromeRuleBrush`** — the menu bar's rule sits a few pixels below it, and a mismatch
between the two reads far worse than either line being faint.

Two things to know before touching it:

- **Stock `TabViewBorderBrush` is a `StaticResource` alias to the `CardStrokeColorDefault`
  *colour*,** not to `CardStrokeColorDefaultBrush`. That alias resolves once, inside the
  framework dictionary, so overriding either the colour or the brush at app level never
  reaches the line. It has to be restated as its own `SolidColorBrush`.
- **Overrides only win because the dictionary carrying them is merged *after*
  `XamlControlsResources`.** The long note at the top of `App.xaml` explains why; moving that
  dictionary into `ThemeDictionaries` as a sibling of `MergedDictionaries` silently puts
  every stock key back.

**Brushes only. Never a thickness, padding or size key on `TabView`.** The fit pass books
each tab's chrome from the template metrics below, and the strip cannot scroll — so anything
that changes a tab's measured width silently falsifies the booking and clips the last tab,
with no scroll arrows to reveal that it happened. Colour cannot move a pixel, which is why
the accent edge on the active tab is free: the 1px border is present in both states.

## The numbers, and where they came from

Re-check these if the Windows App SDK is upgraded — they come from TabView's template metrics
in the WinUI package's `generic.xaml`, confirmed against the running strip.

| constant | value | file | what it covers |
| --- | --- | --- | --- |
| `TabViewItemMaxWidth` | 220 | `App.xaml` | the width cap; read back by `TabMaximumWidth` |
| `ClosableTabChrome` | 50 | `MainWindow.xaml.cs` | header padding 8+4, close margin 4, close button 32, border 1+1 |
| *(plain tab chrome)* | 18 | comment only | padding 8+8, border 1+1 — kept for the record, **nothing books it** |
| `TabWidthSafety` | 3 | `MainWindow.xaml.cs` | ruler-vs-render disagreement, in the fit only |
| `TabSpacing` | 6 | `MainWindow.xaml.cs` | must equal the tab template's `Margin` |
| `AddButtonWidth` | 44 | `MainWindow.xaml.cs` | add button 32 + 3 container padding + 9 ItemsPresenter |
| `Slack` | 6 | `TabTitleFitter.cs` | room left at the end of every fit |
| `MinimumKept` | 6 | `TabTitleFitter.cs` | shortest title worth showing |
| `MeasuringWeight` | SemiBold (600) | `TabTitleFitter.cs` | selected 600 vs 400 elsewhere |

Measured off the running strip across five tabs: an active tab came out at 47.7–48.5 and an
inactive one at 17.8–18.5, with the 1px border present in both states. The 50 stands — a
pixel or two of slack inside a tab that is pinned to this width is invisible, and coming in
under would clip the close button.

## Constraints that must survive any future change

- **Drag-reorder writes back to the bound collection.** `TabView` reorders
  `ViewModel.Tabs` directly. Never bind the strip to a filtered subset, and never replace
  the `ItemsStackPanel`.
- **The tab strip is also the title bar.** Tab rectangles and the `…` button are
  registered as `NonClientRegionKind.Passthrough`; without this, clicks are window drags
  and neither middle-click nor right-click ever becomes a XAML event. A passthrough region
  delivers *every* button, which is the whole of why the tab context menu
  (`MainWindow.TabContextMenu.cs`) needs no Win32 work of its own — and why it appears on a
  tab but not on the strip around one, where Windows' window menu is still the right answer.
  Region updates skip in-flight drags, so the context menu sits a reorder out as well.
- **Tab titles must not bind `TextBlock.Text` directly** — the full name comes back when
  the binding re-evaluates. The untruncated name rides on `Tag`; the window writes the
  fitted string into `Text`.
- **`CloseButtonOverlayMode` is ignored with `TabWidthMode="SizeToContent"`.** Close
  button on the active tab only is done with per-tab `IsClosable` bound to `IsActive`.
- **Every write in these passes must be guarded by a compare.** They run from a layout
  callback; assigning `Text`, `Visibility`, `MinWidth`, `MaxWidth` or a margin
  unconditionally invalidates the layout that called it, and nothing ever settles.
- **Chrome is booked per tab, not per tab state** — see "Two things that must not move".
- `TabSpacing` (code) and the tab template's `Margin` (XAML) must stay equal.

## Approaches already tried that do not work

- **A custom `Panel` in the strip's `ItemsPanel`** — loses ListView drag-reorder entirely.
- **Measuring available room from `TabStripTrailing.ActualWidth`** — star column, so the
  input is the pass's own output; hidden tabs never return on widening.
- **Measuring tab widths from containers** — a collapsed tab measures zero, so the
  measurement depends on the decision it feeds.
- **Booking chrome per tab state (18 / 50)** — correct arithmetic, but it lets a tab resize
  when the close button arrives, which moves every tab after it.
- **Reading the measuring weight off the live selected tab** — a race. WinUI applies the
  selected visual state some time after the selection changes, so a pass landing in between
  reads the old weight off the new tab and fits the whole strip to it; the next pass fits the
  whole strip again. Two re-fits, both moving tabs. A constant cannot lag.
- **Setting `container.Width`** — `TabView` owns it. Clamp `MinWidth` and `MaxWidth` instead.
- **Overriding `CardStrokeColorDefault` / `CardStrokeColorDefaultBrush` to restyle the
  strip's foot line** — the stock `TabViewBorderBrush` alias is already resolved and will
  not pick it up.

## How to check the strip

With enough documents open to overflow:

1. Narrow the window slowly — no tab is ever drawn partially, at any width.
2. Widen it again — tabs come back as room appears.
3. No scroll arrows at any width.
4. `…` stays pinned at the far right, before the caption buttons, and lists every open
   document alphabetically with the active one bold.
5. Pick a document with no visible tab — it becomes active and appears in the last slot.
   (Selecting a different tab afterwards lets it hide again; the substitution is a display
   decision, not a reorder of `ViewModel.Tabs`.)
6. With the strip full, open a file from Explorer — its tab is visible.
7. Drag a tab to a new position — the order in `ViewModel.Tabs` matches the screen.
8. Middle-click a non-active tab — it closes.
9. Right-click a non-active tab — it becomes active and its menu opens on it. Press `Esc`
   and type: the character lands in the document. Right-click the strip *between* tabs and
   Windows' own window menu comes up instead.
10. The window still drags by the empty strip, double-click still maximises, and the
    caption buttons still work.
11. **Click back and forth between two tabs with long names.** Nothing to the right of
    either moves, and no name gains or loses an ellipsis as the selection changes. This is
    the regression the two fixes in "Two things that must not move" exist to prevent, and it
    is the one that comes back most easily.
12. **In both light and dark theme**, the line under the strip and the line under the menu
    bar are the same weight — they sit a few pixels apart, so any difference shows.
