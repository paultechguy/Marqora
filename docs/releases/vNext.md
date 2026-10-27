# Marqora vNext - What's New

<!--
    Notes for the next release, written as each change lands: a ## section per feature, and a
    paragraph with a bold lead per fix under ## Fixes. Write for someone deciding whether to
    download the release, not for someone reading the diff.

    build\New-ReleaseNotes.ps1 moves everything here into docs\releases\v<version>.md, drops
    any heading with nothing under it, and puts this stub back.
-->

## Command line

Marqora now comes with `marqora-cli`, its document commands for a terminal. It formats
documents, numbers or unnumbers their headings, finds and replaces, renumbers lists, sets the
direction of diagrams, finds dead links and missing pictures, prints a contents list or an
outline, and shares a set of documents as a Folio in the zip or folder form. It runs the same
code as the app, so a document formatted from a terminal is formatted exactly as Format Document
would have formatted it.

It is built for folders and scripts: `marqora-cli check "docs/**/*.md"` checks every link in a
folder at once, `--dry-run` shows a change before it is made, `--check` makes a Git hook out of
Format Document, and `--json` and fixed exit codes let a script read the result. Like the app,
it is offline by default: collecting a Folio's web pictures is the one thing that uses the
network, and only when you ask for it by name. Without `--settings`, it uses the built-in
defaults rather than your Preferences, so the same command gives the same result on every
machine.

The installer asks whether to put it on your PATH, and the answer is No unless you say yes;
`Install.cmd -AddToPath` adds it without asking. The manual, with a five-minute tour, is
`docs/CLI.md`, and `docs/CLI-Recipes.md` shows everyday jobs step by step in PowerShell, cmd and
bash: checking a folder before a release, formatting only what you changed, keeping a contents
list up to date, a dated Folio every week. The single-page Folio, and exporting HTML, PDF and
Word, come in a later release.

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

## Flowchart labels no longer pile up

Mermaid puts each label in a flowchart at the middle of its line, so where two lines ran close
together their labels could land on top of each other and neither could be read. Marqora now
slides one along its own line until both are clear, in the preview and in every print and
export. Only the drawing changes; your document is never touched. Preferences > Preview has a
switch to turn it off.

## Fixes

**A diagram opened in its own window fits the window.** Double-clicking a diagram could open
it too wide for its window, with its right side cut off. A very wide diagram stopped shrinking
at 25%, and others were sometimes measured before the window had reached its size. The window
now opens with the whole diagram in view, however wide it is, and keeps it fitted as you resize
the window until you choose a zoom of your own.

**Faster startup with diagrams.** Reopening your last session prepared every document twice
and drew the diagrams in the front tab twice. Each is now done once, so a document full of
diagrams is ready in about half the time.

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

**The source pane's Find bar stands out.** The find and replace bar at the top right of the
source pane was nearly the color of the page behind it, especially in light mode. It now has a
gray panel, a border and a shadow, its search box reads as a box, and Match Case, Whole Word
and Regular Expression show clearly when they are on.
