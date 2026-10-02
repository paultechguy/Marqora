# Marqora vNext - What's New

<!--
    Notes for the next release, written as each change lands: a ## section per feature, and a
    paragraph with a bold lead per fix under ## Fixes. Write for someone deciding whether to
    download the release, not for someone reading the diff.

    build\New-ReleaseNotes.ps1 moves everything here into docs\releases\v<version>.md, drops
    any heading with nothing under it, and puts this stub back from
    build\release-notes-vnext.md.
-->

## Fixes

**Commands that would change a read-only document are grayed out.** The Tools menu's format and
section-number commands, Undo, Redo, Replace, and the editor's right-click edits used to stay
lit on a read-only document. The confirmation for **Format All Open Documents** now counts
only the documents it can change, and says how many read-only ones it will skip.

**Long notes in sequence diagrams stay inside their box.** A note spanning two participants
used to run past both edges of its box when the text was long. It now wraps to fit, in the
preview, the cheatsheet and exports.
