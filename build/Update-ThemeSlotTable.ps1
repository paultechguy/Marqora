<#
.SYNOPSIS
    Rewrites the slot table and the JSON skeleton in docs/ColorThemes-Authoring.md from the
    slot list.

.DESCRIPTION
    The color theme slots are written down once, in src/PaulTechGuy.MQ.Themes/ThemeSlots.cs.
    The authoring document shows them twice - as a table for the person writing a theme, and as
    the skeleton the prompt hands an AI - and both are generated, so a slot added to the code
    reaches the next theme's author without anyone remembering to tell them.

    The generating is done by AuthoringDocumentTests in the theme tests, which can read the slot
    list as the compiler sees it. A script parsing ThemeSlots.cs would be a second copy of the
    list by another name. This runs those tests: with the write switch set, they replace the two
    marked blocks in the document; with -Check, they only report.

    The theme tests run in every test pass, so a document that has fallen behind fails there as
    well; this is the way to bring it up to date.

.PARAMETER Check
    Report only, and exit non-zero if the document has fallen behind the slot list.

.EXAMPLE
    pwsh ./build/Update-ThemeSlotTable.ps1
    pwsh ./build/Update-ThemeSlotTable.ps1 -Check
#>

[CmdletBinding()]
param(
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'tests/PaulTechGuy.MQ.Themes.Tests/PaulTechGuy.MQ.Themes.Tests.csproj'

if ($Check) {
    Remove-Item Env:MARQORA_WRITE_THEME_DOCS -ErrorAction SilentlyContinue
} else {
    $env:MARQORA_WRITE_THEME_DOCS = '1'
}

try {
    dotnet test $project --filter 'FullyQualifiedName~AuthoringDocumentTests' --nologo --verbosity quiet
    $code = $LASTEXITCODE
} finally {
    Remove-Item Env:MARQORA_WRITE_THEME_DOCS -ErrorAction SilentlyContinue
}

if ($code -eq 0) {
    if ($Check) { 'The authoring document matches the slot list.' } else { 'The authoring document is up to date with the slot list.' }
} else {
    'The authoring document has fallen behind the slot list. Run: pwsh ./build/Update-ThemeSlotTable.ps1'
}

exit $code
