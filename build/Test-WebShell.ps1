<#
.SYNOPSIS
    Checks the preview shell's JavaScript for syntax errors and names that do not exist.

.DESCRIPTION
    Tooling, not part of the app. Nothing here ships: no file it reads is changed, nothing is
    written, and webshell/ is byte-identical whether this has been run or not. It is the same
    kind of thing as Add-FileHeaders.ps1 and Test-ButtonStandards.ps1 - a check a developer
    runs.

    It exists because the shell is the one part of this repository the compiler cannot see.
    Every C# mistake of the shape "that name does not exist here" is a build error; the same
    mistake in app.js is a ReferenceError that waits until the line runs. That is not
    theoretical - a scrollbar tick once called defineThemes' private token() from another
    scope, which turned every decoration of its kind off the moment that branch ran, and
    the build was perfectly happy.

    Two passes:

      syntax    node --check, which catches anything that will not parse
      names     eslint's no-undef, which catches a name used where it does not exist

    One rule and no style pass, deliberately. See build/eslint.config.mjs.

    What it cannot do is most things. It does not know Monaco's API, so a misspelled method on
    an editor is invisible to it; it cannot see that a bridge payload field was renamed on one
    side only; and it has no opinion about whether the code is right. It answers one question,
    and it answers it in about two seconds.

.PARAMETER Check
    Exit non-zero when anything is found. This is the form to call from CI.

.PARAMETER Path
    Files or directories to check, relative to the repo root. Defaults to webshell, excluding
    the vendor bundle - those files are third-party, minified, and not ours to lint.

.EXAMPLE
    pwsh ./build/Test-WebShell.ps1

.EXAMPLE
    pwsh ./build/Test-WebShell.ps1 -Check
#>
[CmdletBinding()]
param(
    [switch]$Check,
    [string[]]$Path = @('webshell')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$config = Join-Path $PSScriptRoot 'eslint.config.mjs'

function Get-Relative {
    param([string]$FullName)
    return [System.IO.Path]::GetRelativePath($repoRoot, $FullName)
}

# node is what both passes are built on. Said plainly rather than failing halfway through with
# a message about npx, because somebody without it has nothing to fix in their code.
$node = Get-Command node -ErrorAction SilentlyContinue

if (-not $node) {
    Write-Host "node was not found, so the shell cannot be checked." -ForegroundColor Yellow
    Write-Host "Install Node.js, or skip this check - nothing in the app depends on it."

    # Not a failure. A machine without node has not broken anything; it simply cannot answer.
    exit 0
}

$files = @()

foreach ($item in $Path) {
    $full = Join-Path $repoRoot $item

    if (Test-Path -Path $full -PathType Container) {
        # vendor/ is the restored third-party bundle - Monaco, KaTeX, mermaid, highlight.js. It
        # is minified, it is not ours, and linting it would report thousands of things nobody
        # here can act on.
        $files += Get-ChildItem -Path $full -Filter '*.js' -File -Recurse |
            Where-Object { $_.FullName -notmatch '[\\/]vendor[\\/]' }
    }
    elseif (Test-Path -Path $full -PathType Leaf) {
        $files += Get-Item -Path $full
    }
}

if ($files.Count -eq 0) {
    Write-Host "No shell JavaScript found to check."
    exit 0
}

$problems = New-Object System.Collections.Generic.List[string]

# ---------------------------------------------------------------------------- syntax

foreach ($file in $files) {
    $output = & node --check $file.FullName 2>&1

    if ($LASTEXITCODE -ne 0) {
        $problems.Add("$(Get-Relative $file.FullName): $($output -join ' ')")
    }
}

if ($problems.Count -gt 0) {
    Write-Host "The shell does not parse:" -ForegroundColor Red

    foreach ($problem in $problems) {
        Write-Host "  $problem"
    }

    # Stopping here on purpose. eslint has nothing useful to say about a file that will not
    # parse, and a second wall of errors from the same cause would bury the first.
    if ($Check) { exit 1 }

    exit 0
}

# ----------------------------------------------------------------------------- names

# --yes so an unattended run does not stop on npx's install prompt, and a pinned major so an
# eslint release cannot change what this reports without somebody choosing it. The first run
# fetches; after that npx serves it from its own cache.
Push-Location $repoRoot

try {
    $lint = & npx --yes eslint@9 @($files | ForEach-Object { Get-Relative $_.FullName }) --config $config 2>&1
    $lintExit = $LASTEXITCODE
}
finally {
    Pop-Location
}

if ($lintExit -eq 0) {
    Write-Host "The shell parses, and every name it uses exists ($($files.Count) file(s))."
    exit 0
}

Write-Host "Names used where they do not exist:" -ForegroundColor Red
Write-Host ($lint -join [Environment]::NewLine)
Write-Host ""
Write-Host "A name defined inside another function is not in scope outside it. If it is wanted"
Write-Host "in both places, lift it to the file's own scope rather than reaching for it."

if ($Check) { exit 1 }

exit 0
