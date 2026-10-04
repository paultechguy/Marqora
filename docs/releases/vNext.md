# Marqora vNext - What's New

<!--
    Notes for the next release, written as each change lands: a ## section per feature, and a
    paragraph with a bold lead per fix under ## Fixes. Write for someone deciding whether to
    download the release, not for someone reading the diff.

    build\New-ReleaseNotes.ps1 moves everything here into docs\releases\v<version>.md, drops
    any heading with nothing under it, and puts this stub back from
    build\release-notes-vnext.md.
-->

## Color themes

Give your documents color without touching them. Pick a color theme and Marqora recolors how
every document looks: headings, links, callouts, tables, code and its syntax, lists, quotes,
highlights and diagrams. Eleven themes ship: Default, Ember, Forest, Nordic, Ocean, Orchid,
Pastel, Slate, Sunset, Sweetheart and Vintage Press, each in both light and dark.

Click the palette button at the right-hand end of the menu bar, next to the zoom buttons, to
see them. Point at a theme and the preview changes to it at once, and you can scroll the
document to see more of it before you decide. Click to keep it, or close the gallery to go
back. The same choice is on `View > Color Theme` and in Preferences.

Your prints, PDFs, Word and HTML files, Folios, review pages and copies use the same theme, in
its light colors, so what you send looks like what you saw. If you'd rather keep a colorful
screen and send plainer files, choose a different theme for exports under **Preferences >
Appearance > For exports**.

A theme is a setting, not part of a document: your markdown files are never changed, and
someone opening your file in another editor sees it as they always would.

Default is Marqora as it has always looked, with a few colors darkened so they meet the usual
readability standard: links, the note and warning callout titles, the smallest heading, list
markers, and some code colors. A Word export with Default now follows the preview too, with
near-black headings and a pale table header in place of the old teal ones.

## Everything you print, export or copy is light

Dark mode now stays on your screen. A print, a PDF, an HTML export, a Folio, a Word document,
a shared review page, and a diagram copied as PNG, SVG or rich text all come out light, even
when Marqora is dark. Diagrams used to come along in their dark colors, with pale text on
dark boxes that all but disappeared on a white page, and code copied as rich text carried
the dark theme's colors. A diagram opened in its own window prints, exports and copies light
too, and so does the cheatsheet's Print. A diagram that sets its own theme still uses it.

## Lay a wide diagram out the other way

A diagram drawn left to right can be too wide for a page, and print shrinks it until its text
is hard to read. Now you can redraw it top to bottom. Put the cursor in the diagram and choose
**Format > Diagram Layout > Top to Bottom**, or right-click inside it in the source pane for
the same choices. **Left to Right** turns a tall diagram the other way, and a check mark shows
which way the diagram runs now.

Mermaid lays the whole diagram out again in the new direction with its text upright, and
prints, PDFs, Word exports and copies all follow. Marqora changes only the line that says which
way the diagram runs, one diagram at a time, and `Ctrl+Z` undoes it in one step. Subgraphs keep
any direction they set for themselves.

Flowcharts, state, class, ER and requirement diagrams, git graphs and timelines can be turned.
Diagrams with no direction to change, such as sequence diagrams and Gantt charts, show the
item grayed out, and so does a read-only document.

## Fixes

**Commands that would change a read-only document are grayed out.** The Tools menu's format and
section-number commands, Undo, Redo, Replace, and the editor's right-click edits used to stay
lit on a read-only document. The confirmation for **Format All Open Documents** now counts
only the documents it can change, and says how many read-only ones it will skip.

**Numbered equations no longer show a stray scroll bar.** An `align` or other numbered display
math block could draw a horizontal scroll bar under itself with nothing to scroll, because the
equation numbers reached two pixels past the edge of the page.

**Colored regions in a sequence diagram are readable in dark mode.** A `rect` region painted a
pale color, which is what most authors choose, left the messages and arrows over it nearly
invisible in dark mode. Marqora now shows such a region as a deep shade of the same color on
screen, so its contents read clearly. Prints and exports keep the color as written.

**Highlighted math is readable in dark mode.** A formula inside a `\colorbox` drew white text on
the bright color the author chose, usually yellow, in dark mode. Marqora now shows a light color
box as a deep shade of the same color on screen. Prints, exports and copies keep the color as
written.

**User journey labels sit in their boxes.** The task labels in a user journey diagram could sit
off their boxes in the preview and in a PDF, though the diagram looked right when opened in its
own window.

**The outline filter can always be cleared.** After you clicked a heading in a filtered outline,
the button that clears the filter disappeared, leaving the filter on. A clear button now shows
whenever the filter has text.

**Long notes in sequence diagrams stay inside their box.** A note spanning two participants
used to run past both edges of its box when the text was long. It now wraps to fit, in the
preview, the cheatsheet and exports.

**Diagrams copied as PNG paste into Word and Outlook.** **Copy as PNG** pasted nothing in Word
or Outlook, though it worked in Teams. It now pastes everywhere as a single picture on a white
background, which stays readable in apps with a dark theme.

**Tall diagrams fit on one printed page.** A diagram taller than a page used to be cut off at
the bottom in a print or a PDF. A diagram now prints at its normal size when it fits, and
otherwise shrinks evenly until it fits the page. The "Double-click to open" label no longer
prints either.
