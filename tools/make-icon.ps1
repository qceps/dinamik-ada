# Uygulama simgesini (PNG sıkıştırmalı çok boyutlu .ico) üretir.
# Kullanım: powershell -File tools\make-icon.ps1 -Out src\DynamicIsland.App\Assets\app.ico
param([string]$Out = "$PSScriptRoot\..\src\DynamicIsland.App\Assets\app.ico")

Add-Type -AssemblyName System.Drawing
$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$pngs = @()

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    $w = $s * 0.94; $h = $s * 0.52
    $x = ($s - $w) / 2; $y = ($s - $h) / 2
    $r = $h
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x, $y, $r, $r, 90, 180)
    $path.AddArc($x + $w - $r, $y, $r, $r, 270, 180)
    $path.CloseFigure()

    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 12, 12, 12))), $path)
    $penW = [Math]::Max(1.0, $s / 24.0)
    $g.DrawPath((New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 210, 210, 210)), $penW), $path)

    # Sol: yeşil daire (kapak yerine)
    $d = $h * 0.56
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 30, 215, 96))), $x + $h * 0.24, $y + ($h - $d) / 2, $d, $d)

    # Sağ: üç çubuk
    $bw = [Math]::Max(1.0, $s * 0.055); $gap = $bw * 0.9
    $heights = 0.45, 0.75, 0.55
    $bx = $x + $w - $h * 0.30 - 3 * $bw - 2 * $gap
    for ($i = 0; $i -lt 3; $i++) {
        $bh = $h * $heights[$i]
        $g.FillRectangle([System.Drawing.Brushes]::White, [single]($bx + $i * ($bw + $gap)), [single]($y + ($h - $bh) / 2), [single]$bw, [single]$bh)
    }

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $pngs += , $ms.ToArray()
}

$fs = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush()
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
[System.IO.File]::WriteAllBytes($Out, $fs.ToArray())
Write-Output "Simge yazıldı: $Out ($($fs.Length) bayt)"
