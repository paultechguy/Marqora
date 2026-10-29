# marqora-cli: Marqora's commands in a terminal

<!--
For whoever edits this file. Tests in tests/PaulTechGuy.MQ.Cli.Tests/Manual hold it to the code:

- Every command, alias and option in the command tree has to appear here: each command under a
  heading of its own, and each option in backticks in that command's section. Every example in
  the commands' --help has to appear here word for word.
- The exit-code table has to match ExitCode, and every JSON field and outcome has to be named.
- A fenced block whose first line starts with "marqora-cli " is an example. Each one is run in a
  fresh copy of docs/cli-tour, and the ```text block after it has to be what the terminal shows:
  standard output, then standard error. Put a "cli-example: exit N" comment before one that
  exits with something other than 0, and a "cli-example: no-output" comment before one that
  shows no output, such as a block listing several commands. Either comment anywhere else fails.
-->

`marqora-cli` does what Marqora's Format, Find, Export and Share menus do, without opening a
window: format a document, number its headings, find dead links, list its contents, export it as
HTML, PDF or Word, or share a set of documents as a Folio. It runs the same code the app runs, so
a file formatted here is formatted exactly as **Format Document** would have formatted it, and a
PDF is the one **File > Export > PDF** would have made.

It is for the jobs that are tedious one tab at a time: formatting a folder of notes, checking
every link before a release, a PDF of every document in a folder, or a script that builds the
same Folio every Friday. The [recipes](CLI-Recipes.md) show jobs like these step by step, in
PowerShell, cmd and bash.

Like the app, it is **offline by default**, and nothing happens that you didn't decide. One
option uses the network, `folio --fetch-web-images`, and only when you type it: it collects the
pictures your documents name by web address, from the sites `--dry-run` lists first. An export
draws its document without it, and a picture named by web address is reported, not fetched.

## Install and PATH

`marqora-cli.exe` comes with Marqora, in the same folder as `Marqora.exe`. Installing Marqora
installs it, and its version is always the app's.

This manual comes with it too. It is in the `docs` folder beside `marqora-cli.exe`, with the
[recipes](CLI-Recipes.md) and the [tour](cli-tour/) folder, and **Help > Command Line Manual**
opens it in Marqora, read-only. Every command's `--help` ends by saying where it is.

To run it from any terminal, its folder has to be on your PATH. The installer offers to do that:

- **Double-clicking `Install.cmd`** asks "Add it to your PATH, so any terminal can run
  marqora-cli?" The answer is No unless you type `y`.
- **`Install.cmd -AddToPath`** adds it without asking. `-Quiet` on its own never adds it.

The entry goes on your own PATH, not the machine's, so no administrator is needed. Open a new
terminal afterward: one that was already open still has the old PATH. Uninstalling takes the
entry out again, but only if the installer added it; a folder you put on PATH yourself is left
alone.

Without the PATH entry, run it by its full path:

```powershell
& "$env:LOCALAPPDATA\Programs\Marqora\marqora-cli.exe" version
```

To check it is there:

<!-- cli-example: no-output -->

```powershell
marqora-cli version
marqora-cli --help
```

## A five-minute tour

The tour uses two small documents in [`docs/cli-tour`](cli-tour/), `Guide.md` and `Setup.md`.
Copy that folder somewhere, open a terminal in it, and type along. `Guide.md` has a few mistakes
in it on purpose.

### Find what is broken

`check` finds what the source pane underlines: links to files that are not there, pictures that
are missing, and anchors that point at no heading.

<!-- cli-example: exit 1 -->

```powershell
marqora-cli check Guide.md
```

```text
Guide.md:7:46: error: Nothing at "Glossary.md". [broken-link]
Guide.md:11:1: error: No image at "images/workflow.png". [missing-image]
Guide.md:28:5: error: Nothing in this document is named "#step". [dead-anchor]
```

Each line gives the file, the line and the column, so an editor or a terminal can jump to it.
The word in brackets is the kind of problem, the same in every run, for a script to filter on.
`check` exits 1 when it finds any, which is what makes it useful before a commit.

### Look at the structure

`outline` prints the heading tree with each heading's line number, as the outline pane shows it:

```powershell
marqora-cli outline Guide.md
```

```text
    1  Field Guide
    5    Getting started
   13    Steps
   19    The flow
   26    Questions
```

`toc` prints the same headings as a list of links, ready to paste into the document:

```powershell
marqora-cli toc Guide.md
```

```text
- [Getting started](#getting-started)
- [Steps](#steps)
- [The flow](#the-flow)
- [Questions](#questions)
```

### See a change before you make it

Every command that changes a document has three ways to run. Without a flag it prints the changed
document. `--dry-run` shows what would change, as a diff, and touches nothing. `--write` makes
the change in the file.

<!-- cli-example: exit 1 -->

```powershell
marqora-cli format --check Guide.md Setup.md
```

```text
marqora-cli: Guide.md: would change.
```

`--check` names the files that would change and exits 1 if there are any, so a script can ask
"is everything formatted?" without reading a diff. Here `Setup.md` is already formatted. To see
what would happen to `Guide.md`:

```powershell
marqora-cli format --dry-run Guide.md
```

```text
--- a/Guide.md
+++ b/Guide.md
@@ -13,8 +13,8 @@
 ## Steps

 3. Open the document
-7. Pick a color theme
-9. Share it as a Folio
+4. Pick a color theme
+5. Share it as a Folio

 ## The flow

```

Format Document renumbers the list from where it starts. Happy with that:

```powershell
marqora-cli format --write Guide.md
```

```text
marqora-cli: Guide.md: written (2 lines changed).
```

### Share it

`folio` makes a Folio of the documents and the pictures they use, as **File > Share as Folio**
does. `--dry-run` shows the preflight, what the Folio window would tell you before you share:

```powershell
marqora-cli folio --dry-run -o Field-Guide.zip Guide.md Setup.md
```

```text
Folio:      Field-Guide.zip, a zip
Theme:      Default, from the default
Documents:  2
  Guide.md
  Setup.md
Pictures:   1
Missing:    1 picture not on this machine
Outside:    1 link pointing outside the Folio
Web:        0 pictures left on the web
Estimate:   about 24 KB
marqora-cli: Guide.md:7: warning: Points outside the Folio: Glossary.md [outside-link]
marqora-cli: Guide.md:11: warning: Not on this machine: images/workflow.png [missing-image]
```

The warnings are the rows of the Folio report. They do not stop the Folio from being made; they
say what the person you send it to will not have. Without `--dry-run`, the Folio is written:

```powershell
marqora-cli folio -o Field-Guide.zip Guide.md Setup.md
```

```text
marqora-cli: Guide.md:7: warning: Points outside the Folio: Glossary.md [outside-link]
marqora-cli: Guide.md:11: warning: Not on this machine: images/workflow.png [missing-image]
marqora-cli: Field-Guide.zip: shared 2 documents and 1 picture as a zip.
```

To send someone one document instead, export it. Without `-o`, the file goes beside its document:

<!-- cli-example: no-output -->

```powershell
marqora-cli export pdf Guide.md
marqora-cli export html Guide.md
```

Each one writes `Guide.pdf` or `Guide.html` next to `Guide.md` and lists, as warnings, what the
file lacks, as the app's export report does. [Exports](#exports-where-they-go-and-what-they-need)
has the rest.

That is the tour. The rest of this manual is the detail.

## How commands work

### Naming the documents

Name files, or patterns that match them:

<!-- cli-example: no-output -->

```powershell
marqora-cli check README.md
marqora-cli check "docs/*.md"
marqora-cli check "docs/**/*.md"
```

`*` matches within one folder, and `**` matches any number of folders below it. Put a pattern in
quotes. marqora-cli expands patterns itself, so a pattern means the same files in PowerShell, cmd
and bash; without quotes, bash expands it first, in its own way. Names are matched ignoring
case, as Windows does.

- **Each file is worked on once**, even if it is named twice or matched by two patterns.
- **Files are worked on in order of their full path**, so the same command over the same folder
  always goes through the files in the same order. `folio` is the exception: its documents keep
  the order you name them in, because that is the order the Folio is read in. A pattern's
  matches are sorted among themselves and stay where the pattern is.
- **A name or pattern that finds nothing is an error** (exit 3), reported by name. The files that
  were found are still worked on, unless `--fail-fast` is given.
- **A folder is not a file.** Give the files in it with a pattern, such as `"notes/**/*.md"`.

### Pipes: standard input and output

A single `-` in place of the file names reads the document from standard input. The result goes
to standard output, so marqora-cli can sit in the middle of a pipe:

```powershell
Get-Content Guide.md -Raw | marqora-cli format - | Set-Content Guide.formatted.md
```

A document read this way has no folder of its own, so its relative links and pictures are looked
for in the current folder. `--base-dir` names a different one:

```powershell
Get-Content notes\Guide.md -Raw | marqora-cli check --base-dir notes -
```

`-` cannot be mixed with file names, and `--write` cannot be used with it, because there is no
file to write back to. `folio` does not read standard input at all: a Folio collects the pictures
next to its documents, and a piped document has none.

### Print, dry run, check or write

The commands that change a document are `format`, `number`, `unnumber`, `replace`,
`renumber-lists` and `diagram-layout`. Each takes one of these, or none:

| You give    | What happens                                                                       | Files changed |
| ----------- | ---------------------------------------------------------------------------------- | :-----------: |
| nothing     | The changed document is printed to standard output                                 |      no       |
| `--dry-run` | A unified diff of the change is printed                                            |      no       |
| `--check`   | The files that would change are named, and the run exits 1 if there are any        |      no       |
| `--write`   | Each file is changed in place, and its name is printed with how many lines changed |      yes      |

Only one at a time: giving two is a usage error, exit 2.

`--write` keeps what it did not mean to change. A file keeps its byte order mark or its lack of
one, its line endings, and whether it ends with a newline, unless the command's job is to change
them (`format` with the line-ending options in Preferences). A file that would not change is not
written at all, so its date does not move. The new text is written to a temporary file beside the
original and moved over it, so a run that is stopped part way never leaves half a file.

If any of the files is read-only, `--write` refuses the whole run (exit 3) and names it, before
writing any of them: a run never stops half way through a folder because of one file. The app
refuses to edit a read-only document in the same way.

### Where messages go

What you asked for goes to **standard output**: the printed document, the diff, the outline,
`check`'s problems, the preflight. Everything else goes to **standard error**: what was written,
warnings, errors, and what `--verbose` adds. Redirecting standard output to a file gives you the
result and nothing else.

A message about a file starts with the file's name, and with a line and column when it is about
one place in it:

```text
marqora-cli: Guide.md:11: warning: Not on this machine: images/workflow.png [missing-image]
```

When `check`, `toc` or `outline` is given more than one file, each file's lines come under its
name, with a blank line after them.

### Exports: where they go, and what they need

`export html`, `export pdf` and `export word` make the files **File > Export** makes, through the
same code, one for each document named. The single-page Folio is drawn the same way.

**Where each file goes.**

- **With neither `-o` nor `--out-dir`**, beside its document and named after it: `notes\Guide.md`
  gives `notes\Guide.pdf`. A document read from standard input has no folder, so it needs `-o`.
- **`-o <file>`** names the file, for one document.
- **`--out-dir <folder>`** puts every document's file in that folder, each named after its
  document. The folder is made if it is missing. `-o` and `--out-dir` cannot be combined.

A file that is already there is refused (exit 4) before anything is drawn, unless `--overwrite`
is given. Each file is written beside its name first and moved onto it when it is finished, so a
run that fails or is stopped never leaves half a file.

**What the run tells you.** What the app's export report would list, the run lists as warnings on
standard error, each with its line: a picture that is not there, a diagram that would not draw,
raw HTML that Word cannot show. Advice, such as a PDF printed without page numbers, is
information. Neither fails the run: the file was made, and the run exits 0.

```text
marqora-cli: Guide.md:11: warning: Not found: images/workflow.png [not-found]
marqora-cli: Guide.md: exported to C:\Notes\Guide.pdf.
```

**`--dry-run`** draws nothing, starts nothing and writes nothing. It prints the theme, the page
setup and where each file would go, and lists as warnings what [`check`](#check) finds missing
already. `marqora-cli export pdf --dry-run Guide.md` prints:

```text
Theme:      Default, from the default
Page:       Letter, portrait, normal margins, backgrounds printed
Layout:     cover page off, contents off, header and footer on
Export:     Guide.md -> C:\Notes\Guide.pdf
marqora-cli: Guide.md:7:46: warning: Nothing at "Glossary.md". [broken-link]
marqora-cli: Guide.md:11:1: warning: No image at "images/workflow.png". [missing-image]
```

**A browser nobody sees.** A document is drawn by the WebView2 runtime the app uses, in a browser
of the run's own that never appears on screen: started by the first export, closed when the run
ends, its data in a folder under `%TEMP%\Marqora-cli` that goes with it. The first export adds
about a second and a half to a run; the documents after it share that browser. The browser loads
nothing from the web.

- **A document that does not finish drawing** within `--timeout` seconds, 60 unless you say
  otherwise, fails with exit 5. Nothing is written for it, and the run goes on to the next
  document. The app would export what it had; a script has nobody watching to notice the
  missing diagrams.
- **Exports need a Windows desktop.** They run in a terminal, or a script started from one, on
  a desktop you are signed in to. Over SSH there is no desktop for the browser, and an export
  stops with exit 5; the other commands work anywhere. Exports have not been tried as a
  scheduled task or on a CI service.

**What the settings decide.** Without `--settings`, the built-in defaults, as for every command.
The color theme, the heading numbering, the PDF's and Word's page setup, the cover, contents and
header and footer all come from the settings in force, and `--paper`, `--orientation`, `--margin`
and the background options override the PDF's page. What leaves is light, whatever the theme, as
the app's exports are.

### Global options

These work with every command, before or after its name.

| Option                          | What it does                                                                                                                                 |
| ------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- |
| `--json`                        | One JSON object per file on standard output, instead of text. Messages stay on standard error. See [JSON output](#json-output).              |
| `-q`, `--quiet`                 | Say nothing on standard error but errors and warnings.                                                                                       |
| `-v`, `--verbose`               | Also say which settings and theme are in force, where they came from, and how long things took.                                              |
| `--color <auto\|always\|never>` | Color the messages on standard error. `auto`, the default, colors them only on a terminal, and not at all if the `NO_COLOR` variable is set. |
| `--fail-fast`                   | Stop at the first file that fails, instead of carrying on with the rest.                                                                     |
| `--settings <app\|file>`        | Use the app's saved Preferences, or an exported Preferences file. See [Settings](#settings).                                                 |
| `-h`, `-?`, `--help`            | Show the help for marqora-cli, or for the command it follows.                                                                                |
| `--version`                     | Print the version, the same as `marqora-cli version`.                                                                                        |

Every command's `--help` ends with examples, and then with where this manual is: the copy
installed beside `marqora-cli.exe`, and the one online. Longer examples, with the pipes,
redirection and loops around the commands, are in the [recipes](CLI-Recipes.md).

## The commands

### `format`

Formats markdown as **Format > Format Document** does, with the formatting options from the
settings in force.

| Option                | What it does                                                |
| --------------------- | ----------------------------------------------------------- |
| `--write`, `-w`       | Change each file in place                                   |
| `--check`             | Name the files that would change, and exit 1 if any would   |
| `--dry-run`           | Print a diff of what would change                           |
| `--base-dir <folder>` | Where links resolve for a document read from standard input |

<!-- cli-example: no-output -->

```powershell
marqora-cli format README.md
marqora-cli format --write "docs/**/*.md"
marqora-cli format --check "docs/**/*.md"
```

### `number`

Numbers the headings, as **Format > Number Headings** does. Links in the document that point at a
heading are changed along with it, so they still work. A heading whose anchor cannot be worked
out is left as it was and reported.

Also called `number-headings`.

| Option                                                    | What it does                                                                                                                                                        |
| --------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `--from <h1\|h2\|h3>`                                     | The heading level numbering starts at. Without it: the settings in force, then the level the document already numbers from, then `h1`, as the app's dialog suggests |
| `--style <two-spaces\|one-space\|dot-space\|paren-space>` | What follows each number: `1.2  Title`, `1.2 Title`, `1.2. Title` or `1.2) Title`. Without it, the settings in force, or `two-spaces`                               |
| `--write`, `-w`, `--check`, `--dry-run`, `--base-dir`     | As for `format`                                                                                                                                                     |

<!-- cli-example: no-output -->

```powershell
marqora-cli number --dry-run Guide.md
marqora-cli number --from h2 --style dot-space --write Guide.md
```

### `unnumber`

Removes heading numbers, as **Format > Remove Heading Numbers** does, moving links along with the
headings as `number` does.

Also called `remove-heading-numbers`.

| Option                                                | What it does    |
| ----------------------------------------------------- | --------------- |
| `--write`, `-w`, `--check`, `--dry-run`, `--base-dir` | As for `format` |

<!-- cli-example: no-output -->

```powershell
marqora-cli unnumber Guide.md
marqora-cli unnumber --write "docs/**/*.md"
```

### `replace`

Finds and replaces, as **Edit > Replace All** does, with the options of the Find bar. Like Replace
All, it stops at the same number of matches in one document and says so; run it again for the
rest.

| Option                                                | What it does                                                                   |
| ----------------------------------------------------- | ------------------------------------------------------------------------------ |
| `--find <text>`                                       | What to find. Required                                                         |
| `--with <text>`                                       | What to put in its place. Required, and may be empty, which deletes each match |
| `--match-case`                                        | Only matches with the same capitals                                            |
| `--whole-word`                                        | Only matches that are whole words                                              |
| `--regex`                                             | `--find` is a .NET regular expression, and `--with` its replacement pattern    |
| `--write`, `-w`, `--check`, `--dry-run`, `--base-dir` | As for `format`                                                                |

<!-- cli-example: no-output -->

```powershell
marqora-cli replace --find "Marqora 1.0" --with "Marqora 1.1" --match-case --write docs/*.md
marqora-cli replace --find "colou?r" --with color --regex --dry-run README.md
```

An empty `--with` can be written `--with ""` or `--with=`, which works in every shell. An option
that needs a value and is followed by another option, as in `--with --write`, is refused with exit
2 rather than taking `--write` as its text; to replace with that text, write `--with=--write`.

### `renumber-lists`

Renumbers every numbered list in the document, as **Renumber List** does for one.

| Option                                                | What it does                                                                                                                                                                                                          |
| ----------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `--style <sequential\|ones\|zeros>`                   | `1, 2, 3` (`sequential`), all `1` (`ones`) or all `0` (`zeros`). Without it, each list gets the numbering the app would suggest for that list. A list that cannot take the style asked for is left alone and reported |
| `--write`, `-w`, `--check`, `--dry-run`, `--base-dir` | As for `format`                                                                                                                                                                                                       |

<!-- cli-example: no-output -->

```powershell
marqora-cli renumber-lists --dry-run Guide.md
marqora-cli renumber-lists --style sequential --write "docs/**/*.md"
```

### `diagram-layout`

Sets the direction mermaid diagrams flow in, as **Format > Diagram Layout** does. A diagram of a
kind that has no direction is left alone and reported.

| Option                                                | What it does                                         |
| ----------------------------------------------------- | ---------------------------------------------------- |
| `--direction <top-to-bottom\|left-to-right>`          | The direction to set. Required                       |
| `--diagram <n>`                                       | Only the nth mermaid diagram in the document, from 1 |
| `--write`, `-w`, `--check`, `--dry-run`, `--base-dir` | As for `format`                                      |

```powershell
marqora-cli diagram-layout --direction left-to-right --dry-run Guide.md
```

````text
--- a/Guide.md
+++ b/Guide.md
@@ -19,7 +19,7 @@
 ## The flow

 ```mermaid
-flowchart TD
+flowchart LR
   Write --> Check --> Share
 ```

````

<!-- cli-example: no-output -->

```powershell
marqora-cli diagram-layout --direction left-to-right --write Guide.md
marqora-cli diagram-layout --direction top-to-bottom --diagram 2 --dry-run Guide.md
```

### `check`

Finds dead local links, links to anchors that no heading has, and missing pictures, as the source
pane's underlines do. Each problem is printed on standard output, and the run exits 1 if there is
any. It changes nothing.

| Option                     | What it does                                                                                                    |
| -------------------------- | --------------------------------------------------------------------------------------------------------------- |
| `--ignore-external-images` | Pictures from the web, and pictures outside the document's folder, are not errors. `--verbose` still lists them |
| `--style`                  | Also report the style hints the app shows, such as a picture with no description. They do not fail the run      |
| `--strict`                 | With `--style`, a hint fails the run too                                                                        |
| `--base-dir <folder>`      | Where links resolve for a document read from standard input                                                     |

By default a picture from the web, or from outside the document's folder, is an error: it shows
on your machine and not on anyone else's. `--ignore-external-images` is for documents where that
is intended. Links to web pages are never checked, because checking them would mean going to the
web; `--verbose` lists them as not checked. A link that leaves the document's folder, such as
`../README.md`, is fine when the file is there, as clicking it in the preview opens it. Only a
picture has to be inside the folder.

| Code                   | Severity | Meaning                                                                               |
| ---------------------- | -------- | ------------------------------------------------------------------------------------- |
| `broken-link`          | error    | A link to a file that is not there                                                    |
| `dead-anchor`          | error    | A link to `#something` that no heading in the document is named                       |
| `missing-image`        | error    | A picture that is not there                                                           |
| `remote-image`         | error    | A picture from the web (information with `--ignore-external-images`)                  |
| `outside-folder`       | error    | A picture outside the document's folder (information with `--ignore-external-images`) |
| `missing-alt-text`     | hint     | A picture with no description (with `--style`)                                        |
| `web-link-not-checked` | info     | A link to a web page, listed under `--verbose`                                        |

Under `--style`, the style hints carry the code of the rule that found them.

<!-- cli-example: no-output -->

```powershell
marqora-cli check "docs/**/*.md"
marqora-cli check --ignore-external-images README.md
marqora-cli check --style --strict Guide.md
```

### `toc`

Prints the document's contents as a nested list of links, one for each heading below the title,
with the anchors the preview gives them. Which levels it lists follows the heading numbering in
the settings in force. Nothing in the document is changed; redirect the output to keep it.

| Option                | What it does                                                |
| --------------------- | ----------------------------------------------------------- |
| `--base-dir <folder>` | Where links resolve for a document read from standard input |

<!-- cli-example: no-output -->

```powershell
marqora-cli toc Guide.md
marqora-cli toc Guide.md > contents.md
```

### `outline`

Prints the heading tree with each heading's line number, indented by level, as the outline pane
shows it. Under `--json`, each heading has its level, text, anchor and line.

| Option                | What it does                                                |
| --------------------- | ----------------------------------------------------------- |
| `--base-dir <folder>` | Where links resolve for a document read from standard input |

<!-- cli-example: no-output -->

```powershell
marqora-cli outline Guide.md
marqora-cli outline --json Guide.md
```

### `export html`

Exports each document as one HTML page, as **File > Export > HTML** does: its diagrams drawn, its
math and code finished, its pictures inside the page, so the one file is all there is to send.
The page is as wide as the reading width in the settings' Preview options, or as the window it is
opened in when that has no limit. [Exports](#exports-where-they-go-and-what-they-need) says where
each page goes and what the run needs.

| Option                  | What it does                                                                                                                       |
| ----------------------- | ---------------------------------------------------------------------------------------------------------------------------------- |
| `-o`, `--output <file>` | The file to write, for one document, or `-` for standard output                                                                    |
| `--out-dir <folder>`    | The folder to write into, for any number of documents, each named after its document. Made if it is missing                        |
| `--theme <theme>`       | The color theme the page wears, by id or name. Without it, the theme the settings name for exports, then their theme, then Default |
| `--timeout <seconds>`   | How long one document may take to draw before that file fails with exit 5. Default 60                                              |
| `--dry-run`             | Draw and write nothing; print the theme, where each page would go, and what is already missing                                     |
| `--overwrite`           | Replace a page that already exists                                                                                                 |
| `--base-dir <folder>`   | Where links and pictures resolve for a document read from standard input                                                           |

`-o -` prints the page on standard output, byte for byte the file it would have written, so it
can go into a pipe. Under `--json` the page is the line's `text` instead.

<!-- cli-example: no-output -->

```powershell
marqora-cli export html docs/guide.md
marqora-cli export html --out-dir dist docs/*.md
marqora-cli export html -o - Guide.md > Guide.html
```

### `export pdf`

Exports each document as a PDF, as **File > Export > PDF** does, with the page setup in force: the
paper, orientation, margins and backgrounds, and the cover page, contents and header and footer.
The options below change the page for this run; the cover, contents and header and footer stay as
the settings have them.

| Option                                                                                          | What it does                                                                                                                                                               |
| ----------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `--paper <letter\|a4\|legal>`                                                                   | The paper. Without it, the settings' paper, or Letter                                                                                                                      |
| `--orientation <portrait\|landscape>`                                                           | Portrait or landscape. Without it, the settings' orientation, or portrait                                                                                                  |
| `--margin <normal\|narrow\|moderate\|wide\|none>`                                               | The margins, as Page Setup names them: normal is 1 in all round, narrow 0.5 in, moderate 1 in top and bottom and 0.75 in at the sides, wide 1 in and 2 in, none no margins |
| `--backgrounds`                                                                                 | Print background colors and shading, such as code blocks and table headers. The default, unless the settings turn it off                                                   |
| `--no-backgrounds`                                                                              | Leave them out, for a printer that would spend ink on them. Not with `--backgrounds`                                                                                       |
| `-o`, `--output`, `--out-dir`, `--theme`, `--timeout`, `--dry-run`, `--overwrite`, `--base-dir` | As for `export html`, except that `-o -` cannot be used: a PDF is a file                                                                                                   |

The values ignore case, so `--paper A4` is `--paper a4`. If the PDF engine that lays out pages
cannot lay a document out, the simpler one prints it, as in the app, and the run says so as
information: that PDF has no page numbers, contents page or cover.

<!-- cli-example: no-output -->

```powershell
marqora-cli export pdf docs/guide.md
marqora-cli export pdf --out-dir dist docs/*.md
marqora-cli export pdf --paper a4 --margin narrow --no-backgrounds Guide.md
```

### `export word`

Exports each document as a Word file, as **File > Export > Word** does: headings, lists, tables
and callouts as Word's own, each diagram as a picture, each equation as a Word equation (a
picture only where Word cannot write it), and code in its colors. The page and the cover,
contents and header and footer are the Word setup in the settings, which the app keeps apart from
the PDF's. Word has no options of its own here: to change its page, change the settings.

| Option                                                                                          | What it does                                                                 |
| ----------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------- |
| `-o`, `--output`, `--out-dir`, `--theme`, `--timeout`, `--dry-run`, `--overwrite`, `--base-dir` | As for `export html`, except that `-o -` cannot be used: a `.docx` is a file |

<!-- cli-example: no-output -->

```powershell
marqora-cli export word docs/guide.md
marqora-cli export word --out-dir dist docs/*.md
marqora-cli export word --settings app Guide.md
```

### `folio`

Shares documents as one Folio, as **File > Share as Folio** does: the documents, the pictures they
use, and a manifest, with each picture's link changed to where it now is. `docs/Folio.md` explains
Folios in full.

| Option                         | What it does                                                                                                                                                    |
| ------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `-o`, `--output <path>`        | Where the Folio goes. Required. Ending in `.zip` makes a zip; ending in `\` or `/`, or naming a folder that exists, makes a folder; `.html` makes a single page |
| `--form <single\|folder\|zip>` | The form, when the path does not say. A form that disagrees with the path is a usage error                                                                      |
| `--theme <theme>`              | The color theme the Folio wears, by id or name. Without it, the theme the settings name for exports, then the settings' theme, then Default                     |
| `--shrink-images <pixels>`     | Reduce pictures wider than this, as the preflight's "Shrink images wider than" does. A smaller copy goes in the Folio; your pictures are not touched            |
| `--fetch-web-images`           | Collect the pictures the documents name by web address. The only option that uses the network                                                                   |
| `--timeout <seconds>`          | For the single page: how long one document's render may take before the Folio fails with exit 5. Default 60                                                     |
| `--dry-run`                    | Write nothing; print the preflight                                                                                                                              |
| `--overwrite`                  | Replace an output that already exists: a page, a zip, or what is in a folder                                                                                    |

In the folder and zip forms, every document and picture is byte for byte what the app puts in its
own Folio.

**The single page** is the one the app makes: every document drawn into one `.html` file, its
diagrams, math and highlighting finished and its pictures inside it, readable in any browser and
taken apart again with `unpack`. Drawing it needs the WebView2 runtime, as the app does; the
folder and zip forms do not. A document that does not finish drawing within `--timeout` fails
the Folio with exit 5 and nothing is written, where the app would put that document in unfinished.

**A single page that would be too large** is refused before anything is drawn or written, with
exit 4: past about 50 MB, the size the app's preflight warns at, a page is unwieldy to open. The
message gives the zip's size; share it with `--form zip` (or `-o Handbook.zip`), or make the
pictures smaller with `--shrink-images`. marqora-cli never changes the form on its own.

**The documents keep the order you name them in**, which is the order the Folio is read in.

**What goes to the web, and when.** Without `--fetch-web-images`, a picture named by web address
stays a web address in the Folio, and the preflight counts it. With it, `--dry-run` names the
sites the pictures would be collected from and contacts none of them; the run without `--dry-run`
collects them.

**An output that exists.** A page or zip that is already there is refused (exit 4) unless
`--overwrite` is given. A folder with anything in it is refused too, and `--overwrite` replaces what is in it:
the Folio is written beside the folder first, and the old contents are deleted only once the new
ones are in place, so a run that fails leaves the folder as it was. Some folders are never
emptied, even with `--overwrite`: a drive root, your profile folder, the folder you are in or one
that holds it, and a folder holding one of the documents or a picture the Folio collects. An
output that is one of the documents is always refused.

Everything that could stop the Folio is checked before any of it is made, so a refused run has
collected and written nothing.

<!-- cli-example: no-output -->

```powershell
marqora-cli folio -o Handbook.zip docs/handbook/*.md
marqora-cli folio -o Handbook.html docs/handbook/*.md
marqora-cli folio -o Handbook/ --shrink-images 1600 docs/handbook/*.md
marqora-cli folio --dry-run -o Handbook.zip docs/handbook/*.md
```

### `unpack`

Takes a single-page Folio apart into its documents and pictures, as **File > Open Folio** does.
A Folio in the folder or zip form is already its files, and is refused with what to do instead.

| Option               | What it does                                                                                    |
| -------------------- | ----------------------------------------------------------------------------------------------- |
| `--out-dir <folder>` | The folder to unpack into. Required. It must be missing or empty, unless `--overwrite` is given |
| `--overwrite`        | Replace what is already in the folder                                                           |

`--overwrite` works as it does for `folio`: the Folio is unpacked beside the folder first, and
the same folders are never emptied, including one that holds the Folio being unpacked. An entry
in the Folio that would land outside the folder, or a picture that is not a picture, is left out
and reported.

<!-- cli-example: no-output -->

```powershell
marqora-cli unpack Handbook.html --out-dir Handbook
```

### `themes list`

Lists the color themes an export or a Folio can wear: each one's id, name and description, in the
gallery's order. `--theme` takes either the id or the name.

<!-- cli-example: no-output -->

```powershell
marqora-cli themes list
marqora-cli themes list --json
```

### `version`

Prints marqora-cli's version, which is always the version of the Marqora it came with.

<!-- cli-example: no-output -->

```powershell
marqora-cli version
marqora-cli --version
```

## Settings

Without `--settings`, every command uses Marqora's **built-in defaults**, whatever you have chosen
in the app. That is on purpose: the same command gives the same result on your machine, on a
colleague's and on a build server.

- **`--settings app`** uses the app's own saved Preferences, as they are now.
- **`--settings <file>`** uses a file made with **Preferences > Export preferences...**, or a
  `settings.json` copied from another machine.

marqora-cli only reads settings; it never changes the app's files. A settings file that is
missing or cannot be read stops the run with exit 2, rather than quietly falling back to the
defaults.

The commands that use settings are `format` (the formatting options), `number`, `toc` and
`outline` (heading numbering), `folio` (the color theme), and the exports: the color theme and the
heading numbering for all three, the reading width for `export html`, and the PDF's and Word's
page setup, cover, contents and header and footer for `export pdf` and `export word`. The others
ignore `--settings`.

`--verbose` says which settings are in force and where they came from.

## Themes

An export or a Folio wears one color theme, the same for every document in the run, chosen the
way the app chooses it, with the command line first:

1. `--theme`, if given;
2. the theme the settings name for exports;
3. the settings' own theme, which exports follow when no export theme is named;
4. Default.

A theme is named by its id (`vintage-press`) or its name (`"Vintage Press"`), ignoring case. A
theme that does not exist stops the run with exit 2 before anything is made, even when it comes
from a settings file: the app would quietly use Default instead, but a file sent in the wrong
colors is found out only after it has gone. `marqora-cli themes list` shows them all.

Whatever the theme, what leaves the app is light, as the app's exports are.

## Exit codes

A script can rely on these: a number's meaning never changes.

| Code | Meaning                                                                                                             |
| ---: | ------------------------------------------------------------------------------------------------------------------- |
|    0 | Everything asked for was done                                                                                       |
|    1 | It worked and found something: `check`'s problems, or a file `--check` says would change                            |
|    2 | The command line: an unknown command or option, options that conflict, a theme or settings file that does not exist |
|    3 | An input: a file that is missing, unreadable, not UTF-8 or read-only under `--write`, or two that collide           |
|    4 | An output: it cannot be written, or it exists and `--overwrite` was not given                                       |
|    5 | The browser engine, for the commands that need it                                                                   |
|  130 | Stopped with Ctrl+C or Ctrl+Break                                                                                   |

When a run over several files meets more than one of these, it exits with the most serious, in
the order 130, 5, 4, 3, 2, 1, 0. So `format --check` over ten files, one of them missing, exits 3
and not 1: the answer it gives about the other nine is incomplete.

Ctrl+C stops the run before the next file. A file being written when it comes is either written
in full or left as it was.

## JSON output

`--json` prints one JSON object per line on standard output, one for each file, so a script can
read a run that partly failed one file at a time. Messages still go to standard error.

<!-- cli-example: exit 1 -->

```powershell
marqora-cli check --json Guide.md
```

```text
{"schemaVersion":1,"path":"Guide.md","verb":"check","outcome":"found","outputs":[],"findings":[{"line":7,"column":46,"code":"broken-link","severity":"error","message":"Nothing at \"Glossary.md\"."},{"line":11,"column":1,"code":"missing-image","severity":"error","message":"No image at \"images/workflow.png\"."},{"line":28,"column":5,"code":"dead-anchor","severity":"error","message":"Nothing in this document is named \"#step\"."}]}
```

| Field           | Holds                                                                                                                                |
| --------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| `schemaVersion` | The version of this shape, now 1                                                                                                     |
| `path`          | The file as it was named, or `-` for standard input                                                                                  |
| `verb`          | The command, by its main name: `number`, even when it was typed as `number-headings`                                                 |
| `outcome`       | What happened to the file: one of the outcomes below                                                                                 |
| `outputs`       | Every file written for it; empty when nothing was                                                                                    |
| `findings`      | What the command found in it, each with `line`, `column`, `code`, `severity` and `message`                                           |
| `message`       | Why the file failed; absent otherwise                                                                                                |
| `baseDir`       | For standard input only: the folder its links were resolved against                                                                  |
| `theme`         | On an export or a Folio: the theme's `id`, and the `source` that chose it: `option`, `settingsExport`, `settingsScreen` or `default` |
| `text`          | The changed document, when it was printed rather than written, or the page `export html -o -` printed                                |
| `diff`          | The diff `--dry-run` printed                                                                                                         |
| `headings`      | `outline`'s headings, each with `level`, `text`, `anchor` and `line`                                                                 |
| `preflight`     | `folio --dry-run`'s preflight, on the Folio's own line                                                                               |

A finding's `line` and `column` count from 1; 0 means it is about the whole document. Its
`severity` is `error`, `warning`, `info` or `hint`. Only errors fail a run, and hints only under
`check --strict`.

`folio` gives each document a line of its own with its findings, and one more line for the Folio,
whose `path` is the output. Under `--dry-run` that line carries `preflight`: the `form`, the
`documents` in order, the number of `pictures` and how many were `collected` from elsewhere,
`missingPictures`, `outsideLinks`, `webPictures`, `estimatedBytes`, any `advice`, and, under
`--fetch-web-images`, the `sites` that would be contacted.

The outcomes:

| Outcome       | Means                                                                           |
| ------------- | ------------------------------------------------------------------------------- |
| `printed`     | The result was printed to standard output                                       |
| `written`     | `--write` changed the file                                                      |
| `unchanged`   | The command would change nothing, and the file was left alone                   |
| `wouldChange` | `--check` or `--dry-run`: the file would change, and was not touched            |
| `passed`      | `check`, `toc` or `outline` ran, and found nothing that fails the file          |
| `found`       | `check` found what fails the file                                               |
| `exported`    | A file was made from it: an export or a Folio                                   |
| `failed`      | Nothing could be done with it; `message` says why                               |
| `skipped`     | Not reached: an earlier file failed under `--fail-fast`, or the run was stopped |

**Versioning.** A new field can appear without warning, and `schemaVersion` stays the same, so
read the fields you need and ignore the rest. A field that is removed, renamed or given a new
meaning raises `schemaVersion`, and so would renaming an outcome.

## A pre-commit hook

A Git hook can stop a commit whose markdown is not formatted or has a dead link. Save this as
`.git/hooks/pre-commit` in your repository. Git for Windows runs it with its own shell, and
marqora-cli has to be on PATH.

```sh
#!/bin/sh
# Refuse a commit whose markdown is unformatted or has dead links.
# Checks the markdown files the commit adds or changes, as they are in the working folder.

git diff --cached --name-only -z --diff-filter=ACMR -- '*.md' |
    xargs -0 -r marqora-cli format --check || {
        echo "Format them with: marqora-cli format --write <file>" >&2
        exit 1
    }

git diff --cached --name-only -z --diff-filter=ACMR -- '*.md' |
    xargs -0 -r marqora-cli check
```

The hook checks the files as they are in your working folder, which is what was staged unless
you changed a file again after `git add`. To commit regardless, once, use `git commit
--no-verify`.

This manual has no recipe for GitHub Actions or any other CI service. marqora-cli is not tested
on one, so none is offered.

## Troubleshooting

**"marqora-cli is not recognized".** Its folder is not on your PATH, or the terminal was open
before it was added. Open a new terminal. If it is still missing, install again with
`Install.cmd -AddToPath`, or run it by its full path, as [Install and PATH](#install-and-path)
shows.

**A pattern finds nothing in bash.** Unquoted, bash expands `*.md` itself, and `**` only with
`globstar` on. Put the pattern in quotes, and marqora-cli expands it.

**Accented letters come out wrong.** marqora-cli reads and writes UTF-8. A file in another
encoding is refused (exit 3) rather than guessed at. In PowerShell, the text marqora-cli prints
is read in the console's old code page when it is passed to another command or kept in a
variable, so `é` arrives as `├⌐`. Run `[Console]::OutputEncoding = [Text.UTF8Encoding]::new()`
first, or put it in your `$PROFILE`; the [recipes](CLI-Recipes.md#accented-letters) have the line
for Windows PowerShell 5.1 too. Naming the files, and `--write`, avoid the question.

**The result differs from the app's.** Check which settings are in force with `--verbose`. Without
`--settings`, marqora-cli uses the built-in defaults, not your Preferences; `--settings app` uses
yours.

**An export, or `folio -o Handbook.html`, stops with exit 5.** These draw their documents with the
WebView2 runtime the app uses, and the message says which of these it met:

- **The runtime is not installed.** Windows 11 comes with it; the message gives the page to
  install it from. The zip and folder forms of a Folio, and every other
  command, need no runtime.
- **The files that draw documents are incomplete.** Something has removed part of the
  installation; install Marqora again.
- **A document did not finish drawing in time.** A very long document with many diagrams can
  need more than 60 seconds on a slow machine: pass a longer `--timeout`. The other documents in
  the run are still exported.
- **The browser could not be started.** Most often a session with no desktop, such as SSH; run
  the export in a terminal on your own desktop.

**An export differs from the one the app makes.** The app exports with your Preferences, and
marqora-cli with the built-in defaults unless told otherwise: add `--settings app`. `--verbose`
names the theme and page setup in force and where each came from. A gantt chart draws a line at
today's date, so two exports made on different days differ there.

**A settings file is refused.** It has to be a file made with **Export preferences...**, or a
`settings.json`. A damaged one is refused, never repaired or moved; the app's own file is only
ever read.
