# Marqora vNext - What's New

<!--
    Notes for the next release, written as each change lands: a ## section per feature, and a
    paragraph with a bold lead per fix under ## Fixes. Write for someone deciding whether to
    download the release, not for someone reading the diff.

    build\New-ReleaseNotes.ps1 moves everything here into docs\releases\v<version>.md, drops
    any heading with nothing under it, and puts this stub back.
-->

## Fixes

**The preview's paragraph bar stays put while you edit.** In Side by Side view, the bar beside
the preview paragraph the caret is in disappeared on any edit inside that paragraph — typing,
deleting a selection, or undoing — and stayed gone until the caret moved to another paragraph.
It now holds through edits, and it appears straight away when you switch into Side by Side
rather than waiting for the caret to move.
