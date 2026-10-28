#Requires -Version 7.0
<#
.SYNOPSIS
    Compares a fresh set of exports with the CLI baseline in tests/baselines/cli, and says what
    differs.

.DESCRIPTION
    The baseline is what Marqora 1.0.13 exported before the CLI work changed the app
    (docs/CLI-Implementation-Plan.md, WP-M0-05). Each move in M2 is proved by exporting the same
    set again and finding no difference (WP-M2-01, CLI-ARC-06).

    "No difference" has to allow for what changes on every export by design, and nothing else:

    - Word: the creation and change times in the package properties, the random name the
      package gives that part, the random ids it gives every relationship, and w:rsid values.
      Ids are compared by what they point at rather than what they are called.
    - PDF: /CreationDate, /ModDate, a trailer /ID and XMP dates. When the bytes still differ
      after that, every page of both is rendered and compared as a picture, within a tolerance
      (Compare-PdfPages.ps1).
    - Folio: the export time in folio-manifest.json and in the single-file page's sources, the
      dated name it carries as its folder and its title, and zip entry times. Zip entry order
      is compared, because it is the reading order.
    - Diagrams: the numbers that count across the whole session, in each diagram's id
      (mq-diagram-N) and a sequence diagram's actors (actorN, root-N). Numbers are compared by
      the order they first appear in. And what mermaid leaves to chance in every drawing, in
      HTML and SVG: rough.js's control points (the endpoints are kept), a gantt chart's "today" line, a
      gitGraph commit's generated id, and the random part of an Iconify icon's ids.
    - Preferences: the export time in preferences.json.

    Everything else is compared exactly: HTML and markdown as text, pictures as bytes.

    Which exports there are comes from the baseline README's checklist, the same list
    Get-BaselineHashes.ps1 records. The fresh set uses the same names.

    Writes a report and exits 0 when nothing differs, 1 when something does. Stops before
    comparing if the baseline itself no longer matches MANIFEST.sha256.

.PARAMETER Fresh
    The folder holding the new exports, named as in the baseline README.

.PARAMETER Baseline
    The folder to compare with. Defaults to tests/baselines/cli.

.PARAMETER Name
    Only these exports, by their checklist names; wildcards allowed. Defaults to all of them.

.PARAMETER Report
    Where the report goes, as markdown. Defaults to <Fresh>.comparison.md beside the folder.

.PARAMETER ChannelTolerance
    For PDF pages: how far a color channel may move, out of 255, before the pixel differs.

.PARAMETER PixelTolerance
    For PDF pages: the fraction of a page's pixels that may differ before the page does.

.EXAMPLE
    pwsh ./build/Compare-ExportBaseline.ps1 -Fresh C:\exports\after-M2-02
    pwsh ./build/Compare-ExportBaseline.ps1 -Fresh C:\exports\after-M2-02 -Name *.html, folio.*
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Fresh,

    [string] $Baseline,

    [string[]] $Name = @('*'),

    [string] $Report,

    [int] $ChannelTolerance = 16,

    [double] $PixelTolerance = 0.0005
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'BaselineCommon.ps1')

$Fresh = (Resolve-Path -LiteralPath $Fresh).ProviderPath
$Baseline = if ($Baseline) { (Resolve-Path -LiteralPath $Baseline).ProviderPath } else { $baselineRoot }

# The baseline is checked against its manifest first. A save dialog that remembered this folder
# once wrote a fresh export over a baseline file, and the comparison then reported the stray as
# matching itself. A different -Baseline, such as a second fresh set, has no manifest to check.
if ([IO.Path]::GetFullPath($Baseline) -eq [IO.Path]::GetFullPath($baselineRoot)) {
    & (Join-Path $PSScriptRoot 'Get-BaselineHashes.ps1') -Check
    if ($LASTEXITCODE -ne 0) {
        throw 'The baseline is not as recorded; restore it before comparing anything with it.'
    }
}

if (-not $Report) {
    $Report = "$($Fresh.TrimEnd('\', '/')).comparison.md"
}

$work = Join-Path ([IO.Path]::GetTempPath()) ('export-compare-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null

$textExtensions = '.html', '.htm', '.md', '.json', '.txt', '.css', '.js', '.svg', '.xml', '.rels'

# ---------------------------------------------------------------- what is masked

function Get-MaskedFolioPayload([string] $html) {
    # The single-file Folio's sources are base64 JSON, so the time inside them cannot be masked
    # without decoding them first.
    [regex]::Replace($html, '(<script type="application/vnd\.marqora\.folio\+json">)\s*([A-Za-z0-9+/=\s]+?)\s*(</script>)', {
            param($m)
            $json = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(($m.Groups[2].Value -replace '\s', '')))
            $m.Groups[1].Value + "`n" + (Get-MaskedTime $json) + "`n" + $m.Groups[3].Value
        })
}

function Get-MaskedTime([string] $json) {
    $json -replace '("exportedUtc"\s*:\s*)"[^"]*"', '$1"<masked>"'
}

# Some ids in a diagram come from counters that run for the whole session, so the same document
# exported twice numbers them from wherever the counter stood: each diagram's own id, and a
# sequence diagram's actors. The numbers are replaced in the order they first appear, which
# keeps two apart and still finds one missing. A sequence diagram's stick figure counts its own
# parts too (found 2026-10-28, WP-M2-09: 6 in the app's session, 0 in the CLI's fresh browser).
$sessionCounters = 'mq-diagram-', 'actor', 'root-', 'actor-man-torso', 'actor-man-arms'

function Get-RenumberedDiagrams([string] $text) {
    foreach ($prefix in $sessionCounters) {
        $seen = @{}
        $text = [regex]::Replace($text, "\b$([regex]::Escape($prefix))(\d+)", {
                param($m)
                $n = $m.Groups[1].Value
                if (-not $seen.ContainsKey($n)) { $seen[$n] = $seen.Count + 1 }
                "$prefix#$($seen[$n])"
            })
    }
    $text
}

# What a diagram draws differently every time, whatever the document says (found 2026-10-28, the
# first comparison of a Folio's single page, which renders every document afresh):
# - mermaid draws a shape's straight sides through rough.js, which places each segment's two
#   control points at random along the side. The endpoints are the geometry and are kept;
#   d3's own curves are written with commas only, and are not touched.
# - a gantt chart's "today" line stands where today is.
# - a gitGraph commit with no id is given a random one, and a state diagram's divider too.
# - an Iconify icon's ids carry a random prefix before their counter; renumbered as they appear.
function Get-MaskedChance([string] $text) {
    $n = '-?[\d.]+(?:e[-+]?\d+)?'
    $text = $text -replace "C$n $n, $n $n, ($n $n)", 'C<c>, <c>, $1'
    $text = $text -replace '<line x1="[\d.]+" x2="[\d.]+"( y1="[\d.]+" y2="[\d.]+" class="today")', '<line x1="<today>" x2="<today>"$1'
    $text = $text -replace '\b(\d+)-[0-9a-f]{7}\b', '$1-<commit>'
    $text = $text -replace 'state-id-[a-z0-9]+-', 'state-id-<r>-'

    $seen = @{}
    [regex]::Replace($text, 'IconifyId[0-9a-f]+', {
            param($m)
            if (-not $seen.ContainsKey($m.Value)) { $seen[$m.Value] = $seen.Count + 1 }
            "IconifyId#$($seen[$m.Value])"
        })
}

function Get-NormalText([string] $path) {
    $text = [IO.File]::ReadAllText($path)

    switch -Regex ([IO.Path]::GetFileName($path)) {
        '\.html?$' { $text = Get-MaskedChance (Get-MaskedFolioPayload $text) }
        '\.svg$' { $text = Get-MaskedChance $text }
        '\.json$' { $text = Get-MaskedTime $text }
        '\.(xml|rels|psmdcp)$' {
            $text = $text -replace '\s+w:rsid\w*="[0-9A-Fa-f]+"', ''
            $text = $text -replace '(<dcterms:(created|modified)[^>]*>)[^<]*', '$1<masked>'
        }
    }

    # A Folio is named for the second it was shared, in its title as well as its folder.
    $text = $text -replace 'Folio-\d{4}-\d{2}-\d{2}-\d{6}', 'Folio-<stamp>'
    Get-RenumberedDiagrams $text
}

# A Word package names its relationships at random. Each id is replaced, everywhere in the
# package, by what it points at, so two packages relate the same parts the same way or differ.
function Set-StableWordPackage([string] $folder) {
    foreach ($part in Get-ChildItem -LiteralPath $folder -Recurse -File -Filter '*.psmdcp') {
        Move-Item -LiteralPath $part.FullName -Destination (Join-Path $part.DirectoryName 'core-properties.psmdcp')
    }

    $ids = @{}

    foreach ($rels in Get-ChildItem -LiteralPath $folder -Recurse -File -Filter '*.rels') {
        $xml = [IO.File]::ReadAllText($rels.FullName)

        foreach ($m in [regex]::Matches($xml, '<Relationship\b[^>]*>')) {
            $target = [regex]::Match($m.Value, 'Target="([^"]*)"').Groups[1].Value
            $id = [regex]::Match($m.Value, 'Id="([^"]*)"').Groups[1].Value
            $ids[$id] = 'rel:' + ($target -replace '[0-9a-f]{32}\.psmdcp$', 'core-properties.psmdcp')
        }
    }

    foreach ($file in Get-ChildItem -LiteralPath $folder -Recurse -File | Where-Object { $_.Extension -in '.xml', '.rels' }) {
        $text = [IO.File]::ReadAllText($file.FullName)
        $text = [regex]::Replace($text, '="([^"]+)"', {
                param($m)
                $value = $m.Groups[1].Value
                if ($ids.ContainsKey($value)) { '="' + $ids[$value] + '"' } else { $m.Value }
            })
        $text = $text -replace '[0-9a-f]{32}\.psmdcp', 'core-properties.psmdcp'
        [IO.File]::WriteAllText($file.FullName, $text)
    }
}

function Get-MaskedPdf([string] $path) {
    # Latin-1 maps every byte to one character and back, so the rest of the file is untouched.
    $text = [Text.Encoding]::Latin1.GetString([IO.File]::ReadAllBytes($path))
    $text = $text -replace '/(CreationDate|ModDate)\s*\(D:[^)]*\)', '/$1(<masked>)'
    $text = $text -replace '/ID\s*\[\s*<[0-9A-Fa-f]*>\s*<[0-9A-Fa-f]*>\s*\]', '/ID[<masked>]'
    $text = $text -replace '(<xmp:(CreateDate|ModifyDate|MetadataDate)>)[^<]*', '$1<masked>'
    $text -replace '(<xmpMM:(DocumentID|InstanceID)>)[^<]*', '$1<masked>'
}

# ---------------------------------------------------------------- comparing

function Compare-Text([string] $baseline, [string] $fresh, [string] $label) {
    $a = Get-NormalText $baseline
    $b = Get-NormalText $fresh

    if ($a -ceq $b) {
        return @()
    }

    $linesA = $a -split "`n"
    $linesB = $b -split "`n"
    $line = 0

    while ($line -lt [Math]::Min($linesA.Count, $linesB.Count) -and $linesA[$line] -ceq $linesB[$line]) {
        $line++
    }

    $lineA = if ($line -lt $linesA.Count) { $linesA[$line].TrimEnd("`r") } else { $null }
    $lineB = if ($line -lt $linesB.Count) { $linesB[$line].TrimEnd("`r") } else { $null }

    # Where in the line, because a Word part is one line of many thousand characters.
    $column = 0
    if ($null -ne $lineA -and $null -ne $lineB) {
        while ($column -lt [Math]::Min($lineA.Length, $lineB.Length) -and $lineA[$column] -ceq $lineB[$column]) {
            $column++
        }
    }

    $from = [Math]::Max(0, $column - 40)
    $shown = {
        param($s)
        if ($null -eq $s) { return '(end of file)' }
        $t = $s.Substring([Math]::Min($from, $s.Length))
        $t = if ($t.Length -gt 120) { $t.Substring(0, 120) + '...' } else { $t }
        if ($from -gt 0) { '...' + $t } else { $t }
    }

    @("${label}: differs at line $($line + 1), column $($column + 1) ($($linesA.Count) lines in the baseline, $($linesB.Count) fresh)",
        "    baseline: $(& $shown $lineA)",
        "    fresh:    $(& $shown $lineB)")
}

function Compare-Bytes([string] $baseline, [string] $fresh, [string] $label) {
    if ((Get-FileHash -LiteralPath $baseline).Hash -eq (Get-FileHash -LiteralPath $fresh).Hash) {
        return @()
    }

    @("${label}: the bytes differ ($((Get-Item -LiteralPath $baseline).Length) in the baseline, $((Get-Item -LiteralPath $fresh).Length) fresh)")
}

function Compare-File([string] $baseline, [string] $fresh, [string] $label) {
    $extension = [IO.Path]::GetExtension($baseline).ToLowerInvariant()

    if ($extension -in $textExtensions -or $extension -eq '.psmdcp') {
        return Compare-Text $baseline $fresh $label
    }

    Compare-Bytes $baseline $fresh $label
}

# The folder form of a Folio is one folder named for the moment it was made.
function Get-FolioFolder([string] $folder) {
    $children = @(Get-ChildItem -LiteralPath $folder -Force)

    if ($children.Count -eq 1 -and $children[0].PSIsContainer -and $children[0].Name -match '^Folio-\d{4}-\d{2}-\d{2}-\d{6}$') {
        return $children[0].FullName
    }

    $folder
}

function Compare-Folder([string] $baseline, [string] $fresh, [string] $label) {
    $relative = {
        param($root)
        @(Get-ChildItem -LiteralPath $root -Recurse -File | ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName) -replace '\\', '/' } | Sort-Object)
    }

    $inBaseline = & $relative $baseline
    $inFresh = & $relative $fresh
    $differences = @()

    foreach ($path in $inBaseline | Where-Object { $_ -notin $inFresh }) { $differences += "${label}: $path is missing" }
    foreach ($path in $inFresh | Where-Object { $_ -notin $inBaseline }) { $differences += "${label}: $path is new" }

    foreach ($path in $inBaseline | Where-Object { $_ -in $inFresh }) {
        $differences += @(Compare-File (Join-Path $baseline $path) (Join-Path $fresh $path) "$label/$path")
    }

    $differences
}

function Expand-Package([string] $path, [string] $as) {
    $target = Join-Path $work $as
    [IO.Compression.ZipFile]::ExtractToDirectory($path, $target)
    $target
}

function Get-EntryOrder([string] $path) {
    $zip = [IO.Compression.ZipFile]::OpenRead($path)
    try { @($zip.Entries | ForEach-Object FullName) } finally { $zip.Dispose() }
}

function Compare-Export([string] $name) {
    $baseline = Join-Path $Baseline $name
    $fresh = Join-Path $Fresh $name
    $key = [Guid]::NewGuid().ToString('N')

    if (-not (Test-Path -LiteralPath $fresh)) {
        return [pscustomobject] @{ Name = $name; Result = 'missing'; Details = @("$name is not in the fresh set") }
    }

    $details = @()

    switch -Regex ($name) {
        '\.folder$' {
            $details = @(Compare-Folder (Get-FolioFolder $baseline) (Get-FolioFolder $fresh) $name)
        }
        '\.zip$' {
            $orderA = (Get-EntryOrder $baseline) -join "`n"
            $orderB = (Get-EntryOrder $fresh) -join "`n"
            if ($orderA -cne $orderB) { $details += "${name}: the entries are in a different order" }
            $details += @(Compare-Folder (Expand-Package $baseline "$key-a") (Expand-Package $fresh "$key-b") $name)
        }
        '\.docx$' {
            $a = Expand-Package $baseline "$key-a"
            $b = Expand-Package $fresh "$key-b"
            Set-StableWordPackage $a
            Set-StableWordPackage $b
            $details = @(Compare-Folder $a $b $name)
        }
        '\.pdf$' {
            if ((Get-MaskedPdf $baseline) -ceq (Get-MaskedPdf $fresh)) {
                break
            }

            $pages = Join-Path $work "$key-pages"
            $json = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Compare-PdfPages.ps1') `
                -Baseline $baseline -Fresh $fresh -WorkFolder $pages `
                -ChannelTolerance $ChannelTolerance -PixelTolerance $PixelTolerance

            if ($LASTEXITCODE -ne 0) {
                throw "Compare-PdfPages.ps1 failed on $name."
            }

            $result = $json | ConvertFrom-Json

            if ($result.baselinePages -ne $result.freshPages) {
                $details += "${name}: $($result.baselinePages) pages in the baseline, $($result.freshPages) fresh"
            }

            foreach ($page in @($result.pages) | Where-Object { $_.fraction -gt $PixelTolerance }) {
                $what = if ($page.differing -lt 0) { 'a different size' } else { '{0:P3} of its pixels differ' -f $page.fraction }
                $details += "${name}: page $($page.page), $what"
            }

            if (-not $details) {
                Remove-Item -LiteralPath $pages -Recurse -Force
                return [pscustomobject] @{ Name = $name; Result = 'same as pictures'; Details = @('the bytes differ beyond the dates; every page is within tolerance') }
            }

            $details += "    page images: $pages"
        }
        default {
            $details = @(Compare-File $baseline $fresh $name)
        }
    }

    $result = if ($details) { 'DIFFERENT' } else { 'same' }
    [pscustomobject] @{ Name = $name; Result = $result; Details = $details }
}

# ---------------------------------------------------------------- the run

Add-Type -AssemblyName System.IO.Compression.FileSystem

$names = @(Get-BaselineExportNames | Where-Object { $n = $_; @($Name | Where-Object { $n -like $_ }).Count -gt 0 })

if (-not $names) {
    throw "No export in the baseline checklist matches $($Name -join ', ')."
}

try {
    $results = foreach ($n in $names) {
        Write-Host ("  {0,-40}" -f $n) -NoNewline
        $r = Compare-Export $n
        Write-Host $r.Result -ForegroundColor $(if ($r.Result -in 'same', 'same as pictures') { 'Green' } else { 'Red' })
        $r
    }
}
finally {
    # The page images of a PDF that differs are kept for looking at; everything else goes.
    Get-ChildItem -LiteralPath $work -Directory | Where-Object { $_.Name -notlike '*-pages' } | Remove-Item -Recurse -Force

    if (-not (Get-ChildItem -LiteralPath $work)) {
        Remove-Item -LiteralPath $work
    }
}

$bad = @($results | Where-Object { $_.Result -notin 'same', 'same as pictures' })

$lines = @(
    '# Export comparison'
    ''
    "- Baseline: ``$Baseline``"
    "- Fresh: ``$Fresh``"
    "- Compared: $($results.Count) exports, $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
    "- PDF tolerance: a channel may move by $ChannelTolerance of 255; a page may have $($PixelTolerance.ToString('P3')) of its pixels differ"
    ''
    '| Export | Result |'
    '| --- | --- |'
)
$lines += foreach ($r in $results) { "| ``$($r.Name)`` | $($r.Result) |" }

if ($bad) {
    $lines += ''
    $lines += '## Differences'

    foreach ($r in $bad) {
        $lines += ''
        $lines += "### ``$($r.Name)``"
        $lines += ''
        $lines += '```text'
        $lines += $r.Details
        $lines += '```'
    }
}

[IO.File]::WriteAllText($Report, (($lines -join "`n") + "`n"), [Text.UTF8Encoding]::new($false))

Write-Host ''
if ($bad) {
    Write-Host "$($bad.Count) of $($results.Count) exports differ. Report: $Report" -ForegroundColor Red
    exit 1
}

Write-Host "No difference in $($results.Count) exports. Report: $Report" -ForegroundColor Green
exit 0
