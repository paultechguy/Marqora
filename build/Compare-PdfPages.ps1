<#
.SYNOPSIS
    Compares two PDFs as pictures, page by page, and says whether they look the same.

.DESCRIPTION
    Two exports of the same document never compare equal as bytes: a PDF carries the moment it
    was made. Compared as pictures they can, so each page of both is rendered to a PNG by
    ConvertTo-PdfPages.ps1, and the pairs are compared pixel by pixel
    (docs/CLI-Implementation-Plan.md, WP-M2-01).

    A pixel differs when any of its color channels differs by more than -ChannelTolerance out of
    255. A page differs when more than -PixelTolerance of its pixels do. The defaults are small:
    one changed word on a page is several times the pixel tolerance, while the edge shading of
    the same drawing rendered twice stays inside the channel tolerance. The same PDF rendered
    twice gives identical pages, so any tolerance is for real exports, not for the renderer.

    Writes one line of JSON to the pipeline, for Compare-ExportBaseline.ps1 to read:
    { same, baselinePages, freshPages, pages: [ { page, differing, total, fraction } ] }

    Windows PowerShell 5.1 only, for the reason ConvertTo-PdfPages.ps1 gives, and because
    System.Drawing is there without a reference to find.

.PARAMETER Baseline
    The PDF that is right.

.PARAMETER Fresh
    The PDF to check against it.

.PARAMETER WorkFolder
    Where the page images go, in a "baseline" and a "fresh" folder. Kept, so a page that differs
    can be looked at. Defaults to a new folder under the temp directory.

.PARAMETER ChannelTolerance
    How far one color channel may move, 0 to 255, before the pixel counts as different.

.PARAMETER PixelTolerance
    The fraction of a page's pixels that may differ before the page does.

.EXAMPLE
    powershell -NoProfile -File build\Compare-PdfPages.ps1 -Baseline a.pdf -Fresh b.pdf
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Baseline,

    [Parameter(Mandatory = $true)]
    [string] $Fresh,

    [string] $WorkFolder,

    [int] $ChannelTolerance = 16,

    [double] $PixelTolerance = 0.0005
)

$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSEdition -ne 'Desktop') {
    throw 'Run this with powershell.exe: the pages are rendered and compared in Windows PowerShell 5.1.'
}

if (-not $WorkFolder) {
    $WorkFolder = Join-Path ([IO.Path]::GetTempPath()) ('pdf-compare-' + [Guid]::NewGuid().ToString('N'))
}

$render = Join-Path $PSScriptRoot 'ConvertTo-PdfPages.ps1'
$baselinePages = @(& $render -Path $Baseline -OutputFolder (Join-Path $WorkFolder 'baseline'))
$freshPages = @(& $render -Path $Fresh -OutputFolder (Join-Path $WorkFolder 'fresh'))

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class PageImages
{
    // The number of pixels whose channels differ by more than the tolerance, or -1 when the two
    // images are not the same size, which a page that moved by a line would not be.
    public static long Differing(string first, string second, int tolerance, out long total)
    {
        using (var a = new Bitmap(first))
        using (var b = new Bitmap(second))
        {
            total = (long)a.Width * a.Height;

            if (a.Width != b.Width || a.Height != b.Height)
            {
                return -1;
            }

            var area = new Rectangle(0, 0, a.Width, a.Height);
            BitmapData da = a.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData db = b.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                int stride = da.Stride;
                var rowA = new byte[stride];
                var rowB = new byte[stride];
                long differing = 0;

                for (int y = 0; y < a.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(da.Scan0, y * stride), rowA, 0, stride);
                    Marshal.Copy(IntPtr.Add(db.Scan0, y * db.Stride), rowB, 0, stride);

                    for (int x = 0; x < a.Width * 4; x += 4)
                    {
                        if (Math.Abs(rowA[x] - rowB[x]) > tolerance
                            || Math.Abs(rowA[x + 1] - rowB[x + 1]) > tolerance
                            || Math.Abs(rowA[x + 2] - rowB[x + 2]) > tolerance
                            || Math.Abs(rowA[x + 3] - rowB[x + 3]) > tolerance)
                        {
                            differing++;
                        }
                    }
                }

                return differing;
            }
            finally
            {
                a.UnlockBits(da);
                b.UnlockBits(db);
            }
        }
    }
}
'@

$pages = @()
$same = $baselinePages.Count -eq $freshPages.Count

for ($index = 0; $index -lt [Math]::Min($baselinePages.Count, $freshPages.Count); $index++) {
    $total = [long] 0
    $differing = [PageImages]::Differing($baselinePages[$index], $freshPages[$index], $ChannelTolerance, [ref] $total)
    $fraction = if ($differing -lt 0) { 1.0 } else { $differing / [double] $total }

    if ($fraction -gt $PixelTolerance) {
        $same = $false
    }

    $pages += [ordered] @{
        page      = $index + 1
        differing = $differing
        total     = $total
        fraction  = [Math]::Round($fraction, 6)
    }
}

[ordered] @{
    same          = $same
    baselinePages = $baselinePages.Count
    freshPages    = $freshPages.Count
    workFolder    = $WorkFolder
    pages         = $pages
} | ConvertTo-Json -Depth 4 -Compress
