<#
.SYNOPSIS
    Finds places still making the retired absolute claim about network access.

.DESCRIPTION
    Marqora used to say "no network calls", full stop. It no longer can: a Folio collects the
    pictures a document names by web address when the author ticks a box in the preflight. What
    replaced it is a narrower claim that actually holds - the app does nothing on its own, and
    is offline by default.

    The naive version of this check greps the tree for "no network calls" and reports about
    thirty hits, most of which are true statements that should not change - and a check that
    cries wolf is a check somebody switches off. So this encodes the triage instead:

      * SURFACES is a curated list of the files that carry the claim to a reader. Those are
        checked for the retired phrasings, the way Set-AmericanSpelling.ps1 carries a curated
        stem list rather than every difference between the two Englishes.

      * ALLOWED lists the places that say something network-shaped and are still correct -
        launching a link in the shell, the preview's content policy, build-time asset fetching.
        These are exempt by file and phrase, not waved through by accident.

      * Anything outside both, anywhere in the tree, is reported as new drift. That is the case
        this really exists for: a release note written from memory, months from now.

    There is no CI in this repository, so nothing runs this on its own. It belongs in the gates
    in docs/Releasing.md, which is the one moment it reliably gets run.

.PARAMETER Check
    Report only, and exit non-zero if anything is found. The form a gate would use.

.EXAMPLE
    pwsh ./build/Test-NetworkClaim.ps1
    pwsh ./build/Test-NetworkClaim.ps1 -Check
#>

[CmdletBinding()]
param(
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot

# The phrasings that are no longer true, as regular expressions.
#
# Singular and plural both, which is not fussiness: the first version of this file checked only
# "no network calls" and walked straight past "no network call" in the app's own Welcome
# document - the most-read prose in the product. Anything added here gets the same treatment.
$retired = @(
    'no network calls?'
    'never goes to the network'
    'never go(es)? near the network'
    'no calls to the network'
    'zero network'
    "can't have an asterisk"
    'cannot have an asterisk'
)

# What a reader sees. Everything here must carry the current claim.
$surfaces = @(
    'README.md'
    'CONTRIBUTING.md'
    'docs/index.html'
    'docs/Architecture.md'
    'docs/BlockedContent.md'
    'docs/Folio.md'
    'docs/Sponsors.md'
    'docs/WordExport.md'
    'docs/Releasing.md'
    'src/PaulTechGuy.MQ.App/Assets/Welcome to Marqora.md'
    'build/installer/README.txt'
    'build/release-footer-template.md'
    'build/release-notes-template.md'
    'build/New-Release.ps1'
    'src/PaulTechGuy.MQ.App/Views/AboutDialog.cs'
)

# Still true, and must not be "fixed". Each is a file plus the phrase that is allowed in it.
$allowed = @(
    @{ Path = 'src/PaulTechGuy.MQ.App/Services/ExternalLink.cs'; Phrase = 'no network calls' }
    @{ Path = 'src/PaulTechGuy.MQ.App/ViewModels/MainViewModel.cs'; Phrase = 'No network call happens here' }
    @{ Path = 'src/PaulTechGuy.MQ.App/Views/AboutDialog.cs'; Phrase = 'still makes no network calls' }
    @{ Path = 'src/PaulTechGuy.MQ.App/Views/SupportDialog.cs'; Phrase = 'makes no network calls' }
    @{ Path = 'src/PaulTechGuy.MQ.Docx/DocxImages.cs'; Phrase = 'no network calls' }
    @{ Path = 'src/PaulTechGuy.MQ.Docx/InlineRenderer.cs'; Phrase = 'no network call' }
    @{ Path = 'src/PaulTechGuy.MQ.Domain/AppSettings.cs'; Phrase = 'no network calls' }
    @{ Path = 'src/PaulTechGuy.MQ.Abstractions/Services/IUpdateReminderService.cs'; Phrase = 'network call' }
    @{ Path = 'build/Get-WebAssets.ps1'; Phrase = 'no network' }
    @{ Path = 'webshell/app.js'; Phrase = 'network call' }

    # Singular, and each about one particular operation that really does reach nothing. These
    # are the reason the broadened pattern needs an allowlist rather than a shorter pattern:
    # "no network call" is false as a claim about the product and true as a claim about a
    # hyperlink, and only a person can tell which is which.
    @{ Path = 'docs/BlockedContent.md'; Phrase = 'No network call happens inside Marqora' }
    @{ Path = 'src/PaulTechGuy.MQ.Folio/FolioPlanner.cs'; Phrase = 'planner still makes no network call' }
    @{ Path = 'tests/PaulTechGuy.MQ.Docx.Tests/ImageTests.cs'; Phrase = 'costs Marqora no network call' }
    @{ Path = 'tests/PaulTechGuy.MQ.Docx.Tests/ImageTests.cs'; Phrase = 'needs no network call' }
)

# Written by the tests and by this file itself; scanning either is noise. '.claude' holds the
# worktrees agent sessions work in - whole stale copies of the tree, gitignored and never shipped -
# and one left behind blocked a release by failing on its own copies of allowed comments.
$skipDirectories = @('.git', '.claude', 'bin', 'obj', 'node_modules', 'vendor', 'artifacts', 'releases')
$skipFiles = @('Test-NetworkClaim.ps1', 'UltimateMarkdownContent.md')

function Test-Allowed {
    param([string]$Relative, [string]$Line)

    foreach ($entry in $allowed) {
        if ($Relative -ieq $entry.Path -and $Line -imatch [regex]::Escape($entry.Phrase)) {
            return $true
        }
    }

    return $false
}

$findings = [System.Collections.Generic.List[object]]::new()

$files = Get-ChildItem -Path $repo -Recurse -File -Include '*.md', '*.html', '*.cs', '*.ps1', '*.js', '*.txt' |
    Where-Object {
        $relative = [IO.Path]::GetRelativePath($repo, $_.FullName)
        $parts = $relative -split '[\\/]'

        -not ($parts | Where-Object { $skipDirectories -contains $_ }) -and
        -not ($skipFiles -contains $_.Name)
    }

foreach ($file in $files) {
    $relative = ([IO.Path]::GetRelativePath($repo, $file.FullName)) -replace '\\', '/'
    $number = 0

    foreach ($line in (Get-Content -LiteralPath $file.FullName)) {
        $number++

        foreach ($pattern in $retired) {
            if ($line -imatch $pattern) {
                if (Test-Allowed -Relative $relative -Line $line) {
                    continue
                }

                $scope = if ($surfaces -contains $relative) { 'surface' } else { 'drift' }

                $findings.Add([pscustomobject]@{
                    Path  = $relative
                    Line  = $number
                    Scope = $scope
                    Text  = $line.Trim()
                })

                break
            }
        }
    }
}

if ($findings.Count -eq 0) {
    Write-Host "The retired network claim appears nowhere it should not."
    exit 0
}

$surfaceHits = @($findings | Where-Object { $_.Scope -eq 'surface' })
$driftHits = @($findings | Where-Object { $_.Scope -eq 'drift' })

if ($surfaceHits.Count -gt 0) {
    Write-Host "Retired claim on a reader-facing surface ($($surfaceHits.Count)):" -ForegroundColor Red

    foreach ($hit in $surfaceHits) {
        $text = if ($hit.Text.Length -gt 100) { $hit.Text.Substring(0, 100) + '...' } else { $hit.Text }
        Write-Host "  $($hit.Path):$($hit.Line)  $text"
    }
}

if ($driftHits.Count -gt 0) {
    Write-Host ""
    Write-Host "New occurrences outside the known-true list ($($driftHits.Count)):" -ForegroundColor Yellow

    foreach ($hit in $driftHits) {
        $text = if ($hit.Text.Length -gt 100) { $hit.Text.Substring(0, 100) + '...' } else { $hit.Text }
        Write-Host "  $($hit.Path):$($hit.Line)  $text"
    }

    Write-Host ""
    Write-Host "If one of these is still true, add it to ALLOWED with the phrase that makes it so."
}

exit 1
