#Requires -Version 7.0

<#
.SYNOPSIS
    Builds the Marqora release zip: one command, one file, ready to hand to someone else.

.DESCRIPTION
    Restores the third-party web assets if they are missing, publishes the app, stages it
    alongside the installer scripts, and compresses the lot into

        build\artifacts\Marqora-<version>-win-x64.zip

    What comes out is self-contained in every sense. The .NET runtime and the Windows App
    SDK are inside it, so the target machine needs neither. The installer is per-user and
    writes nothing outside HKCU and the user's profile, so it needs no administrator. And
    the app makes no network calls at runtime, so it needs no connection.

    The zip has no wrapper folder. Its contents sit at the root, so extracting it into a
    folder you already made does not bury everything a level deeper, and Explorer's Extract
    All still proposes a destination named after the zip:

        README.txt          what to do, in plain text
        Install.cmd         double-click to install
        Uninstall.cmd       double-click to remove
        install\            the scripts both wrappers call
        app\                the published application

    Tests are opt-in via -Test. Repackaging is something you do repeatedly while getting
    the installer right, and paying for the full suite on every iteration only teaches you
    to stop running the script.

.PARAMETER Configuration
    Build configuration. Release by default, and there is rarely a reason to change it:
    only Release is self-contained and precompiled, so a Debug zip would need the .NET
    runtime already present on the target machine.

.PARAMETER Test
    Runs the test suite before publishing and stops if anything fails. Worth it for a
    release you are actually going to send someone.

.PARAMETER ForceAssets
    Re-downloads the web assets even when webshell\vendor already looks complete.

.PARAMETER OutputDirectory
    Where the zip is written. Defaults to build\artifacts, which is git-ignored.

.PARAMETER KeepStaging
    Leaves the staged folder in place next to the zip, which is the quickest way to inspect
    exactly what shipped without unzipping it again.

.PARAMETER ShowBuildOutput
    Streams the dotnet output instead of capturing it. The output is shown automatically
    when a step fails; this is for when a step succeeds and you still want to see it.

.EXAMPLE
    pwsh .\build\New-Release.ps1

    The usual invocation.

.EXAMPLE
    pwsh .\build\New-Release.ps1 -Test

    The same, gated on a green test run.

.NOTES
    Exit codes: 0 success, 1 failure.
#>

[CmdletBinding()]
param(
    [string] $Configuration = 'Release',

    [switch] $Test,

    [switch] $ForceAssets,

    [string] $OutputDirectory,

    [switch] $KeepStaging,

    [switch] $ShowBuildOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Progress output, sizes and the dotnet wrapper are shared with New-ReleaseNotes.ps1 and
# Publish-Release.ps1, so they live in one file rather than three.
. (Join-Path $PSScriptRoot 'ReleaseCommon.ps1')

$repoRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repoRoot 'src\PaulTechGuy.MQ.App\PaulTechGuy.MQ.App.csproj'
$solution = Join-Path $repoRoot 'PaulTechGuy.MQ.slnx'
$buildProps = Join-Path $repoRoot 'Directory.Build.props'
$installerSource = Join-Path $PSScriptRoot 'installer'
$associationScript = Join-Path $PSScriptRoot 'Register-FileAssociation.ps1'
$webAssetScript = Join-Path $PSScriptRoot 'Get-WebAssets.ps1'

# The same file the app project's VerifyWebAssets target checks for. Matching it means this
# script fails at step one with an explanation rather than at the publish with an MSBuild
# error the user has to go and look up.
$webAssetSentinel = Join-Path $repoRoot 'webshell\vendor\monaco\vs\loader.js'

$artifacts = if ($OutputDirectory) { [System.IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $PSScriptRoot 'artifacts' }
$staging = Join-Path $artifacts '.stage'
$runtimeIdentifier = 'win-x64'

Initialize-TaskList -Total $(if ($Test) { 5 } else { 4 })

function Get-PublishedVersion {
    param([string] $Exe)

    $productVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Exe).ProductVersion

    if ($productVersion) {
        # Build metadata after a '+' is for diagnostics, not for a file name.
        $clean = $productVersion.Split('+')[0].Trim()

        if ($clean) {
            return $clean
        }
    }

    # The executable is the ground truth for what was actually built, but if it carries no
    # version at all the props file is a reasonable second opinion.
    if (Test-Path -LiteralPath $buildProps) {
        $node = ([xml] (Get-Content -LiteralPath $buildProps -Raw)).SelectSingleNode('//Version')

        if ($node -and $node.InnerText) {
            return $node.InnerText.Trim()
        }
    }

    throw 'Could not determine the version to name the release after.'
}

function New-StagedRelease {
    param(
        [string] $PublishDir,
        [string] $Version
    )

    $name = "Marqora-$Version-$runtimeIdentifier"
    $root = Join-Path $staging $name

    New-Item -ItemType Directory -Path $root -Force | Out-Null

    # A move rather than a copy: same volume, so this is a rename, and copying 180 MB twice
    # to produce one zip is time spent for nothing.
    Move-Item -LiteralPath $PublishDir -Destination (Join-Path $root 'app')

    $installDir = Join-Path $root 'install'
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null

    foreach ($file in @('Install.ps1', 'Uninstall.ps1')) {
        Copy-Item -LiteralPath (Join-Path $installerSource $file) -Destination $installDir -Force
    }

    # The association script lives in build\ because it is useful on its own during
    # development. The installer needs its own copy, so there is exactly one source for it
    # and no chance of the shipped copy drifting from the one that gets tested by hand.
    Copy-Item -LiteralPath $associationScript -Destination $installDir -Force

    foreach ($file in @('Install.cmd', 'Uninstall.cmd')) {
        Copy-Item -LiteralPath (Join-Path $installerSource $file) -Destination $root -Force
    }

    # The readme carries the version so a zip that has been sitting in a downloads folder
    # for six months still says which one it is.
    $readme = Get-Content -LiteralPath (Join-Path $installerSource 'README.txt') -Raw
    $readme.Replace('{{VERSION}}', $Version) |
        Set-Content -LiteralPath (Join-Path $root 'README.txt') -Encoding UTF8 -NoNewline

    return $root
}

function New-ReleaseArchive {
    param(
        [string] $StagedRoot,
        [string] $ZipPath
    )

    if (Test-Path -LiteralPath $ZipPath) {
        Remove-Item -LiteralPath $ZipPath -Force
    }

    # No base directory: what comes out of the zip is the release itself, not a folder
    # containing it. Explorer's Extract All already proposes a destination named after the
    # zip, so the double-click path still lands in a folder of its own.
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $StagedRoot,
        $ZipPath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)
}

try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    Write-Host ''
    Write-Host 'Marqora release' -ForegroundColor White
    Write-Host ''

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
        Invoke-Dotnet -Arguments @('test', $solution, '-c', $Configuration, '--nologo') -FailureMessage 'Tests failed.' -Stream:$ShowBuildOutput
        Write-Done 'passed'
    }

    # ---- publish
    Write-Task "publish $Configuration"

    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }

    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    $publishDir = Join-Path $staging 'app'

    Invoke-Dotnet `
        -Arguments @('publish', $appProject, '-c', $Configuration, '-o', $publishDir, '--nologo') `
        -FailureMessage 'The publish failed.' `
        -Stream:$ShowBuildOutput

    $publishedExe = Join-Path $publishDir 'Marqora.exe'

    if (-not (Test-Path -LiteralPath $publishedExe -PathType Leaf)) {
        Write-Failed
        throw "The publish completed but produced no Marqora.exe in '$publishDir'."
    }

    # The app cannot start without these two, and neither is copied by the plain publish
    # targets - see PublishWinUIResources in the app project. Checking here turns a zip
    # that fails on the user's machine with an unresolvable ms-appx:/// URI into a build
    # that fails on this one.
    foreach ($required in @('Marqora.pri', 'App.xbf', 'Assets\web\shell.html', 'Assets\web\vendor\monaco\vs\loader.js')) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishDir $required))) {
            Write-Failed
            throw "The publish is missing '$required'. The app would install but not start."
        }
    }

    # The welcome document is what a new release introduces itself with, and its absence is
    # invisible at run time: the app logs a line and opens nothing at all. Said out loud here
    # rather than thrown, because the release is perfectly usable without it.
    $welcomeMissing = -not (Test-Path -LiteralPath (Join-Path $publishDir 'Assets\Welcome to Marqora.md'))

    $publishSize = Get-DirectorySize -Path $publishDir
    Write-Done (Format-Size $publishSize)

    if ($welcomeMissing) {
        Write-Host '        no welcome document in the publish; this release will not introduce itself' `
            -ForegroundColor Yellow
    }

    # ---- stage
    Write-Task 'stage installer'
    $version = Get-PublishedVersion -Exe $publishedExe
    $stagedRoot = New-StagedRelease -PublishDir $publishDir -Version $version
    Write-Done "v$version"

    # ---- zip
    Write-Task 'zip'
    New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
    $zipPath = Join-Path $artifacts ("Marqora-$version-$runtimeIdentifier.zip")
    New-ReleaseArchive -StagedRoot $stagedRoot -ZipPath $zipPath

    $zipSize = (Get-Item -LiteralPath $zipPath).Length
    Write-Done (Format-Size $zipSize)

    # A checksum beside the zip, so whoever receives it can confirm it arrived intact.
    # Cheap to produce and the only thing this build can offer in place of a signature.
    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    "$hash  $(Split-Path -Leaf $zipPath)" | Set-Content -LiteralPath "$zipPath.sha256" -Encoding ASCII

    if (-not $KeepStaging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }

    Write-Host ''
    Write-Host "  $zipPath" -ForegroundColor White
    Write-Host "  SHA256 $hash" -ForegroundColor DarkGray
    Write-Host ''
    Write-Host '  Copy the zip to the other machine, extract it into a folder, run Install.cmd.' `
        -ForegroundColor DarkGray
    Write-Host ''

    exit 0
}
catch {
    Write-Failure $_.Exception.Message
    exit 1
}
