# The CLI export baseline in tests/baselines/cli, for Get-BaselineHashes.ps1 and
# Compare-ExportBaseline.ps1: the one copy of where it is and what it is made of.
# Dot-sourced by both.

$baselineRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'tests/baselines/cli'
$baselineReadme = Join-Path $baselineRoot 'README.md'

# The README's checklist is the one list of what the baseline holds. Every item of the form
# "- [ ] `name`" is an export, a file or a folder, so the README and the scripts cannot disagree
# about it.
function Get-BaselineExportNames {
    if (-not (Test-Path -LiteralPath $baselineReadme)) {
        throw "No checklist at $baselineReadme."
    }

    $names = foreach ($line in Get-Content -LiteralPath $baselineReadme) {
        if ($line -match '^\s*- \[[ xX]\] `([^`]+)`') {
            $Matches[1]
        }
    }

    if (-not $names) {
        throw "No checklist items found in $baselineReadme."
    }

    @($names)
}
