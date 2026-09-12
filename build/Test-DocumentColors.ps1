#Requires -Version 7.0

<#
.SYNOPSIS
    Checks that the document colors agree between the preview stylesheet and the C# that
    writes Word files.

.DESCRIPTION
    Marqora states its document colors once, in webshell/app.css, and the preview, the HTML
    export and the printed page all read them from there. The Word export cannot: it builds a
    .docx without a browser anywhere in the picture, and it has to keep working when the
    preview has not been asked at all - which is what lets a Word export degrade instead of
    failing.

    So four callout colors and the accent are written down a second time, in
    src/PaulTechGuy.MQ.Domain. A second copy drifts, and a drifted copy is the kind of defect
    nobody finds: the preview shows one green and the exported document shows another, and
    neither looks wrong on its own.

    This script is what stops that. It reads both files and reports every disagreement. It is
    the same bargain Test-ButtonStandards.ps1 strikes for the compact button metrics - pushing
    a handful of colors across the WebView bridge would cost more than it saves, but a test
    costs nothing.

    Report only; it never writes. There is nothing here a script could fix without choosing
    which of the two files was right.

.PARAMETER Check
    Exit non-zero when anything disagrees. The form CI wants.

.EXAMPLE
    pwsh ./build/Test-DocumentColors.ps1
    pwsh ./build/Test-DocumentColors.ps1 -Check
#>

[CmdletBinding()]
param(
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$appCss = Join-Path $repoRoot 'webshell/app.css'
$calloutCs = Join-Path $repoRoot 'src/PaulTechGuy.MQ.Domain/CalloutColors.cs'
$accentCs = Join-Path $repoRoot 'src/PaulTechGuy.MQ.Domain/DocumentAccent.cs'

foreach ($required in @($appCss, $calloutCs, $accentCs)) {
    if (-not (Test-Path $required)) {
        Write-Error "Expected to find $required."
        exit 2
    }
}

$cssText = Get-Content -Raw $appCss
$calloutText = Get-Content -Raw $calloutCs
$accentText = Get-Content -Raw $accentCs

<#
    The first declaration wins deliberately: app.css declares each color on :root and then
    again inside the dark-theme block. A document leaving the app is always light - a printed
    page and a Word file are white whatever the window is wearing - so the light value is the
    one that has to match.
#>
function Get-CssColor {
    param([string]$Name)

    if ($cssText -match "--$Name\s*:\s*(#[0-9a-fA-F]{6})") { return $Matches[1].ToLowerInvariant() }
    return $null
}

function Get-CsharpColor {
    param([string]$Text, [string]$Constant)

    if ($Text -match "$Constant\s*=\s*`"(#[0-9a-fA-F]{6})`"") { return $Matches[1].ToLowerInvariant() }
    return $null
}

$pairs = @(
    @{ Css = 'mq-tip';       Constant = 'TipHex';       Text = $calloutText; File = 'CalloutColors.cs'; What = 'the tip callout' }
    @{ Css = 'mq-important'; Constant = 'ImportantHex'; Text = $calloutText; File = 'CalloutColors.cs'; What = 'the important callout' }
    @{ Css = 'mq-warning';   Constant = 'WarningHex';   Text = $calloutText; File = 'CalloutColors.cs'; What = 'the warning callout' }
    @{ Css = 'mq-danger';    Constant = 'CautionHex';   Text = $calloutText; File = 'CalloutColors.cs'; What = 'the caution callout' }
    @{ Css = 'mq-mark';      Constant = 'MarkHex';      Text = $calloutText; File = 'CalloutColors.cs'; What = 'the highlight' }
)

$findings = [System.Collections.Generic.List[object]]::new()

foreach ($pair in $pairs) {
    $css = Get-CssColor $pair.Css
    $code = Get-CsharpColor $pair.Text $pair.Constant

    if ($null -eq $css) {
        $findings.Add("Could not read --$($pair.Css) from webshell/app.css.")
        continue
    }

    if ($null -eq $code) {
        $findings.Add("Could not read $($pair.Constant) from $($pair.File).")
        continue
    }

    if ($css -ne $code) {
        $findings.Add("$($pair.What) is $css in app.css and $code in $($pair.File).")
    }
}

<#
    The accent is the one color that genuinely has a single source: the host posts it to the
    shell, and app.css names no teal of its own. So the check here is the opposite one - that
    app.css has not quietly grown a literal that would override what it is sent.
#>
$accent = Get-CsharpColor $accentText 'LightHex'

if ($null -eq $accent) {
    $findings.Add('Could not read LightHex from DocumentAccent.cs.')
}
elseif ($cssText -match '--mq-accent-screen\s*:\s*#[0-9a-fA-F]{6}') {
    $findings.Add(
        'app.css declares a literal --mq-accent-screen. That color is posted by the host ' +
        "(DocumentAccent.LightHex is $accent); a literal here would win and the two would part company.")
}

if ($findings.Count -eq 0) {
    $count = $pairs.Count
    Write-Host "Document colors agree across app.css and Domain ($count checked)."
    exit 0
}

Write-Host "Document colors: $($findings.Count) disagreement(s)."
Write-Host ''

foreach ($finding in $findings) {
    Write-Host "  $finding"
}

Write-Host ''
Write-Host 'The preview reads webshell/app.css; the Word export reads the C#. Both have to say'
Write-Host 'the same thing, and only a person can decide which one is right.'

if ($Check) { exit 1 }
exit 0
