<#
.SYNOPSIS
    Restores the third-party web assets the Marqora preview shell depends on.

.DESCRIPTION
    Monaco, Mermaid and KaTeX are pulled from the npm registry and unpacked into
    webshell/vendor. That folder is git-ignored, so run this
    once after cloning and again whenever the pinned versions below change.

    Everything is served locally from a WebView2 virtual host: the app makes no network
    calls at runtime and renders correctly offline.

.PARAMETER Force
    Re-download even when the target folders already exist.

.EXAMPLE
    pwsh ./build/Get-WebAssets.ps1
#>
[CmdletBinding()]
param(
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Pinned so a build is reproducible. Bump deliberately, then re-run with -Force.
$packages = @(
    @{ Name = 'monaco-editor';            Version = '0.56.0';  Target = 'monaco' }
    @{ Name = 'mermaid';                  Version = '11.17.0'; Target = 'mermaid' }
    @{ Name = 'katex';                    Version = '0.18.4';  Target = 'katex' }
    @{ Name = '@highlightjs/cdn-assets';  Version = '11.12.0'; Target = 'highlight' }
)

$repoRoot  = Split-Path -Parent $PSScriptRoot
$vendorDir = Join-Path $repoRoot 'webshell/vendor'
$stageDir  = Join-Path ([System.IO.Path]::GetTempPath()) ("marqora-assets-" + [System.Guid]::NewGuid().ToString('N'))

function Expand-NpmPackage {
    param([string]$Name, [string]$Version, [string]$Destination)

    # Scoped packages keep the scope in the registry path but not in the file name:
    # https://registry.npmjs.org/@scope/pkg/-/pkg-1.2.3.tgz
    $shortName = $Name.Split('/')[-1]
    $safeName  = $Name.Replace('/', '-').TrimStart('@')

    $tarball = "https://registry.npmjs.org/$Name/-/$shortName-$Version.tgz"
    $archive = Join-Path $stageDir "$safeName-$Version.tgz"
    $extract = Join-Path $stageDir $safeName

    Write-Host "  downloading $Name@$Version" -ForegroundColor DarkGray
    Invoke-WebRequest -Uri $tarball -OutFile $archive -UseBasicParsing

    New-Item -ItemType Directory -Path $extract -Force | Out-Null
    # tar ships with Windows 10 1803 and later.
    tar -xzf $archive -C $extract
    if ($LASTEXITCODE -ne 0) { throw "Failed to extract $Name@$Version" }

    return (Join-Path $extract 'package')
}

function Copy-Asset {
    param([string]$From, [string]$To)

    if (-not (Test-Path $From)) { throw "Expected asset not found: $From" }

    $parent = Split-Path -Parent $To
    if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }

    Copy-Item -Path $From -Destination $To -Recurse -Force
}

if ((Test-Path $vendorDir) -and -not $Force) {
    $existing = @(Get-ChildItem -Path $vendorDir -Directory -ErrorAction SilentlyContinue)
    if ($existing.Count -ge 3) {
        Write-Host 'Web assets already present. Re-run with -Force to refresh.' -ForegroundColor Yellow
        return
    }
}

New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

try {
    Write-Host 'Restoring Marqora web assets' -ForegroundColor Cyan

    foreach ($package in $packages) {
        $root = Expand-NpmPackage -Name $package.Name -Version $package.Version -Destination $package.Target
        $dest = Join-Path $vendorDir $package.Target

        if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
        New-Item -ItemType Directory -Path $dest -Force | Out-Null

        switch ($package.Target) {
            'monaco' {
                # Only the minified AMD bundle is needed; the ESM and dev trees are not used.
                Copy-Asset -From (Join-Path $root 'min/vs') -To (Join-Path $dest 'vs')
                # Source maps roughly double the payload and are never read in production.
                Get-ChildItem -Path $dest -Recurse -Filter '*.map' | Remove-Item -Force

                <#
                    Monaco carries language services for TypeScript, CSS, HTML and JSON,
                    about 16 MB of worker payload. Marqora only ever creates markdown
                    models, and a language worker is fetched only when a model of that
                    language exists, so none of these is ever requested.

                    Only the payloads under vs/assets and vs/language are removed. The small
                    AMD wrappers sitting directly in vs/ (ts.worker-<hash>.js and friends)
                    are static dependencies of vs/editor/editor.main and must stay, or the
                    editor fails to load at all.
                #>
                $workerPayloads = @(
                    Get-ChildItem -Path (Join-Path $dest 'vs/assets') -File -ErrorAction SilentlyContinue |
                        Where-Object { $_.Name -match '^(ts|css|html|json)\.worker' }
                    Get-ChildItem -Path (Join-Path $dest 'vs/language') -Recurse -File -ErrorAction SilentlyContinue |
                        Where-Object { $_.Name -match '\.worker\.js$' }
                )

                if ($workerPayloads) {
                    $freed = ($workerPayloads | Measure-Object -Property Length -Sum).Sum / 1MB
                    $workerPayloads | Remove-Item -Force
                    Write-Host ("  dropped unused language workers ({0:N1} MB)" -f $freed) -ForegroundColor DarkGray
                }
            }
            'mermaid' {
                # The ESM build, not the UMD bundle. The UMD file contains several anonymous
                # AMD defines, which Monaco's loader on the same page refuses, and it carries
                # every diagram type up front. This entry point is 30 KB and pulls each
                # diagram grammar on demand from chunks/.
                Copy-Asset -From (Join-Path $root 'dist/mermaid.esm.min.mjs') -To (Join-Path $dest 'mermaid.esm.min.mjs')
                Copy-Asset -From (Join-Path $root 'dist/chunks/mermaid.esm.min') -To (Join-Path $dest 'chunks/mermaid.esm.min')
                Get-ChildItem -Path $dest -Recurse -Filter '*.map' | Remove-Item -Force
            }
            'highlight' {
                Copy-Asset -From (Join-Path $root 'highlight.min.js') -To (Join-Path $dest 'highlight.min.js')
                Copy-Asset -From (Join-Path $root 'styles/github.min.css') -To (Join-Path $dest 'github.min.css')
                Copy-Asset -From (Join-Path $root 'styles/github-dark.min.css') -To (Join-Path $dest 'github-dark.min.css')
            }
            'katex' {
                Copy-Asset -From (Join-Path $root 'dist/katex.min.js')  -To (Join-Path $dest 'katex.min.js')
                Copy-Asset -From (Join-Path $root 'dist/katex.min.css') -To (Join-Path $dest 'katex.min.css')
                Copy-Asset -From (Join-Path $root 'dist/fonts')         -To (Join-Path $dest 'fonts')
                Copy-Asset -From (Join-Path $root 'dist/contrib/auto-render.min.js') -To (Join-Path $dest 'auto-render.min.js')
                # KaTeX ships every font in three formats; only woff2 is reachable from WebView2.
                Get-ChildItem -Path (Join-Path $dest 'fonts') -Include '*.ttf', '*.woff' -Recurse | Remove-Item -Force
            }
        }

        Write-Host "  installed $($package.Name) -> vendor/$($package.Target)" -ForegroundColor Green
    }

    $size = (Get-ChildItem -Path $vendorDir -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB
    Write-Host ("Done. Vendor bundle is {0:N1} MB." -f $size) -ForegroundColor Cyan
}
finally {
    if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force -ErrorAction SilentlyContinue }
}
