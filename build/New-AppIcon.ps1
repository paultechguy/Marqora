<#
.SYNOPSIS
    Builds Assets\MarqoraLogo.ico from Assets\MarqoraLogo.png.

.DESCRIPTION
    The icon is a derived artifact, not artwork in its own right. Drawing it by hand in an
    icon editor means it can drift from the logo the app draws, which is exactly how a
    stale icon survives a logo change unnoticed.

    Windows needs a real .ico in two places that will not take a PNG: the ApplicationIcon
    the compiler stamps into the executable, which is what Explorer and the taskbar read,
    and AppWindow.SetIcon for the window itself. So the .ico stays, and this rebuilds it.

    Scaling runs through WPF rather than GDI+ because WPF composites in premultiplied
    alpha. Resampling straight ARGB blends the fully transparent pixels outside the circle,
    whose colour is black, into the edge, and the 16px frame comes out with a dark fringe.

.EXAMPLE
    pwsh .\build\New-AppIcon.ps1
#>
[CmdletBinding()]
param(
    [string] $Source      = (Join-Path $PSScriptRoot '..\src\PaulTechGuy.MQ.App\Assets\MarqoraLogo.png'),
    [string] $Destination = (Join-Path $PSScriptRoot '..\src\PaulTechGuy.MQ.App\Assets\MarqoraLogo.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

$Source      = [IO.Path]::GetFullPath($Source)
$Destination = [IO.Path]::GetFullPath($Destination)

if (-not (Test-Path $Source)) { throw "Source image not found: $Source" }

# 256 is stored as PNG, the rest as bitmaps. That split is what Windows itself produces and
# keeps the file small without relying on PNG frames being understood at every size.
$sizes = 16, 24, 32, 48, 64, 128, 256

$frame = [Windows.Media.Imaging.BitmapDecoder]::Create(
    [Uri] $Source, 'None', 'OnLoad').Frames[0]

Write-Host "source: $($frame.PixelWidth)x$($frame.PixelHeight)  ->  $Destination"

function Resize-Frame([int] $size) {
    $visual = [Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    [Windows.Media.RenderOptions]::SetBitmapScalingMode($visual, 'HighQuality')
    $context.DrawImage($frame, [Windows.Rect]::new(0, 0, $size, $size))
    $context.Close()

    $rendered = [Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, 'Pbgra32')
    $rendered.Render($visual)

    # Straight BGRA: an icon's bitmap frames are not premultiplied.
    [Windows.Media.Imaging.FormatConvertedBitmap]::new($rendered, [Windows.Media.PixelFormats]::Bgra32, $null, 0)
}

function Get-PngBytes($bitmap) {
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    $encoder.Save($stream)
    $stream.ToArray()
}

function Get-BmpBytes($bitmap, [int] $size) {
    $stride = $size * 4
    $pixels = [byte[]]::new($stride * $size)
    $bitmap.CopyPixels($pixels, $stride, 0)

    $out = [IO.MemoryStream]::new()
    $w = [IO.BinaryWriter]::new($out)

    # BITMAPINFOHEADER. biHeight is doubled: the header describes the colour bitmap and the
    # legacy AND mask together, even though a 32bpp icon carries its transparency in alpha.
    $w.Write([uint32] 40); $w.Write([int32] $size); $w.Write([int32] ($size * 2))
    $w.Write([uint16] 1);  $w.Write([uint16] 32);   $w.Write([uint32] 0)
    $w.Write([uint32] ($stride * $size))
    $w.Write([int32] 0); $w.Write([int32] 0); $w.Write([uint32] 0); $w.Write([uint32] 0)

    # Bottom-up, which is how a DIB stores its rows.
    for ($y = $size - 1; $y -ge 0; $y--) { $w.Write($pixels, $y * $stride, $stride) }

    # AND mask: one bit per pixel, rows padded to 4 bytes, set where the pixel is transparent.
    $maskStride = [math]::Ceiling($size / 32) * 4
    for ($y = $size - 1; $y -ge 0; $y--) {
        $row = [byte[]]::new($maskStride)
        for ($x = 0; $x -lt $size; $x++) {
            if ($pixels[($y * $stride) + ($x * 4) + 3] -eq 0) {
                $row[[math]::Floor($x / 8)] = $row[[math]::Floor($x / 8)] -bor (0x80 -shr ($x % 8))
            }
        }
        $w.Write($row, 0, $maskStride)
    }

    $w.Flush()
    $out.ToArray()
}

$images = foreach ($size in $sizes) {
    $bitmap = Resize-Frame $size
    $bytes = if ($size -eq 256) { Get-PngBytes $bitmap } else { Get-BmpBytes $bitmap $size }
    Write-Host ("  {0,3}px  {1,7:N0} bytes  {2}" -f $size, $bytes.Length, $(if ($size -eq 256) { 'png' } else { 'bmp' }))
    [pscustomobject]@{ Size = $size; Bytes = $bytes }
}

$file = [IO.MemoryStream]::new()
$writer = [IO.BinaryWriter]::new($file)

# ICONDIR, then one ICONDIRENTRY per image, then the images themselves.
$writer.Write([uint16] 0); $writer.Write([uint16] 1); $writer.Write([uint16] $images.Count)

$offset = 6 + (16 * $images.Count)
foreach ($image in $images) {
    # 256 is written as 0: the field is a single byte.
    $writer.Write([byte] ($image.Size % 256)); $writer.Write([byte] ($image.Size % 256))
    $writer.Write([byte] 0); $writer.Write([byte] 0)
    $writer.Write([uint16] 1); $writer.Write([uint16] 32)
    $writer.Write([uint32] $image.Bytes.Length); $writer.Write([uint32] $offset)
    $offset += $image.Bytes.Length
}

foreach ($image in $images) { $writer.Write($image.Bytes, 0, $image.Bytes.Length) }
$writer.Flush()

[IO.File]::WriteAllBytes($Destination, $file.ToArray())
Write-Host "wrote $($file.Length) bytes"
