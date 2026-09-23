# Marqora vNext - What's New

## Fixes

**Format Document leaves `**____**` alone.** A table with a bold blank to fill in, written as
`**____**`, came back from Format Document as `****__**` in one cell and `**__****` in the next,
and the preview showed stray asterisks. The rule that settles bold on one style had read four
underscores as bold markers and then carried the match through the pipe into the next cell.
Bold and italic are now matched only within a single cell, and only when the markers are
exactly what they claim to be, so the placeholder comes back as it was written.

**A short table column stays on one line.** A table fills the width of the page, and when one
column holds sentences the browser pays for it by squeezing the others down to their longest
single word — so a "Due date" header broke over two lines beside a column of notes, and the usual
cure was a row of `&nbsp;` typed into the header. Markdown has no way to say how wide a column
should be, and now it does not need one: a column whose every cell is short keeps its words
together in the preview, in exported HTML and PDF, and in Copy as Rich Text. A table made of
nothing but short columns is left exactly as it was, and only so many columns are held unwrapped
in any one table, so a wide table still fits a narrow pane rather than running off the page.
Word export is unchanged — it has always given each column a share of the width measured from
what is in it, so a short column there never had to fight for room.

**`&amp;` in a heading reads as `&` in the outline.** A character written as a code — `&amp;`
for an ampersand, `&nbsp;` for a space that will not break, `&#233;` for é — always showed
correctly in the preview and in exported documents, but was dropped from a few places that read
the text on its own: the outline showed `# Q&amp;A` as "QA", and a picture missing from a Word
export left "[QA]" in its place. Both now show the character it stands for. Links to headings
are unchanged, with one small exception: a heading containing a non-breaking space typed as the
character itself, or a tab, now gets the same anchor GitHub gives it.

