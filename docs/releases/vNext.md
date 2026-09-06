# Marqora vNext - What's New

## Paste an Image

Take a screenshot, press `Ctrl+V` in a document, and it is there. Marqora writes the picture
into a folder beside the document — `guide.md` gets `guide.assets` — and puts
`![](guide.assets/image-1.png)` in at the caret, with the cursor already between the alt-text
brackets so the next thing you type describes the picture. The status bar says what was saved
and where. Nothing else asks you anything.

Where the file lands is yours to choose, on the Editor page of Preferences: a folder named after
the document, one shared `images` folder for everything in that directory, or directly beside
the document. A folder per document is the default because it makes "which pictures belong to
this file" answerable by looking, which is what Save As and every later tidy-up depend on.

Screenshots are large, and a capture from a 4K monitor is several megabytes of PNG that is wider
than the preview will ever show it. So anything Marqora encodes itself is held to a width — 1920
by default, adjustable, and switchable off. Image *files* are exempt: a file you copied or picked
is copied byte for byte, because a file you chose is a file you meant. There is a separate box if
you want the limit to apply to those too, and it is off.

Pasting into a document that has never been saved asks you to save it first. "Beside the
document" has no meaning until there is one, and writing pictures to a temporary folder to be
moved later would give you a document whose links are correct only until you close it.

As ever, nothing goes near the network. A picture copied from a web page is taken from the
clipboard's own bitmap; the URL sitting beside it is never fetched.

## Three More Ways In

`Insert > Image...` picks a file. It is copied in beside the document and linked, the same as a
paste — except when the file is already in the folder the paste would have put it, in which case
it is referenced where it lies rather than duplicated under a numbered name.

`Ctrl+V` also takes a file copied in Explorer, which is the same journey with the original bytes
preserved exactly. Copy several at once and they arrive as separate images, one per block.

`Insert > Screen Clip...` opens Windows' own snipping overlay — the one you already know from
`Win+Shift+S` — and puts the capture straight into the document when you are done. Escape it and
nothing happens. The item is hidden on a machine where the Snipping Tool has been removed.

## Undo Takes the File With It

Press `Ctrl+Z` after pasting and the reference goes, as you would expect — and so does the file,
which you might not. A folder that quietly fills with pictures nobody chose to keep is the usual
cost of a paste feature, and it seemed worth not charging.

Two rules keep that safe. Files are **moved, not deleted**: they go to a folder under Marqora's
own data directory, so redo brings them back and a mistake costs a folder to look in rather than
your screenshot. And it stops at the first save — once a document has been written to disk with
the picture in it, that picture belongs to a saved file and Marqora never touches it again. Undo
an hour later in an unsaved document and the file still goes; delete the line a week after saving
and it stays exactly where it is.

## Save As Brings the Pictures Along

Saving a document somewhere else used to leave every image behind, with the references pointing
at a folder that is no longer next to it — the sort of thing found weeks later by a reader rather
than by the author. Save As now notices and offers to copy them across, repointing the references
to match. Decline and nothing moves; the links that no longer resolve are underlined, so the
document says so rather than quietly rotting.

## The Source Pane Learned About Images

Hover an image reference in the markdown and you get a thumbnail of it. This is the payoff for
keeping the source visible: every other editor in this category either hides the markdown or
shows you a path.

Type `![](` and Marqora offers the image files near the document, with the relative path already
correct and a preview in the details pane. It appears on the bracket rather than as you write
prose, so it stays out of the way of writing.

An image with no alt text is underlined too, in grey rather than a warning color, because nothing
is broken — a picture nobody has described still renders. Badges wrapped in a link are skipped,
since the link already carries the name, and the whole check has its own box on the Preview page
if you would rather not be reminded.

## Dead Links Offer to Fix Themselves

Broken links, missing images and headings that no longer exist are now marked the way a
misspelling is, and answer the same way: right-click one, or press `Ctrl+.`, and Marqora offers
what you probably meant. A `logo.png` that should have been `logo.PNG`, a `#getting-stated` that
should have been `#getting-started`. Nothing on the menu but the fix, and the option to remove
the reference entirely.

The hover says what is wrong and stops there. The old marker brought a "View Problem" row and a
"No quick fixes available" line, the second of which was never true, and neither said anything
the squiggle had not.

## An Insert Menu, and a Shorter Format Bar

The Format menu had grown to fourteen items answering two different questions, so the things you
*insert* moved to a menu of their own: **Insert** (`Alt+I`) holds images, screen clips, links,
tables, code blocks, rules, diagrams and snippets. Format keeps what marks up text that is already
there. Nothing in Insert needs a selection to mean something and everything in Format does, which
is the line the two are named for.

The format bar lost its Diagram dropdown to that menu and gained a Code Block button, which was
costing two clicks for a one-keystroke command. The dropdown that remains is called Snippet,
because that is now what it holds. Inline code and code block wear `</>` and `{ }` — the marks
other editors use, and, at last, two different ones.

## Fixes

**Save As now re-points the document.** After saving to a new folder the preview kept serving
images from the old one, the link checks kept reporting against it, and a printed page was still
filed under the previous name. All three followed the document.

**Edit > Paste is one undo step.** It also respects the file's line endings; pasting several
lines into a CRLF document used to leave lone newlines behind.

**A sibling folder is no longer mistaken for the document's own.** `C:\docs2` passed a check
meant to admit only things inside `C:\docs`, in three separate places.
