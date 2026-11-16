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
rather than waiting for the caret to move. It no longer lingers in Preview view, where there is
no caret for it to follow.

**A colon before closing bold no longer turns into a face.** Typed faces such as `:)` and `:*`
were converted to emoji, and they are made of markdown's own punctuation: a line like
`` **`settings.json`:** `` read `:*` as a kiss, which took one of the closing asterisks and left
the bold unfinished. Marqora now does what GitHub does: `:rocket:`-style shortcodes still become
emoji, and typed faces stay as the text you wrote, in the preview and in every export.
