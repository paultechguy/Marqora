#Requires -Version 7.0

<#
.SYNOPSIS
    Starts a release: bumps the version and moves the vNext notes into this version's file.

.DESCRIPTION
    The first of the two release steps, and the only one that produces something you have to
    think about. It changes three files in the working tree and stops:

        Directory.Build.props        <Version> set to the number you passed
        docs\releases\v<version>.md  the notes from docs\releases\vNext.md, titled for this version
        docs\releases\vNext.md       reset to the empty stub this script carries

    vNext.md is where the notes are written as each change lands, so by now the release's notes
    already exist; this moves them rather than leaving you to copy them. Any heading with nothing
    under it is dropped on the way, as is the stub's guidance comment. A vNext with nothing in it
    beyond headings stops the preflight - there would be nothing to release.

    Nothing is committed and nothing is pushed. You read the notes over, then commit all three
    files to dev as an ordinary change - reviewed the way any other change is reviewed. By the
    time Publish-Release.ps1 runs, "dev is ready to release" is a plain fact about dev rather
    than a state this script left behind.

    That ordering is the point. Release notes are the one part of a release that needs
    judgement, and judgement should not happen inside a script run with a half-finished
    release waiting on it.

    The gates here are the cheap ones - branch, tree, ancestry, and whether the version is
    still free, and whether vNext.md has anything to move. Moving a markdown file has no
    business running the test suite; the full set runs in Publish-Release.ps1 where they
    actually gate something irreversible.

.PARAMETER Version
    The version to release, as three numbers: 0.3.0. Drives Directory.Build.props, the notes
    file name, and later the tag, the zip name and the release title.

.PARAMETER Force
    Overwrite an existing notes file for this version. The version bump is idempotent, so this
    is about throwing away edits made to the notes since the first run. When that run has
    already reset vNext.md, the notes are taken from vNext.md as committed in HEAD, so a re-run
    produces the same file the first run did.

.PARAMETER Check
    Run the gates and report, then stop. Nothing is written.

.PARAMETER Republish
    Allow a version that already has a draft release and a tag. Use it with -Force to rewrite
    the notes of a version you are about to rebuild with Publish-Release.ps1 -Republish; it
    changes nothing here beyond the tag gate's answer.

.EXAMPLE
    pwsh .\build\New-ReleaseNotes.ps1 -Version 0.3.0

.EXAMPLE
    pwsh .\build\New-ReleaseNotes.ps1 -Version 0.3.0 -Check

    Ask whether a release could start, without starting one.

.NOTES
    Exit codes: 0 success, 1 failure.
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [string] $Version,

    [switch] $Force,

    [switch] $Check,

    [switch] $Republish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'ReleaseCommon.ps1')

$repoRoot = Split-Path -Parent $PSScriptRoot
$buildProps = Join-Path $repoRoot 'Directory.Build.props'
$vNextRelative = 'docs/releases/vNext.md'
$vNextPath = Join-Path $repoRoot ($vNextRelative -replace '/', [System.IO.Path]::DirectorySeparatorChar)
$notesRelative = "docs/releases/v$Version.md"
$notesPath = Join-Path $repoRoot ($notesRelative -replace '/', [System.IO.Path]::DirectorySeparatorChar)

try {
    Assert-VersionString $Version

    Write-Host ''
    Write-Host "Marqora release notes $Version" -ForegroundColor White
    Write-Host ''

    # ---- gates
    Write-Host '  preflight' -ForegroundColor DarkGray

    # The three files this script is about to touch are allowed to be dirty already, so that a
    # second run with -Force works without making you revert the first one by hand, and so that
    # notes added to vNext since the last commit are not a reason to refuse.
    Test-GateTree -RepoRoot $repoRoot -Branch 'dev' -AllowDirty @('Directory.Build.props', $notesRelative, $vNextRelative)
    Test-GateAncestor -RepoRoot $repoRoot
    Test-GateTagFree -RepoRoot $repoRoot -Version $Version -Republish:$Republish
    Test-GateNotesAbsent -NotesPath $notesPath -Force:$Force
    $vNext = Test-GateVNext -RepoRoot $repoRoot -VNextRelative $vNextRelative

    # What vNext.md goes back to once its notes have moved. Held here rather than in a file of
    # its own: a separate stub file looked like the place to write the notes, and a release's
    # worth of them were written there instead of in vNext.md.
    $stub = @"
# Marqora vNext - What's New

<!--
    Notes for the next release, written as each change lands: a ## section per feature, and a
    paragraph with a bold lead per fix under ## Fixes. Write for someone deciding whether to
    download the release, not for someone reading the diff.

    build\New-ReleaseNotes.ps1 moves everything here into docs\releases\v<version>.md, drops
    any heading with nothing under it, and puts this stub back.
-->

## Fixes
"@.Replace("`r`n", "`n") + "`n"

    Write-Host ''

    if ($Check) {
        Write-Host '  Nothing written. A release of ' -NoNewline -ForegroundColor DarkGray
        Write-Host $Version -NoNewline -ForegroundColor White
        Write-Host ' can start from here.' -ForegroundColor DarkGray
        Write-Host ''
        exit 0
    }

    Initialize-TaskList -Total 3

    # ---- version
    Write-Task 'version'
    $previous = Get-RepoVersion -PropsPath $buildProps

    if ($PSCmdlet.ShouldProcess($buildProps, "Set <Version> to $Version")) {
        $changed = Set-RepoVersion -PropsPath $buildProps -Version $Version
    }
    else {
        $changed = $false
    }

    if ($previous -eq $Version) {
        Write-Done "already $Version"
    }
    elseif ($changed) {
        Write-Done "$previous -> $Version"
    }
    else {
        Write-Done "$previous -> $Version (not written)"
    }

    # ---- notes
    Write-Task 'release notes'
    $notes = ConvertTo-ReleaseNotes -Text $vNext.Text -Version $Version

    if ($PSCmdlet.ShouldProcess($notesPath, "Write release notes from $vNextRelative ($($vNext.Source))")) {
        Save-Text -Path $notesPath -Text $notes
    }

    Write-Done $notesRelative

    # ---- vNext
    # Only after the notes are written: a failure above leaves vNext as it was.
    Write-Task 'vNext reset'

    if ($PSCmdlet.ShouldProcess($vNextPath, 'Reset to the stub')) {
        Save-Text -Path $vNextPath -Text $stub
    }

    Write-Done $vNextRelative

    # ---- what to do next
    # Publish-Release.ps1 refuses notes that still say TODO; better to hear it now.
    $todos = @($notes -split '\r?\n' | Where-Object { $_ -match 'TODO' }).Count

    Write-Host ''
    Write-Host '  Next' -ForegroundColor White
    Write-Host "    Read $notesRelative over as someone deciding whether to download it." -ForegroundColor DarkGray
    if ($todos) {
        Write-Host "    It has $todos line(s) saying TODO, which Publish-Release.ps1 will refuse." -ForegroundColor Yellow
    }
    Write-Host '    Then commit all three files to dev:' -ForegroundColor DarkGray
    Write-Host ''
    Write-Host "      git add Directory.Build.props $notesRelative $vNextRelative" -ForegroundColor Gray
    Write-Host "      git commit -m ""Release notes for $Version""" -ForegroundColor Gray
    Write-Host '      git push origin dev' -ForegroundColor Gray
    Write-Host ''
    Write-Host '    Then publish:' -ForegroundColor DarkGray
    Write-Host ''
    Write-Host "      pwsh .\build\Publish-Release.ps1 -Version $Version" -ForegroundColor Gray
    Write-Host ''
    Write-Host '  Changed your mind' -ForegroundColor White
    Write-Host ''
    Write-Host '      git checkout -- Directory.Build.props' -ForegroundColor Gray
    Write-Host "      git checkout -- $vNextRelative" -ForegroundColor Gray
    Write-Host "      Remove-Item $notesRelative" -ForegroundColor Gray
    Write-Host ''

    exit 0
}
catch {
    Write-Failure $_.Exception.Message
    exit 1
}
