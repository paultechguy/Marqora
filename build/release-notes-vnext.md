# Marqora vNext - What's New

<!--
    Notes for the next release, written as each change lands: a ## section per feature, and a
    paragraph with a bold lead per fix under ## Fixes. Write for someone deciding whether to
    download the release, not for someone reading the diff.

    build\New-ReleaseNotes.ps1 moves everything here into docs\releases\v<version>.md, drops
    any heading with nothing under it, and puts this stub back from
    build\release-notes-vnext.md.
-->

## PDF, Print and Word Now Match

Export to PDF, Print and Export to Word used to disagree: the PDF came out at about two thirds
of its size with no page numbers, so the better route to a PDF was through Word. Now all three
start from one statement of the page - the same faces, sizes and spacing - and the same choices
in their dialogs. PDF and Print lay the document out into real pages at true size, with a
running header, page numbers, and an optional title page and contents page with page numbers,
the very ones Export to Word offers and shares with them. Footnotes sit at the foot of their
page, wide code wraps instead of running off the edge, a collapsed `<details>` prints open,
and callouts and tables are kept off page breaks. The PDF carries bookmarks, tagging for screen
readers and the document's own title rather than a file path. Word catches up on the other side:
matrices, cases and `\left( … \right)` brackets grow with what they hold, `\color` and
`\colorbox` keep their colors, `align` lines are numbered, an equation Word has no form for goes
in as a picture of it, quotes in a row stay separate and nested ones step in, raw HTML tables
become real tables, a styled HTML block keeps its border and fill, and diagrams of the types
Word draws well go in as sharp vectors. Whatever an export cannot carry is named in its report
rather than dropped in silence, and the PDF now has a report too, shown only when something is
missing. Print is a two-column dialog that fits a laptop screen, and `Ctrl+P` in the cheatsheet
opens it rather than the browser's own.

## Fixes

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
