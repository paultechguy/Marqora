# Marqora vNext - What's New

## Pick a shared review up again

A review page you shared can now be dropped back into Marqora, or opened with
**Review > Resume Shared Review...**, and the review starts again where it was: the text as you
reviewed it, its pictures, and every comment in place, in a tab of its own labeled like
`notes.md (review)`. Hours later, days after you ended it, or on another computer. Share then
offers to save back to the page it came from. A resumed review exports whole too - Word, HTML,
Copy as Rich Text and a Folio all carry its pictures. Pages shared by 1.0.10 cannot be resumed;
they were written before the page carried what resuming needs.

## Comment on a diagram

A review can now comment on a diagram. While you are commenting, right-click a diagram and
choose **Comment on Diagram...**, or point at it and click **Comment on diagram** in its corner.
The comment is about the whole diagram: it shows as a numbered badge on the diagram, with the
note in the margin of the shared page, and in **Copy as Markdown** it follows the diagram as
`{>>On diagram (flowchart, line 42): ...<<}`, leaving the diagram itself as it was. A shared
page that has a diagram comment needs this version of Marqora or later to resume.

## Hide and show the outline where it is

The outline no longer needs the View menu or Alt+4 to put away. A button at the end of the
**Filter outline** row hides it, and a narrow strip stays behind at the edge of the window with
a button that brings it back. Double-clicking works too: in the empty space below the headings
to hide the outline, and anywhere on the strip to show it again.

## Links to files on your computer

Clicking a link in the preview to a file beside your document - `[Merge strategy](MERGE-STRATEGY.md#incident-history)`,
`../specs/plan.md`, `budget.xlsx` - no longer opens a browser on an address that goes nowhere.
A markdown document opens in Marqora, or comes forward if it is already open, at the heading
the link names. Anything else opens in the app Windows uses for it, a link to a folder opens it
in Explorer, and a link to a file that is not there says so, with the path it was looking for.
A file Windows would run as a program - an `.exe`, a script, a shortcut - always asks first.
To have links to other files shown in their folder instead of opened, use **Preferences >
Preview > Links**.

## Fixes

**Side-by-side view keeps the line you are editing in view.** The preview used to line up only
the top of each pane, so in a file whose lines wrap in the source pane, a line near the bottom
of the source could sit well above its match in the preview, or off the top of it, and near the
end of a document the preview ran ahead of the source. The preview now keeps the line with the
cursor at the same height as it is in the source, and follows a line a third of the way down
when you scroll the cursor away. Find Next from the find box moves the preview too.

**Remove Section Numbers finds sub-sections numbered under a named parent.** A plan written as
`## Phase 1 — Prep` over `### 1.1`, `### 1.2`, then `## Phase 2 — Cutover` over `### 2.1`, was
not recognized as numbered, because the front of each number is the parent's name in words
rather than a count the document keeps, so the command found nothing to remove. It now reads
sub-sections that share their parent's number and count up from 1 beneath it: `1.1 Pipeline
routing` becomes `Pipeline routing`, and `Phase 1 — Prep` keeps its name. A document the
ordinary count already explains is read exactly as before, and a run like `### 2.4 Spring`,
`### 2.5 Summer` still stays a list of versions. The confirmation also stopped counting headings
with no number at all among the ones it calls years or quantities.

**File > Open takes several files at once.** The Open dialog used to accept only one file, so
opening a handful meant going back to it for each, or dragging them in from Explorer instead. It
now takes as many as you select with Ctrl+click or Shift+click. They open in tabs sorted by name,
with the first one active, and more than 25 asks first, as Open Folder and a drop already do. If
some of them cannot be read, the rest still open, and one message afterwards names the ones
that did not. A Folio is still unpacked on its own - picked together with other files, the
Folio is unpacked and the others are ignored, so open them separately.
