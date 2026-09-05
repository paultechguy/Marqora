<#
.SYNOPSIS
    Rewrites British spellings to American ones across the tree.

.DESCRIPTION
    Marqora's text is American English - the labels a user reads, and the comments and
    documents beside them, so that one file does not say "colour" while the dialog it
    describes says "Color".

    The word list below is deliberately short. It holds the words that have actually turned
    up wearing the wrong spelling, not every difference between the two Englishes; a word
    joins it when it is found, not in anticipation. Each entry is a stem, matched anywhere
    in a word, so "colour" also covers colours, coloured, colouring and recoloured.

    Case is preserved from what was there: COLOUR, Colour and colour come back as COLOR,
    Color and color.

    Nothing here understands code from prose. That is safe only while no identifier, CSS
    custom property or serialized property name contains one of these stems - checked when
    the list was written, and worth checking again before adding to it.

.PARAMETER Check
    Report the occurrences and exit non-zero. Nothing is written. This is the CI form.

.PARAMETER Path
    Directories to scan, relative to the repo root. The whole tree by default, which is what
    catches CLAUDE.md and the release-note templates alongside src and docs. This script
    states the words it replaces and would report itself, but it is a .ps1 and no .ps1 is
    scanned - keep it that way if the extension list ever grows.

.EXAMPLE
    pwsh ./build/Set-AmericanSpelling.ps1

.EXAMPLE
    pwsh ./build/Set-AmericanSpelling.ps1 -Check
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$Check,
    [string[]]$Path = @('.')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# British stem on the left, American on the right. Both lower case; the case of what was
# found is put back afterwards.
$words = [ordered]@{
    'colour' = 'color'
}

$extensions = @('*.cs', '*.xaml', '*.js', '*.css', '*.html', '*.json', '*.md')

$repoRoot = Split-Path -Parent $PSScriptRoot
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

# Generated code, build output, the vendored web bundle and the tooling folders are none of
# Marqora's business. artifacts holds built release bodies, which are already published.
$excluded = '[\\/](obj|bin|vendor|node_modules|artifacts|\.git|\.vs|\.github|\.claude)[\\/]'

$pattern = [regex]::new(
    '(' + (($words.Keys | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

<#
.SYNOPSIS
    The American spelling, wearing the case the British one was found in.
#>
function Get-Replacement {
    param([string]$Found)

    $american = $words[$Found.ToLowerInvariant()]

    if ($Found -ceq $Found.ToUpperInvariant()) {
        return $american.ToUpperInvariant()
    }

    if ([char]::IsUpper($Found[0])) {
        return $american.Substring(0, 1).ToUpperInvariant() + $american.Substring(1)
    }

    return $american
}

$files = foreach ($relative in $Path) {
    $root = Join-Path $repoRoot $relative

    if (-not (Test-Path $root)) {
        Write-Warning "skipping $relative (not found)"
        continue
    }

    foreach ($extension in $extensions) {
        Get-ChildItem -Path $root -Filter $extension -Recurse -File |
            Where-Object { $_.FullName -notmatch $excluded }
    }
}

$changed = @()
$found = @()

foreach ($file in $files | Sort-Object FullName -Unique) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    $matched = $pattern.Matches($text)

    if ($matched.Count -eq 0) {
        continue
    }

    $relative = $file.FullName.Substring($repoRoot.Length + 1)

    if ($Check) {
        # Line numbers, so a report can be walked rather than searched. The text before the
        # match is counted rather than the file split: a match is a position, not a line.
        foreach ($item in $matched) {
            $line = ($text.Substring(0, $item.Index) -split "`n").Count

            $found += [pscustomobject]@{
                File = $relative
                Line = $line
                Text = $item.Value
            }
        }

        continue
    }

    if (-not $PSCmdlet.ShouldProcess($relative, 'rewrite spellings')) {
        continue
    }

    $updated = $pattern.Replace($text, { param($item) Get-Replacement $item.Value })

    [System.IO.File]::WriteAllText($file.FullName, $updated, $utf8NoBom)

    $changed += [pscustomobject]@{ File = $relative; Count = $matched.Count }
}

if ($Check) {
    if ($found.Count -eq 0) {
        Write-Host "American spelling throughout $(@($files).Count) file(s)." -ForegroundColor Green
        exit 0
    }

    Write-Host "British spelling in $($found.File | Select-Object -Unique | Measure-Object | ForEach-Object Count) file(s), $($found.Count) occurrence(s):" -ForegroundColor Yellow

    foreach ($item in $found | Sort-Object File, Line) {
        Write-Host "  $($item.File):$($item.Line)  $($item.Text)"
    }

    Write-Host ""
    Write-Host "Run: pwsh ./build/Set-AmericanSpelling.ps1" -ForegroundColor DarkGray

    exit 1
}

if ($changed.Count -eq 0) {
    Write-Host "Nothing to rewrite across $(@($files).Count) file(s)." -ForegroundColor Green
    exit 0
}

$total = ($changed | Measure-Object -Property Count -Sum).Sum

Write-Host "Rewrote $total occurrence(s) in $($changed.Count) file(s):" -ForegroundColor Green

foreach ($item in $changed | Sort-Object File) {
    Write-Host "  $($item.File)  ($($item.Count))" -ForegroundColor DarkGray
}

exit 0
