<#
    Рисует app.ico и app.png — значок AgentVitals в духе iOS: сквиркл с градиентом
    и белая кардиограмма. Запускать вручную после правки рисунка:
    powershell -File tools\make-icons.ps1
#>
Add-Type -AssemblyName System.Drawing

$sizes = 256, 128, 64, 48, 32, 24, 20, 16
$top = [System.Drawing.Color]::FromArgb(0xFF, 0x7A, 0x59)
$bottom = [System.Drawing.Color]::FromArgb(0xFF, 0x2D, 0x55)

# Кардиограмма на сетке 32×32 — та же, что у значка в трее.
$pulse = @(@(2, 18), @(9, 18), @(12, 10), @(16, 26), @(20, 5), @(23, 18), @(30, 18))

# Сквиркл iOS — суперэллипс |x|^5 + |y|^5 = 1: углы мягче, чем у скруглённого квадрата.
function Add-Squircle([System.Drawing.Drawing2D.GraphicsPath]$path, [single]$size) {
    $half = $size / 2
    $points = for ($i = 0; $i -lt 360; $i++) {
        $t = $i * [Math]::PI / 180
        $cos = [Math]::Cos($t)
        $sin = [Math]::Sin($t)
        $x = $half + $half * [Math]::Sign($cos) * [Math]::Pow([Math]::Abs($cos), 0.4)
        $y = $half + $half * [Math]::Sign($sin) * [Math]::Pow([Math]::Abs($sin), 0.4)
        New-Object System.Drawing.PointF $x, $y
    }
    $path.AddPolygon([System.Drawing.PointF[]]$points)
}

function New-Bitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-Squircle $tile $size
    $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush (
        (New-Object System.Drawing.PointF 0, 0),
        (New-Object System.Drawing.PointF 0, $size), $top, $bottom)
    $g.FillPath($fill, $tile)

    # Линия занимает две трети плитки по центру; на мелких размерах толще — иначе тает в сглаживании.
    $scale = $size * 0.66 / 28
    $points = foreach ($p in $pulse) {
        New-Object System.Drawing.PointF (($p[0] - 16) * $scale + $size / 2), (($p[1] - 15.5) * $scale + $size / 2)
    }
    $width = $size * $(if ($size -le 32) { 0.12 } else { 0.075 })
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $width
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawLines($pen, [System.Drawing.PointF[]]$points)

    $pen.Dispose(); $fill.Dispose(); $tile.Dispose(); $g.Dispose()
    return $bmp
}

<#
    Кадр значка. PNG внутри .ico понимают только у стороны 256; всё, что меньше,
    кладём точками — иначе GDI+ отказывается разбирать собственный файл.
#>
function New-Frame([System.Drawing.Bitmap]$bmp) {
    $size = $bmp.Width
    $stream = New-Object System.IO.MemoryStream
    if ($size -ge 256) {
        $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return ,$stream.ToArray()
    }

    $writer = New-Object System.IO.BinaryWriter $stream
    $writer.Write([UInt32]40)                # BITMAPINFOHEADER
    $writer.Write([Int32]$size)
    $writer.Write([Int32]($size * 2))        # цвет и маска прозрачности одной высотой
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]0)                 # BI_RGB
    $writer.Write([UInt32]($size * $size * 4))
    $writer.Write([Int32]0); $writer.Write([Int32]0)
    $writer.Write([UInt32]0); $writer.Write([UInt32]0)

    # Точки лежат снизу вверх, по четыре байта: синий, зелёный, красный, прозрачность.
    $rect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $row = New-Object byte[] ($size * 4)
    for ($y = $size - 1; $y -ge 0; $y--) {
        [System.Runtime.InteropServices.Marshal]::Copy(
            [IntPtr]($data.Scan0.ToInt64() + $y * $data.Stride), $row, 0, $row.Length)
        $writer.Write($row)
    }
    $bmp.UnlockBits($data)

    # Маска не нужна — прозрачность в самих точках, но место под неё занять обязаны.
    $maskRow = New-Object byte[] ([Math]::Ceiling($size / 32.0) * 4)
    for ($y = 0; $y -lt $size; $y++) {
        $writer.Write($maskRow)
    }

    $writer.Flush()
    return ,$stream.ToArray()
}

$root = Split-Path $PSScriptRoot -Parent
$frames = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($size in $sizes) {
    $bmp = New-Bitmap $size
    if ($size -eq 256) {
        $bmp.Save((Join-Path $root 'src\AgentVitals\app.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    $frames.Add((New-Frame $bmp))
    $bmp.Dispose()
}

$file = [System.IO.File]::Create((Join-Path $root 'src\AgentVitals\app.ico'))
$writer = New-Object System.IO.BinaryWriter $file
$writer.Write([UInt16]0)                     # reserved
$writer.Write([UInt16]1)                     # type: icon
$writer.Write([UInt16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $writer.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
    $writer.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
    $writer.Write([byte]0)                   # палитра не используется
    $writer.Write([byte]0)                   # reserved
    $writer.Write([UInt16]1)                 # color planes
    $writer.Write([UInt16]32)                # бит на пиксель
    $writer.Write([UInt32]$frames[$i].Length)
    $writer.Write([UInt32]$offset)
    $offset += $frames[$i].Length
}

foreach ($frame in $frames) {
    $writer.Write($frame)
}

$writer.Close(); $file.Close()
Write-Host "app.ico - $($sizes.Count) sizes, app.png - 256"
