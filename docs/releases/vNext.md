# Marqora vNext - What's New

## Where a document's numbers start

`Alt+5` has always let you drop Marqora's numbers from one document without touching the
preference or any other tab — the answer to someone else's file that writes "1.2 Scope" into the
heading text itself. It was a blunt answer to the usual complaint, though. More often than not the
author numbered their headings perfectly well and simply began counting at `##` while your
preference begins at `#`, so the two sets sit a component apart on every heading and there was
nothing to do but switch yours off.

`Alt+Shift+1`, `Alt+Shift+2` and `Alt+Shift+3` now say where this document's count starts —
the same three levels the preference offers, for the tab in front of you only. `View > Heading
Numbers` has grown into those four rows, so the menu says which one a document is on rather than
only whether it is numbered.

`Alt+5` still turns the numbers off and back on, and now toggles against the level you named: set
a document to start at `##`, press `Alt+5` to read the author's numbering on its own, press it
again and you are back at `##` rather than somewhere you have to set again. Nothing here is
written to your file or to your preferences, and it lasts as long as the tab does.

## Heading numbers in the file itself

Everything Marqora produces is numbered already — the preview, the outline, Print, the PDF, HTML
and Word exports, Copy as Rich Text — so numbers written into the markdown are for text that
*leaves* Marqora: a pull request, a wiki, an issue, an email, anywhere nothing will number it on
the way. `Format > Heading > Number Headings` writes them in, starting from whichever level the
document is already showing, so the file ends up reading the way the screen did a moment earlier.

It replaces rather than adds, which makes it a renumber. Insert a section into the middle of a
numbered document, run it, and everything below it moves along one instead of being fixed by hand.
`Remove Heading Numbers` is the other direction, for taking someone else's hard-coded numbers out
so Marqora's own can do the job — and it leaves alone any heading whose leading number was never a
section number, so a "2026 Budget" sitting between "2" and "4" keeps its year.

Renaming a heading changes the anchor it answers to, so links inside the same document are moved
to match and a hand-written table of contents keeps working; a link from another file still points
at the old anchor, and both commands say so before they run. A document that numbers its own
headings now opens with Marqora's numbering already stood down rather than showing both sets at
once, which `Preferences > Preview` can turn off. `Ctrl+Z` takes any of it back in one step.

## Fixes

**A diagram in an exported file now does something when you click it.** Hovering a diagram in an
exported HTML file or a Folio offered to open it on a double-click, and double-clicking did
nothing whatever: the badge came across with the app's own stylesheet, while the part that made
it work stayed behind in the app. It now reads **Click to view**, and clicking a diagram lifts it
out of the page onto its own card, clear of the text around it; clicking again puts it back. It
is the whole diagram fitted to the window rather than a magnifying glass — a small diagram grows
a good deal, and a large one, already fitted to the width of the page, comes back about the size
it was.

**A Folio shared as a single page is offered as `Folio-...`.** A Folio is deliberately an
ordinary HTML file so that anybody can open it, which also means that in a folder beside an
ordinary exported page the two are one icon and one extension — nothing to tell them apart. The
suggested name now carries the prefix, and a name that already begins that way is left as it is.
Sharing as a zip is unchanged.

**Switching the view keeps your place.** Moving between split, source and preview view put the
pane that stayed wherever the other one had last been left, which on a long document meant
losing the paragraph you were reading. The switch now carries the line across.
