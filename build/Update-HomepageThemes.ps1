<#
.SYNOPSIS
    Rewrites the color theme swatches on the project homepage from the theme files.

.DESCRIPTION
    docs/index.html shows every color theme as a small card: the theme's heading color and five of
    its accents, once on a light page and once on a dark one. The cards are drawn from the shipped
    theme files in src/PaulTechGuy.MQ.Themes/Themes/, so the homepage shows the app's real colors
    and a new theme needs no artwork - but only if the cards are regenerated, which is what this is.

    Only the block between the theme-strip markers in the page is written. Everything else on the
    page is hand-written and is never touched.

    The order is the app's: Default first, then by name, ignoring case - the same rule
    ThemeCatalog applies, so the homepage and the gallery agree.

.PARAMETER Check
    Report only, and exit non-zero if the homepage has fallen behind the theme files. The form a
    release gate would use.

.EXAMPLE
    pwsh ./build/Update-HomepageThemes.ps1
    pwsh ./build/Update-HomepageThemes.ps1 -Check
#>

[CmdletBinding()]
param(
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$themeDir = Join-Path $repo 'src/PaulTechGuy.MQ.Themes/Themes'
$page = Join-Path $repo 'docs/index.html'

$start = '<!-- theme-strip:start -->'
$end = '<!-- theme-strip:end -->'

# The two pages a card sits on: the neutral page every theme draws on, in each mode.
$lightPage = '#ffffff'
$darkPage = '#1f1f1f'

# Which slots become the card's five dots, in order.
$dots = 'link', 'callout-tip-bar', 'callout-important-bar', 'callout-warning-bar', 'syntax-keyword'

$themes = [System.Collections.Generic.List[object]]::new()

foreach ($file in Get-ChildItem -LiteralPath $themeDir -Filter '*.json') {
    $themes.Add((Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json))
}

# Default first, the rest by name with no regard to case: ThemeCatalog's own order.
$themes.Sort([Comparison[object]] {
    param($a, $b)

    if ($a.id -eq 'default') { return -1 }
    if ($b.id -eq 'default') { return 1 }

    [string]::Compare($a.name, $b.name, [StringComparison]::OrdinalIgnoreCase)
})

function Get-Half($palette, [string]$pageColor, [string]$label) {
    $swatches = ($dots | ForEach-Object { '<i class="sw" style="background:' + $palette.$_ + '"></i>' }) -join ''

    '<div class="theme-half" style="background:' + $pageColor + '" aria-label="' + $label + '">' +
    '<b class="aa" style="color:' + $palette.'heading-1' + '">Aa</b><span class="sws">' + $swatches + '</span></div>'
}

$cards = foreach ($theme in $themes) {
    $name = [System.Net.WebUtility]::HtmlEncode($theme.name)

    @(
        '        <div class="theme-card">'
        '          <div class="theme-pair">' +
        (Get-Half $theme.light $lightPage "$name, light") +
        (Get-Half $theme.dark $darkPage "$name, dark") +
        '</div>'
        '          <span class="theme-name">' + $name + '</span>'
        '        </div>'
    ) -join "`n"
}

$block = "`n" + ($cards -join "`n") + "`n        "

$html = [System.IO.File]::ReadAllText($page)
$text = $html.Replace("`r`n", "`n")
$from = $text.IndexOf($start, [StringComparison]::Ordinal)
$to = $text.IndexOf($end, [StringComparison]::Ordinal)

if ($from -lt 0 -or $to -lt $from) {
    throw "docs/index.html has no '$start' ... '$end' block to write into."
}

$head = $text.Substring(0, $from + $start.Length)
$tail = $text.Substring($to)
$updated = $head + $block + $tail

if ($updated -ceq $text) {
    'The homepage swatches match the theme files.'
    exit 0
}

if ($Check) {
    'The homepage swatches have fallen behind the theme files. Run: pwsh ./build/Update-HomepageThemes.ps1'
    exit 1
}

# Back in the file's own line endings, so the rewrite does not turn into a whole-file diff.
if ($html.Contains("`r`n")) { $updated = $updated.Replace("`n", "`r`n") }

[System.IO.File]::WriteAllText($page, $updated, [System.Text.UTF8Encoding]::new($false))

"The homepage now shows $($themes.Count) themes: $(($themes | ForEach-Object { $_.name }) -join ', ')."
