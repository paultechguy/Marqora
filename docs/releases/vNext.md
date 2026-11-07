# Marqora vNext - What's New

<!--
    Notes for the next release, written as each change lands: a ## section per feature, and a
    paragraph with a bold lead per fix under ## Fixes. Write for someone deciding whether to
    download the release, not for someone reading the diff.

    build\New-ReleaseNotes.ps1 moves everything here into docs\releases\v<version>.md, drops
    any heading with nothing under it, and puts this stub back.
-->

## Marqora for ARM PCs

Marqora now comes in a version built for Windows 11 on ARM, alongside the x64 one. Every release
carries both zips, and the release page says how to tell which one your PC needs. The app and
`marqora-cli` do the same things in both.

If you have been running the x64 version on an ARM PC, it keeps working, and installing the ARM
version over it keeps your settings. The x64 installer now tells you when an ARM version is
available for your PC. The ARM installer stops on an x64 PC before it changes anything, and says
which zip to get instead.

## Fixes
