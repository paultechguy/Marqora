# Marqora vNext - What's New

## Export to Word

`Tools > Export to Word...` turns the document in front of you into a real `.docx` — Word's own
heading styles, tables you can resize, footnotes at the foot of the page, diagrams as pictures,
and equations you can click into and edit. It opens in Word as a document somebody wrote there,
not as something converted.

Numbering is Word's own, both for lists and for numbered headings. Add a section in Word a
fortnight later and everything after it renumbers itself, the way it would in a document you
started there.

The dialog asks for paper size, orientation and margins — Word's own presets, so Normal is the
inch Word means by it — and offers three things only Word has: a header with page numbers, a
table of contents, and a title page. The title page is a template: it carries a title,
subtitle, date, version and author, fills in whatever your front matter knows, and leaves the
rest as gray words to type over. The last two are off unless you ask for them.

Ask for both and the file comes out as three Word sections, the way a report is put together:
the title page carries no header or page number, the contents number themselves i, ii, iii,
and the body starts again at 1. The footer is the number on its own. Your answers are remembered separately from the PDF ones.

## Replace All

`Edit > Replace All...` (`Ctrl+Shift+H`) is Find All with a **Replace with** box folded out —
the chevron beside the search box opens and closes it, so switching between finding and
replacing needs no dialog of its own. `Ctrl+H` still belongs to the editor's own replace, for
one document at a time; this one runs across every open tab at once, and runs the search itself
rather than waiting on a Find All you already ran. It asks before touching anything, naming how
many matches it found and in how many documents. In regular-expression mode the replacement can
reach into the match with `$1`, `$2`, `${name}` or `$&` — the icon beside the box lists whichever
of those the current pattern actually captured; in plain text mode the replacement goes in
exactly as typed. An empty **Replace with** box deletes every match outright, and the
confirmation says so plainly rather than leaving it to be discovered afterward. Enter on that
confirmation cancels: replacing across every open document at once is worth a deliberate click.

Nothing is written to disk on its own: every document it touches simply becomes unsaved, the
same as if you had typed the change yourself, and `Ctrl+Z` takes it back one document at a time,
whether or not that tab is the one on screen. A search matching more than 5,000 times is refused
rather than run partway and left inconsistent.

## Copy as PNG

A diagram can now be copied as a picture rather than as markup — ready to paste straight into a
document, a message or a ticket. It comes across cropped to the drawing itself, at twice its size
so it stays sharp, and with a transparent background wherever the application you paste into can
take one.

Both diagram menus offer it: right-click a diagram in the preview pane, or right-click inside the
window a double-click opens. `Copy as SVG` sits beside it in both, renamed from
`Copy Diagram (SVG)`.

## Maximize opened diagrams

Hold `Shift` while double-clicking a diagram in the preview pane and its window opens maximized,
with the diagram fitted to it. The `Preview` page of preferences has **Maximize opened diagrams**
for anyone who wants that every time; `Shift` then gives you the other one instead, so whichever
way the preference is set, the window you did not choose is still a key away. It works on a
diagram whose window is already open, too — that one comes forward maximized and refitted, which
is the short way back from a window you have zoomed into the corner of. It only ever grows such a
window; one you sized and parked yourself is never dragged back down.

Fitting a diagram to its window now enlarges as readily as it shrinks. It used to stop at full
size, which is what made a maximized window look wrong — the window filled the screen while a
small diagram stayed exactly as big as it had always been, marooned in the middle. A flowchart of
four boxes now fills the window it is given, and stays sharp doing it: the drawing is re-rendered
at the new size rather than stretched. `Fit to Window` in the diagram menu follows the same rule.

Right-clicking a diagram in the preview pane now offers `Open in Window` and `Open Maximized`
alongside the two copy items. Neither reads the preference: each does what it says.

## Numbered headings in the outline

With **Number headings** on, the outline panel (`View > Outline`, `Alt+4`) now shows the same
numbers the preview does, dimmed ahead of each heading — so a section reading `2.3` on the page
reads `2.3` in the panel beside it. There is no second switch to find: the one on the `Preview`
page of preferences drives both, and the panel and the page are never left disagreeing about
what a section is called.

The rules are the ones already in use. A heading above the level the count starts at is left
unnumbered in the panel too, so `From heading 2` leaves a document's title as plain words in
both places. A number belongs to the whole document rather than to whatever the panel happens
to be showing, so filtering the list, or shortening it with **List headings**, leaves every
number exactly as it was.

The filter box still searches the words alone — typing `2.3` finds nothing, because the number
is the preview's rather than the document's. Copying a row with `Ctrl+C` brings the number along
with the heading.

As before, none of this is written into your markdown.
