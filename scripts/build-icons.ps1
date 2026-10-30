<#
.SYNOPSIS
  Builds assets\logo.png (1024px, transparent) and assets\AppIcon.ico from assets\logo.svg.

.DESCRIPTION
  Renders the SVG with headless Microsoft Edge on a white and on a black background, then recovers the
  alpha channel from the difference between the two renders. No third-party tools are required.
  Run from any directory:  pwsh -File scripts\build-icons.ps1
#>
param(
    [string]$Edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'assets'
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("daignostic-icons-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $tmp | Out-Null

try {
    $svgUri = 'file:///' + ((Join-Path $assets 'logo.svg') -replace '\\', '/')
    foreach ($bg in 'white', 'black') {
        $html = Join-Path $tmp "$bg.html"
        Set-Content $html "<html><body style='margin:0;background:$bg'><img src='$svgUri' width='1024' height='1024' style='display:block'></body></html>" -Encoding utf8
        & $Edge --headless=new --disable-gpu --hide-scrollbars --window-size=1024,1024 `
            "--screenshot=$(Join-Path $tmp "on-$bg.png")" ('file:///' + ($html -replace '\\', '/')) | Out-Null
    }

    function Read-Pixels([string]$path) {
        $bmp = [System.Drawing.Bitmap]::FromFile($path)
        $rect = New-Object System.Drawing.Rectangle 0, 0, $bmp.Width, $bmp.Height
        $data = $bmp.LockBits($rect, 'ReadOnly', 'Format32bppArgb')
        $bytes = New-Object byte[] ($data.Stride * $bmp.Height)
        [Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
        $bmp.UnlockBits($data)
        $w = $bmp.Width; $h = $bmp.Height
        $bmp.Dispose()
        return @{ Bytes = $bytes; Width = $w; Height = $h }
    }

    $white = Read-Pixels (Join-Path $tmp 'on-white.png')
    $black = Read-Pixels (Join-Path $tmp 'on-black.png')
    $out = New-Object byte[] $white.Bytes.Length

    for ($i = 0; $i -lt $out.Length; $i += 4) {
        $diff = ($white.Bytes[$i] - $black.Bytes[$i]) + ($white.Bytes[$i + 1] - $black.Bytes[$i + 1]) + ($white.Bytes[$i + 2] - $black.Bytes[$i + 2])
        $alpha = 1.0 - ($diff / 765.0)
        if ($alpha -le 0.003) { continue }
        if ($alpha -gt 1) { $alpha = 1.0 }
        for ($c = 0; $c -lt 3; $c++) {
            $v = [Math]::Round($black.Bytes[$i + $c] / $alpha)
            $out[$i + $c] = [byte][Math]::Min(255, $v)
        }
        $out[$i + 3] = [byte][Math]::Round($alpha * 255)
    }

    $full = New-Object System.Drawing.Bitmap $white.Width, $white.Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $rect = New-Object System.Drawing.Rectangle 0, 0, $full.Width, $full.Height
    $data = $full.LockBits($rect, 'WriteOnly', 'Format32bppArgb')
    [Runtime.InteropServices.Marshal]::Copy($out, 0, $data.Scan0, $out.Length)
    $full.UnlockBits($data)
    $full.Save((Join-Path $assets 'logo.png'), [System.Drawing.Imaging.ImageFormat]::Png)

    $sizes = 16, 24, 32, 48, 64, 128, 256
    $pngs = foreach ($s in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = 'HighQualityBicubic'
        $g.PixelOffsetMode = 'HighQuality'
        $g.SmoothingMode = 'HighQuality'
        $g.DrawImage($full, 0, 0, $s, $s)
        $g.Dispose()
        $ms = New-Object IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        , $ms.ToArray()
    }
    $full.Dispose()

    $icoPath = Join-Path $assets 'AppIcon.ico'
    $fs = [IO.File]::Create($icoPath)
    $bw = New-Object IO.BinaryWriter $fs
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]
        $dim = if ($s -ge 256) { 0 } else { $s }
        $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32)
        $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
        $offset += $pngs[$i].Length
    }
    foreach ($p in $pngs) { $bw.Write($p) }
    $bw.Dispose(); $fs.Dispose()
    Write-Host "Wrote $(Join-Path $assets 'logo.png') and $icoPath"
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
