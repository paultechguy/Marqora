# Marqora vNext - What's New

<!--
    Notes for the next release, written as each change lands: a ## section per feature, and a
    paragraph with a bold lead per fix under ## Fixes. Write for someone deciding whether to
    download the release, not for someone reading the diff.

    build\New-ReleaseNotes.ps1 moves everything here into docs\releases\v<version>.md, drops
    any heading with nothing under it, and puts this stub back.
-->

## Command line

Marqora now comes with `marqora-cli`, its document commands for a terminal. It formats
documents, numbers or unnumbers their headings, finds and replaces, renumbers lists, sets the
direction of diagrams, finds dead links and missing pictures, prints a contents list or an
outline, exports documents as HTML, PDF or Word, and shares a set of documents as a Folio: a zip,
a folder, or a single page. It runs the same code as the app, so a document formatted from a
terminal is formatted exactly as Format Document would have formatted it, and a PDF is the one
File > Export > PDF would have made.

`marqora-cli export pdf "docs/**/*.md"` puts a PDF beside every document in a folder; `--out-dir`
gathers them in a folder of their own instead, and `--paper`, `--orientation`, `--margin` and
`--no-backgrounds` set the page for one run. What the app's export report would list, the run
lists on the document's own lines. Exports draw their documents with the same browser engine the
app uses, never on screen, and need a desktop you are signed in to.

It is built for folders and scripts: `marqora-cli check "docs/**/*.md"` checks every link in a
folder at once, `--dry-run` shows a change before it is made, `--check` makes a Git hook out of
Format Document, and `--json` and fixed exit codes let a script read the result. Like the app,
it is offline by default: collecting a Folio's web pictures is the one thing that uses the
network, and only when you ask for it by name. Without `--settings`, it uses the built-in
defaults rather than your Preferences, so the same command gives the same result on every
machine.

The installer asks whether to put it on your PATH, and the answer is No unless you say yes;
`Install.cmd -AddToPath` adds it without asking. The manual, with a five-minute tour, is
`docs/CLI.md`, and `docs/CLI-Recipes.md` shows everyday jobs step by step in PowerShell, cmd and
bash: checking a folder before a release, formatting only what you changed, keeping a contents
list up to date, exporting only the documents that changed, a dated Folio every week.

## Word export no longer depends on the preview

A Word export used to take its diagrams, equations and code colors from the preview, so a
preview that was still catching up, or had just been switched to another tab, could cost the
file its pictures. Marqora now draws them for the export in a browser of its own that is never
on screen, from exactly the text being exported. The same document gives the same Word file
whatever the window is doing. The first Word export after starting Marqora takes a moment
longer while that browser starts; the ones after it do not.

## Fixes

**Links to other folders are no longer marked broken.** A link such as `../README.md`, to a file
outside the document's own folder, was underlined as broken even though clicking it
in the preview opened the file. A link is now checked wherever it leads. Pictures are still
expected beside the document, because that is where the preview loads them from.
