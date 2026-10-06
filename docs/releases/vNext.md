# Marqora vNext - What's New

<!--
    Notes for the next release, written as each change lands: a ## section per feature, and a
    paragraph with a bold lead per fix under ## Fixes. Write for someone deciding whether to
    download the release, not for someone reading the diff.

    build\New-ReleaseNotes.ps1 moves everything here into docs\releases\v<version>.md, drops
    any heading with nothing under it, and puts this stub back from
    build\release-notes-vnext.md.
-->

## A margin around diagrams copied as PNG

**Copy as PNG** now leaves a little space around the diagram, so its outer shapes no longer
touch the edge of the picture you paste. This applies in the preview and in a diagram's own
window.

## Fixes

**A diagram opened in its own window fits the window.** Double-clicking a diagram could open
it too wide for its window, with its right side cut off. A very wide diagram stopped shrinking
at 25%, and others were sometimes measured before the window had reached its size. The window
now opens with the whole diagram in view, however wide it is, and keeps it fitted as you resize
the window until you choose a zoom of your own.
