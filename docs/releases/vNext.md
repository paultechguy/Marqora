# Marqora vNext - What's New

## Comment on a Document Without Changing It

Reading someone else's markdown and wanting to say something about it used to mean typing a
callout into the text, saving a copy, and sending the copy back. **Review > Start Commenting**
(Ctrl+Shift+R) does it the way Word and Acrobat do: select a passage in the preview, attach a
comment, and see it highlighted in the page with the note in a sidebar beside it.

- **Nothing changes in the document.** While you comment, the text is locked and the window shows
  the preview alone. The comments are kept in memory for this sitting only and are never written
  into the file. Your previous view comes back when you end the review.
- **Select, then Add comment.** An Add comment button appears over the text you select; click it or press
  Ctrl+Shift+M, type the note in the sidebar, and press Save or Ctrl+Enter. Hovering a comment in
  the page lights its card, and hovering a card lights its comment. Each card can be edited or
  deleted, and the numbers run down the page in reading order.
- **Share Review...** saves one read-only HTML page for the author: the document as you read it,
  with every comment highlighted and its note in the margin, and a line at the top saying which
  version you reviewed and when. It opens in any browser, needs nothing installed, and runs no
  script. The first review is offered in Documents; later ones go wherever you last saved one.
  **Show in Folder** and **Copy File** follow, and the second puts the file on the clipboard,
  ready to paste into an email or chat as an attachment.
- **Copy as Markdown** puts the source on the clipboard with every comment written in beside the
  words it is about, in CriticMarkup (`{==passage==}{>>comment<<}`). It is the form to paste into
  an AI and ask it to act on the comments. The shared page carries the same copy inside it.
- **End Commenting** asks first if you have comments nobody has seen, and closing the tab or the
  app asks the same. Ending is final: the comments go with it.

One document is reviewed at a time. A document with unsaved changes has to be saved before it
can be commented on, because the review locks it; pasted text in a new tab can be commented on
straight away. If the file changes on disk during a review, the banner says so and the comments
stay with the version you are reading. Pictures, diagrams and equations cannot be commented on.

## Fixes

**A reload can no longer land in the wrong tab.** Reloading a document from disk and closing its
tab straight afterwards could, while the file was still being read, write that file's text into
whichever tab had taken the closed one's place, and mark it saved, which lost that tab's unsaved
changes without a prompt. A reload now checks that its document is still there when the read
finishes, and one nobody asked for never overwrites edits typed in the meantime.

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

**A table with emoji in it lines up.** Format Document pads each cell so the pipes fall in one
column, but it measured by characters rather than by the room they take on screen. A ✅ or ❌ is
one character drawn two columns wide, so in a table with a status column of them every emoji row
ran one column past the header and the divider. Cells are now measured the way the editor draws
them: an emoji, or a Chinese, Japanese or Korean character, takes two columns, and a joined emoji
or a flag counts once. Arrows, dashes and check boxes such as `☐` are drawn one column wide and
still count as one.

**Formatting Options shows all of its rules.** The dialog lays its rules out in three columns,
but it was held to a width too narrow for them, so the third column — Blockquote space, Table
formatting, Code fences, Underlined headings and Emphasis markers — was cut off at the right edge,
along with the ends of the two notes beneath. The dialog is now wide enough for everything on it.

