<!--
    The shape of a release's notes when written from nothing. build\New-ReleaseNotes.ps1 now
    builds docs\releases\v<version>.md from docs\releases\vNext.md instead; this file remains
    what Publish-Release.ps1 compares the notes against to catch a release nobody wrote up.

    Write for someone deciding whether to download this, not for someone reading the diff.
    Delete any heading that has nothing under it - an empty section reads worse than no
    section. Publish-Release.ps1 refuses to run while a placeholder is left behind, or while
    this file still matches the template it came from.

    Install steps, the download link and the checksum are added automatically when the
    release is published. Do not repeat them here.
-->

# Marqora {{VERSION}}

## What's new

TODO - one short paragraph. What can someone do with this release that they could not do
before, or what stopped being annoying?

### Added

- TODO

### Changed

- TODO

### Fixed

- TODO

### Known issues

- TODO - or delete this heading
