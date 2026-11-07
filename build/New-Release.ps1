#Requires -Version 7.0

<#
.SYNOPSIS
    Builds the Marqora release zips: one command, one zip per architecture, ready to hand to
    someone else.

.DESCRIPTION
    Restores the third-party web assets if they are missing. Then, for each architecture, it
    publishes the app and marqora-cli into one folder, checks the files they share are the same
    bytes, checks every native file is built for that architecture, stages it alongside the
    installer scripts, and compresses the lot into

        build\artifacts\Marqora-<version>-win-x64.zip      for x64 PCs
        build\artifacts\Marqora-<version>-win-arm64.zip    for ARM PCs

    Each zip has a .sha256 file beside it. A release always has both zips; nothing here leaves
    one out, so Publish-Release.ps1 never has to ask what it was given.

    What comes out is self-contained in every sense. The .NET runtime and the Windows App
    SDK are inside it, so the target machine needs neither. The installer is per-user and
    writes nothing outside HKCU and the user's profile, so it needs no administrator. And
    the app contacts nothing on its own at runtime, so it needs no connection.

    The zip has no wrapper folder. Its contents sit at the root, so extracting it into a
    folder you already made does not bury everything a level deeper, and Explorer's Extract
    All still proposes a destination named after the zip:

        README.txt          what to do, in plain text
        Install.cmd         double-click to install
        Uninstall.cmd       double-click to remove
        install\            the scripts both wrappers call
        app\                the published application, with marqora-cli beside it

    An x64 PC cannot start the ARM64 build, so the ARM64 zip is proven by reading its files
    here and by running it on an ARM PC before release (docs/ARM-Release-Checklist.md).

    Tests are opt-in via -Test, and run once: they run on x64 whichever zip is being built.
    Repackaging is something you do repeatedly while getting
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
    Where the zips are written. Defaults to build\artifacts, which is git-ignored.

.PARAMETER KeepStaging
    Leaves the staged folders in place next to the zips, one per architecture, which is the
    quickest way to inspect exactly what shipped without unzipping it again.

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
$cliProject = Join-Path $repoRoot 'src\PaulTechGuy.MQ.Cli\PaulTechGuy.MQ.Cli.csproj'
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

# Web assets and, when asked for, the tests happen once. Five steps happen per architecture.
Initialize-TaskList -Total ($MqReleaseArchitectures.Count * 5 + $(if ($Test) { 2 } else { 1 }))

try {
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

    # Anything left from an earlier run goes first, so a failure below cannot leave a new zip
    # beside an old one of the same version for Publish-Release.ps1 to pick up as a set.
    $version = Get-RepoVersion -PropsPath $buildProps

    foreach ($architecture in $MqReleaseArchitectures) {
        $oldZip = Join-Path $artifacts (Get-ReleaseZipName -Version $version -Architecture $architecture)
        Remove-Item -LiteralPath $oldZip, "$oldZip.sha256" -Force -ErrorAction SilentlyContinue
    }

    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }

    New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
    $built = New-Object System.Collections.Generic.List[object]

    foreach ($architecture in $MqReleaseArchitectures) {
        # A folder each, so nothing from one architecture can reach the other's zip.
        $archStaging = Join-Path $staging "win-$architecture"
        New-Item -ItemType Directory -Path $archStaging -Force | Out-Null
        $publishDir = Join-Path $archStaging 'app'

        # ---- publish
        Write-Task "publish $architecture"

        Invoke-Dotnet `
            -Arguments @('publish', $appProject, '-c', $Configuration, '-o', $publishDir, "-p:MarqoraArch=$architecture", '--nologo') `
            -FailureMessage "The $architecture publish failed." `
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
                throw "The $architecture publish is missing '$required'. The app would install but not start."
            }
        }

        # The zips are named for the version in Directory.Build.props, so the executable has to
        # carry that version, or the name would be a claim about a build that does not exist.
        $appVersion = Get-PublishedVersion -Exe $publishedExe

        if ($appVersion -ne $version) {
            Write-Failed
            throw "Directory.Build.props says $version but the built $architecture Marqora.exe reports $appVersion."
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

        # ---- the command line, published beside the app (ReleaseCommon's Add-CliToPublish)
        Write-Task "publish marqora-cli $architecture"
        $cliSummary = Add-CliToPublish `
            -CliProject $cliProject `
            -PublishDir $publishDir `
            -StagingRoot $archStaging `
            -Configuration $Configuration `
            -AppVersion $appVersion `
            -Architecture $architecture `
            -ShowBuildOutput:$ShowBuildOutput
        Write-Done $cliSummary

        # ---- every native file, after the merge, so one pass covers both executables
        Write-Task "check files $architecture"
        Write-Done (Assert-PublishArchitecture -PublishDir $publishDir -Architecture $architecture)

        # ---- stage
        Write-Task "stage installer $architecture"
        $stagedRoot = New-StagedRelease `
            -PublishDir $publishDir `
            -Version $version `
            -StagingRoot $archStaging `
            -InstallerSource $installerSource `
            -AssociationScript $associationScript `
            -Architecture $architecture
        Write-Done "v$version"

        # ---- zip
        Write-Task "zip $architecture"
        $zipPath = Join-Path $artifacts (Get-ReleaseZipName -Version $version -Architecture $architecture)
        New-ReleaseArchive -StagedRoot $stagedRoot -ZipPath $zipPath

        $zipSize = (Get-Item -LiteralPath $zipPath).Length
        Write-Done (Format-Size $zipSize)

        # A checksum beside the zip, so whoever receives it can confirm it arrived intact.
        # Cheap to produce and the only thing this build can offer in place of a signature.
        $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
        "$hash  $(Split-Path -Leaf $zipPath)" | Set-Content -LiteralPath "$zipPath.sha256" -Encoding ASCII

        $built.Add([pscustomobject]@{ Architecture = $architecture; Zip = $zipPath; Hash = $hash })
    }

    if (-not $KeepStaging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }

    Write-Host ''

    foreach ($zip in $built) {
        Write-Host "  $($zip.Zip)" -ForegroundColor White
        Write-Host "  SHA256 $($zip.Hash)" -ForegroundColor DarkGray
        Write-Host "  For $($zip.Architecture) PCs." -ForegroundColor DarkGray
        Write-Host ''
    }

    Write-Host '  Copy the right zip to the other machine, extract it into a folder, run Install.cmd.' `
        -ForegroundColor DarkGray
    Write-Host ''

    exit 0
}
catch {
    Write-Failure $_.Exception.Message
    exit 1
}
