# Marqora vNext - What's New

## Paste an Image

Take a screenshot, press `Ctrl+V` in a document, and it is there: Marqora writes the picture
into a folder beside the document — `guide.md` gets `guide.assets` — and puts the reference in
at the caret, with the cursor already between the alt-text brackets. Where the file lands is
yours to choose on the Editor page of Preferences, and anything Marqora encodes itself is held
to a width, 1920 by default, so a capture from a 4K monitor does not arrive as several
megabytes of PNG wider than the preview will ever show it. Images have a menu of their own now:
`Insert > Image...` picks a file, `Insert > Screen Clip...` opens Windows' own snipping overlay,
and `Ctrl+V` takes a file copied in Explorer just as happily as a bitmap. `Ctrl+Z` takes the
file back out again as well as the reference — moved to a folder under Marqora's own data
directory rather than deleted, and never once the document has been saved with the picture in
it. Save As notices the images and offers to bring them along, repointing the references to
match. In the source pane, hovering a reference shows a thumbnail, typing `![](` offers the
image files near the document with the relative path already correct, and an image with no alt
text is underlined in grey rather than a warning color, because nothing is broken. As ever,
nothing goes near the network: a picture copied from a web page is taken from the clipboard's
own bitmap, and the URL sitting beside it is never fetched.

## Check for Updates

Marqora still asks GitHub nothing, which is exactly why it cannot tell you whether a newer
version exists — a check would be a network call, and the promise on the front of the README is
worth more than the convenience. What it keeps instead is a clock: after thirty days a reminder
appears in the status bar suggesting you look, and clicking it hands the releases page to your
browser. It is a live reminder rather than a check at startup, because this is the sort of
program people leave open for weeks; it waits for a pause in your typing, gives way to the
changed-on-disk notice when both have something to say, and appearing is what resets it, so
ignoring it does not bring it back tomorrow. Installing a new version resets it too. The
interval lives on the Advanced page of Preferences and zero switches it off, `Help > Check for
Updates` opens the same page whenever you want it, and **About Marqora** says when the next
reminder is due.

## Fixes

**Save As now re-points the document.** After saving to a new folder the preview kept serving
images from the old one, the link checks kept reporting against it, and a printed page was still
filed under the previous name. All three followed the document.

**Edit > Paste is one undo step.** It also respects the file's line endings; pasting several
lines into a CRLF document used to leave lone newlines behind.

**A sibling folder is no longer mistaken for the document's own.** `C:\docs2` passed a check
meant to admit only things inside `C:\docs`, in three separate places.
