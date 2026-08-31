#Requires -Version 7.0

<#
.SYNOPSIS
    Publishes a release that dev is already ready for: promotes master, builds, tags, and
    leaves a draft on GitHub for you to check before anyone else sees it.

.DESCRIPTION
    The second of the two release steps, and the one that touches the outside world. It runs
    only when dev is already carrying the version bump and the finished release notes, both
    committed and pushed by you as an ordinary change.

    This script writes no commits. It promotes, builds, tags and uploads:

        1  promote master   git merge --ff-only dev, then push
        2  build            New-Release.ps1 -Test, from master
        3  tag              annotated v<version> on master, then push
        4  release body     the committed notes plus the generated footer
        5  draft            gh release create --draft, with both assets

    Nothing it does is unrecoverable. The fast-forward moves master to a commit already on
    dev. Tags sit outside the repository ruleset and can be deleted. A draft is private until
    you publish it, and deletes cleanly. The worst outcome is that you delete a draft and a
    tag and try again.

    The draft is deliberately where the release stops. Downloading its asset and installing
    that - rather than a zip built locally moments earlier - is the only way to test the
    literal bytes GitHub will serve.

.PARAMETER Version
    The version to release, as three numbers: 0.3.0. Must match Directory.Build.props, which
    is a gate rather than a formality: it catches a bump that was never pushed, and last
    release's number typed out of habit.

.PARAMETER Verify
    Skip the release and check one that already exists: download both assets from GitHub,
    confirm the zip matches its checksum, and confirm the archive's layout. Run it after you
    have clicked Publish.

.PARAMETER Yes
    Skip the confirmation. For an unattended run.

.PARAMETER ShowBuildOutput
    Stream the dotnet output instead of capturing it. Output is shown automatically when a
    step fails; this is for when it succeeds and you still want to read it.

.EXAMPLE
    pwsh .\build\Publish-Release.ps1 -Version 0.3.0

.EXAMPLE
    pwsh .\build\Publish-Release.ps1 -Version 0.3.0 -WhatIf

    Run every gate for real, then print the git and gh commands without running them.

.EXAMPLE
    pwsh .\build\Publish-Release.ps1 -Version 0.3.0 -Verify

.NOTES
    Exit codes: 0 success, 1 failure.
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [string] $Version,

    [switch] $Verify,

    [switch] $Yes,

    [switch] $ShowBuildOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'ReleaseCommon.ps1')

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'PaulTechGuy.MQ.slnx'
$buildProps = Join-Path $repoRoot 'Directory.Build.props'
$headerScript = Join-Path $PSScriptRoot 'Add-FileHeaders.ps1'
$releaseScript = Join-Path $PSScriptRoot 'New-Release.ps1'
$notesTemplate = Join-Path $PSScriptRoot 'release-notes-template.md'
$footerTemplate = Join-Path $PSScriptRoot 'release-footer-template.md'
$artifacts = Join-Path $PSScriptRoot 'artifacts'

$runtimeIdentifier = 'win-x64'
$configuration = 'Release'
$tag = "v$Version"
$zipName = "Marqora-$Version-$runtimeIdentifier.zip"
$zipPath = Join-Path $artifacts $zipName
$shaPath = "$zipPath.sha256"
$notesRelative = "docs/releases/v$Version.md"
$notesPath = Join-Path $repoRoot ($notesRelative -replace '/', [System.IO.Path]::DirectorySeparatorChar)
$bodyPath = Join-Path $artifacts "release-body-$Version.md"

$dryRun = [bool] $WhatIfPreference

function Get-AssetHash {
    param([Parameter(Mandatory)][string] $Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-RecordedHash {
    param([Parameter(Mandatory)][string] $Path)

    $line = ([System.IO.File]::ReadAllText($Path)).Trim()

    return ($line -split '\s+')[0].ToUpperInvariant()
}

function Get-ZipRootEntry {
    param([Parameter(Mandatory)][string] $Path)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($Path)

    try {
        return $zip.Entries | ForEach-Object { $_.FullName.Split('/')[0] } | Sort-Object -Unique
    }
    finally {
        $zip.Dispose()
    }
}

# ------------------------------------------------------------------------------------------
#  -Verify: check a release that already exists
# ------------------------------------------------------------------------------------------

function Invoke-VerifyRelease {
    param([Parameter(Mandatory)][string] $Slug)

    $staging = Join-Path ([System.IO.Path]::GetTempPath()) "marqora-verify-$Version"

    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }

    New-Item -ItemType Directory -Path $staging -Force | Out-Null

    Initialize-TaskList -Total 3

    Write-Task 'download'
    Invoke-Gh `
        -Arguments @('release', 'download', $tag, '--repo', $Slug, '--dir', $staging, '--clobber') `
        -FailureMessage "Could not download the assets for $tag. Is the release published?" | Out-Null

    $downloadedZip = Join-Path $staging $zipName
    $downloadedSha = Join-Path $staging "$zipName.sha256"

    foreach ($required in @($downloadedZip, $downloadedSha)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
            Write-Failed
            throw "The release is missing '$(Split-Path -Leaf $required)'."
        }
    }

    Write-Done (Format-Size (Get-Item -LiteralPath $downloadedZip).Length)

    Write-Task 'checksum'
    $actual = Get-AssetHash -Path $downloadedZip
    $recorded = Get-RecordedHash -Path $downloadedSha

    if ($actual -ne $recorded) {
        Write-Failed
        Write-Host ''
        Write-Host "    recorded  $recorded" -ForegroundColor DarkGray
        Write-Host "    actual    $actual" -ForegroundColor DarkGray
        Write-Host ''
        throw 'The published zip does not match its published checksum.'
    }

    Write-Done 'matches'

    Write-Task 'archive layout'
    $expected = @('app', 'install', 'Install.cmd', 'README.txt', 'Uninstall.cmd')
    $actualRoots = @(Get-ZipRootEntry -Path $downloadedZip)

    if (Compare-Object -ReferenceObject $expected -DifferenceObject $actualRoots) {
        Write-Failed
        Write-CapturedOutput $actualRoots
        throw "The zip's root does not hold the expected five entries."
    }

    Write-Done "$($actualRoots.Count) entries at the root"

    Remove-Item -LiteralPath $staging -Recurse -Force

    Write-Host ''
    Write-Host "  $tag is published, intact, and laid out correctly." -ForegroundColor Green
    Write-Host ''
}

# ------------------------------------------------------------------------------------------

try {
    Assert-VersionString $Version

    Write-Host ''
    Write-Host "Marqora release $Version" -ForegroundColor White
    Write-Host ''

    $slug = Get-RepoSlug -RepoRoot $repoRoot

    if ($Verify) {
        Invoke-VerifyRelease -Slug $slug
        exit 0
    }

    # ---- gates
    Write-Host '  preflight' -ForegroundColor DarkGray

    Test-GateTooling
    Test-GateTree -RepoRoot $repoRoot -Branch 'dev'
    Test-GateAncestor -RepoRoot $repoRoot
    Test-GateTagFree -RepoRoot $repoRoot -Version $Version
    Test-GateVersionMatch -PropsPath $buildProps -Version $Version
    Test-GateNotesReady -NotesPath $notesPath -TemplatePath $notesTemplate -Version $Version
    Test-GateTests -Solution $solution -Configuration $configuration -Stream:$ShowBuildOutput
    Test-GateHeaders -ScriptPath $headerScript
    Test-GateBuild -Solution $solution -Configuration $configuration -Stream:$ShowBuildOutput

    Write-Host ''

    # ---- confirm
    $masterSha = (Invoke-Git -Arguments @('-C', $repoRoot, 'rev-parse', '--short', 'origin/master')).Output
    $devSha = (Invoke-Git -Arguments @('-C', $repoRoot, 'rev-parse', '--short', 'origin/dev')).Output
    $promoting = (Invoke-Git -Arguments @('-C', $repoRoot, 'rev-list', '--count', 'origin/master..origin/dev')).Output

    Write-Host '  About to release' -ForegroundColor White
    Write-Host ''
    Write-Host "    promote   master $masterSha -> $devSha ($promoting commit(s), fast-forward)" -ForegroundColor Gray
    Write-Host "    build     $zipName from master" -ForegroundColor Gray
    Write-Host "    tag       $tag on master, annotated" -ForegroundColor Gray
    Write-Host "    publish   draft release on $slug" -ForegroundColor Gray
    Write-Host ''
    Write-Host '    All of this is reversible: the fast-forward only moves master to a commit' -ForegroundColor DarkGray
    Write-Host '    already on dev, and tags and drafts can both be deleted.' -ForegroundColor DarkGray
    Write-Host ''

    if ($dryRun) {
        Write-Host '    -WhatIf: gates ran for real, nothing below will.' -ForegroundColor Yellow
        Write-Host ''
    }
    elseif (-not $Yes) {
        $answer = Read-Host '  Continue? [y/N]'

        if ($answer -notin @('y', 'Y', 'yes', 'Yes')) {
            Write-Host ''
            Write-Host '  Nothing done.' -ForegroundColor DarkGray
            Write-Host ''
            exit 0
        }

        Write-Host ''
    }

    Initialize-TaskList -Total 5
    $returnToDev = $false

    try {
        # ---- 1. promote master
        Write-Task 'promote master'

        if ($dryRun) {
            Write-Skipped 'what-if'
            Write-Hint 'git switch master'
            Write-Hint 'git merge --ff-only dev'
            Write-Hint 'git push origin master'
        }
        else {
            $hasLocalMaster = (Invoke-Git -Arguments @('-C', $repoRoot, 'branch', '--list', 'master')).Output

            if ($hasLocalMaster) {
                Invoke-Git -Arguments @('-C', $repoRoot, 'switch', 'master') -FailureMessage 'Could not switch to master.' | Out-Null
            }
            else {
                Invoke-Git -Arguments @('-C', $repoRoot, 'switch', '-c', 'master', '--track', 'origin/master') -FailureMessage 'Could not create a local master.' | Out-Null
            }

            $returnToDev = $true

            Invoke-Git -Arguments @('-C', $repoRoot, 'merge', '--ff-only', 'dev') -FailureMessage 'master could not be fast-forwarded to dev.' | Out-Null
            Invoke-Git -Arguments @('-C', $repoRoot, 'push', 'origin', 'master') -FailureMessage 'Could not push master.' | Out-Null

            Write-Done "fast-forward, $promoting commit(s)"
        }

        # ---- 2. build from master
        Write-Task 'build from master'

        if ($dryRun) {
            Write-Skipped 'what-if'
            Write-Hint "pwsh .\build\New-Release.ps1 -Test    (~3 minutes)"
        }
        else {
            $buildArgs = @('-NoProfile', '-File', $releaseScript, '-Test')

            if ($ShowBuildOutput) {
                $buildArgs += '-ShowBuildOutput'
            }

            $buildOutput = & pwsh @buildArgs 2>&1
            $buildCode = $LASTEXITCODE

            if ($buildCode -ne 0) {
                Write-Failed
                Write-CapturedOutput $buildOutput
                throw "New-Release.ps1 failed (exit code $buildCode)."
            }

            # The zip is named from the version in the built executable. If that disagrees with
            # what we are releasing, the bump did not take and the tag would name a build that
            # does not exist.
            if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
                Write-Failed
                $produced = Get-ChildItem -LiteralPath $artifacts -Filter '*.zip' -File | ForEach-Object { $_.Name }
                Write-CapturedOutput $produced
                throw "The build produced no '$zipName'. Directory.Build.props and the built executable disagree about the version."
            }

            Write-Done (Format-Size (Get-Item -LiteralPath $zipPath).Length)
        }

        # ---- 3. tag
        Write-Task 'tag'

        if ($dryRun) {
            Write-Skipped 'what-if'
            Write-Hint "git tag -a $tag -m ""Marqora $Version"""
            Write-Hint "git push origin $tag"
        }
        else {
            Invoke-Git -Arguments @('-C', $repoRoot, 'tag', '-a', $tag, '-m', "Marqora $Version") -FailureMessage "Could not create the tag $tag." | Out-Null
            Invoke-Git -Arguments @('-C', $repoRoot, 'push', 'origin', $tag) -FailureMessage "Could not push the tag $tag." | Out-Null

            Write-Done "$tag on master"
        }

        # ---- 4. release body
        Write-Task 'release body'

        # Under -WhatIf the build did not run, so there may be no checksum to fold in. An
        # older zip left in artifacts is good enough to render a representative body.
        if ($dryRun -and -not (Test-Path -LiteralPath $shaPath -PathType Leaf)) {
            Write-Skipped 'what-if, nothing built to hash'
        }
        else {
            $hash = Get-RecordedHash -Path $shaPath
            $footer = Expand-Template -Path $footerTemplate -Token @{
                VERSION      = $Version
                ZIP          = $zipName
                SHA256       = $hash
                DOWNLOAD_URL = "https://github.com/$slug/releases/download/$tag/$zipName"
            }

            $body = ([System.IO.File]::ReadAllText($notesPath)).TrimEnd() + "`n`n" + $footer

            if (-not $dryRun) {
                Save-Text -Path $bodyPath -Text $body
            }

            $bodyWords = ($body -split '\s+' | Where-Object { $_ }).Count
            Write-Done "$bodyWords words"
        }

        # ---- 5. draft
        Write-Task 'draft'

        if ($dryRun) {
            Write-Skipped 'what-if'
            Write-Hint "gh release create $tag --repo $slug --draft --verify-tag --target master"
            Write-Hint "    --title ""Marqora $Version"" --notes-file <body> $zipName $zipName.sha256"

            Write-Host ''
            Write-Host '  Nothing reached origin. Confirm with:' -ForegroundColor DarkGray
            Write-Host '    git log origin/dev..dev' -ForegroundColor Gray
            Write-Host '    git ls-remote --tags origin' -ForegroundColor Gray
            Write-Host ''
            exit 0
        }

        $created = Invoke-Gh -Arguments @(
            'release', 'create', $tag,
            '--repo', $slug,
            '--draft',
            '--verify-tag',
            '--target', 'master',
            '--title', "Marqora $Version",
            '--notes-file', $bodyPath,
            $zipPath,
            $shaPath
        ) -FailureMessage 'Could not create the draft release.'

        Write-Done '2 assets'

        $draftUrl = ($created.Output -split '\r?\n' | Where-Object { $_ -match '^https://' } | Select-Object -Last 1)
    }
    finally {
        if ($returnToDev) {
            & git -C $repoRoot switch dev --quiet 2>&1 | Out-Null
        }
    }

    Write-Host ''
    Write-Host '  Draft created. It is private until you publish it.' -ForegroundColor White
    Write-Host ''

    if ($draftUrl) {
        Write-Host "    $draftUrl" -ForegroundColor White
        Write-Host ''
    }

    Write-Host '  Smoke-test the draft before publishing - this is the only way to test the' -ForegroundColor DarkGray
    Write-Host '  bytes GitHub will actually serve:' -ForegroundColor DarkGray
    Write-Host ''
    Write-Host "      gh release download $tag --repo $slug --dir `$env:TEMP\marqora-$Version" -ForegroundColor Gray
    Write-Host "      # extract it, run Install.cmd, launch, check Help > About says $Version" -ForegroundColor Gray
    Write-Host ''
    Write-Host '  Then click Publish release on the page above, and confirm what shipped:' -ForegroundColor DarkGray
    Write-Host ''
    Write-Host "      pwsh .\build\Publish-Release.ps1 -Version $Version -Verify" -ForegroundColor Gray
    Write-Host ''
    Write-Host '  If it is wrong, nothing is stuck:' -ForegroundColor DarkGray
    Write-Host ''
    Write-Host "      gh release delete $tag --repo $slug --yes" -ForegroundColor Gray
    Write-Host "      git push --delete origin $tag; git tag -d $tag" -ForegroundColor Gray
    Write-Host ''

    exit 0
}
catch {
    Write-Failure $_.Exception.Message
    exit 1
}
