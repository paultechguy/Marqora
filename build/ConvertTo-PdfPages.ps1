<#
.SYNOPSIS
    Renders each page of a PDF to a PNG, with the PDF engine built into Windows.

.DESCRIPTION
    For looking at what an export produced, and for comparing two PDFs page by page as pictures
    rather than as bytes: a PDF carries dates and ids that differ on every run, so two files
    that look the same never compare equal (docs/CLI-Implementation-Plan.md, WP-M2-01).

    Uses Windows.Data.Pdf, so there is nothing to install. That is a WinRT API, which Windows
    PowerShell 5.1 can call and PowerShell 7 cannot; run it with powershell.exe, not pwsh.

.PARAMETER Path
    The PDF to render.

.PARAMETER OutputFolder
    Where the pages go, as page-01.png, page-02.png and so on. Created if missing. Defaults to a
    folder beside the PDF named after it.

.PARAMETER Width
    The width of each page image in pixels. Defaults to 1224, one and a half times a Letter
    page at 96 dpi: enough to read body text and see a diagram's labels.

.EXAMPLE
    powershell -NoProfile -File build\ConvertTo-PdfPages.ps1 -Path out\cheatsheet.pdf
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Path,

    [string] $OutputFolder,

    [int] $Width = 1224
)

$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSEdition -ne 'Desktop') {
    throw 'Windows.Data.Pdf is a WinRT API, which only Windows PowerShell 5.1 can call. Run this with powershell.exe.'
}

$pdf = (Resolve-Path -LiteralPath $Path).ProviderPath

if (-not $OutputFolder) {
    $OutputFolder = Join-Path (Split-Path -Parent $pdf) ([IO.Path]::GetFileNameWithoutExtension($pdf))
}

New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null
$OutputFolder = (Resolve-Path -LiteralPath $OutputFolder).ProviderPath

Add-Type -AssemblyName System.Runtime.WindowsRuntime

# WinRT operations are awaited through the .NET task bridge: AsTask, then Wait.
$asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() |
    Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 }

$asTaskOfResult = $asTask | Where-Object { $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
$asTaskOfAction = $asTask | Where-Object { $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction' } | Select-Object -First 1

function Wait-Operation($operation, [Type] $resultType) {
    $task = $asTaskOfResult.MakeGenericMethod($resultType).Invoke($null, @($operation))
    $task.Wait() | Out-Null
    $task.Result
}

function Wait-Action($action) {
    $task = $asTaskOfAction.Invoke($null, @($action))
    $task.Wait() | Out-Null
}

[void][Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
[void][Windows.Storage.StorageFolder, Windows.Storage, ContentType = WindowsRuntime]
[void][Windows.Storage.Streams.IRandomAccessStream, Windows.Storage.Streams, ContentType = WindowsRuntime]
[void][Windows.Data.Pdf.PdfDocument, Windows.Data.Pdf, ContentType = WindowsRuntime]
[void][Windows.Data.Pdf.PdfPageRenderOptions, Windows.Data.Pdf, ContentType = WindowsRuntime]

$file = Wait-Operation ([Windows.Storage.StorageFile]::GetFileFromPathAsync($pdf)) ([Windows.Storage.StorageFile])
$document = Wait-Operation ([Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($file)) ([Windows.Data.Pdf.PdfDocument])
$folder = Wait-Operation ([Windows.Storage.StorageFolder]::GetFolderFromPathAsync($OutputFolder)) ([Windows.Storage.StorageFolder])

$digits = [Math]::Max(2, ([string]$document.PageCount).Length)

for ($index = 0; $index -lt $document.PageCount; $index++) {
    $page = $document.GetPage($index)

    try {
        $options = New-Object Windows.Data.Pdf.PdfPageRenderOptions
        $options.DestinationWidth = $Width

        $name = 'page-{0}.png' -f ($index + 1).ToString().PadLeft($digits, '0')
        $target = Wait-Operation ($folder.CreateFileAsync($name, [Windows.Storage.CreationCollisionOption]::ReplaceExisting)) ([Windows.Storage.StorageFile])
        $stream = Wait-Operation ($target.OpenAsync([Windows.Storage.FileAccessMode]::ReadWrite)) ([Windows.Storage.Streams.IRandomAccessStream])

        try {
            Wait-Action ($page.RenderToStreamAsync($stream, $options))
        }
        finally {
            $stream.Dispose()
        }

        Join-Path $OutputFolder $name
    }
    finally {
        $page.Dispose()
    }
}
