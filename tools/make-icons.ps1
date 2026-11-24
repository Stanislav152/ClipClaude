# Генерирует src/ClipClaude/Resources/app.ico и busy.ico (16/20/24/32/48/256, PNG внутри ICO).
# Запуск: powershell -NoProfile -ExecutionPolicy Bypass -File tools\make-icons.ps1
Add-Type -AssemblyName System.Drawing

$outDir = Join-Path $PSScriptRoot "..\src\ClipClaude\Resources"
New-Item -ItemType Directory -Force $outDir | Out-Null
$sizes = 16, 20, 24, 32, 48, 256

function New-Frame([int]$size, [bool]$busy) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(1, [int]($size * 0.04))
    $rect = New-Object System.Drawing.RectangleF $pad, $pad, ($size - 2 * $pad), ($size - 2 * $pad)
    $r = $rect.Width * 0.24
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.X, $rect.Y, 2 * $r, 2 * $r, 180, 90)
    $path.AddArc($rect.Right - 2 * $r, $rect.Y, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($rect.Right - 2 * $r, $rect.Bottom - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()

    $color = if ($busy) { [System.Drawing.Color]::FromArgb(255, 47, 111, 191) } else { [System.Drawing.Color]::FromArgb(255, 204, 102, 68) }
    $g.FillPath((New-Object System.Drawing.SolidBrush $color), $path)
    # тонкая светлая обводка — видна на тёмной панели задач
    $penW = [Math]::Max(1, $size / 32)
    $g.DrawPath((New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(110, 255, 255, 255)), $penW), $path)

    $white = [System.Drawing.Brushes]::White
    if ($busy) {
        $d = $size * 0.16
        $y = $size / 2 - $d / 2
        foreach ($cx in 0.25, 0.5, 0.75) { $g.FillEllipse($white, $size * $cx - $d / 2, $y, $d, $d) }
    } else {
        # «Aa» с галочкой — проверка текста
        $fontSize = $size * 0.50
        $font = New-Object System.Drawing.Font "Segoe UI", $fontSize, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
        $fmt = New-Object System.Drawing.StringFormat
        $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
        $textRect = New-Object System.Drawing.RectangleF 0, (-$size * 0.06), $size, $size
        $g.DrawString("Aa", $font, $white, $textRect, $fmt)
        if ($size -ge 24) {
            $pen = New-Object System.Drawing.Pen $white, ([Math]::Max(1.5, $size * 0.07))
            $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
            $pts = [System.Drawing.PointF[]]@(
                (New-Object System.Drawing.PointF ($size * 0.30), ($size * 0.76)),
                (New-Object System.Drawing.PointF ($size * 0.44), ($size * 0.86)),
                (New-Object System.Drawing.PointF ($size * 0.70), ($size * 0.70)))
            $g.DrawLines($pen, $pts)
        }
    }
    $g.Dispose()
    return $bmp
}

function Write-Ico([string]$file, [bool]$busy) {
    # 256 — PNG, остальные — 32-битный DIB (максимальная совместимость).
    $pngs = foreach ($s in $sizes) {
        $bmp = New-Frame $s $busy
        $ms = New-Object System.IO.MemoryStream
        if ($s -ge 256) {
            $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        } else {
            $bw = New-Object System.IO.BinaryWriter $ms
            $maskRow = [int]([Math]::Ceiling($s / 32.0) * 4)
            $bw.Write([UInt32]40); $bw.Write([Int32]$s); $bw.Write([Int32]($s * 2))
            $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]0)
            $bw.Write([UInt32]($s * $s * 4 + $maskRow * $s)); $bw.Write([Int32]0); $bw.Write([Int32]0)
            $bw.Write([UInt32]0); $bw.Write([UInt32]0)
            for ($y = $s - 1; $y -ge 0; $y--) {
                for ($x = 0; $x -lt $s; $x++) {
                    $c = $bmp.GetPixel($x, $y)
                    $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
                }
            }
            $bw.Write((New-Object byte[] ($maskRow * $s)))
            $bw.Flush()
        }
        $bmp.Dispose()
        , $ms.ToArray()
    }
    $fs = [System.IO.File]::Create($file)
    $w = New-Object System.IO.BinaryWriter $fs
    $w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]; $b = if ($s -ge 256) { 0 } else { $s }
        $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([UInt16]1); $w.Write([UInt16]32)
        $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$offset)
        $offset += $pngs[$i].Length
    }
    foreach ($p in $pngs) { $w.Write($p) }
    $w.Close()
}

Write-Ico (Join-Path $outDir "app.ico") $false
Write-Ico (Join-Path $outDir "busy.ico") $true
Write-Host "Icons written to $outDir"
