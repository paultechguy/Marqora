<#
.SYNOPSIS
    Records, and later checks, the CLI export baseline in tests/baselines/cli.

.DESCRIPTION
    The baseline is a set of exports made from the app before the CLI work changed any of its
    code (docs/CLI-Implementation-Plan.md, WP-M0-05). Its whole value is that it has not moved
    since, so it is recorded once as a manifest of SHA-256 hashes and checked against that
    manifest whenever it is used.

    The exports themselves are git-ignored. MANIFEST.sha256 is committed, beside the README
    whose checklist says what the baseline is made of.

    The checklist is the one list of what is expected. Every item in it of the form
    "- [ ] `name`" is a file or a folder this script looks for, so the README and the script
    cannot disagree about it. A folder - the Folio's folder form - is hashed file by file.

    Without -Check it writes the manifest, after making sure every expected export is there,
    and prints the line for the plan's §9.3 record: the version of the app that made the
    exports and the commit it was built from, the count, the size and a hash of the manifest
    itself. It refuses to replace a manifest that already exists unless -Force is given,
    because replacing it is choosing a new baseline.

.PARAMETER Check
    Compare what is on disk with MANIFEST.sha256 and exit non-zero on any file changed,
    missing, or added. Writes nothing.

.PARAMETER Force
    Replace an existing MANIFEST.sha256.

.PARAMETER App
    The Marqora.exe the exports were made with, read for its version and the commit it was
    built from. Defaults to the Release build in the baseline worktree the README sets up,
    ..\PaulTechGuy.Marqora-baseline, and failing that to this checkout's own Release build.

.EXAMPLE
    pwsh ./build/Get-BaselineHashes.ps1
    pwsh ./build/Get-BaselineHashes.ps1 -Check
#>

[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$Force,
    [string]$App
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$root = Join-Path $repo 'tests/baselines/cli'
$readme = Join-Path $root 'README.md'
$manifest = Join-Path $root 'MANIFEST.sha256'

# What the baseline folder holds besides the exports: never hashed.
$ownFiles = @('README.md', 'MANIFEST.sha256')
$ownFolders = @('inputs')

function Get-ExpectedNames {
    $names = foreach ($line in Get-Content -LiteralPath $readme) {
        if ($line -match '^\s*- \[[ xX]\] `([^`]+)`') {
            $Matches[1]
        }
    }

    if (-not $names) {
        throw "No checklist items found in $readme."
    }

    @($names)
}

function Get-Hashes {
    $files = Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object {
        $relative = [IO.Path]::GetRelativePath($root, $_.FullName)
        $first = ($relative -split '[\\/]')[0]

        -not ($ownFiles -contains $relative) -and -not ($ownFolders -contains $first)
    }

    foreach ($file in $files) {
        [pscustomobject]@{
            Path   = ([IO.Path]::GetRelativePath($root, $file.FullName)) -replace '\\', '/'
            Hash   = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            Length = $file.Length
        }
    }
}

if (-not (Test-Path -LiteralPath $readme)) {
    throw "No checklist at $readme."
}

# Ordinal, so the manifest's order does not depend on the culture of whoever runs this.
$current = @([Linq.Enumerable]::OrderBy(
        [object[]]@(Get-Hashes),
        [Func[object, string]] { param($entry) $entry.Path },
        [StringComparer]::Ordinal))

if ($Check) {
    if (-not (Test-Path -LiteralPath $manifest)) {
        Write-Host "No baseline recorded yet: $manifest is missing." -ForegroundColor Red
        exit 1
    }

    $recorded = @{}

    foreach ($line in Get-Content -LiteralPath $manifest) {
        if ($line -match '^([0-9a-f]{64})  (.+)$') {
            $recorded[$Matches[2]] = $Matches[1]
        }
    }

    $onDisk = @{}
    foreach ($entry in $current) { $onDisk[$entry.Path] = $entry.Hash }

    $changed = @($recorded.Keys | Where-Object { $onDisk.ContainsKey($_) -and $onDisk[$_] -ne $recorded[$_] } | Sort-Object)
    $missing = @($recorded.Keys | Where-Object { -not $onDisk.ContainsKey($_) } | Sort-Object)
    $added = @($onDisk.Keys | Where-Object { -not $recorded.ContainsKey($_) } | Sort-Object)

    if ($changed.Count + $missing.Count + $added.Count -eq 0) {
        Write-Host "The baseline is as recorded: $($recorded.Count) file(s)."
        exit 0
    }

    foreach ($path in $changed) { Write-Host "  changed  $path" -ForegroundColor Red }
    foreach ($path in $missing) { Write-Host "  missing  $path" -ForegroundColor Red }
    foreach ($path in $added) { Write-Host "  added    $path" -ForegroundColor Yellow }

    Write-Host "The baseline differs from MANIFEST.sha256."
    exit 1
}

# Recording. Every expected export first: a baseline with a hole in it is not one.
$absent = @(Get-ExpectedNames | Where-Object {
        $path = Join-Path $root $_

        if (Test-Path -LiteralPath $path -PathType Container) {
            -not (Get-ChildItem -LiteralPath $path -Recurse -File | Select-Object -First 1)
        }
        else {
            -not (Test-Path -LiteralPath $path -PathType Leaf)
        }
    })

if ($absent.Count -gt 0) {
    Write-Host "Not in $root yet ($($absent.Count)):" -ForegroundColor Red
    foreach ($name in $absent) { Write-Host "  $name" }
    exit 1
}

if ((Test-Path -LiteralPath $manifest) -and -not $Force) {
    Write-Host "MANIFEST.sha256 already exists. Replacing it chooses a new baseline; pass -Force if that is what you mean." -ForegroundColor Red
    exit 1
}

$lines = foreach ($entry in $current) { "$($entry.Hash)  $($entry.Path)" }

# LF and no BOM, the way sha256sum writes one, so `sha256sum -c` reads it too.
[IO.File]::WriteAllText($manifest, (($lines -join "`n") + "`n"), [Text.UTF8Encoding]::new($false))

$bytes = ($current | Measure-Object -Property Length -Sum).Sum
$sealed = (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash.ToLowerInvariant()

# The app that made the exports. Its product version carries the commit it was built from after
# a plus sign, and that commit - not this checkout's - is the baseline's: the README has the
# exports made by a build of a release, from a worktree beside this one.
$release = 'src/PaulTechGuy.MQ.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/Marqora.exe'

if (-not $App) {
    $worktree = Join-Path (Split-Path -Parent $repo) "PaulTechGuy.Marqora-baseline/$release"
    $App = if (Test-Path -LiteralPath $worktree) { $worktree } else { Join-Path $repo $release }
}

$version = 'unknown'
$builtFrom = 'unknown'

if (Test-Path -LiteralPath $App) {
    $product = (Get-Item -LiteralPath $App).VersionInfo.ProductVersion
    $version, $revision = $product -split '\+', 2

    if ($revision) {
        $builtFrom = $revision.Substring(0, [Math]::Min(7, $revision.Length))
    }
}
else {
    Write-Host "No app at $App, so its version is not recorded. Pass -App with the Marqora.exe that made the exports." -ForegroundColor Yellow
}

Write-Host "Wrote $manifest."
Write-Host ""
Write-Host "For the plan's section 9.3 record:"
Write-Host ("  {0} files, {1:N0} bytes; made with Marqora {2} built from {3}; MANIFEST.sha256 {4}" -f $current.Count, $bytes, $version, $builtFrom, $sealed.Substring(0, 16))
