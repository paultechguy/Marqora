# Marqora vNext - What's New

## A document you only meant to read

Some documents are open to be consulted rather than written: a specification, a style guide,
somebody else's README sitting beside the thing you are actually working on. Right-click its tab
and tick **Read-Only**, or use `File > Read-Only`, and Marqora will not write that file again
until you take the tick off. The mark is remembered per file, so it is still there when you open
the document next week.

It is worth having because of autosave. With autosave on, one stray keystroke in a document you
were only reading is enough to have it saved — and if you also format on save, the whole file is
reformatted on its way to disk, without a prompt. A marked document is skipped by autosave
entirely, Save and Save All pass it by, `Ctrl+Shift+H` leaves it out of Replace All rather than
counting it and then skipping it, and the commands that rewrite a document say so in the status
bar instead of quietly doing nothing.

Marking a document you have already started editing is allowed, and it does not take your
editing away: the text stays, undo keeps working, and nothing reaches the file. **Save As** is
how you keep such edits — it writes them somewhere else and the copy arrives unmarked, ready to
be worked on normally. Closing that tab offers Save As rather than Save, for the same reason.

Two things a mark deliberately does not do. It is Marqora's own, not the read-only tick in the
file's Windows properties — this never touches that, in either direction. And it does not stop
the document changing: a marked file rewritten by something else is still picked up the way any
other open file is. It stops *Marqora* writing your file, which is the part that was happening
by itself.

Select All and Copy work as they always did. Cut copies instead of cutting, and says so.

## A callout takes the text you were looking at

A callout is almost always made *out of* something already written, and inserting one used to drop
an empty one above it and leave you to retype the sentence. `Insert > Callouts` now takes what you
have selected in with it — a few words out of the middle of a paragraph splits the paragraph, with
what came before and after left reading correctly on either side — and with nothing selected, the
paragraph the caret is in goes in whole. A caret in a list, a table or a code block still inserts
an empty callout, on the grounds that clicking *Note* should not silently move a forty-line table;
select the table first and it wraps.

Your own snippets can do it too: `$SEL` in a snippet file is where the captured text goes, beside
the `$0` that has always said where the caret lands.

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

**A pasted table gets its header back.** Copy as Rich Text carries the preview's own stylesheet
across, and Word and Outlook do read it — code keeps its highlighting, links keep their color — but
a table is not laid out from CSS. Word imports one into its own table model and takes each cell's
shading from that cell, so the rule describing the header was understood and then dropped at the
table boundary, and a pasted table lost the line between the head and the data entirely. The table
rules are now written onto the cells themselves on the way to the clipboard, read back out of the
same stylesheet so the colors are still stated only once. Striped rows come with them, having
depended on a selector Word does not implement either.

**Callouts paste in their own colors.** The same fragment described its tints with modern CSS that
Word and Outlook do not recognize as color at all, so each declaration was thrown away rather than
approximated. A note, tip, important, warning or caution box kept the plain gray underneath its
tint and quietly came out the wrong color. Those shades are now worked out against the white they
are dropped onto before the fragment leaves. An exported HTML file is unchanged either way, since a
browser blends them correctly on its own.

**A diagram pasted into Word is a diagram.** Copying a document with a mermaid diagram in it put
the diagram's labels into the pasted document as ordinary lines of text — "Source pane", "Preview
pane" and the rest, one after another, reading as prose the author never wrote. A diagram is an
inline drawing, which Word does not draw and does not skip either: it throws the shapes away and
keeps the words inside them. The clipboard now carries the picture instead, the same one the Word
export and the preview's own Copy as PNG already use, so a diagram copied from the page matches the
one copied from its own window. A diagram the shell cannot draw is left out altogether rather than
scattered across the page.

**An equation pasted into Word is an equation.** Math went across as a scramble of overlapping
characters. KaTeX draws every equation twice — once as MathML, which it hides, and once as the
visible version, built from positioned pieces measured against a font — and both were being
copied. Word has no way to place the positioned pieces and the MathML it could have used was
hidden, so the two arrived on top of each other. The clipboard now carries the MathML on its own,
which Word turns into one of its own equations: a real one, that the person you sent it to can
click into and edit, rather than a picture or a row of stray symbols. The preview, printing and
every exported file keep the drawn version, which is the better of the two in a browser.

**A copied document carrying math is much smaller.** Because an equation now travels as MathML,
the fonts KaTeX draws its own version in have nothing left to set: they were going onto the
clipboard anyway, twenty files encoded into roughly 430 KB on every copy of a document with an
equation in it. They are no longer included. The one rule that still matters — the one that puts a
display equation on its own centered line — goes across as before, and exported HTML files and
Folios are unchanged, since those carry the drawn version and genuinely need the fonts.
