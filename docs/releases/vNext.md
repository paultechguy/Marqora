# Marqora vNext - What's New

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
