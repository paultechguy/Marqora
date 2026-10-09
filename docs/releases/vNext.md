# Marqora vNext - What's New

<!--
    Notes for the next release, written as each change lands: a ## section per feature, and a
    paragraph with a bold lead per fix under ## Fixes. Write for someone deciding whether to
    download the release, not for someone reading the diff.

    build\New-ReleaseNotes.ps1 moves everything here into docs\releases\v<version>.md, drops
    any heading with nothing under it, and puts this stub back.
-->

## PDF, Print and Word Now Match

The PDF used to come out at about two thirds of its size with no page numbers, so the better
route to a PDF was through Word. Now PDF, Print and Word share one page design - the same
faces, sizes and spacing - and the same dialog choices: a running header, page numbers, and an
optional title page and contents page. Footnotes sit at the foot of their page, wide code
wraps, and callouts and tables are kept off page breaks. The PDF carries bookmarks, tagging
for screen readers and the document's own title. Word gains better math, raw HTML tables,
styled HTML blocks and sharp vector diagrams. Anything an export cannot carry is named in its
report rather than dropped in silence, and Print is a two-column dialog that fits a laptop
screen.

## A margin around diagrams copied as PNG

**Copy as PNG** now leaves a little space around the diagram, so its outer shapes no longer
touch the edge of the picture you paste. This applies in the preview and in a diagram's own
window.

## Fixes

**A diagram opened in its own window fits the window.** Double-clicking a diagram could open
it too wide for its window, with its right side cut off. A very wide diagram stopped shrinking
at 25%, and others were sometimes measured before the window had reached its size. The window
now opens with the whole diagram in view, however wide it is, and keeps it fitted as you resize
the window until you choose a zoom of your own.

**PDF and Word exports are now near identical.** The same document exported both ways has the
same line spacing, page count and contents page, and the last visible differences - nested
quotes, bullets beside task items, equations in table cells, the cover title and the title
page - are gone.

**File dialogs remember where you were.** Open, Save As, Open Folder, Share Review, each kind
of export and each kind of import keep their own last folder, so a Word export opens where the
last Word export went, not where the last PDF or opened file did.

**No landing page flash at startup.** Restoring a session no longer shows the landing page for
a moment before the documents come back, and a slow start says "Opening your documents…"
until the first one is on screen.

**Equation numbers count.** The lines of an `align` were all numbered (1) in the preview; they
now count 1, 2, 3, in the preview and in every export.

**A diagram's PDF keeps its frame.** Export to PDF from a diagram window lost the thin rounded
border round the diagram; it has it again.
