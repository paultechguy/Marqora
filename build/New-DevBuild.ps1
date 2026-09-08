#Requires -Version 7.0

<#
.SYNOPSIS
    Zips the working tree as it stands, under a version you name, so you can install it on
    another machine. Nothing to do with releasing.

.DESCRIPTION
    The same publish, staging and installer that New-Release.ps1 produces - the recipient
    extracts it and double-clicks Install.cmd exactly as they would a real release - built
    from whatever is in the tree right now and labeled with a version you supply:

        build\artifacts\dev\Marqora-<version>-win-x64.zip

    What it does not do is the point. It writes no commits, moves no branches, creates no
    tags, uploads nothing, and above all does not touch <Version> in Directory.Build.props.
    The version you pass reaches the build as an MSBuild property and no further, so the
    repository is exactly as you left it and Publish-Release.ps1's version gate still means
    what it says.

    -Version is mandatory on purpose. Without it the build would inherit the number in
    Directory.Build.props, and a test build would land on the other machine claiming to be
    the release of the same name - identically named zip, identical About box, different
    bytes. Naming it yourself is the whole reason this script exists.

    The version is not only in the file name. It is stamped into the executable, so
    Help > About on the test machine reports it, and the README.txt inside the zip carries
    it along with the branch and commit it came from.

    Read the warning in the zip's README before installing one of these over a Marqora that
    someone relies on: the installer is the release installer, so it takes over the same
    install directory, the same shortcuts and the same file associations.

.PARAMETER Version
    What to call this build. Three numbers, optionally followed by a prerelease suffix:

        0.3.0-test4        0.2.3-dev.3024647        0.3.0-rc1        0.3.0

    A suffix is worth using. The numbers alone are indistinguishable from a real release,
    which is the confusion this script is meant to prevent.

.PARAMETER Test
    Runs the test suite first and stops if anything fails. Off by default: the point of a
    dev build is the turnaround, and a suite that runs on every iteration is a suite you
    learn to stop invoking.

.PARAMETER ForceAssets
    Re-downloads the web assets even when webshell\vendor already looks complete.

.PARAMETER OutputDirectory
    Where the zip is written. Defaults to build\artifacts\dev - a floor below the release
    artifacts rather than beside them, so a dev zip is never picked up by hand in mistake
    for one the pipeline built. Git-ignored either way.

.PARAMETER KeepStaging
    Leaves the staged folder next to the zip, which is the quickest way to see what shipped
    without unzipping it again.

.PARAMETER ShowBuildOutput
    Streams the dotnet output instead of capturing it. Output is shown automatically when a
    step fails; this is for when it succeeds and you still want to read it.

.EXAMPLE
    pwsh .\build\New-DevBuild.ps1 -Version 0.3.0-test4

    The usual invocation.

.EXAMPLE
    pwsh .\build\New-DevBuild.ps1 -Version 0.3.0-rc1 -Test

    The same, gated on a green test run, for something you are handing to another person.

.NOTES
    Exit codes: 0 success, 1 failure.

    Release builds only. Debug is framework-dependent - see SelfContained in the app project
    - so a Debug zip would need .NET already installed on the far machine and would not start
    without it. A switch that quietly produces an artifact that cannot run is worse than no
    switch, so there is not one.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Version,

    [switch] $Test,

    [switch] $ForceAssets,

    [string] $OutputDirectory,

    [switch] $KeepStaging,

    [switch] $ShowBuildOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Progress output, the dotnet and git wrappers, and the staging and zip steps are shared with
# New-Release.ps1. This script is the release build minus the release: same output, a
# different source for the version, and nothing written back to the repository.
. (Join-Path $PSScriptRoot 'ReleaseCommon.ps1')

$repoRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repoRoot 'src\PaulTechGuy.MQ.App\PaulTechGuy.MQ.App.csproj'
$solution = Join-Path $repoRoot 'PaulTechGuy.MQ.slnx'
$installerSource = Join-Path $PSScriptRoot 'installer'
$associationScript = Join-Path $PSScriptRoot 'Register-FileAssociation.ps1'
$webAssetScript = Join-Path $PSScriptRoot 'Get-WebAssets.ps1'

# The same file the app project's VerifyWebAssets target checks for, so a missing checkout of
# the web assets fails at step one with an explanation rather than at the publish with an
# MSBuild error the reader has to go and look up.
$webAssetSentinel = Join-Path $repoRoot 'webshell\vendor\monaco\vs\loader.js'

$artifacts = if ($OutputDirectory) { [System.IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $PSScriptRoot 'artifacts\dev' }
$staging = Join-Path $artifacts '.stage'
$runtimeIdentifier = 'win-x64'
$configuration = 'Release'

# Three numbers, then an optional prerelease suffix. Deliberately looser than
# Test-VersionString, which the release pipeline uses and which allows the numbers alone: a
# dev build is exactly the case a suffix exists for.
$devVersionPattern = '^(\d+\.\d+\.\d+)(-[0-9A-Za-z][0-9A-Za-z.-]*)?$'

function Assert-DevVersion {
    param([Parameter(Mandatory)][string] $Value)

    # Build metadata is dropped when the version is read back off the executable, so a version
    # carrying it would name the zip one thing and the About box another. Said here rather
    # than discovered on the other machine.
    if ($Value.Contains('+')) {
        throw "'$Value' carries build metadata after a '+'. That is stripped from the version the app reports, so use a prerelease suffix instead: 0.3.0-test4."
    }

    if ($Value -notmatch $devVersionPattern) {
        throw "'$Value' is not a version this can build. Expected three numbers with an optional suffix, as in 0.3.0-test4 or 0.3.0."
    }

    # AssemblyVersion and FileVersion are numeric-only fields. Returning the numeric part so
    # the caller can pass them explicitly, rather than leaving the SDK to work out which part
    # of a suffixed version is the number, is what keeps a suffix from becoming a build error.
    return $Matches[1]
}

# Which tree this came out of. Read-only, and best-effort: a build from an export with no .git
# is unusual but not a reason to refuse.
function Get-TreeProvenance {
    $branch = Invoke-Git -Arguments @('-C', $repoRoot, 'rev-parse', '--abbrev-ref', 'HEAD') -AllowFailure
    $commit = Invoke-Git -Arguments @('-C', $repoRoot, 'rev-parse', '--short', 'HEAD') -AllowFailure

    if ($branch.ExitCode -ne 0 -or $commit.ExitCode -ne 0) {
        return [pscustomobject]@{ Text = 'not a git working tree'; Dirty = $false }
    }

    $status = Invoke-Git -Arguments @('-C', $repoRoot, 'status', '--porcelain') -AllowFailure
    $dirty = ($status.ExitCode -eq 0) -and [bool] $status.Output

    $text = '{0} @ {1}' -f $branch.Output, $commit.Output

    if ($dirty) {
        $text += ' with uncommitted changes'
    }

    return [pscustomobject]@{ Text = $text; Dirty = $dirty }
}

# The recipient's first stop is README.txt, so that is where this belongs. Prepended to the
# release readme rather than templated separately: that readme is the one that gets
# maintained, and a second copy of it here would be out of date within two releases.
function Add-DevBuildBanner {
    param(
        [Parameter(Mandatory)][string] $StagedRoot,
        [Parameter(Mandatory)][string] $Label,
        [Parameter(Mandatory)][string] $Provenance
    )

    $rule = '=' * 75
    $built = Get-Date -Format 'yyyy-MM-dd HH:mm'

    $banner = @"
$rule
DEVELOPMENT BUILD - $Label
$rule

This is not a release. It was built straight from a working tree:

  Source    $Provenance
  Built     $built

It is here to try something out, and nothing has tested it beyond whoever
sent it to you. Installing it replaces any Marqora already on this machine
- same install folder, same shortcuts, same file associations - and Help >
About will report the version above. Your settings, recent files list and
snippets are kept, as they are across any upgrade.

To get back to a real release, download it from the project's releases page
and run its Install.cmd over the top of this one.


"@

    $path = Join-Path $StagedRoot 'README.txt'
    $existing = Get-Content -LiteralPath $path -Raw

    Set-Content -LiteralPath $path -Value ($banner + $existing) -Encoding UTF8 -NoNewline
}

Initialize-TaskList -Total $(if ($Test) { 5 } else { 4 })

try {
    $numericVersion = Assert-DevVersion -Value $Version

    Write-Host ''
    Write-Host "Marqora dev build $Version" -ForegroundColor White
    Write-Host ''

    $provenance = Get-TreeProvenance

    # ---- web assets
    Write-Task 'web assets'

    if ($ForceAssets -or -not (Test-Path -LiteralPath $webAssetSentinel)) {
        $note = if ($ForceAssets) { 'refreshed' } else { 'restored' }
        $output = & pwsh -NoProfile -File $webAssetScript @(if ($ForceAssets) { '-Force' }) 2>&1

        if ($LASTEXITCODE -ne 0) {
            Write-Failed
            $output | ForEach-Object { Write-Host $_ }
            throw 'Restoring the web assets failed.'
        }

        if (-not (Test-Path -LiteralPath $webAssetSentinel)) {
            Write-Failed
            throw "Get-WebAssets.ps1 finished but '$webAssetSentinel' is still missing."
        }

        Write-Done $note
    }
    else {
        Write-Done 'cached'
    }

    # ---- tests
    if ($Test) {
        Write-Task 'tests'
        Invoke-Dotnet -Arguments @('test', $solution, '-c', $configuration, '--nologo') -FailureMessage 'Tests failed.' -Stream:$ShowBuildOutput
        Write-Done 'passed'
    }

    # ---- publish
    Write-Task "publish $configuration"

    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }

    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    $publishDir = Join-Path $staging 'app'

    # Version on the command line beats the <Version> in Directory.Build.props for this build
    # and this build only; the file on disk is untouched. AssemblyVersion and FileVersion take
    # the numeric part because neither field accepts a prerelease suffix.
    Invoke-Dotnet `
        -Arguments @(
            'publish', $appProject,
            '-c', $configuration,
            '-o', $publishDir,
            "-p:Version=$Version",
            "-p:AssemblyVersion=$numericVersion.0",
            "-p:FileVersion=$numericVersion.0",
            '--nologo') `
        -FailureMessage 'The publish failed.' `
        -Stream:$ShowBuildOutput

    $publishedExe = Join-Path $publishDir 'Marqora.exe'

    if (-not (Test-Path -LiteralPath $publishedExe -PathType Leaf)) {
        Write-Failed
        throw "The publish completed but produced no Marqora.exe in '$publishDir'."
    }

    # The app cannot start without these, and none of them is copied by the plain publish
    # targets - see PublishWinUIResources in the app project. Checking here turns a zip that
    # fails on the far machine with an unresolvable ms-appx:/// URI into a build that fails
    # on this one.
    foreach ($required in @('Marqora.pri', 'App.xbf', 'Assets\web\shell.html', 'Assets\web\vendor\monaco\vs\loader.js')) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishDir $required))) {
            Write-Failed
            throw "The publish is missing '$required'. The app would install but not start."
        }
    }

    # Absent, this is invisible at run time: the app logs a line and opens nothing at all.
    # Said out loud rather than thrown, because the build is perfectly usable without it.
    $welcomeMissing = -not (Test-Path -LiteralPath (Join-Path $publishDir 'Assets\Welcome to Marqora.md'))

    $publishSize = Get-DirectorySize -Path $publishDir
    Write-Done (Format-Size $publishSize)

    if ($welcomeMissing) {
        Write-Note 'no welcome document in the publish; this build will not introduce itself'
    }

    # ---- stage
    Write-Task 'stage installer'

    # Read back rather than assumed. The executable is what lands on the other machine and
    # what About reports, so if the property did not reach it the name on the zip would be a
    # claim about a build that does not carry it - the one thing this script exists to stop.
    $built = Get-PublishedVersion -Exe $publishedExe

    if ($built -ne $Version) {
        Write-Failed
        throw "Asked for version '$Version' but the built Marqora.exe reports '$built'. The zip would be named one thing and Help > About would say another."
    }

    $stagedRoot = New-StagedRelease `
        -PublishDir $publishDir `
        -Version $Version `
        -StagingRoot $staging `
        -InstallerSource $installerSource `
        -AssociationScript $associationScript `
        -RuntimeIdentifier $runtimeIdentifier

    Add-DevBuildBanner -StagedRoot $stagedRoot -Label $Version -Provenance $provenance.Text
    Write-Done $provenance.Text

    # ---- zip
    Write-Task 'zip'
    New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
    $zipPath = Join-Path $artifacts ("Marqora-$Version-$runtimeIdentifier.zip")
    New-ReleaseArchive -StagedRoot $stagedRoot -ZipPath $zipPath

    $zipSize = (Get-Item -LiteralPath $zipPath).Length
    Write-Done (Format-Size $zipSize)

    # A checksum beside the zip, so whoever receives it can confirm it arrived intact. 84 MB
    # across a USB stick or a mail server does occasionally arrive short.
    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    "$hash  $(Split-Path -Leaf $zipPath)" | Set-Content -LiteralPath "$zipPath.sha256" -Encoding ASCII

    if (-not $KeepStaging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }

    Write-Host ''
    Write-Host "  $zipPath" -ForegroundColor White
    Write-Host "  SHA256 $hash" -ForegroundColor DarkGray
    Write-Host ''

    if ($provenance.Dirty) {
        Write-Host '  Built with uncommitted changes; the commit in the readme is only half the story.' `
            -ForegroundColor Yellow
        Write-Host ''
    }

    Write-Host '  Copy the zip to the other machine, extract it into a folder, run Install.cmd.' `
        -ForegroundColor DarkGray
    Write-Host '  Directory.Build.props was not touched.' -ForegroundColor DarkGray
    Write-Host ''

    exit 0
}
catch {
    Write-Failure $_.Exception.Message
    exit 1
}
