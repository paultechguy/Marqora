# marqora-cli recipes

<!--
For whoever edits this file. It is held to the same rules as docs/CLI.md, by the same tests in
tests/PaulTechGuy.MQ.Cli.Tests/Manual:

- A fenced block whose first line starts with "marqora-cli " is an example. One with a ```text
  block after it is run in a fresh copy of docs/cli-tour and has to show that output. Most here
  are pipelines, which the tests cannot run: put a "cli-example: no-output" comment before each.
- Every marqora-cli command in every block, pipelines included, has to parse: an option that is
  renamed or removed fails the build until the recipe using it is changed.
- The shell code around the commands was run by hand in PowerShell 7, Windows PowerShell 5.1, cmd
  and Git Bash; the plan's records say when. Run a recipe you change.
-->

The [manual](CLI.md) says what each command does. This page is for getting a job done: each recipe
is a task people commonly want, with the commands for **PowerShell**, **cmd** and **bash**,
including the pipes, redirection and loops around them.

The recipes assume marqora-cli is on your PATH, and that you are in the folder that holds your
`docs` folder. Change `"docs/**/*.md"` to name your own documents.

## Before you start

### Which shell

- **PowerShell 7** is the easiest: its JSON and object handling make the reporting recipes short.
  Tell it once to read marqora-cli's text as UTF-8; see [Accented letters](#accented-letters).
- **Windows PowerShell 5.1**, the one built into Windows, works once it is told to use UTF-8, in
  the same place.
- **cmd** works for everything but the recipes that read JSON; for those, it says so and shows
  how to run the PowerShell recipe from cmd.
- **bash** means bash in WSL, or Git Bash: in either, follow the recipes marked **bash:**. In WSL,
  a Windows program is run by its full name, `marqora-cli.exe`; [Using WSL](#using-wsl) sets it
  up so the bash recipes work as written. Git Bash runs `marqora-cli` as it is.

### Quoting

Put a pattern in double quotes in every shell: `"docs/**/*.md"`. marqora-cli expands it itself, so
it means the same files everywhere.

Text with a `$` in it needs care. PowerShell and bash both read `$1` inside double quotes as a
variable, so use single quotes for a regular expression's replacement there: `--with 'v$1'`. cmd
has no single quotes, and leaves `$1` alone inside double quotes.

### Redirection at a glance

What you asked for goes to standard output, and messages go to standard error, so the two can be
sent to different places.

| To                              | PowerShell          | cmd               | bash              |
| ------------------------------- | ------------------- | ----------------- | ----------------- |
| Save the result                 | `> out.txt`         | `> out.txt`       | `> out.txt`       |
| Add to the end of a file        | `>> out.txt`        | `>> out.txt`      | `>> out.txt`      |
| Save the messages               | `2> messages.txt`   | `2> messages.txt` | `2> messages.txt` |
| Save both in one file           | `*> all.txt`        | `> all.txt 2>&1`  | `> all.txt 2>&1`  |
| Throw the result away           | `> $null`           | `> nul`           | `> /dev/null`     |
| Pass the messages along a pipe  | `2>&1 \|`           | `2>&1 \|`         | `2>&1 \|`         |
| The exit code of the last run   | `$LASTEXITCODE`     | `%ERRORLEVEL%`    | `$?`              |
| Run the next only if it went OK | `&&` (PowerShell 7) | `&&`              | `&&`              |

The exit codes are in the manual's [Exit codes](CLI.md#exit-codes): 0 is done, 1 is "found
something", and anything higher is a failure.

### Accented letters

marqora-cli reads and writes UTF-8.

- **PowerShell 7** reads marqora-cli's text in the console's old code page whenever it passes the
  text on to a command or keeps it in a variable: into `Set-Clipboard`, `Select-String`,
  `ConvertFrom-Json`, or `$toc = marqora-cli toc README.md`. Then `é` arrives as `├⌐`. Run this
  first, or put it in your `$PROFILE`:

  ```powershell
  [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
  ```

  `>` into a file needs nothing: it saves exactly what marqora-cli wrote (PowerShell 7.4 and
  later).

- **Windows PowerShell 5.1** decodes marqora-cli's output the same way, and `>` saves the file as
  UTF-16. Run this first, or put it in your `$PROFILE`:

  ```powershell
  $OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
  ```

  and save with `| Set-Content -Encoding utf8 out.txt` rather than `> out.txt`.

- **cmd** saves and pipes the bytes untouched. To see accented letters correctly on screen, run
  `chcp 65001` first.
- **bash** needs nothing.

A file passed through a PowerShell pipeline into `Set-Content` comes out with Windows line endings,
because PowerShell rejoins the lines itself. Name files to marqora-cli and use `--write` when the
line endings matter; it keeps each file's own.

## Checking documents

### Check a folder before a release, and keep the report

`check` prints one line per problem, so the report is just its standard output.

<!-- cli-example: no-output -->

```powershell
marqora-cli check "docs/**/*.md" > link-report.txt
```

The same line works in cmd and bash. Under each file's name come its problems, then a blank line.
The exit code says whether there were any: 1 if so, 0 if not.

A link that leaves the document's folder, such as `../README.md`, is checked like any other: it is
fine when the file is there. Pictures are different: one from outside the document's folder is
reported, because it would not appear for anyone else.

### Show one kind of problem

Every line ends with the kind of problem in brackets, such as `[broken-link]`, so a filter on it
picks one kind out. The kinds are listed in the manual's [`check`](CLI.md#check).

PowerShell:

<!-- cli-example: no-output -->

```powershell
marqora-cli check "docs/**/*.md" | Select-String -SimpleMatch '[broken-link]'
```

cmd:

<!-- cli-example: no-output -->

```bat
marqora-cli check "docs/**/*.md" | findstr /c:"[broken-link]"
```

bash:

<!-- cli-example: no-output -->

```bash
marqora-cli check "docs/**/*.md" | grep -F '[broken-link]'
```

### Count the problems by kind

For a sense of how much is wrong before you start fixing it.

PowerShell reads the JSON, one object per file, each with its findings:

<!-- cli-example: no-output -->

```powershell
marqora-cli check --json "docs/**/*.md" | ConvertFrom-Json | ForEach-Object findings |
    Group-Object code -NoElement | Sort-Object Count -Descending | Select-Object Count, Name
```

cmd counts one kind at a time:

<!-- cli-example: no-output -->

```bat
marqora-cli check "docs/**/*.md" | find /c "[broken-link]"
```

bash:

<!-- cli-example: no-output -->

```bash
marqora-cli check "docs/**/*.md" | grep -o '\[[a-z-]*\]$' | sort | uniq -c | sort -rn
```

### Send the problems to a spreadsheet

A CSV file with a row per problem, for sharing out the fixing or tracking it. This one is
PowerShell's, because it reads the JSON:

<!-- cli-example: no-output -->

```powershell
marqora-cli check --json "docs/**/*.md" | ConvertFrom-Json |
    ForEach-Object { $doc = $_.path; $_.findings | Select-Object @{ n = 'file'; e = { $doc } }, line, column, code, severity, message } |
    Export-Csv link-report.csv -NoTypeInformation -Encoding utf8BOM
```

`utf8BOM` is so that Excel reads accented letters correctly; in Windows PowerShell 5.1, write
`-Encoding UTF8`, which adds the mark anyway. From cmd, save the recipe as `Report.ps1` and run
`pwsh -NoProfile -File Report.ps1`.

### Stop a script when something is broken

marqora-cli's exit code is what a script tests. Each of these stops at a broken link and passes the
code on, so whatever ran the script sees the failure too.

PowerShell:

<!-- cli-example: no-output -->

```powershell
marqora-cli check "docs/**/*.md"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
```

cmd, in a `.cmd` file:

<!-- cli-example: no-output -->

```bat
marqora-cli check "docs/**/*.md"
if errorlevel 1 exit /b %errorlevel%
```

bash:

<!-- cli-example: no-output -->

```bash
marqora-cli check "docs/**/*.md" || exit
```

To tell "found problems" from "could not finish", test the number. In PowerShell:

<!-- cli-example: no-output -->

```powershell
marqora-cli check "docs/**/*.md"
switch ($LASTEXITCODE) {
    0 { 'All clear.' }
    1 { 'Problems found; the list is above.' }
    default { "marqora-cli could not finish (exit $LASTEXITCODE)." }
}
```

### Check what you are about to commit

The manual's [pre-commit hook](CLI.md#a-pre-commit-hook) checks your files as they are in the
working folder. To check one exactly as it is staged, have Git print the staged copy and pipe it
in. `--base-dir` says which folder its links are relative to, since a piped document has none of
its own. The command is the same in all three shells:

```bash
git show :docs/Guide.md | marqora-cli check --base-dir docs -
```

The problems are reported against `-`, standard input. Links and pictures are still looked for in
the working folder.

## Formatting

### Preview a whole folder's formatting as one diff

`--dry-run` prints a diff of every file that would change, and changes none of them. Saved, it
opens in any diff viewer, or in VS Code with `code format.diff`.

<!-- cli-example: no-output -->

```powershell
marqora-cli format --dry-run "docs/**/*.md" > format.diff
```

The same line works in cmd and bash, and in Windows PowerShell 5.1 with
[the change above](#accented-letters).

### List the files that need formatting

`format --check` names them on standard error, so the names have to be sent along the pipe with
`2>&1`, or read from the JSON.

PowerShell:

<!-- cli-example: no-output -->

```powershell
marqora-cli format --check --json "docs/**/*.md" 2>$null | ConvertFrom-Json |
    Where-Object outcome -eq wouldChange | ForEach-Object path
```

cmd:

<!-- cli-example: no-output -->

```bat
marqora-cli format --check "docs/**/*.md" 2>&1 | findstr /c:"would change"
```

bash, just the names:

<!-- cli-example: no-output -->

```bash
marqora-cli format --check "docs/**/*.md" 2>&1 | sed -n 's/^marqora-cli: \(.*\): would change\.$/\1/p'
```

### Format only the files you changed

For a repository where the rest of the files are someone else's. Git lists the markdown files
changed since the last commit, staged or not, and marqora-cli formats those. Run it from the top
of the repository, because Git names the files from there.

PowerShell:

```powershell
$changed = git diff --name-only --diff-filter=ACMR HEAD -- '*.md'
if ($changed) { marqora-cli format --write $changed }
```

The `if` matters: with nothing changed, marqora-cli would be given no files, and stop with exit 2.

cmd, at the prompt (in a `.cmd` file, write `%%f` for `%f`):

```bat
for /f "delims=" %f in ('git diff --name-only --diff-filter=ACMR HEAD -- *.md') do marqora-cli format --write "%f"
```

bash:

```bash
git diff --name-only -z --diff-filter=ACMR HEAD -- '*.md' | xargs -0 -r marqora-cli format --write
```

Files you have added but not yet staged are not in the list. `git add` them first.

### Format the markdown on the clipboard

For a piece of markdown you are writing somewhere else, such as an issue, a wiki page or an email.
It goes in from the clipboard and comes back formatted. PowerShell:

```powershell
Get-Clipboard -Raw | marqora-cli format - | Set-Clipboard
```

In cmd and bash, save the text to a file and format that instead.

### Tidy a whole folder in one go

Several commands one after another, each only if the one before it worked. Here every diagram is
turned to flow left to right, then everything is formatted. The line is the same in PowerShell 7,
cmd and bash:

<!-- cli-example: no-output -->

```powershell
marqora-cli diagram-layout --direction left-to-right --write "docs/**/*.md" && marqora-cli format --write "docs/**/*.md"
```

Windows PowerShell 5.1 has no `&&`; write the second command on its own line, after
`if ($LASTEXITCODE -eq 0)`.

Look first with `--dry-run` in place of `--write`, or commit before you start, so the change is
easy to look over and to undo.

## Headings and contents

### Put a contents list on the clipboard

`toc` prints the document's headings as a list of links, ready to paste. PowerShell:

<!-- cli-example: no-output -->

```powershell
marqora-cli toc README.md | Set-Clipboard
```

In cmd and bash, save it to a file and copy from there:

<!-- cli-example: no-output -->

```bat
marqora-cli toc README.md > contents.md
```

### Keep a README's contents list up to date

Mark where the list goes, once, with two comments in the document:

```markdown
<!-- toc -->
<!-- /toc -->
```

Then this puts the current list between them, replacing whatever was there. Run it after you
change the headings, or from a script before every release.

PowerShell, which keeps the file's own line endings:

```powershell
$text = Get-Content README.md -Raw -Encoding utf8
$nl   = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
$toc  = (marqora-cli toc README.md) -join $nl
$text = [regex]::Replace($text, '(?s)(?<=<!-- toc -->\r?\n).*?(?=<!-- /toc -->)', { $toc + $nl })
[IO.File]::WriteAllText((Resolve-Path README.md), $text)
```

The list is put in by a script block, `{ $toc + $nl }`, rather than as replacement text, so a
heading with a `$` in it goes in as written.

bash, for a file with Unix line endings:

```bash
toc=$(marqora-cli toc README.md)
TOC="$toc" awk '
  /<!-- \/toc -->/ { skip = 0 }
  !skip { print }
  /<!-- toc -->/ { print ENVIRON["TOC"]; skip = 1 }
' README.md > README.tmp && mv README.tmp README.md
```

From cmd, save the PowerShell recipe as `Update-Toc.ps1` and run `pwsh -NoProfile -File
Update-Toc.ps1`.

### Make an index page for a folder

A `docs/index.md` with a link to each document in the folder, named by its first heading, for a
folder that has grown past remembering what is in it.

PowerShell:

<!-- cli-example: no-output -->

```powershell
marqora-cli outline --json "docs/*.md" | ConvertFrom-Json |
    Where-Object { (Split-Path $_.path -Leaf) -ne 'index.md' } |
    ForEach-Object {
        $file  = Split-Path $_.path -Leaf
        $title = if ($_.headings) { $_.headings[0].text } else { [IO.Path]::GetFileNameWithoutExtension($file) }
        "- [$title]($($file -replace ' ', '%20'))"
    } | Set-Content docs/index.md -Encoding utf8
```

bash:

```bash
for f in docs/*.md; do
  [ "$(basename "$f")" = index.md ] && continue
  title=$(marqora-cli outline "$f" | head -n 1 | sed -E 's/^ *[0-9]+ +//')
  echo "- [${title:-$(basename "$f" .md)}]($(basename "$f" | sed 's/ /%20/g'))"
done > docs/index.md
```

A document with no headings is listed by its file name. Run `marqora-cli format --write
docs/index.md` afterward if you want it formatted as the rest are.

### Number a handbook's headings

Look at the numbering first, then make it. `--from h2` leaves each document's title unnumbered and
starts at its sections. The commands are the same in every shell:

<!-- cli-example: no-output -->

```powershell
marqora-cli number --from h2 --dry-run "docs/handbook/*.md"
marqora-cli number --from h2 --write "docs/handbook/*.md"
```

Links to the headings are changed with them, so they still work. To take the numbers off again:

<!-- cli-example: no-output -->

```powershell
marqora-cli unnumber --write "docs/handbook/*.md"
```

## Find and replace

### Rename something across a folder

A product renamed, a person who has left, a server that moved. Preview, then write. `--match-case`
keeps it from touching the same letters in another word's capitals:

<!-- cli-example: no-output -->

```powershell
marqora-cli replace --find "Acme Notes" --with "Marqora" --match-case --dry-run "docs/**/*.md"
marqora-cli replace --find "Acme Notes" --with "Marqora" --match-case --write "docs/**/*.md"
```

The commands are the same in every shell. Add `--whole-word` when the text you are finding could
be part of a longer word.

### Rewrite with a regular expression

`--regex` makes `--find` a .NET regular expression, and `--with` can use what it captured, as `$1`,
`$2` and so on. Here "version 1.2" becomes "v1.2" wherever it is written.

PowerShell, with single quotes so that `$1` reaches marqora-cli:

<!-- cli-example: no-output -->

```powershell
marqora-cli replace --regex --find 'version (\d+\.\d+)' --with 'v$1' --dry-run "docs/**/*.md"
```

cmd:

<!-- cli-example: no-output -->

```bat
marqora-cli replace --regex --find "version (\d+\.\d+)" --with "v$1" --dry-run "docs/**/*.md"
```

bash:

<!-- cli-example: no-output -->

```bash
marqora-cli replace --regex --find 'version (\d+\.\d+)' --with 'v$1' --dry-run "docs/**/*.md"
```

In a `.cmd` file, a `%` in the expression has to be written `%%`.

### Delete every match

An empty `--with` deletes what `--find` finds. Here, a "DRAFT: " left at the start of headings:

<!-- cli-example: no-output -->

```powershell
marqora-cli replace --find "DRAFT: " --with "" --match-case --write "docs/**/*.md"
```

That line works in PowerShell 7, cmd and bash. **Windows PowerShell 5.1 drops an empty argument**
before marqora-cli sees it; marqora-cli then finds `--with` followed by `--match-case`, and stops
with exit 2 rather than guess. `--with=`, with nothing after the `=`, is empty text in every
shell, 5.1 included:

<!-- cli-example: no-output -->

```powershell
marqora-cli replace --find "DRAFT: " --with= --match-case --write "docs/**/*.md"
```

## Exporting

### A PDF beside every document

The everyday export: each document's PDF next to it, named after it. The command is the same in
every shell:

<!-- cli-example: no-output -->

```powershell
marqora-cli export pdf "docs/**/*.md"
```

A PDF that is already there is refused, so the second time, say to replace them:

<!-- cli-example: no-output -->

```powershell
marqora-cli export pdf --overwrite "docs/**/*.md"
```

`export html` and `export word` work the same way. For a printer, leave out the shaded
backgrounds and use the paper your country uses:

<!-- cli-example: no-output -->

```powershell
marqora-cli export pdf --overwrite --paper a4 --no-backgrounds "docs/**/*.md"
```

### Exports in a folder of their own

To hand a set of documents to someone, put the exports in one folder instead, named for the day:

PowerShell:

<!-- cli-example: no-output -->

```powershell
marqora-cli export pdf --out-dir "Handbook-$(Get-Date -Format yyyy-MM-dd)" "docs/handbook/*.md"
```

cmd (in a `.cmd` file, write `%%d` for `%d`):

```bat
for /f %d in ('powershell -NoProfile -Command "Get-Date -Format yyyy-MM-dd"') do marqora-cli export pdf --out-dir "Handbook-%d" "docs/handbook/*.md"
```

bash:

<!-- cli-example: no-output -->

```bash
marqora-cli export pdf --out-dir "Handbook-$(date +%F)" "docs/handbook/*.md"
```

Each PDF is named after its document, so two documents with the same name in different folders
would land on one file. The run refuses that before writing anything (exit 3) and names both;
export them in two runs, or rename one.

### Export only what has changed

A folder that keeps a PDF beside each document only needs the PDFs whose document is newer, or
that are missing.

PowerShell:

```powershell
$stale = Get-ChildItem docs -Recurse -Filter *.md | Where-Object {
    $pdf = [IO.Path]::ChangeExtension($_.FullName, '.pdf')
    -not (Test-Path $pdf) -or $_.LastWriteTime -gt (Get-Item $pdf).LastWriteTime
}
if ($stale) { marqora-cli export pdf --overwrite $stale.FullName }
```

bash:

```bash
stale=()
while IFS= read -r -d '' f; do
  [ "$f" -nt "${f%.md}.pdf" ] && stale+=("$f")
done < <(find docs -name '*.md' -print0)
[ ${#stale[@]} -gt 0 ] && marqora-cli export pdf --overwrite "${stale[@]}"
```

`-nt` is true when the PDF is missing as well as when it is older. cmd cannot compare two files'
times without a good deal of ceremony; from cmd, run the PowerShell recipe saved as
`Export-Changed.ps1`:

```bat
pwsh -NoProfile -File Export-Changed.ps1
```

## Folios

### A dated Folio every week

A Folio named for the day it was made, so last week's copy is kept beside this week's.

PowerShell:

<!-- cli-example: no-output -->

```powershell
marqora-cli folio -o "Handbook-$(Get-Date -Format yyyy-MM-dd).zip" "docs/handbook/*.md"
```

cmd, which asks PowerShell for the date because cmd's own `%DATE%` is written differently in
every region (in a `.cmd` file, write `%%d` for `%d`):

```bat
for /f %d in ('powershell -NoProfile -Command "Get-Date -Format yyyy-MM-dd"') do marqora-cli folio -o "Handbook-%d.zip" "docs/handbook/*.md"
```

bash:

<!-- cli-example: no-output -->

```bash
marqora-cli folio -o "Handbook-$(date +%F).zip" "docs/handbook/*.md"
```

To have Windows make it every Friday, save this as `Make-Handbook.ps1` beside the documents:

```powershell
Set-Location $PSScriptRoot
$name = "Handbook-$(Get-Date -Format yyyy-MM-dd).zip"
try {
    marqora-cli folio --quiet --color never -o $name "docs/handbook/*.md" *>> folio.log
}
catch {
    "$(Get-Date -Format s) $_" >> folio.log
    exit 1
}
exit $LASTEXITCODE
```

Then, in Task Scheduler, create a task with a weekly trigger whose action starts `pwsh.exe` with
the arguments `-NoProfile -File "C:\path\to\Make-Handbook.ps1"`. Warnings and errors go to
`folio.log`, and marqora-cli's exit code becomes the task's *Last Run Result*.

The `try` is for the day the task cannot find marqora-cli. Without it, PowerShell reports that
to no one, and the task's result is 0, as if the Folio had been made. With it, `folio.log` says
what went wrong and the result is 1. To fix it, name marqora-cli by its full path, as the
manual's [Install and PATH](CLI.md#install-and-path) shows.

Run twice on the same day, the second run stops with exit 4 rather than replace the first;
add `--overwrite` if replacing it is what you want.

### A Folio in a reading order you keep in a file

A Folio's documents are read in the order they are named. With a pattern they come in
alphabetical order, so for any other order, keep the list in a file, one document per line:

```text
docs/handbook/Welcome.md
docs/handbook/Setup.md
docs/handbook/Daily-use.md
```

PowerShell:

<!-- cli-example: no-output -->

```powershell
marqora-cli folio -o Handbook.zip (Get-Content reading-order.txt)
```

cmd, saved as a `.cmd` file:

```bat
@echo off
setlocal enabledelayedexpansion
set "docs="
for /f "usebackq delims=" %%f in ("reading-order.txt") do set docs=!docs! "%%f"
marqora-cli folio -o Handbook.zip !docs!
```

bash, which takes out the Windows line endings a list made in Notepad has:

```bash
mapfile -t docs < <(tr -d '\r' < reading-order.txt)
marqora-cli folio -o Handbook.zip "${docs[@]}"
```

### Refuse to send a Folio with missing pictures

`folio` makes the Folio even when a picture is missing, and warns. A script that sends it on can
look at the preflight first and stop.

PowerShell:

```powershell
$preflight = marqora-cli folio --dry-run --json -o Handbook.zip "docs/handbook/*.md" 2>$null |
    ConvertFrom-Json | Where-Object preflight | ForEach-Object preflight
if ($preflight.missingPictures -gt 0) { throw "$($preflight.missingPictures) pictures are missing." }
marqora-cli folio -o Handbook.zip "docs/handbook/*.md"
```

cmd, in a `.cmd` file:

<!-- cli-example: no-output -->

```bat
marqora-cli folio --dry-run -o Handbook.zip "docs/handbook/*.md" 2>nul | findstr /r /b /c:"Missing: *[1-9]" >nul && (echo Pictures are missing. & exit /b 1)
marqora-cli folio -o Handbook.zip "docs/handbook/*.md"
```

bash:

```bash
if marqora-cli folio --dry-run -o Handbook.zip "docs/handbook/*.md" 2>/dev/null | grep -q '^Missing: *[1-9]'; then
  echo "Pictures are missing." >&2
  exit 1
fi
marqora-cli folio -o Handbook.zip "docs/handbook/*.md"
```

The warnings on standard error name each missing picture and the line that uses it.

## Settings

### Use your own Preferences

Without `--settings`, marqora-cli uses the built-in defaults, so the result is the same on every
machine. To format as the app does for you, with your own choices in Preferences:

<!-- cli-example: no-output -->

```powershell
marqora-cli format --settings app --write "docs/**/*.md"
```

### Share one set of settings with a team

Export your Preferences once, with **Preferences > Export preferences...**, into the repository,
say as `marqora-settings.json`, and commit it. Then everyone, and every script, formats the same
way:

<!-- cli-example: no-output -->

```powershell
marqora-cli format --settings marqora-settings.json --check "docs/**/*.md"
```

Put the same `--settings` in the pre-commit hook, so the hook asks for the formatting the team
agreed.

### A short name that brings your settings

If you always want your own settings, give yourself a short command that adds them.

PowerShell, in your `$PROFILE`:

```powershell
function mq { marqora-cli --settings app @args }
```

cmd, for the window it is typed in:

```bat
doskey mq=marqora-cli --settings app $*
```

bash, in `~/.bashrc`:

```bash
alias mq='marqora-cli --settings app'
```

Then `mq format --write notes.md` formats with your Preferences. A global option such as
`--settings` can come before the command's name, which is what makes this work.

## Using WSL

In WSL, use the recipes marked **bash:**, as they are written. The PowerShell and cmd ones are for
those Windows shells and do not run in WSL. The commands themselves are the same in every shell;
what differs is the quoting, the redirection and the pipes around them, and in WSL those are
bash's.

marqora-cli is a Windows program, and WSL runs it, but by its full name: `marqora-cli.exe`. WSL
finds it on the Windows PATH, which it adds to its own unless that has been turned off.

To use the name the recipes use, link it once into a folder on WSL's PATH:

```bash
mkdir -p ~/.local/bin
ln -s "$(command -v marqora-cli.exe)" ~/.local/bin/marqora-cli
```

A link, rather than an alias, also works inside `xargs` and scripts, which do not see aliases. On
most distributions `~/.local/bin` is on PATH once it exists; if `marqora-cli version` is not found,
open a new terminal.

Three more things are different from a Linux program:

- **Give marqora-cli relative paths.** Relative paths and patterns mean the same thing to both
  sides, whether the documents are on a Windows drive, under `/mnt/c`, or in your Linux home.
- **A Linux absolute path is read as a Windows one.** From a folder on drive C:,
  `/tmp/Handbook.zip` becomes `C:\tmp\Handbook.zip`, with no error. Turn it into the right Windows path with `wslpath -w`:
  `marqora-cli folio -o "$(wslpath -w ~/Handbook.zip)" "docs/*.md"`.
- **Its output is UTF-8 with Unix line endings**, so `grep`, `sed` and `awk` read it as they read
  any program's.

Git Bash needs none of this: it runs `marqora-cli` by that name, and the bash recipes work as they
are.
